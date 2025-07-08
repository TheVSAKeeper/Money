using Microsoft.AspNetCore.Identity;
using Money.Data.Entities;
using System.Text;
using System.Text.RegularExpressions;

namespace Money.Business.Services;

public partial class AccountsService(
    RequestEnvironment environment,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext context,
    QueueHolder queueHolder,
    BusinessObservabilityService observabilityService)
{
    public async Task RegisterAsync(RegisterAccount model, CancellationToken cancellationToken = default)
    {
        using var span = observabilityService.StartBusinessOperationSpan("register", "account");

        try
        {
            span?.SetTag("account.username", model.UserName);
            span?.SetTag("account.has_email", !string.IsNullOrEmpty(model.Email));

            observabilityService.AddBusinessOperationStartEvent("account_registration", new()
            {
                ["username"] = model.UserName,
                ["has_email"] = !string.IsNullOrEmpty(model.Email),
            });

            observabilityService.AddEvent("UsernameValidation");

            if (UserNameRegex().IsMatch(model.UserName) == false)
            {
                observabilityService.AddValidationEvent("username", false, ["Invalid characters in username"]);
                throw new EntityExistsException("Извините, но имя пользователя не может содержать служебные символы.");
            }

            observabilityService.AddValidationEvent("username", true);

            observabilityService.AddEvent("CheckExistingUser");
            var user = await userManager.FindByNameAsync(model.UserName);

            if (user != null)
            {
                observabilityService.AddEvent("UserAlreadyExists");
                throw new EntityExistsException("Извините, но пользователь с таким именем уже зарегистрирован. Пожалуйста, попробуйте другое имя пользователя.");
            }

            if (model.Email != null)
            {
                observabilityService.AddEvent("CheckExistingEmail");
                user = await userManager.FindByEmailAsync(model.Email);

                if (user != null)
                {
                    if (user.EmailConfirmed)
                    {
                        observabilityService.AddEvent("EmailAlreadyConfirmed");
                        throw new EntityExistsException("Извините, но пользователь с таким email уже зарегистрирован. Пожалуйста, попробуйте другоЙ email.");
                    }

                    observabilityService.AddEvent("CleanupUnconfirmedEmail");
                    user.Email = null;
                    user.EmailConfirmCode = null;
                    await userManager.UpdateAsync(user);
                }
            }

            observabilityService.AddEvent("CreateNewUser");

            user = new()
            {
                UserName = model.UserName,
                Email = model.Email,
            };

            if (model.Email != null)
            {
                observabilityService.AddEvent("GenerateEmailConfirmCode");
                user.EmailConfirmCode = GetCode(6);
            }

            observabilityService.AddEvent("CreateUserInIdentity");
            var result = await userManager.CreateAsync(user, model.Password);

            if (result.Succeeded == false)
            {
                var errors = result.Errors.Select(error => error.Description).ToArray();
                observabilityService.AddValidationEvent("user_creation", false, errors);
                throw new IncorrectDataException($"Ошибки: {string.Join("; ", errors)}");
            }

            observabilityService.AddValidationEvent("user_creation", true);

            observabilityService.AddEvent("CreateDomainUser");
            await AddNewUser(user.Id, cancellationToken);

            if (model.Email != null)
            {
                observabilityService.AddEvent("SendConfirmationEmail");
                SendEmail(user.UserName, model.Email, user.EmailConfirmCode!);
            }

            observabilityService.AddBusinessOperationEndEvent("account_registration", true, new()
            {
                ["user_id"] = user.Id,
            });
        }
        catch (Exception ex)
        {
            observabilityService.RecordException(ex);

            observabilityService.AddBusinessOperationEndEvent("account_registration", false, new()
            {
                ["error_type"] = ex.GetType().Name,
            });

            throw;
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var user = environment.AuthUser;

        if (user == null)
        {
            throw new BusinessException("Извините, но пользователь не указан.");
        }

        var domainUser = await context.DomainUsers.SingleAsync(x => x.AuthUserId == user.Id);

        if (domainUser.TransporterPassword != null)
        {
            if (!LegacyAuth.Validate(user.UserName!, currentPassword, domainUser.TransporterPassword))
            {
                throw new PermissionException("Неверное имя пользователя или пароль.");
            }

            await userManager.AddPasswordAsync(user, newPassword);
            domainUser.TransporterPassword = null;
            await context.SaveChangesAsync();
        }
        else
        {
            var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

            if (result.Succeeded == false)
            {
                throw new IncorrectDataException($"Ошибки: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }
    }

    public async Task<int> EnsureUserIdAsync(Guid authUserId, CancellationToken cancellationToken = default)
    {
        var domainUser = await context.DomainUsers.FirstOrDefaultAsync(x => x.AuthUserId == authUserId, cancellationToken);

        if (domainUser != null)
        {
            return domainUser.Id;
        }

        return await AddNewUser(authUserId, cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);
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
        await context.SaveChangesAsync(cancellationToken);

        SendEmail(user.UserName!, user.Email, user.EmailConfirmCode);
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

    private void SendEmail(string userName, string email, string confirmCode)
    {
        const string Title = "Подтверждение регистрации";
        var body = $"Здравствуйте, {userName}!\r\nВаш код для подтверждения регистрации на сайте Филочек:\r\n{confirmCode}";

        // TODO: Стоит ли добавлять новое письмо, если уже есть в очереди письмо на тот же email
        queueHolder.MailMessages.Enqueue(new(email, Title, body));
    }

    // TODO Подумать над переносом в сервис
    private async Task<int> AddNewUser(Guid authUserId, CancellationToken cancellationToken = default)
    {
        var domainUser = new DomainUser
        {
            AuthUserId = authUserId,
        };

        await context.DomainUsers.AddAsync(domainUser, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return domainUser.Id;
    }
}
