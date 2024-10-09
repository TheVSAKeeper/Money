using Money.Api.Definitions.Base;
using Money.Api.Middlewares;
using Money.Business.Configs;

namespace Money.Api.Definitions;

public class FilesStorageDefinition : AppDefinition
{
    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        IConfigurationSection filesStorage = builder.Configuration.GetSection("FilesStorage");

        FilesStorageConfig? filesStorageConfig = filesStorage.Get<FilesStorageConfig>();
        string? path = filesStorageConfig?.Path;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ApplicationException("FilesStoragePath is missing");
        }

        if (Directory.Exists(path) == false)
        {
            Directory.CreateDirectory(path);
        }

        builder.Services.Configure<FilesStorageConfig>(filesStorage);
    }

    public override void ConfigureApplication(WebApplication app)
    {
        app.UseMiddleware<FileUploadMiddleware>();
    }
}
