using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password");
var chPassword = builder.AddParameter("clickhouse-password");
var cubeSecret = builder.AddParameter("cube-secret");

var routing = builder.AddPostgres("routing", password: password, port: 21701)
    .WithPgAdmin(c => c.WithHostPort(21700))
    .WithDataVolume();

var routingDb = routing.AddDatabase("RoutingDb", "money_routing");

var dunduk = builder.AddPostgres("dunduk", password: password, port: 21702).WithDataVolume();
var dundukDb = dunduk.AddDatabase("DundukDb", "money_dunduk");

var funduk = builder.AddPostgres("funduk", password: password, port: 21703).WithDataVolume();
var fundukDb = funduk.AddDatabase("FundukDb", "money_funduk");

var burunduk = builder.AddPostgres("burunduk", password: password, port: 21704).WithDataVolume();
var burundukDb = burunduk.AddDatabase("BurundukDb", "money_burunduk");

var redis = builder.AddRedis("redis", 21705)
    .WithRedisCommander(c => c.WithHostPort(port: 21706))
    .WithDataVolume();

var clickhouse = builder.AddClickHouse("clickhouse", password: chPassword, port: 21707).WithDataVolume();

var clickhousedb = clickhouse.AddDatabase("clickhousedb");

var chEndpoint = clickhouse.GetEndpoint("http");
var chUrl = ReferenceExpression.Create($"http://{chEndpoint.Property(EndpointProperty.Host)}:{chEndpoint.Property(EndpointProperty.Port)}");

var chui = builder.AddContainer("ch-ui", "caioricciuti/ch-ui", "latest")
    .WithImageRegistry("ghcr.io")
    .WithHttpEndpoint(21709, 3488, "chui-http")
    .WithEnvironment("CLICKHOUSE_URL", chUrl)
    .WithVolume("chui-data", "/app/data")
    .WithReference(clickhouse)
    .WaitFor(clickhouse);

var cube = builder.AddContainer("cube", "cubejs/cube", "v1.6")
    .WithEndpoint(4000, 4000, name: "api", scheme: "http")
    .WithEndpoint(3000, 3000, name: "playground", scheme: "http")
    .WithEnvironment("CUBEJS_DB_TYPE", "clickhouse")
    .WithEnvironment("CUBEJS_DB_HOST", chEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CUBEJS_DB_PORT", chEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("CUBEJS_DB_PASS", chPassword)
    .WithEnvironment("CUBEJS_DB_NAME", "clickhousedb")
    .WithEnvironment("CUBEJS_API_SECRET", cubeSecret)
    .WithEnvironment("CUBEJS_DEV_MODE", "true")
    .WithBindMount("../../cube", "/cube/conf")
    .WaitFor(clickhouse);

var cubeApiEndpoint = cube.GetEndpoint("api");
var cubeBaseUrl = ReferenceExpression.Create($"http://{cubeApiEndpoint.Property(EndpointProperty.Host)}:{cubeApiEndpoint.Property(EndpointProperty.Port)}");

var api = builder.AddProject<Money_Api>("api")
    .WithReference(routingDb)
    .WaitFor(routingDb)
    .WithReference(dundukDb)
    .WaitFor(dundukDb)
    .WithReference(fundukDb)
    .WaitFor(fundukDb)
    .WithReference(burundukDb)
    .WaitFor(burundukDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(clickhousedb)
    .WaitFor(clickhousedb)
    .WithEnvironment("Cube__BaseUrl", cubeBaseUrl)
    .WithEnvironment("Cube__ApiSecret", cubeSecret)
    .WaitFor(cube);

builder.AddProject<Money_Web>("frontend")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

await builder.Build().RunAsync();
