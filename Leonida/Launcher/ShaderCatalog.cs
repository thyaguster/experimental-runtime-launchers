using System.Text.Json;

internal static class ShaderCatalog
{
    internal static bool IsReady(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "index.json")));
            if (index.RootElement.ValueKind != JsonValueKind.Object || !index.RootElement.EnumerateObject().Any()) return false;
            foreach (var file in Directory.EnumerateFiles(directory, "*.spv"))
            {
                using var stream = File.OpenRead(file);
                using var reader = new BinaryReader(stream);
                if (stream.Length >= 20 && reader.ReadUInt32() == 0x07230203) return true;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        return false;
    }
}
