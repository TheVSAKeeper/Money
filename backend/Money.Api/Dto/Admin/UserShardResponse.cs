namespace Money.Api.Dto.Admin;

/// <summary>
/// Информация о пользователе и его шарде.
/// </summary>
public class UserShardInfo
{
    /// <summary>
    /// Логин пользователя.
    /// </summary>
    public required string UserName { get; init; }

    /// <summary>
    /// Email пользователя.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Имя шарда.
    /// </summary>
    public required string ShardName { get; init; }

    /// <summary>
    /// Дата назначения на шард.
    /// </summary>
    public DateTime AssignedAt { get; init; }
}
