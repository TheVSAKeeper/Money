namespace Money.Business.Configs;

public class FilesStorageConfig
{
    private string? _storagePath;

    public required string StoragePath { get; init; }

    public string Path => _storagePath ??= IsRelative
        ? System.IO.Path.Combine(Directory.GetCurrentDirectory(), StoragePath)
        : StoragePath;

    public required bool IsRelative { get; init; }
}
