using System.Diagnostics;

internal static class FirstRun
{
    internal static bool Ensure(string root, string project, string? previewPath = null)
    {
        if (previewPath is null && Directory.Exists(Path.Combine(root, "launcher-data")) && Directory.Exists(Path.Combine(root, "Source"))) return true;
        using var dialog = new Form { Text = project + " — First-time setup", ClientSize = new Size(700, 340), StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 10), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dialog.Controls.Add(layout);
        var intro = new Label { Text = "Experimental launcher / build manager\nChoose where this project's source, caches, tools and saves will be stored.\nNo game files or upstream engines are included in this release.", AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        layout.Controls.Add(intro); layout.SetColumnSpan(intro, 2);
        var game = new TextBox { Dock = DockStyle.Fill };
        var data = new TextBox { Dock = DockStyle.Fill };
        void Row(string label, TextBox field, bool folder)
        {
            var title = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            layout.Controls.Add(title); layout.SetColumnSpan(title, 2);
            layout.Controls.Add(field);
            var browse = new Button { Text = "Browse…", AutoSize = true };
            layout.Controls.Add(browse);
            browse.Click += (_, _) =>
            {
                if (folder) { using var picker = new FolderBrowserDialog { Description = "Choose an empty data folder", UseDescriptionForTitle = true }; if (picker.ShowDialog(dialog) == DialogResult.OK) data.Text = picker.SelectedPath; }
                else { using var picker = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", CheckFileExists = true }; if (picker.ShowDialog(dialog) == DialogResult.OK) game.Text = picker.FileName; }
            };
        }
        Row("Game executable (optional for initial setup and research checks)", game, false);
        Row("Project data folder (required; choose an empty folder on a drive with space)", data, true);
        var create = new Button { Text = "Create project folders", AutoSize = true, Padding = new Padding(8), Margin = new Padding(0, 16, 0, 0) };
        layout.Controls.Add(create);
        create.Click += (_, _) =>
        {
            try
            {
                if (!Path.IsPathFullyQualified(data.Text.Trim())) throw new InvalidOperationException("Choose an absolute project data folder.");
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.GetFullPath(Path.Combine(root, "..", "tools", "Initialize-Project.ps1")), "-ProjectRoot", root, "-DataRoot", data.Text.Trim() }) start.ArgumentList.Add(arg);
                if (!string.IsNullOrWhiteSpace(game.Text)) { start.ArgumentList.Add("-GameExe"); start.ArgumentList.Add(game.Text.Trim()); }
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit(); Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new InvalidOperationException(error.Result);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, "Setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        if (previewPath is not null)
        {
            dialog.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(previewPath);
            return true;
        }
        return dialog.ShowDialog() == DialogResult.OK;
    }
}
