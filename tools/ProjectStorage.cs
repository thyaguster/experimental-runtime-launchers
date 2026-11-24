using System.Diagnostics;

internal static class ProjectStorage
{
    internal static string FindRoot(string stateDirectory)
    {
        var directory = new DirectoryInfo(stateDirectory);
        var actual = directory.Exists ? directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName : directory.FullName;
        return Directory.GetParent(actual)!.FullName;
    }

    internal static ProcessStartInfo ChangeCommand(string launcherRoot, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
            throw new ArgumentException("Choose an absolute project folder path.");
        var script = Path.GetFullPath(Path.Combine(launcherRoot, "..", "tools", "Set-ProjectFolder.ps1"));
        if (!File.Exists(script)) throw new FileNotFoundException("Set-ProjectFolder.ps1 is missing from the shared tools folder.");
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = launcherRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ProjectRoot", launcherRoot, "-Destination", Path.GetFullPath(destination) })
            start.ArgumentList.Add(arg);
        return start;
    }
}
