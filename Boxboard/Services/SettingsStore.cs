using System.Text.Json;
using Boxboard.Models;

namespace Boxboard.Services;

public interface ISettingsStore
{
    Task<BoardSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(BoardSettings settings, CancellationToken ct = default);
}

public sealed class SettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly FileStream _lock;
    public string SettingsPath { get; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boxboard", "settings.json");

    public SettingsStore(string path)
    {
        SettingsPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        // Keep one writer for this layout for the lifetime of the application.
        _lock = new FileStream(SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public async Task<BoardSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(SettingsPath))
            return new BoardSettings();
        var json = await File.ReadAllTextAsync(SettingsPath, ct);
        var settings = JsonSerializer.Deserialize<BoardSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("Boxboard settings cannot be null.");
        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(BoardSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, null);
            else
                File.Move(temporaryPath, SettingsPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public void Dispose() => _lock.Dispose();
}
