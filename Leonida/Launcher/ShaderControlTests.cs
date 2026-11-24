using System.Diagnostics;

internal static class ShaderControlTests
{
    internal static int Worker(string[] args)
    {
        var path = args[1];
        if (args.Contains("--spawn"))
        {
            while (!File.Exists(path + ".go")) Thread.Sleep(20);
            using var child = Process.Start(WorkerStart(path + ".child"))!;
            File.WriteAllText(path + ".childpid", child.Id.ToString());
        }
        while (true) { File.AppendAllText(path, "."); Thread.Sleep(30); }
    }

    static ProcessStartInfo WorkerStart(string path, bool spawn = false)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--pause-worker"); start.ArgumentList.Add(path);
        if (spawn) start.ArgumentList.Add("--spawn");
        return start;
    }

    internal static int Run(string dataRoot)
    {
        var fixture = Path.Combine(dataRoot, "pause-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var path = Path.Combine(fixture, "parent.txt");
        var unrelatedPath = Path.Combine(fixture, "unrelated.txt");
        try
        {
            using var unrelated = Process.Start(WorkerStart(unrelatedPath))!;
            using var parent = Process.Start(WorkerStart(path, spawn: true))!;
            try
            {
                using var control = new ShaderProcessControl(parent);
                File.WriteAllText(path + ".go", "go");
                Wait(() => Length(path) > 2 && Length(path + ".child") > 2 && Length(unrelatedPath) > 2, "Workers did not start.");
                control.Pause();
                var parentLength = Length(path);
                var childLength = Length(path + ".child");
                var unrelatedLength = Length(unrelatedPath);
                Thread.Sleep(300);
                if (Length(path) != parentLength || Length(path + ".child") != childLength || Length(unrelatedPath) <= unrelatedLength)
                    throw new Exception("Pause must freeze the owned tree and leave unrelated processes running.");
                control.Resume();
                Wait(() => Length(path) > parentLength && Length(path + ".child") > childLength, "Workers did not resume.");
                control.Pause();
                var childId = int.Parse(File.ReadAllText(path + ".childpid"));
                control.Stop();
                Wait(() => parent.HasExited && Exited(childId), "Stop while paused left a worker alive.");
                if (unrelated.HasExited) throw new Exception("Unrelated process was terminated.");
                using var disposableParent = Process.Start(WorkerStart(Path.Combine(fixture, "dispose.txt"), spawn: true))!;
                var disposableControl = new ShaderProcessControl(disposableParent);
                var disposablePath = Path.Combine(fixture, "dispose.txt");
                File.WriteAllText(disposablePath + ".go", "go");
                try
                {
                    Wait(() => Length(disposablePath + ".child") > 2, "Cleanup test child did not start.");
                    var cleanupChildId = int.Parse(File.ReadAllText(disposablePath + ".childpid"));
                    disposableControl.Pause();
                    disposableControl.Dispose();
                    Wait(() => disposableParent.HasExited && Exited(cleanupChildId), "Closing the owner left paused workers behind.");
                }
                finally { disposableControl.Dispose(); }
            }
            finally
            {
                if (!parent.HasExited) parent.Kill(true);
                if (!unrelated.HasExited) unrelated.Kill(true);
                parent.WaitForExit(5000); unrelated.WaitForExit(5000);
            }
            File.WriteAllText(Path.Combine(dataRoot, "pause-control-test.txt"), "PASS: parent and child pause, unrelated process isolation, exact resume, repeated pause, stop while paused, job cleanup.");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(dataRoot, "pause-control-test.txt"), error.ToString()); return 1; }
        finally
        {
            // The uniquely named fixture is created directly under the supplied data root.
            foreach (var file in Directory.EnumerateFiles(fixture)) File.Delete(file);
            Directory.Delete(fixture);
        }
    }

    static long Length(string path) { try { return new FileInfo(path).Length; } catch (IOException) { return 0; } }
    static bool Exited(int id) { try { using var p = Process.GetProcessById(id); return p.HasExited; } catch (ArgumentException) { return true; } }
    static void Wait(Func<bool> predicate, string error)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate()) { if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception(error); Thread.Sleep(30); }
    }
}
