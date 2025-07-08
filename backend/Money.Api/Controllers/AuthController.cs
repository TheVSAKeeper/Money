#pragma warning disable S6931

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Money.Api.Services;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;

namespace Money.Api.Controllers;

[ApiController]
public class AuthController(AuthService authService, ObservabilityService observabilityService) : ControllerBase
{
    private const string AuthenticationScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    /// <summary>
    /// Обменивает учетные данные пользователя на токен доступа.
    /// </summary>
    /// <remarks>
    /// Этот метод обрабатывает запросы на получение токена доступа, используя учетные данные пользователя.
    /// </remarks>
    /// <returns>Токен доступа в формате JSON.</returns>
    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ClaimsPrincipal), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("Не удалось получить запрос OpenID Connect.");

        var grantType = request.GrantType ?? "unknown";

        ClaimsIdentity identity;

        if (request.IsPasswordGrantType())
        {
            observabilityService.AddEvent("PasswordGrantProcessing");
            identity = await authService.HandlePasswordGrantAsync(request);
        }
        else if (request.IsRefreshTokenGrantType())
        {
            observabilityService.AddEvent("RefreshTokenGrantProcessing");
            var result = await HttpContext.AuthenticateAsync(AuthenticationScheme);
            identity = await authService.HandleRefreshTokenGrantAsync(result);
        }
        else
        {
            observabilityService.AddEvent("UnsupportedGrantType", new()
            {
                ["grant_type"] = grantType,
            });

            throw new NotImplementedException("Указанный тип предоставления не реализован.");
        }

        return SignIn(new(identity), AuthenticationScheme);
    }

    /// <summary>
    /// Возвращает информацию о пользователе.
    /// </summary>
    /// <remarks>
    /// Этот метод обрабатывает запросы на получение информации о пользователе.
    /// </remarks>
    /// <returns>Информация о пользователе в формате JSON.</returns>
    [Authorize(AuthenticationSchemes = AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Userinfo()
    {
        return Ok(await authService.GetUserInfoAsync(User));
    }
}
