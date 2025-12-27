var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume();

var db = postgres.AddDatabase("ApplicationDbContext", databaseName: "money_db");

var api = builder.AddProject<Projects.Money_Api>("api")
    .WithReference(db)
    .WaitFor(db);

builder.AddProject<Projects.Money_Web>("frontend")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

builder.Build().Run();
