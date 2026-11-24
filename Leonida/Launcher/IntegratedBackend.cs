using System.Diagnostics;
using System.Text.Json;

namespace LeonidaLauncher;

internal static class IntegratedBackend
{
    internal static string CurrentExecutable
    {
        get
        {
            var root = Path.Combine(Program.Root, "Engine") + Path.DirectorySeparatorChar;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "integrated-current.json")));
                var path = Path.GetFullPath(Path.Combine(root, json.RootElement.GetProperty("executable").GetString()!));
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or ArgumentException or UnauthorizedAccessException) { }
            return Path.Combine(root, "integrated-not-built", "victory.exe");
        }
    }

    internal static ProcessStartInfo Command(Settings settings, string command)
    {
        var state = Path.Combine(Program.StateDir, "integrated-victory");
        var start = new ProcessStartInfo(Path.GetFullPath(settings.EngineExe))
        {
            WorkingDirectory = Program.StateDir, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("LEONIDA_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("XCL_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["VICTORY_GAME_DIR"] = Path.GetDirectoryName(Path.GetFullPath(settings.GameExe))!;
        start.Environment["VICTORY_CACHE_DIR"] = Path.Combine(state, "cache");
        start.Environment["VICTORY_DATA_DIR"] = Path.Combine(state, "saves");
        start.Environment["VICTORY_LOG"] = "info";
        if (settings.Warp) start.Environment["VICTORY_D3D12_WARP"] = "1";
        if (settings.SeriesSMenuPicture) start.Environment["VICTORY_SERIES_S_MENU_PICTURE"] = "1";
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(Path.GetFullPath(settings.GameExe));
        if (command == "run")
        {
            start.ArgumentList.Add("--unimplemented");
            start.ArgumentList.Add("trap");
            start.ArgumentList.Add("--execute");
            start.ArgumentList.Add("--console");
            start.ArgumentList.Add("series-s");
        }
        return start;
    }
}
