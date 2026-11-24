using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LeonidaLauncher;

internal static class Program
{
    internal static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    internal static readonly string StateDir = Path.Combine(Root, "launcher-data");

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 1 && args[0] == "--pause-worker") return ShaderControlTests.Worker(args);
        if (args.Contains("--control-tests")) return ShaderControlTests.Run(StateDir);
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--mock-engine"))
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { args, game = Environment.GetEnvironmentVariable("LEONIDA_SAVE_DIR"), warp = Environment.GetEnvironmentVariable("LEONIDA_D3D12") }));
            Console.WriteLine("mock stdout");
            Console.Error.WriteLine("mock stderr");
            return 7;
        }
        if (args.Length == 2 && args[0] == "--preview-first-run")
        {
            FirstRun.Ensure(Program.Root, "Leonida", args[1]);
            return 0;
        }
        if (!args.Contains("--preview") && !FirstRun.Ensure(Program.Root, "Leonida")) return 0;
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
        if (command is not ("run" or "inspect")) throw new ArgumentException("Unsupported Leonida command.");
        if (settings.Backend == "Integrated") return IntegratedBackend.Command(settings, command);
        var exe = Path.GetFullPath(settings.GameExe);
        var start = new ProcessStartInfo(Path.GetFullPath(settings.EngineExe))
        {
            WorkingDirectory = StateDir, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("LEONIDA_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("XCL_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["LEONIDA_SAVE_DIR"] = Path.Combine(StateDir, "saves");
        if (settings.Vulkan) start.Environment["LEONIDA_D3D12"] = "vulkan";
        if (!string.IsNullOrWhiteSpace(settings.ShaderDir)) start.Environment["LEONIDA_SHADERS"] = Path.GetFullPath(settings.ShaderDir);
        start.ArgumentList.Add(command == "run" ? "--run" : "--dry-run");
        start.ArgumentList.Add(exe);
        start.ArgumentList.Add("--game-dir");
        start.ArgumentList.Add(Path.GetDirectoryName(exe)!);
        start.ArgumentList.Add("--overlay");
        start.ArgumentList.Add(Path.Combine(StateDir, "overlay"));
        return start;
    }

}

internal sealed class Settings
{
    public string Backend { get; set; } = "Native";
    public string NativeEngineExe { get; set; } = "";
    public string IntegratedEngineExe { get; set; } = "";
    public bool Warp { get; set; }
    public bool SeriesSMenuPicture { get; set; }
    public string GameExe { get; set; } = "";
    public string EngineExe { get; set; } = EngineInstall.CurrentExecutable;
    public bool Vulkan { get; set; } = true;
    public string ShaderDir { get; set; } = "";
    internal static string FilePath => Path.Combine(Program.StateDir, "settings.json");
    internal static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            if (settings.Backend == "Integrated")
                settings.EngineExe = string.IsNullOrWhiteSpace(settings.IntegratedEngineExe) || EngineInstall.IsManaged(settings.IntegratedEngineExe) ? IntegratedBackend.CurrentExecutable : settings.IntegratedEngineExe;
            else if (EngineInstall.IsManaged(settings.EngineExe)) settings.EngineExe = EngineInstall.CurrentExecutable;
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
    internal static readonly string LegacyExecutable = Path.Combine(Program.Root, "build", "gta6rt.exe");
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
    readonly TextBox game = new(), engine = new(), shaders = new(), projectFolder = new();
    readonly CheckBox vulkan = new() { Text = "Vulkan renderer (requires translated SPIR-V shaders)", AutoSize = true };
    readonly ComboBox backend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 };
    readonly CheckBox warp = new() { Text = "Software rendering (WARP, slower)", AutoSize = true };
    readonly CheckBox menuPicture = new() { Text = "Series S menu presentation workaround (diagnostic)", AutoSize = true };
    string nativeEngine = "", integratedEngine = "";
    bool Integrated => backend.SelectedIndex == 0;
    string BackendName => Integrated ? "Integrated" : "Native";
    readonly RichTextBox log = new() { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(18, 22, 30), ForeColor = Color.FromArgb(198, 212, 228), Font = new Font("Consolas", 9), Dock = DockStyle.Fill, WordWrap = false };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(117, 222, 185), Margin = new Padding(0, 8, 0, 8) };
    readonly Label version = new() { AutoSize = true, ForeColor = Color.FromArgb(153, 166, 185), Margin = new Padding(0, 8, 0, 8) };
    readonly List<Control> actions = new();
    readonly Button stop;
    readonly Button launch;
    readonly Button prepareShaders;
    readonly Button pause;
    readonly Label preparationNotice = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(255, 210, 132), Margin = new Padding(0, 0, 0, 5) };
    readonly Label preparationDetail = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
    Process? process;
    ShaderProcessControl? shaderControl;
    StreamWriter? logFile;
    bool busy;
    string runningTask = "";

    public LauncherForm()
    {
        Text = "Leonida Launcher";
        Size = new Size(1000, 1020);
        MinimumSize = new Size(900, 940);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 31, 42);
        ForeColor = Color.FromArgb(232, 237, 245);
        Font = new Font("Segoe UI", 10);
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(viewport);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 1120, Padding = new Padding(22), ColumnCount = 1, RowCount = 14 };
        for (int i = 0; i < 13; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        viewport.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Leonida", Font = new Font("Segoe UI", 26, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) });
        var preparationBanner = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.FromArgb(57, 44, 29), Padding = new Padding(14), Margin = new Padding(0, 4, 0, 16) };
        preparationBanner.Controls.Add(preparationNotice);
        preparationBanner.Controls.Add(preparationDetail);
        var preparation = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        prepareShaders = ActionButton("Generate shaders first", () => ShaderSetup("Prepare"), true);
        prepareShaders.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        preparation.Controls.Add(prepareShaders);
        pause = ActionButton("Pause", PauseOrResume);
        actions.Remove(pause);
        pause.Enabled = false;
        preparation.Controls.Add(pause);
        preparation.Controls.Add(ActionButton("Install / repair shader tools", () => ShaderSetup("InstallTools")));
        preparationBanner.Controls.Add(preparation);
        layout.Controls.Add(preparationBanner);
        layout.Controls.Add(PathRow("GAME EXECUTABLE", game, "Select the game executable"));
        layout.Controls.Add(PathRow("PROJECT DATA FOLDER — caches, saves, engines and builds", projectFolder, "Choose an empty project folder", true));
        layout.Controls.Add(PathRow("SELECTED BACKEND ENGINE", engine, "Select the chosen backend's runtime executable"));
        layout.Controls.Add(PathRow("VULKAN SHADERS (NATIVE BACKEND ONLY)", shaders, "Choose the SPIR-V catalog folder", true));
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        backend.Items.AddRange(new object[] { "Integrated VIctory / native D3D12 — recent fork progress", "Native gta6rt / Vulkan — older shader workflow" });
        options.Controls.Add(backend);
        options.Controls.Add(warp);
        options.Controls.Add(menuPicture);
        actions.Add(backend); actions.Add(warp); actions.Add(menuPicture);
        options.Controls.Add(vulkan);
        actions.Add(vulkan);
        layout.Controls.Add(options);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        launch = ActionButton("3. Launch", async () => await RunEngine("run"), true);
        buttons.Controls.Add(launch);
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
        links.Controls.Add(ActionButton("Project README", () => { Process.Start(new ProcessStartInfo("https://github.com/tudorek12345/Leonida#readme") { UseShellExecute = true }); return Task.CompletedTask; }));
        links.Controls.Add(ActionButton("Delete Vulkan shader cache…", DeleteShaderCache));
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
        nativeEngine = saved.Backend == "Native" ? saved.EngineExe : saved.NativeEngineExe;
        integratedEngine = saved.Backend == "Integrated" ? saved.EngineExe : saved.IntegratedEngineExe;
        backend.SelectedIndex = saved.Backend == "Integrated" ? 0 : 1;
        warp.Checked = saved.Warp; menuPicture.Checked = saved.SeriesSMenuPicture;
        backend.SelectedIndexChanged += (_, _) =>
        {
            if (Integrated) { nativeEngine = engine.Text; engine.Text = string.IsNullOrWhiteSpace(integratedEngine) ? IntegratedBackend.CurrentExecutable : integratedEngine; }
            else { integratedEngine = engine.Text; engine.Text = string.IsNullOrWhiteSpace(nativeEngine) ? EngineInstall.CurrentExecutable : nativeEngine; }
            RefreshPreparation();
        };
        projectFolder.Text = ProjectStorage.FindRoot(Program.StateDir);
        vulkan.Checked = saved.Vulkan; shaders.Text = saved.ShaderDir;
        RefreshPreparation();
        status.Text = Integrated ? "Integrated VIctory selected · Build engine, then try Launch" : ShaderCatalog.IsReady(shaders.Text) ? "Shader catalog found · Ready to try launching" : ShaderToolsReady ? "Shader tools installed · Click Generate shaders first above" : "Shaders not prepared · Install shader tools, then generate shaders";
        Write("Choose Integrated VIctory for the recent fork progress, or Native gta6rt for the older Vulkan workflow. Build/Update/Roll back apply to the selected backend.");
        Write("Tools, shader work and logs use your project folder. The asset export is not run.");
        shaders.TextChanged += (_, _) => RefreshPreparation();
        engine.TextChanged += (_, _) => RefreshPreparation();
        game.TextChanged += (_, _) => RefreshPreparation();
        vulkan.CheckedChanged += (_, _) => RefreshPreparation();
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

    Settings Current() => new() { GameExe = game.Text.Trim(), EngineExe = engine.Text.Trim(), Vulkan = vulkan.Checked, ShaderDir = shaders.Text.Trim(), Backend = BackendName, Warp = warp.Checked, SeriesSMenuPicture = menuPicture.Checked, NativeEngineExe = Integrated ? nativeEngine : engine.Text.Trim(), IntegratedEngineExe = Integrated ? engine.Text.Trim() : integratedEngine };

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
            status.Text = "SHA-256 computed";
            Write("Compare this value with a trusted source.");
        }
        finally { SetBusy(false); }
    }

    async Task RunEngine(string command)
    {
        EnsureProjectFolderApplied();
        var settings = Current();
        if (!File.Exists(settings.GameExe)) throw new FileNotFoundException("Choose an existing game EXE.");
        if (!File.Exists(settings.EngineExe)) throw new FileNotFoundException("Selected backend engine is missing. Click Build engine first.");
        if (Path.GetFullPath(settings.GameExe).Equals(Path.GetFullPath(settings.EngineExe), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select distinct game and Leonida executables.");
        if (!Integrated && !File.Exists(Path.Combine(Path.GetDirectoryName(settings.EngineExe)!, "gta6rt_tls_host.dll"))) throw new FileNotFoundException("gta6rt_tls_host.dll must be beside the native runtime. Build the engine first.");
        if (!Integrated && command == "run" && settings.Vulkan && !ShaderCatalog.IsReady(settings.ShaderDir)) throw new InvalidOperationException("Prepare shaders first, or choose an existing translated shader catalog. An empty folder is not sufficient.");
        Directory.CreateDirectory(Program.StateDir);
        settings.Save();
        await RunProcess(Program.EngineCommand(settings, command), command == "run" ? "Game session" : "Inspect EXE");
    }

    async Task BuildEngine()
    {
        EnsureProjectFolderApplied();
        var script = Path.Combine(Program.Root, "Build-Leonida.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Build-Leonida.ps1 is missing from the launcher folder.");
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("-Backend"); start.ArgumentList.Add(BackendName);
        var result = await RunProcess(start, "Engine build");
        if (result == 0) { engine.Text = Integrated ? IntegratedBackend.CurrentExecutable : EngineInstall.CurrentExecutable; Current().Save(); }
        version.Text = Integrated ? "Selected backend: integrated VIctory" : EngineInstall.VersionLabel;
    }

    async Task UpdateEngine(string action)
    {
        EnsureProjectFolderApplied();
        var script = Path.Combine(Program.Root, "Update-Leonida.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Update-Leonida.ps1 is missing from the launcher folder.");
        Current().Save();
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action }) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("-Backend"); start.ArgumentList.Add(BackendName);
        var result = await RunProcess(start, action == "Check" ? "Update check" : action == "Rollback" ? "Engine rollback" : "Engine update");
        if (result == 0 && action != "Check") { engine.Text = Integrated ? IntegratedBackend.CurrentExecutable : EngineInstall.CurrentExecutable; Current().Save(); }
        version.Text = Integrated ? "Selected backend: integrated VIctory" : EngineInstall.VersionLabel;
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
        if (result == 0) shaders.Text = Settings.Load().ShaderDir;
        if (result == 0) Write("Project data folder: " + projectFolder.Text);
    }

    async Task<int> RunProcess(ProcessStartInfo start, string task, bool saveLog = true, bool canPause = false)
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
            runningTask = task;
            RefreshPreparation();
            status.Text = task + " running…";
            process = new Process { StartInfo = start };
            process.Start(); stop.Enabled = saveLog;
            if (canPause)
            {
                try { shaderControl = new ShaderProcessControl(process); }
                catch { if (!process.HasExited) process.Kill(true); throw; }
            }
            RefreshPreparation();
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
            shaderControl?.Dispose(); shaderControl = null;
            process?.Dispose(); process = null;
            runningTask = "";
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
            var message = runningTask.StartsWith("Shader", StringComparison.Ordinal) ? "Stop shader setup? Completed shader stages are retained; unfinished work may need repeating." : "Stop this task and its child processes? Unsaved game progress will be lost.";
            if (MessageBox.Show(this, message, "Stop task", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { if (shaderControl is not null) shaderControl.Stop(); else process.Kill(true); Write("Stop requested."); }
        }
        return Task.CompletedTask;
    }

    void SetBusy(bool value)
    {
        busy = value;
        foreach (var control in actions) control.Enabled = !value;
        stop.Enabled = false;
        UseWaitCursor = value;
        RefreshPreparation();
    }

    string ShaderToolsBin => Path.Combine(ProjectStorage.FindRoot(Program.StateDir), "tools", "shader-env", "Library", "bin");
    bool ShaderToolsReady => new[] { "llvm-mc.exe", "spirv-as.exe", "spirv-val.exe" }.All(name => File.Exists(Path.Combine(ShaderToolsBin, name)));

    void RefreshPreparation()
    {
        if (prepareShaders.Parent is not null) prepareShaders.Parent.Visible = !Integrated;
        if (shaders.Parent is not null) shaders.Parent.Visible = !Integrated;
        vulkan.Visible = !Integrated;
        backend.Enabled = !busy;
        warp.Enabled = menuPicture.Enabled = !busy && Integrated;
        vulkan.Enabled = shaders.Enabled = !busy && !Integrated;
        if (Integrated)
        {
            prepareShaders.Enabled = false; pause.Enabled = false;
            launch.Enabled = !busy && File.Exists(engine.Text.Trim()); launch.Text = "Launch integrated VIctory";
            preparationNotice.Text = File.Exists(engine.Text.Trim()) ? "INTEGRATED VICTORY — READY TO TRY" : "BUILD INTEGRATED VICTORY FIRST";
            preparationDetail.Text = "Recent fork progress uses native D3D12, with the Series S profile.\nShaders compile during use into this project's separate cache. Vulkan shader generation is not required.";
            version.Text = "Selected backend: integrated VIctory · " + Path.GetFileName(Path.GetDirectoryName(engine.Text));
            UseWaitCursor = busy;
            return;
        }
        var toolsReady = ShaderToolsReady;
        var shadersReady = ShaderCatalog.IsReady(shaders.Text.Trim());
        version.Text = EngineInstall.VersionLabel + "  ·  Tools: " + (toolsReady ? "installed" : "missing") + "  ·  Shaders: " + (shadersReady ? "catalog found" : "not prepared");
        launch.Enabled = !busy && File.Exists(engine.Text.Trim()) && (!vulkan.Checked || shadersReady);
        prepareShaders.Enabled = !busy && toolsReady && File.Exists(game.Text.Trim());
        var needsShaders = vulkan.Checked && !shadersReady;
        var paused = shaderControl?.IsPaused == true;
        pause.Text = paused ? "Resume" : "Pause";
        pause.Enabled = shaderControl is not null && process is { HasExited: false };
        UseWaitCursor = busy && runningTask != "Shader preparation";
        preparationNotice.Text = paused ? "SHADER GENERATION PAUSED" : runningTask == "Shader preparation" ? "GENERATING SHADERS — please wait" : needsShaders ? "FIRST RUN: GENERATE SHADERS BEFORE LAUNCHING" : shadersReady ? "SHADERS READY — you can launch" : "HOST D3D12 SELECTED";
        preparationNotice.ForeColor = needsShaders ? Color.FromArgb(255, 210, 132) : Color.FromArgb(117, 222, 185);
        preparationDetail.Text = paused ? "Click Resume to continue from the same point. Paused workers keep their memory.\nStop ends this run; completed stages can be reused later." : needsShaders
            ? "Vulkan cannot launch until you generate the game's shaders.\n" + (toolsReady ? "Tools are installed. Click Generate shaders first below. Build / Update engine does not do this." : "Install shader tools first, then generate shaders. Build / Update engine does not do this.")
            : shadersReady ? "The shader catalog is present. You can try launching; gameplay remains experimental." : "Shader generation is required when you enable the Vulkan renderer.";
        prepareShaders.Text = needsShaders ? "Generate shaders first" : "Regenerate / resume shaders";
        prepareShaders.BackColor = needsShaders ? Color.FromArgb(117, 222, 185) : Color.FromArgb(43, 51, 67);
        prepareShaders.ForeColor = needsShaders ? Color.FromArgb(18, 31, 29) : ForeColor;
        launch.Text = needsShaders ? "Launch locked — generate shaders first" : "Launch";
        launch.BackColor = needsShaders ? Color.FromArgb(43, 51, 67) : Color.FromArgb(117, 222, 185);
    }

    async Task ShaderSetup(string action)
    {
        EnsureProjectFolderApplied();
        Current().Save();
        var script = Path.Combine(Program.Root, "Shader-Setup.ps1");
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action, "-GameExe", game.Text.Trim() }) start.ArgumentList.Add(arg);
        var result = await RunProcess(start, action == "Prepare" ? "Shader preparation" : "Shader tools setup", canPause: action == "Prepare");
        if (result == 0 && action == "Prepare")
        {
            shaders.Text = Path.Combine(ProjectStorage.FindRoot(Program.StateDir), "work", "shaders", "spirv");
            Current().Save();
        }
        RefreshPreparation();
        if (result == 0 && action == "InstallTools") status.Text = "Shader tools installed · Click Generate shaders first above";
        if (result == 0 && action == "Prepare") status.Text = "Shader preparation finished · You can try Launch";
    }

    Task PauseOrResume()
    {
        if (shaderControl is null || process is not { HasExited: false }) return Task.CompletedTask;
        if (shaderControl.IsPaused) { shaderControl.Resume(); status.Text = "Shader generation resumed"; }
        else { shaderControl.Pause(); status.Text = "Shader generation paused · Click Resume to continue"; }
        Write(status.Text);
        RefreshPreparation();
        return Task.CompletedTask;
    }

    async Task DeleteShaderCache()
    {
        EnsureProjectFolderApplied();
        if (busy) throw new InvalidOperationException("Stop the current task before deleting its cache.");
        var cache = Path.Combine(ProjectStorage.FindRoot(Program.StateDir), "work", "shaders");
        if (MessageBox.Show(this, "Permanently delete Leonida's generated shader cache?\n\n" + cache + "\n\nThis removes extracted shaders, disassembly and translated SPIR-V. You must generate them again.\n\nGame files, saves, overlay, tools and installed engines are kept.", "Delete shader cache?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Program.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Program.Root, "Delete-ShaderCache.ps1"), "-Confirmed" }) start.ArgumentList.Add(arg);
        var result = await RunProcess(start, "Shader cache deletion", canPause: false);
        RefreshPreparation();
        if (result == 0) status.Text = "Shader cache deleted · Generate shaders before using Vulkan";
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
            var catalogFixture = Path.Combine(Program.StateDir, "shader-catalog-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(catalogFixture);
            try
            {
                Check(!ShaderCatalog.IsReady(catalogFixture), "Empty shader folder is not ready");
                File.WriteAllText(Path.Combine(catalogFixture, "index.json"), "{}");
                File.WriteAllBytes(Path.Combine(catalogFixture, "synthetic.spv"), new byte[] { 3, 2, 35, 7, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 });
                Check(!ShaderCatalog.IsReady(catalogFixture), "Empty shader index is not ready");
                File.WriteAllText(Path.Combine(catalogFixture, "index.json"), "{\"synthetic\":{}}");
                Check(ShaderCatalog.IsReady(catalogFixture), "Recognize a populated shader catalog");
                File.WriteAllBytes(Path.Combine(catalogFixture, "synthetic.spv"), new byte[20]);
                Check(!ShaderCatalog.IsReady(catalogFixture), "Reject non-SPIR-V file");
            }
            finally { foreach (var file in Directory.EnumerateFiles(catalogFixture)) File.Delete(file); Directory.Delete(catalogFixture); }
            var settings = new Settings { GameExe = @"D:\folder with spaces\game.exe", EngineExe = Environment.ProcessPath!, Vulkan = true, ShaderDir = @"D:\shader folder" };
            var run = Program.EngineCommand(settings, "run");
            Check(run.ArgumentList.SequenceEqual(new[] { "--run", settings.GameExe, "--game-dir", Path.GetDirectoryName(settings.GameExe)!, "--overlay", Path.Combine(Program.StateDir, "overlay") }), "Native runtime arguments / external overlay");
            Check(run.Environment["LEONIDA_D3D12"] == "vulkan" && run.Environment["LEONIDA_SHADERS"] == settings.ShaderDir, "Vulkan and shader selection");
            var inspect = Program.EngineCommand(settings, "inspect");
            Check(inspect.ArgumentList[0] == "--dry-run" && !inspect.ArgumentList.Contains("--run") && !inspect.ArgumentList.Contains("--init"), "Inspection must not initialize or execute the game");
            Check(inspect.WorkingDirectory == Program.StateDir, "Runtime working directory must stay outside game folder");
            settings.Vulkan = false;
            Check(!Program.EngineCommand(settings, "run").Environment.ContainsKey("LEONIDA_D3D12"), "Host renderer selection");
            settings.Backend = "Integrated"; settings.Warp = true; settings.SeriesSMenuPicture = true;
            var integrated = Program.EngineCommand(settings, "run");
            Check(integrated.ArgumentList.SequenceEqual(new[] { "run", settings.GameExe, "--unimplemented", "trap", "--execute", "--console", "series-s" }), "Integrated VIctory launch arguments");
            Check(integrated.Environment["VICTORY_D3D12_WARP"] == "1" && integrated.Environment["VICTORY_SERIES_S_MENU_PICTURE"] == "1", "Explicit integrated diagnostics");
            Check(integrated.Environment["VICTORY_CACHE_DIR"] == Path.Combine(Program.StateDir, "integrated-victory", "cache") && !integrated.Environment.ContainsKey("LEONIDA_SHADERS"), "Separate integrated cache and no Vulkan shader inheritance");
            Check(Program.EngineCommand(settings, "inspect").ArgumentList.SequenceEqual(new[] { "inspect", settings.GameExe }), "Integrated inspection does not execute");
            settings.Backend = "Native";
            var probe = Path.Combine(Program.StateDir, "mock result with spaces.json");
            run.ArgumentList.Clear();
            foreach (var arg in new[] { "--mock-engine", probe, settings.GameExe }) run.ArgumentList.Add(arg);
            using var process = Process.Start(run)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(); Task.WaitAll(stdout, stderr);
            Check(process.ExitCode == 7, "Exit code propagation");
            using var json = JsonDocument.Parse(File.ReadAllText(probe));
            Check(json.RootElement.GetProperty("args")[2].GetString() == settings.GameExe, "Paths containing spaces");
            Check(json.RootElement.GetProperty("warp").GetString() == "vulkan", "Child renderer environment");
            Check(json.RootElement.GetProperty("game").GetString() == Path.Combine(Program.StateDir, "saves"), "Child saves directory");
            File.Delete(probe);
            using var form = new LauncherForm();
            form.Show(); Application.DoEvents();
            Check(form.Visible && form.Controls.Count > 0, "Form initialization");
            form.Close();
            File.WriteAllText(resultFile, "PASS: shader readiness, native launch arguments, external overlay/saves, inspection isolation, Vulkan/shader settings, process paths with spaces, exit codes, GUI initialization.");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(resultFile, e.ToString()); return 1; }
    }
}
