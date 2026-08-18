namespace SalesforceGrpc.Helpers;

public class ViteManifest {
    private static readonly Dictionary<string, Dictionary<string, ViteManifestEntry>?> _manifests = new();
    private static readonly object Lock = new();
    
    public static ViteManifestEntry? GetEntry(string manifestPath, string entryKey = "dist")
    {
        if (!_manifests.ContainsKey(entryKey) || _manifests[entryKey] == null)
        {
            lock (Lock)
            {
                if (!_manifests.ContainsKey(entryKey) || _manifests[entryKey] == null)
                {
                    LoadManifest(entryKey);
                }
            }
        }

        _manifests[entryKey].TryGetValue(manifestPath, out var entry);
        return entry;
    }

    private static void LoadManifest(string distDir = "dist")
    {
        var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", distDir, ".vite", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _manifests[distDir] = new Dictionary<string, ViteManifestEntry>();
            return;
        }
        
        var json = File.ReadAllText(manifestPath);
        _manifests[distDir] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ViteManifestEntry>>(json) 
                              ?? new Dictionary<string, ViteManifestEntry>();
    }

    public static void Reload(string? distDir = null)
    {
        lock (Lock)
        {
            if (distDir != null)
            {
                _manifests.Remove(distDir);
            }
            else
            {
                _manifests.Clear();
            }
        }
    }
}