using Microsoft.AspNetCore.Identity;
using Money.Business;
using Money.Data.Entities;
using OpenIddict.Abstractions;

namespace Money.Api.Middlewares;

public sealed class AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        RequestEnvironment environment,
        UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.GetClaim(OpenIddictConstants.Claims.Subject);

        if (userId != null)
        {
            logger.LogDebug("Получен claim Subject={AuthUserId} для запроса {Method} {Path}",
                userId,
                context.Request.Method,
                context.Request.Path);

            var user = await userManager.FindByIdAsync(userId);

            if (user != null)
            {
                var accountsService = context.RequestServices.GetRequiredService<AccountsService>();
                var (domainUserId, shardName) = await accountsService.EnsureUserIdAsync(user.Id, context.RequestAborted);
                environment.UserId = domainUserId;
                environment.ShardName = shardName;
                environment.AuthUser = user;

                logger.LogDebug("Контекст запроса установлен: AuthUserId={AuthUserId}, DomainUserId={DomainUserId}, шард={ShardName}, запрос={Method} {Path}",
                    user.Id,
                    domainUserId,
                    shardName,
                    context.Request.Method,
                    context.Request.Path);
            }
            else
            {
                logger.LogWarning("Пользователь с AuthUserId={AuthUserId} не найден в RoutingDb (claim Subject присутствует, но запись отсутствует)",
                    userId);
            }
        }

        await next(context);
    }
}
