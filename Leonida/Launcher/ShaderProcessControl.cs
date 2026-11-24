using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Owns only the preparation process started by this launcher and its children.
// The job kills the tree if the launcher exits unexpectedly, including while paused.
internal sealed class ShaderProcessControl : IDisposable
{
    readonly Process root;
    readonly List<IntPtr> suspended = new();
    readonly HashSet<int> threadIds = new();
    IntPtr job;
    internal bool IsPaused { get; private set; }

    internal ShaderProcessControl(Process process)
    {
        root = process;
        job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        var info = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job, root.Handle))
        {
            var error = new Win32Exception();
            CloseHandle(job); job = IntPtr.Zero;
            throw error;
        }
    }

    internal void Pause()
    {
        if (IsPaused || root.HasExited) return;
        try
        {
            var frozen = new HashSet<int>();
            var family = new HashSet<int> { root.Id };
            for (int pass = 0; pass < 12; pass++)
            {
                // Freeze parents before children, then rescan to catch children that
                // were created while an existing child was still running.
                var parents = ProcessParents();
                bool added;
                do
                {
                    added = false;
                    foreach (var pair in parents)
                        if (family.Contains(pair.Value) && family.Add(pair.Key)) added = true;
                } while (added);
                var pending = family.Where(id => !frozen.Contains(id)).ToArray();
                if (pending.Length == 0) { IsPaused = true; return; }
                foreach (var id in pending)
                {
                    using var process = TryProcess(id);
                    if (process is not null && process.StartTime >= root.StartTime) SuspendThreads(process);
                    frozen.Add(id);
                }
            }
            throw new InvalidOperationException("The preparation tree kept changing; pause was cancelled and all threads were resumed.");
        }
        catch { Resume(); throw; }
    }

    void SuspendThreads(Process process)
    {
        for (int pass = 0; pass < 12; pass++)
        {
            process.Refresh();
            if (process.HasExited) return;
            bool added = false;
            foreach (ProcessThread thread in process.Threads)
            {
                using (thread)
                {
                    if (threadIds.Contains(thread.Id)) continue;
                    var handle = OpenThread(0x0802, false, (uint)thread.Id); // QUERY_LIMITED_INFORMATION | SUSPEND_RESUME
                    if (handle == IntPtr.Zero)
                    {
                        if (Marshal.GetLastWin32Error() == 87) continue; // thread exited
                        throw new Win32Exception();
                    }
                    if (GetProcessIdOfThread(handle) != process.Id || !GetExitCodeThread(handle, out var code) || code != 259)
                    { CloseHandle(handle); continue; }
                    if (SuspendThread(handle) == uint.MaxValue)
                    { var error = new Win32Exception(); CloseHandle(handle); throw error; }
                    suspended.Add(handle); threadIds.Add(thread.Id); added = true;
                }
            }
            if (!added) return;
        }
        throw new InvalidOperationException("Could not pause every worker thread.");
    }

    internal void Resume()
    {
        Exception? error = null;
        for (int i = suspended.Count - 1; i >= 0; i--)
        {
            var handle = suspended[i];
            if (ResumeThread(handle) == uint.MaxValue && GetExitCodeThread(handle, out var code) && code == 259)
                error ??= new Win32Exception();
            CloseHandle(handle);
        }
        suspended.Clear(); threadIds.Clear(); IsPaused = false;
        if (error is not null) throw error;
    }

    internal void Stop()
    {
        if (job != IntPtr.Zero && !TerminateJobObject(job, 1)) throw new Win32Exception();
        Resume();
    }

    public void Dispose()
    {
        // Closing the job terminates any remaining children. Handles held for a
        // paused worker are then released without leaving suspended orphans.
        if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
        foreach (var handle in suspended) CloseHandle(handle);
        suspended.Clear(); threadIds.Clear(); IsPaused = false;
    }

    static Process? TryProcess(int id)
    {
        try { return Process.GetProcessById(id); }
        catch (ArgumentException) { return null; }
    }

    static Dictionary<int, int> ProcessParents()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var result = new Dictionary<int, int>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = "" };
            if (!Process32First(snapshot, ref entry)) throw new Win32Exception();
            do { result[(int)entry.Id] = (int)entry.ParentId; } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr Heap;
        public uint Module, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetProcessIdOfThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
}
