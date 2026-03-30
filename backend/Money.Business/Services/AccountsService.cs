using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Money.Business.Interfaces;
using Money.Data.Entities;
using Money.Data.Sharding;
using System.Text;
using System.Text.RegularExpressions;

namespace Money.Business.Services;

public sealed partial class AccountsService(
    RequestEnvironment environment,
    UserManager<ApplicationUser> userManager,
    RoutingDbContext routingContext,
    ShardedDbContextFactory shardFactory,
    ShardRouter shardRouter,
    IEmailQueueService emailQueueService,
    ILogger<AccountsService> logger)
{
    public async Task RegisterAsync(RegisterAccount model, CancellationToken cancellationToken = default)
    {
        if (!UserNameRegex().IsMatch(model.UserName))
        {
            throw new EntityExistsException("Извините, но имя пользователя не может содержать служебные символы.");
        }

        var user = await userManager.FindByNameAsync(model.UserName);

        if (user != null)
        {
            throw new EntityExistsException("Извините, но пользователь с таким именем уже зарегистрирован. Пожалуйста, попробуйте другое имя пользователя.");
        }

        if (model.Email != null)
        {
            user = await userManager.FindByEmailAsync(model.Email);

            if (user != null)
            {
                if (user.EmailConfirmed)
                {
                    throw new EntityExistsException("Извините, но пользователь с таким email уже зарегистрирован. Пожалуйста, попробуйте другоЙ email.");
                }

                user.Email = null;
                user.EmailConfirmCode = null;
                await userManager.UpdateAsync(user);
            }
        }

        user = new()
        {
            UserName = model.UserName,
            Email = model.Email,
        };

        if (model.Email != null)
        {
            user.EmailConfirmCode = GetCode(6);
        }

        logger.LogInformation("Регистрация нового пользователя: UserName={UserName}", model.UserName);

        var result = await userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            logger.LogWarning("Ошибка создания Identity-пользователя {UserName}: {Errors}",
                model.UserName,
                string.Join("; ", result.Errors.Select(e => e.Description)));

            throw new IncorrectDataException($"Ошибки: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        logger.LogInformation("Identity-пользователь создан: UserName={UserName}, AuthUserId={AuthUserId}",
            model.UserName,
            user.Id);

        await AddNewUser(user.Id, cancellationToken);

        if (model.Email != null)
        {
            await SendEmailAsync(user.UserName, model.Email, user.EmailConfirmCode!);
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var user = environment.AuthUser;
        if (user == null)
        {
            throw new BusinessException("Извините, но пользователь не указан.");
        }

        var shardName = shardRouter.ResolveShard(user.Id);
        await using var shardContext = shardFactory.Create(shardName);

        logger.LogDebug("Смена пароля: AuthUserId={AuthUserId}, шард={ShardName}",
            user.Id,
            shardName);

        var domainUser = await shardContext.DomainUsers.SingleAsync(x => x.AuthUserId == user.Id);
        if (domainUser.TransporterPassword != null)
        {
            if (!LegacyAuth.Validate(user.UserName!, currentPassword, domainUser.TransporterPassword))
            {
                throw new PermissionException("Неверное имя пользователя или пароль.");
            }

            await userManager.AddPasswordAsync(user, newPassword);
            domainUser.TransporterPassword = null;
            await shardContext.SaveChangesAsync();

            logger.LogInformation("Устаревший пароль мигрирован на Identity: AuthUserId={AuthUserId}, DomainUserId={DomainUserId}",
                user.Id,
                domainUser.Id);
        }
        else
        {
            var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
            {
                throw new IncorrectDataException($"Ошибки: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }

            logger.LogInformation("Пароль успешно изменён: AuthUserId={AuthUserId}", user.Id);
        }
    }

    public async Task<(int UserId, string ShardName)> EnsureUserIdAsync(Guid authUserId, CancellationToken cancellationToken = default)
    {
        var shardName = shardRouter.ResolveShard(authUserId);

        logger.LogDebug("Поиск доменного пользователя: AuthUserId={AuthUserId}, целевой шард={ShardName}",
            authUserId,
            shardName);

        await using var shardContext = shardFactory.Create(shardName);

        var domainUser = await shardContext.DomainUsers
            .FirstOrDefaultAsync(x => x.AuthUserId == authUserId, cancellationToken);

        if (domainUser != null)
        {
            logger.LogDebug("Доменный пользователь найден: AuthUserId={AuthUserId}, DomainUserId={DomainUserId}, шард={ShardName}",
                authUserId,
                domainUser.Id,
                shardName);

            return (domainUser.Id, shardName);
        }

        logger.LogInformation("Доменный пользователь не найден на шарде {ShardName} для AuthUserId={AuthUserId}, создаём нового",
            shardName,
            authUserId);

        var userId = await AddNewUser(authUserId, cancellationToken);
        return (userId, shardName);
    }

    public async Task<Guid> GetAuthUserIdAsync(int domainUserId, string shardName, CancellationToken cancellationToken = default)
    {
        await using var shardContext = shardFactory.Create(shardName);
        var domainUser = await shardContext.DomainUsers
            .FirstAsync(x => x.Id == domainUserId, cancellationToken);

        return domainUser.AuthUserId;
    }

    public async Task ConfirmEmailAsync(string confirmCode, CancellationToken cancellationToken = default)
    {
        var user = environment.AuthUser;

        if (user == null)
        {
            throw new BusinessException("Извините, но пользователь не указан.");
        }

        if (user.EmailConfirmed)
        {
            throw new BusinessException("Извините, но у вас уже подтвержденный email.");
        }

        if (user.Email == null)
        {
            throw new BusinessException("Простите, но у вас не заполнен email.");
        }

        if (user.EmailConfirmCode != confirmCode)
        {
            throw new BusinessException("Извините, код подтверждения недействительный.");
        }

        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
    }

    public async Task ResendConfirmCodeAsync(CancellationToken cancellationToken = default)
    {
        var user = environment.AuthUser;

        if (user == null)
        {
            throw new BusinessException("Извините, но пользователь не указан.");
        }

        if (user.EmailConfirmed)
        {
            throw new BusinessException("Извините, но у вас уже подтвержденный email.");
        }

        if (user.Email == null)
        {
            throw new BusinessException("Простите, но у вас не заполнен email.");
        }

        // TODO: Проверка на повторную отправку по времени (защита от частых запросов)

        user.EmailConfirmCode = GetCode(6);
        await userManager.UpdateAsync(user);

        await SendEmailAsync(user.UserName!, user.Email, user.EmailConfirmCode);
    }

    private static string GetCode(int length, string allowedChars = "1234567890")
    {
        var result = new StringBuilder(length);

        while (result.Length < length)
        {
            var index = Random.Shared.Next(allowedChars.Length);
            result.Append(allowedChars[index]);
        }

        return result.ToString();
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex UserNameRegex();

    private async Task SendEmailAsync(string userName, string email, string confirmCode)
    {
        const string Title = "Подтверждение регистрации";
        var body = $"Здравствуйте, {userName}!\r\nВаш код для подтверждения регистрации на сайте Филочек:\r\n{confirmCode}";

        // TODO: Стоит ли добавлять новое письмо, если уже есть в очереди письмо на тот же email
        await emailQueueService.EnqueueAsync(new MailMessage(email, Title, body));
    }

    // TODO Подумать над переносом в сервис
    private async Task<int> AddNewUser(Guid authUserId, CancellationToken cancellationToken = default)
    {
        var shardName = shardRouter.ResolveShard(authUserId);

        logger.LogInformation("Создание доменного пользователя: AuthUserId={AuthUserId}, назначенный шард={ShardName}",
            authUserId,
            shardName);

        await using var shardContext = shardFactory.Create(shardName);

        var domainUser = new DomainUser { AuthUserId = authUserId };
        await shardContext.DomainUsers.AddAsync(domainUser, cancellationToken);
        await shardContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Доменный пользователь создан: AuthUserId={AuthUserId}, DomainUserId={DomainUserId}, шард={ShardName}",
            authUserId,
            domainUser.Id,
            shardName);

        routingContext.ShardMappings.Add(new()
        {
            UserId = domainUser.Id,
            ShardName = shardName,
            AssignedAt = DateTime.UtcNow,
        });

        await routingContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Аудит назначения шарда сохранён в RoutingDb: DomainUserId={DomainUserId}, шард={ShardName}",
            domainUser.Id,
            shardName);

        return domainUser.Id;
    }
}
