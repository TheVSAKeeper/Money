namespace Money.Data.Graph;

public record GraphDto(List<NodeDto> Nodes, List<EdgeDto> Edges);

public record NodeDto(string Id, string Label, Dictionary<string, object> Properties);

public record EdgeDto(string From, string To, string Type, Dictionary<string, object> Properties);
