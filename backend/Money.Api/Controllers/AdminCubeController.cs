using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Money.Api.Services.Analytics;
using Money.Business;
using OpenIddict.Validation.AspNetCore;

namespace Money.Api.Controllers;

/// <summary>
/// OLAP-аналитика через Cube.dev.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("Admin/Cube")]
public class AdminCubeController(
    CubeApiService cubeApi,
    RequestEnvironment environment,
    ILogger<AdminCubeController> logger) : ControllerBase
{
    /// <summary>
    /// Расходы по категориям за указанный период.
    /// </summary>
    [HttpGet("Expenses")]
    [ProducesResponseType(typeof(CubeResultSet), StatusCodes.Status200OK)]
    public async Task<CubeResultSet> GetExpenses(
        [FromQuery] string period = "last 6 months",
        [FromQuery] string granularity = "month",
        CancellationToken ct = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));

        logger.LogDebug("Cube expenses request for userId={UserId}, period={Period}", environment.UserId, period);

        return await cubeApi.GetExpenseCubeAsync(environment.UserId, from, to, ["category_name"], granularity, ct);
    }

    /// <summary>
    /// Анализ долгов по владельцам.
    /// </summary>
    [HttpGet("Debts")]
    [ProducesResponseType(typeof(CubeResultSet), StatusCodes.Status200OK)]
    public async Task<CubeResultSet> GetDebts(
        [FromQuery] string period = "last 6 months",
        CancellationToken ct = default)
    {
        logger.LogDebug("Cube debts request for userId={UserId}", environment.UserId);

        return await cubeApi.GetDebtCubeAsync(environment.UserId, ct: ct);
    }

    /// <summary>
    /// Тренды доходов vs расходов.
    /// </summary>
    [HttpGet("Trends")]
    [ProducesResponseType(typeof(CubeResultSet), StatusCodes.Status200OK)]
    public async Task<CubeResultSet> GetTrends(
        [FromQuery] string granularity = "week",
        [FromQuery] string dateRange = "last 3 months",
        CancellationToken ct = default)
    {
        logger.LogDebug("Cube trends request for userId={UserId}", environment.UserId);

        return await cubeApi.GetTrendCubeAsync(environment.UserId, granularity, dateRange, ct);
    }

    /// <summary>
    /// Метаданные кубов: список measures и dimensions.
    /// </summary>
    [HttpGet("Meta")]
    [ProducesResponseType(typeof(CubeMeta), StatusCodes.Status200OK)]
    public async Task<CubeMeta> GetMeta(CancellationToken ct = default)
    {
        return await cubeApi.GetMetaAsync(ct);
    }
}
