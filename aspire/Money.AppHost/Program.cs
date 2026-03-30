using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password");
var chPassword = builder.AddParameter("clickhouse-password");
var cubeSecret = builder.AddParameter("cube-secret");
var neo4jUser = builder.AddParameter("neo4j-user");
var neo4jPassword = builder.AddParameter("neo4j-password");
var minioPassword = builder.AddParameter("minio-password");

var routing = builder.AddPostgres("routing", password: password, port: 21701)
    .WithPgAdmin(c => c.WithHostPort(21700))
    .WithDataVolume();

var routingDb = routing.AddDatabase("RoutingDb", "money_routing");
var nessieDb = routing.AddDatabase("NessieDb", "money_nessie");

var dunduk = builder.AddPostgres("dunduk", password: password, port: 21702).WithDataVolume();
var dundukDb = dunduk.AddDatabase("DundukDb", "money_dunduk");

var funduk = builder.AddPostgres("funduk", password: password, port: 21703).WithDataVolume();
var fundukDb = funduk.AddDatabase("FundukDb", "money_funduk");

var burunduk = builder.AddPostgres("burunduk", password: password, port: 21704).WithDataVolume();
var burundukDb = burunduk.AddDatabase("BurundukDb", "money_burunduk");

var redis = builder.AddRedis("redis", 21705)
    .WithRedisCommander(c => c.WithHostPort(port: 21706))
    .WithDataVolume();

var neo4j = builder.AddContainer("neo4j", "neo4j", "2026.02.3")
    .WithEndpoint(21710, 7474, name: "browser", scheme: "http")
    .WithEndpoint(7687, 7687, name: "bolt", isProxied: false)
    .WithEnvironment("NEO4J_AUTH", ReferenceExpression.Create($"{neo4jUser}/{neo4jPassword}"))
    .WithVolume("neo4j-data", "/data");

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
    .WithEndpoint(21712, 4000, name: "api", scheme: "http")
    .WithEndpoint(21713, 3000, name: "playground", scheme: "http")
    .WithEnvironment("CUBEJS_DB_TYPE", "clickhouse")
    .WithEnvironment("CUBEJS_DB_HOST", chEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CUBEJS_DB_PORT", chEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("CUBEJS_DB_PASS", chPassword)
    .WithEnvironment("CUBEJS_DB_NAME", "clickhousedb")
    .WithEnvironment("CUBEJS_API_SECRET", cubeSecret)
    .WithEnvironment("CUBEJS_DEV_MODE", "true")
    .WithEnvironment("CUBESTORE_DATA_DIR", "/var/cubestore")
    .WithBindMount("../../cube", "/cube/conf")
    .WithVolume("cubestore-data", "/var/cubestore")
    .WaitFor(clickhouse);

var cubeApiEndpoint = cube.GetEndpoint("api");
var cubeBaseUrl = ReferenceExpression.Create($"http://{cubeApiEndpoint.Property(EndpointProperty.Host)}:{cubeApiEndpoint.Property(EndpointProperty.Port)}");

var minio = builder.AddContainer("minio", "minio/minio", "latest")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
    .WithHttpEndpoint(21714, 9000, "s3")
    .WithHttpEndpoint(21715, 9001, "console")
    .WithVolume("minio-data", "/data")
    .WithHttpHealthCheck("/minio/health/live", endpointName: "s3");

builder.AddContainer("minio-init", "minio/mc", "latest")
    .WithEntrypoint("/bin/sh")
    .WithArgs("-c",
        "mc alias set local http://minio:9000 minioadmin $MINIO_ROOT_PASSWORD && " + "mc mb local/lakehouse-warehouse --ignore-existing && " + "echo 'Buckets created'")
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
    .WaitFor(minio);

var routingEndpoint = routing.GetEndpoint("tcp");

var nessie = builder.AddContainer("nessie", "ghcr.io/projectnessie/nessie", "latest")
    .WithHttpEndpoint(21716, 19120, "api")
    .WithEnvironment("NESSIE_VERSION_STORE_TYPE", "JDBC")
    .WithEnvironment("QUARKUS_DATASOURCE_JDBC_URL",
        ReferenceExpression.Create($"jdbc:postgresql://{routingEndpoint.Property(EndpointProperty.Host)}:{routingEndpoint.Property(EndpointProperty.Port)}/money_nessie"))
    .WithEnvironment("QUARKUS_DATASOURCE_USERNAME", "postgres")
    .WithEnvironment("QUARKUS_DATASOURCE_PASSWORD", password)
    .WithEnvironment("nessie.catalog.default-warehouse", "lakehouse")
    .WithEnvironment("nessie.catalog.warehouses.lakehouse.location", "s3://lakehouse-warehouse/")
    .WithEnvironment("nessie.catalog.service.s3.default-options.endpoint", "http://minio:9000")
    .WithEnvironment("nessie.catalog.service.s3.default-options.path-style-access", "true")
    .WithEnvironment("nessie.catalog.service.s3.default-options.region", "us-east-1")
    .WithEnvironment("nessie.catalog.service.s3.default-options.access-key",
        "urn:nessie-secret:quarkus:nessie.catalog.secrets.access-key")
    .WithEnvironment("nessie.catalog.secrets.access-key.name", "minioadmin")
    .WithEnvironment("nessie.catalog.secrets.access-key.secret", minioPassword)
    .WithHttpHealthCheck("/api/v2/config", endpointName: "api")
    .WaitFor(routing)
    .WaitFor(minio);

var trino = builder.AddContainer("trino", "trinodb/trino", "latest")
    .WithHttpEndpoint(21717, 8080, "http")
    .WithBindMount("../trino-config/catalog", "/etc/trino/catalog", true)
    .WithHttpHealthCheck("/v1/info", endpointName: "http")
    .WaitFor(nessie)
    .WaitFor(minio);

var minioS3 = minio.GetEndpoint("s3");
var nessieApi = nessie.GetEndpoint("api");
var trinoHttp = trino.GetEndpoint("http");

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
    .WaitFor(neo4j)
    .WithEnvironment("Neo4j__BoltUri", ReferenceExpression.Create($"bolt://{neo4j.GetEndpoint("bolt").Property(EndpointProperty.Host)}:{neo4j.GetEndpoint("bolt").Property(EndpointProperty.Port)}"))
    .WithEnvironment("Neo4j__User", neo4jUser)
    .WithEnvironment("Neo4j__Password", neo4jPassword)
    .WithEnvironment("Cube__BaseUrl", cubeBaseUrl)
    .WithEnvironment("Cube__ApiSecret", cubeSecret)
    .WaitFor(cube)
    .WithEnvironment("Lakehouse__MinioEndpoint",
        ReferenceExpression.Create($"{minioS3.Property(EndpointProperty.Host)}:{minioS3.Property(EndpointProperty.Port)}"))
    .WithEnvironment("Lakehouse__MinioAccessKey", "minioadmin")
    .WithEnvironment("Lakehouse__MinioSecretKey", minioPassword)
    .WithEnvironment("Lakehouse__NessieUri",
        ReferenceExpression.Create($"http://{nessieApi.Property(EndpointProperty.Host)}:{nessieApi.Property(EndpointProperty.Port)}"))
    .WithEnvironment("Lakehouse__TrinoUri",
        ReferenceExpression.Create($"http://{trinoHttp.Property(EndpointProperty.Host)}:{trinoHttp.Property(EndpointProperty.Port)}"))
    .WithEnvironment("Lakehouse__Warehouse", "s3://lakehouse-warehouse/")
    .WaitFor(minio)
    .WaitFor(nessie)
    .WaitFor(trino);

builder.AddProject<Money_Web>("frontend")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

await builder.Build().RunAsync();
