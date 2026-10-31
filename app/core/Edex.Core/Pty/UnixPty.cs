// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Edex.Core.Pty;

/// <summary>Pseudo terminal for Linux and macOS based on forkpty(3).</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class UnixPty : IPtyConnection
{
    private readonly int _pid;
    private readonly int _master;
    private readonly FileStream _output;
    private readonly FileStream _input;
    private int _disposed;

    public int ProcessId => _pid;
    public Stream Output => _output;
    public Stream Input => _input;
    public int? ExitCode { get; private set; }
    public event EventHandler<int>? Exited;

    private UnixPty(int pid, int master)
    {
        _pid = pid;
        _master = master;
        _output = new FileStream(new SafeFileHandle((IntPtr)master, ownsHandle: true), FileAccess.Read, 1, false);
        _input = new FileStream(new SafeFileHandle((IntPtr)dup(master), ownsHandle: true), FileAccess.Write, 1, false);

        var waiter = new Thread(() =>
        {
            int status;
            while (waitpid(_pid, out status, 0) == -1 && Marshal.GetLastPInvokeError() == EINTR) { }
            int code = (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
            ExitCode = code;
            Exited?.Invoke(this, code);
        })
        { IsBackground = true, Name = "pty-exit-watcher" };
        waiter.Start();
    }

    public static UnixPty Spawn(PtyOptions options)
    {
        string executable = ResolveExecutable(options.Shell);
        var env = PtyFactory.BuildEnvironment(options.Environment);
        string cwd = !string.IsNullOrEmpty(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory)
            ? options.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Everything the child needs is marshalled before fork: after fork only async-signal-safe calls are made.
        var allocations = new List<IntPtr>();
        IntPtr Str(string s) { var p = Marshal.StringToHGlobalAnsi(s); allocations.Add(p); return p; }
        IntPtr Array(IEnumerable<string> items)
        {
            var pointers = items.Select(Str).Append(IntPtr.Zero).ToArray();
            var block = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);
            for (int i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(block, i * IntPtr.Size, pointers[i]);
            allocations.Add(block);
            return block;
        }

        IntPtr path = Str(executable);
        IntPtr argv = Array(new[] { executable }.Concat(options.Args));
        IntPtr envp = Array(env.Select(e => $"{e.Key}={e.Value}"));
        IntPtr dir = Str(cwd);
        var size = new WinSize { Rows = (ushort)Math.Max(2, options.Rows), Columns = (ushort)Math.Max(2, options.Columns) };

        PrepareChildCalls();
        int pid = ForkPty(out int master, ref size);
        if (pid == 0)
        {
            chdir(dir);
            execve(path, argv, envp);
            _exit(127);
        }

        int error = Marshal.GetLastPInvokeError();
        foreach (var p in allocations) Marshal.FreeHGlobal(p);
        if (pid < 0) throw new IOException($"forkpty failed (errno {error})");
        return new UnixPty(pid, master);
    }

    private static int ForkPty(out int master, ref WinSize size)
    {
        try
        {
            return forkpty(out master, IntPtr.Zero, IntPtr.Zero, ref size);
        }
        catch (EntryPointNotFoundException)
        {
            // glibc older than 2.34 keeps forkpty in libutil.
            return forkpty_libutil(out master, IntPtr.Zero, IntPtr.Zero, ref size);
        }
    }

    private static void PrepareChildCalls()
    {
        // Generate the interop stubs now so the child never needs the JIT.
        foreach (var name in new[] { nameof(chdir), nameof(execve), nameof(_exit) })
        {
            var method = typeof(UnixPty).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (method is not null) Marshal.Prelink(method);
        }
    }

    internal static string ResolveExecutable(string shell)
    {
        if (shell.Contains('/')) return shell;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, shell);
            if (File.Exists(candidate)) return candidate;
        }
        return shell;
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var size = new WinSize { Rows = (ushort)Math.Max(2, rows), Columns = (ushort)Math.Max(2, columns) };
        ioctl(_master, OperatingSystem.IsMacOS() ? (UIntPtr)0x80087467u : (UIntPtr)0x5414u, ref size);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        kill(_pid, SIGHUP);
        try { _input.Dispose(); } catch (IOException) { }
        try { _output.Dispose(); } catch (IOException) { }
    }

    private const int SIGHUP = 1;
    private const int EINTR = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize { public ushort Rows, Columns, XPixels, YPixels; }

    [DllImport("libc", SetLastError = true)]
    private static extern int forkpty(out int master, IntPtr name, IntPtr termios, ref WinSize size);

    [DllImport("libutil.so.1", EntryPoint = "forkpty", SetLastError = true)]
    private static extern int forkpty_libutil(out int master, IntPtr name, IntPtr termios, ref WinSize size);

    [DllImport("libc")]
    private static extern int execve(IntPtr path, IntPtr argv, IntPtr envp);

    [DllImport("libc")]
    private static extern int chdir(IntPtr path);

    [DllImport("libc")]
    private static extern void _exit(int status);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, UIntPtr request, ref WinSize size);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);

    [DllImport("libc")]
    private static extern int kill(int pid, int signal);
}
