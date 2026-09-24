using System.Text.Json;
using Bevdox.Models;

namespace Bevdox.Services;

public sealed class ConfigStore
{
    private const string ConfigDir = ".config";
    private const string ConfigFileName = "bevdox";
    private readonly string _configPath;

    public ConfigStore()
    {
        var oneDrivePath = Environment.GetEnvironmentVariable("OneDriveCommercial")
            ?? Environment.GetEnvironmentVariable("OneDrive")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dir = Path.Combine(oneDrivePath, ConfigDir);
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, ConfigFileName);
    }

    public string ConfigPath => _configPath;

    public async Task<BevdoxConfig> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_configPath))
            return new BevdoxConfig();

        try
        {
            var json = await File.ReadAllTextAsync(_configPath, ct);
            return JsonSerializer.Deserialize(json, BevdoxJsonContext.Default.BevdoxConfig)
                ?? new BevdoxConfig();
        }
        catch (JsonException)
        {
            return new BevdoxConfig();
        }
    }

    public async Task SaveAsync(BevdoxConfig config, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(config, BevdoxJsonContext.Default.BevdoxConfig);
        await File.WriteAllTextAsync(_configPath, json, ct);
    }

    /// <summary>
    /// Merges fresh discovery results into the existing config, preserving user renames.
    /// </summary>
    public static BevdoxConfig Merge(BevdoxConfig existing, List<DevBoxInstance> freshInstances)
    {
        var renameMap = existing.Instances
            .Where(i => i.DisplayName is not null)
            .ToDictionary(i => i.UniqueId, i => i.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var instance in freshInstances)
        {
            if (renameMap.TryGetValue(instance.UniqueId, out var displayName))
                instance.DisplayName = displayName;
        }

        // Guard: don't wipe out a valid cache with empty results (likely auth failure)
        if (freshInstances.Count == 0 && existing.Instances.Count > 0)
            return existing;

        return new BevdoxConfig
        {
            LastRefreshUtc = DateTimeOffset.UtcNow,
            Instances = freshInstances
        };
    }
}
