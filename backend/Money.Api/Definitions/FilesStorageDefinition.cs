using Money.Api.Definitions.Base;
using Money.Api.Middlewares;
using Money.Business.Configs;

namespace Money.Api.Definitions;

public class FilesStorageDefinition : AppDefinition
{
    public override bool Enabled => false;

    public override void ConfigureServices(WebApplicationBuilder builder)
    {
        IConfigurationSection filesStorage = builder.Configuration.GetSection("FilesStorage");

        FilesStorageConfig? filesStorageConfig = filesStorage.Get<FilesStorageConfig>();

        if (filesStorageConfig == null || string.IsNullOrEmpty(filesStorageConfig.Path))
        {
            throw new ApplicationException("FilesStoragePath is missing");
        }

        if (!Directory.Exists(filesStorageConfig.Path))
        {
            Directory.CreateDirectory(filesStorageConfig.Path);
        }

        builder.Services.Configure<FilesStorageConfig>(filesStorage);
    }

    public override void ConfigureApplication(WebApplication app)
    {
        app.UseMiddleware<FileUploadMiddleware>();
    }
}
