namespace Money.Api.Services.Analytics;

public record CubeQuery
{
    public string[] Measures { get; init; } = [];
    public string[] Dimensions { get; init; } = [];
    public CubeFilter[] Filters { get; init; } = [];
    public CubeTimeDimension[] TimeDimensions { get; init; } = [];
    public int Limit { get; init; }
}

public record CubeFilter(string Member, string Operator, string[] Values);

public record CubeTimeDimension(string Dimension, string Granularity, string[] DateRange);

public record CubeResultSet
{
    public List<Dictionary<string, object?>> Data { get; init; } = [];
    public Dictionary<string, CubeAnnotation>? Annotation { get; init; }
}

public record CubeAnnotation(string Title, string ShortTitle, string Type);

public record CubeMeta
{
    public List<CubeDef> Cubes { get; init; } = [];
}

public record CubeDef(string Name, List<CubeMemberDef> Measures, List<CubeMemberDef> Dimensions);

public record CubeMemberDef(string Name, string Title, string ShortTitle, string Type);
