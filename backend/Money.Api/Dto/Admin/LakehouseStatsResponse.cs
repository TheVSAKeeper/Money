using Money.Api.Services.Lakehouse;

namespace Money.Api.Dto.Admin;

public class LakehouseStatsResponse
{
    public List<LakehouseLayerInfo> Layers { get; set; } = [];
    public DateTimeOffset? LastSyncUtc { get; set; }
    public long TotalEventsProcessed { get; set; }
}
