namespace Money.Api.Services.Lakehouse;

public class LakehouseSettings
{
    public string MinioEndpoint { get; set; } = "localhost:9000";
    public string MinioAccessKey { get; set; } = "minioadmin";
    public string MinioSecretKey { get; set; } = "minioadmin123!";
    public string NessieUri { get; set; } = "http://localhost:19120";
    public string TrinoUri { get; set; } = "http://localhost:8080";
    public string Warehouse { get; set; } = "s3://lakehouse-warehouse/";
    public double SyncIntervalSeconds { get; set; } = 30;
    public double TransformIntervalSeconds { get; set; } = 300;
    public double ReconciliationIntervalHours { get; set; } = 6;
}
