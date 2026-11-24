using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace VictoryLauncher;

internal static class Program
{
    internal static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    internal static readonly string StateDir = Path.Combine(Root, "launcher-data");

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--mock-engine"))
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { args, game = Environment.GetEnvironmentVariable("VICTORY_GAME_DIR"), warp = Environment.GetEnvironmentVariable("VICTORY_D3D12_WARP") }));
            Console.WriteLine("mock stdout");
            Console.Error.WriteLine("mock stderr");
            return 7;
        }
        if (args.Length == 2 && args[0] == "--preview-first-run")
        {
            FirstRun.Ensure(Program.Root, "VIctory", args[1]);
            return 0;
        }
        if (!args.Contains("--preview") && !FirstRun.Ensure(Program.Root, "VIctory")) return 0;
        using var form = new LauncherForm();
        if (args.Length == 2 && args[0] == "--preview")
        {
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]);
            return 0;
        }
        Application.Run(form);
        return 0;
    }

    internal static ProcessStartInfo EngineCommand(Settings settings, string command)
    {
        var exe = Path.GetFullPath(settings.GameExe);
        var start = new ProcessStartInfo(Path.GetFullPath(settings.EngineExe))
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.Environment["VICTORY_GAME_DIR"] = Path.GetDirectoryName(exe)!;
        // Do not inherit unrelated diagnostics or scripted key presses from the shell.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)).ToArray())
            if (key != "VICTORY_GAME_DIR") start.Environment.Remove(key);
        start.Environment["VICTORY_CACHE_DIR"] = Path.Combine(StateDir, "cache");
        start.Environment["VICTORY_DATA_DIR"] = Path.Combine(StateDir, "saves");
        start.Environment["VICTORY_LOG"] = "info";
        if (settings.Warp) start.Environment["VICTORY_D3D12_WARP"] = "1";
        start.ArgumentList.Add(command);
        if (command != "config") start.ArgumentList.Add(exe);
        if (command == "run")
        {
            start.ArgumentList.Add("--execute");
            start.ArgumentList.Add("--console");
            start.ArgumentList.Add(settings.SeriesS ? "series-s" : "series-x");
        }
        return start;
    }
}

internal sealed class Settings
{
    public string GameExe { get; set; } = "";
    public string EngineExe { get; set; } = EngineInstall.CurrentExecutable;
    public bool Warp { get; set; } = true;
    public bool SeriesS { get; set; }
    internal static string FilePath => Path.Combine(Program.StateDir, "settings.json");
    internal static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            if (string.IsNullOrWhiteSpace(settings.EngineExe) || EngineInstall.IsManaged(settings.EngineExe)) settings.EngineExe = EngineInstall.CurrentExecutable;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Program.StateDir);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}

internal static class EngineInstall
{
    internal static readonly string LegacyExecutable = Path.Combine(Program.Root, "Source", "target", "diag", "victory.exe");
    internal static string CurrentExecutable
    {
        get
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Root, "Engine", "current.json")));
                var root = Path.Combine(Program.Root, "Engine") + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(root, json.RootElement.GetProperty("executable").GetString()!));
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : LegacyExecutable;
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or ArgumentException or UnauthorizedAccessException) { return LegacyExecutable; }
        }
    }
    internal static bool IsManaged(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.Equals(LegacyExecutable, StringComparison.OrdinalIgnoreCase) || full.StartsWith(Path.Combine(Program.Root, "Engine") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }
    internal static string VersionLabel
    {
        get
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Root, "Engine", "current.json")));
                var commit = json.RootElement.GetProperty("commit").GetString()!;
                return "Installed engine: " + commit[..Math.Min(commit.Length, 12)];
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return "Installed engine: original build"; }
        }
    }
}

internal sealed class LauncherForm : Form
{
    readonly TextBox game = new(), engine = new(), projectFolder = new();
    readonly CheckBox warp = new() { Text = "Software rendering (WARP) — diagnostics build required", AutoSize = true };
    readonly CheckBox console = new() { Text = "Xbox Series S mode", AutoSize = true };
    readonly RichTextBox log = new() { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(18, 22, 30), ForeColor = Color.FromArgb(198, 212, 228), Font = new Font("Consolas", 9), Dock = DockStyle.Fill, WordWrap = false };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(117, 222, 185), Margin = new Padding(0, 8, 0, 8) };
    readonly Label version = new() { AutoSize = true, ForeColor = Color.FromArgb(153, 166, 185), Margin = new Padding(0, 8, 0, 8) };
    readonly List<Control> actions = new();
    readonly Button stop;
    Process? process;
    StreamWriter? logFile;
    bool busy;

    public LauncherForm()
    {
        Text = "VIctory Launcher";
        Size = new Size(1000, 960);
        MinimumSize = new Size(900, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 31, 42);
        ForeColor = Color.FromArgb(232, 237, 245);
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 14 };
        for (int i = 0; i < 13; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "VIctory", Font = new Font("Segoe UI", 30, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) });
        layout.Controls.Add(new Label { Text = "WINDOWS LAUNCHER  /  EXPERIMENTAL", ForeColor = Color.FromArgb(117, 222, 185), AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
        layout.Controls.Add(new Label { Text = "Upstream: not playable yet. Menus work; world rendering is incomplete.\nHardware rendering can hang the GPU and reset the display driver. WARP is slow.", AutoSize = true, ForeColor = Color.FromArgb(244, 199, 126), Margin = new Padding(0, 0, 0, 16) });
        layout.Controls.Add(PathRow("GAME EXECUTABLE", game, "Select the game executable"));
        layout.Controls.Add(PathRow("PROJECT DATA FOLDER — caches, saves, engines and builds", projectFolder, "Choose an empty project folder", true));
        layout.Controls.Add(PathRow("VICTORY ENGINE", engine, "Select victory.exe"));
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        options.Controls.Add(warp);
        options.Controls.Add(console);
        actions.Add(warp); actions.Add(console);
        layout.Controls.Add(options);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        buttons.Controls.Add(ActionButton("Launch", async () => await RunEngine("run"), true));
        buttons.Controls.Add(ActionButton("Inspect EXE", async () => await RunEngine("inspect")));
        buttons.Controls.Add(ActionButton("Compute SHA-256", VerifyHash));
        buttons.Controls.Add(ActionButton("Build engine", BuildEngine));
        stop = ActionButton("Stop", Stop);
        actions.Remove(stop);
        stop.Enabled = false;
        buttons.Controls.Add(stop);
        layout.Controls.Add(buttons);
        var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        links.Controls.Add(ActionButton("Open logs / saves", () => { Directory.CreateDirectory(Program.StateDir); Process.Start(new ProcessStartInfo(Path.Combine(ProjectStorage.FindRoot(Program.StateDir), "launcher-data")) { UseShellExecute = true }); return Task.CompletedTask; }));
        links.Controls.Add(ActionButton("Project README", () => { Process.Start(new ProcessStartInfo("https://github.com/Marvisak/VIctory#readme") { UseShellExecute = true }); return Task.CompletedTask; }));
        layout.Controls.Add(links);
        var updates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
        updates.Controls.Add(ActionButton("Clone upstream", () => RunSetupAction("Clone-Upstream.ps1", "Clone upstream")));
        updates.Controls.Add(ActionButton("Prerequisites", () => RunSetupAction("Check-Prerequisites.ps1", "Prerequisite check")));
        updates.Controls.Add(ActionButton("Check updates", () => UpdateEngine("Check")));
        updates.Controls.Add(ActionButton("Update engine", () => UpdateEngine("Update")));
        updates.Controls.Add(ActionButton("Roll back", () => UpdateEngine("Rollback")));
        layout.Controls.Add(updates);
        version.Text = EngineInstall.VersionLabel;
        layout.Controls.Add(version);
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "SESSION OUTPUT", AutoSize = true, ForeColor = Color.FromArgb(153, 166, 185), Margin = new Padding(0, 4, 0, 8) });
        layout.Controls.Add(log);
        var saved = Settings.Load();
        game.Text = saved.GameExe; engine.Text = saved.EngineExe;
        projectFolder.Text = ProjectStorage.FindRoot(Program.StateDir);
        warp.Checked = saved.Warp; console.Checked = saved.SeriesS;
        status.Text = File.Exists(engine.Text) ? "Engine found · Ready to inspect or launch" : "Engine not built · Select Build engine to compile VIctory";
        Write("Choose the title EXE and VIctory engine. Launch runs the title inside VIctory.");
        Write("An EXE alone may be insufficient: game archives, DLLs and package metadata must be supplied separately.");
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; Write("A task is running. Wait for it to finish, or use Stop when available."); return; }
            try { Current().Save(); } catch (Exception error) { Write(error.Message); }
        };
    }

    Control PathRow(string label, TextBox field, string title, bool folder = false)
    {
        var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = field == projectFolder ? 3 : 2, Margin = new Padding(0, 0, 0, 10) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        if (field == projectFolder) panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(153, 166, 185), Margin = new Padding(0, 0, 0, 5) }, 0, 0);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 0)!, field == projectFolder ? 3 : 2);
        field.Dock = DockStyle.Fill; field.BackColor = Color.FromArgb(39, 46, 60); field.ForeColor = ForeColor; field.BorderStyle = BorderStyle.FixedSingle;
        field.Margin = new Padding(0, 4, 8, 0);
        panel.Controls.Add(field, 0, 1);
        var browse = ActionButton("Browse…", () =>
        {
            if (folder)
            {
                using var directory = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
                if (Directory.Exists(field.Text)) directory.SelectedPath = field.Text;
                if (directory.ShowDialog(this) == DialogResult.OK) field.Text = directory.SelectedPath;
                return Task.CompletedTask;
            }
            using var dialog = new OpenFileDialog { Title = title, Filter = "Executable (*.exe)|*.exe", CheckFileExists = true };
            if (File.Exists(field.Text)) dialog.FileName = field.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
            return Task.CompletedTask;
        });
        panel.Controls.Add(browse, 1, 1);
        if (field == projectFolder) panel.Controls.Add(ActionButton("Apply folder", ChangeProjectFolder), 2, 1);
        actions.Add(field);
        return panel;
    }

    Button ActionButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 5, 10, 5), BackColor = primary ? Color.FromArgb(117, 222, 185) : Color.FromArgb(43, 51, 67), ForeColor = primary ? Color.FromArgb(18, 31, 29) : ForeColor, Margin = new Padding(0, 0, 8, 4) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += async (_, _) => { try { await action(); } catch (Exception e) { Write("ERROR: " + e.Message); status.Text = "Task failed · See session output"; } };
        actions.Add(button);
        return button;
    }

    Settings Current() => new() { GameExe = game.Text.Trim(), EngineExe = engine.Text.Trim(), Warp = warp.Checked, SeriesS = console.Checked };

    void Write(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Write(text)); return; }
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        if (log.TextLength > 300_000) log.Clear();
        log.AppendText(line + Environment.NewLine);
        log.ScrollToCaret();
        logFile?.WriteLine(line);
    }

    async Task VerifyHash()
    {
        if (!File.Exists(game.Text.Trim())) throw new FileNotFoundException("Choose an existing game EXE.");
        SetBusy(true);
        try
        {
            var path = game.Text.Trim();
            Write("Hashing " + path);
            var hash = await Task.Run(() => { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); });
            Write("SHA-256: " + hash);
            status.Text = "SHA-256 calculated · Compare with a trusted value of your choice";
        }
        finally { SetBusy(false); }
    }

    async Task RunEngine(string command)
    {
        EnsureProjectFolderApplied();
        var settings = Current();
        if (!File.Exists(settings.GameExe)) throw new FileNotFoundException("Choose an existing game EXE.");
        if (!File.Exists(settings.EngineExe)) throw new FileNotFoundException("VIctory engine is missing. Build it first or browse to victory.exe.");
        if (Path.GetFullPath(settings.GameExe).Equals(Path.GetFullPath(settings.EngineExe), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select distinct game and VIctory executables.");
        if (command == "run" && settings.Warp && !Path.GetFullPath(settings.EngineExe).Equals(Path.GetFullPath(new Settings().EngineExe), StringComparison.OrdinalIgnoreCase))
            Write("WARP needs an engine built with --features diagnostics. A plain release build ignores this option.");
        settings.Save();
        await RunProcess(Program.EngineCommand(settings, command), command == "run" ? "Game session" : "Inspect EXE");
    }

    async Task BuildEngine()
    {
        EnsureProjectFolderApplied();
        var script = Path.Combine(Program.Root, "Build-VIctory.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Build-VIctory.ps1 is missing from the launcher folder.");
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        var result = await RunProcess(start, "Engine build");
        if (result == 0) { engine.Text = new Settings().EngineExe; Current().Save(); }
        version.Text = EngineInstall.VersionLabel;
    }

    async Task UpdateEngine(string action)
    {
        EnsureProjectFolderApplied();
        var script = Path.Combine(Program.Root, "Update-VIctory.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Update-VIctory.ps1 is missing from the launcher folder.");
        Current().Save();
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action }) start.ArgumentList.Add(arg);
        var result = await RunProcess(start, action == "Check" ? "Update check" : action == "Rollback" ? "Engine rollback" : "Engine update");
        if (result == 0 && action != "Check") { engine.Text = EngineInstall.CurrentExecutable; Current().Save(); }
        version.Text = EngineInstall.VersionLabel;
        if (result != 0) Write("The installed engine is unchanged. See output above for the failure.");
    }

    async Task RunSetupAction(string file, string label)
    {
        EnsureProjectFolderApplied();
        Current().Save();
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.GetFullPath(Path.Combine(Program.Root, "..", "tools", file)), "-ProjectRoot", Program.Root }) start.ArgumentList.Add(arg);
        await RunProcess(start, label);
    }

    void EnsureProjectFolderApplied()
    {
        if (!Path.IsPathFullyQualified(projectFolder.Text.Trim()) ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder.Text.Trim())).Equals(Path.TrimEndingDirectorySeparator(ProjectStorage.FindRoot(Program.StateDir)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Click Apply folder to save the project folder change before continuing.");
    }

    async Task ChangeProjectFolder()
    {
        var command = ProjectStorage.ChangeCommand(Program.Root, projectFolder.Text.Trim());
        Current().Save();
        // A migration must not hold open or append to a log inside the folder being copied.
        var result = await RunProcess(command, "Project folder move", saveLog: false);
        projectFolder.Text = ProjectStorage.FindRoot(Program.StateDir);
        if (result == 0) Write("Project data folder: " + projectFolder.Text);
    }

    async Task<int> RunProcess(ProcessStartInfo start, string task, bool saveLog = true)
    {
        SetBusy(true);
        try
        {
            if (saveLog)
            {
                Directory.CreateDirectory(Path.Combine(Program.StateDir, "logs"));
                logFile = new StreamWriter(Path.Combine(Program.StateDir, "logs", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.log")) { AutoFlush = true };
            }
            Write(task + " starting");
            status.Text = task + " running…";
            process = new Process { StartInfo = start };
            process.Start(); stop.Enabled = saveLog;
            var stdout = Pump(process.StandardOutput);
            var stderr = Pump(process.StandardError);
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            var code = process.ExitCode;
            status.Text = $"{task} exited with code {code}";
            Write(status.Text);
            return code;
        }
        finally
        {
            process?.Dispose(); process = null;
            logFile?.Dispose(); logFile = null;
            SetBusy(false);
        }
    }

    async Task Pump(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) Write(line);
    }

    Task Stop()
    {
        if (process is { HasExited: false })
        {
            if (MessageBox.Show(this, "Stop this task and its child processes? Unsaved game progress will be lost.", "Stop task", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { process.Kill(true); Write("Stop requested."); }
        }
        return Task.CompletedTask;
    }

    void SetBusy(bool value)
    {
        busy = value;
        foreach (var control in actions) control.Enabled = !value;
        stop.Enabled = false;
        UseWaitCursor = value;
    }
}

internal static class SelfTest
{
    internal static int Run()
    {
        Directory.CreateDirectory(Program.StateDir);
        var resultFile = Path.Combine(Program.StateDir, "self-test.txt");
        try
        {
            void Check(bool value, string message) { if (!value) throw new Exception(message); }
            var settings = new Settings { GameExe = @"D:\folder with spaces\game.exe", EngineExe = Environment.ProcessPath!, Warp = true, SeriesS = true };
            var run = Program.EngineCommand(settings, "run");
            Check(run.ArgumentList.SequenceEqual(new[] { "run", settings.GameExe, "--execute", "--console", "series-s" }), "Launch arguments");
            Check(!run.UseShellExecute && run.Environment["VICTORY_D3D12_WARP"] == "1", "Environment / direct process launch");
            Check(!Program.EngineCommand(settings, "inspect").ArgumentList.Contains("--execute"), "Inspect must not execute title");
            settings.Warp = false;
            Check(!Program.EngineCommand(settings, "run").Environment.ContainsKey("VICTORY_D3D12_WARP"), "Hardware mode must clear WARP");
            var probe = Path.Combine(Program.StateDir, "mock result with spaces.json");
            run.WorkingDirectory = Program.Root;
            run.ArgumentList.Clear();
            foreach (var arg in new[] { "--mock-engine", probe, settings.GameExe }) run.ArgumentList.Add(arg);
            using var process = Process.Start(run)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(); Task.WaitAll(stdout, stderr);
            Check(process.ExitCode == 7, "Exit code propagation");
            using var json = JsonDocument.Parse(File.ReadAllText(probe));
            Check(json.RootElement.GetProperty("args")[2].GetString() == settings.GameExe, "Paths containing spaces must survive process invocation");
            Check(json.RootElement.GetProperty("warp").GetString() == "1", "Child environment");
            File.Delete(probe);
            using var form = new LauncherForm();
            form.Show(); Application.DoEvents();
            Check(form.Visible && form.Controls.Count > 0, "Form initialization");
            form.Close();
            File.WriteAllText(resultFile, "PASS: launch arguments, inspect isolation, WARP environment, process invocation with spaces, exit codes, GUI initialization.");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(resultFile, e.ToString()); return 1; }
    }
}
