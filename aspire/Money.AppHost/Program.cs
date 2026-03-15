using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password");

var routing = builder.AddPostgres("routing", password: password).WithPgAdmin().WithDataVolume();
var routingDb = routing.AddDatabase("RoutingDb", "money_routing");

var dunduk = builder.AddPostgres("dunduk", password: password).WithDataVolume();
var dundukDb = dunduk.AddDatabase("DundukDb", "money_dunduk");

var funduk = builder.AddPostgres("funduk", password: password).WithDataVolume();
var fundukDb = funduk.AddDatabase("FundukDb", "money_funduk");

var burunduk = builder.AddPostgres("burunduk", password: password).WithDataVolume();
var burundukDb = burunduk.AddDatabase("BurundukDb", "money_burunduk");

var api = builder.AddProject<Money_Api>("api")
    .WithReference(routingDb)
    .WaitFor(routingDb)
    .WithReference(dundukDb)
    .WaitFor(dundukDb)
    .WithReference(fundukDb)
    .WaitFor(fundukDb)
    .WithReference(burundukDb)
    .WaitFor(burundukDb);

builder.AddProject<Money_Web>("frontend")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

await builder.Build().RunAsync();
