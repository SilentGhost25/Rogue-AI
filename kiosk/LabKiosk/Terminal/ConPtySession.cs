using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static LabKiosk.Terminal.ConPtyNative;

namespace LabKiosk.Terminal;

/// <summary>
/// One real pseudo console hosting one fixed program (agy, or opencode attach). Never a shell:
/// the executable and arguments come from kiosk settings, not from participant input.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private IntPtr _hpc;
    private FileStream? _input;
    private FileStream? _output;
    private Process? _process;
    private readonly object _writeGate = new();
    private int _disposed;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    private ConPtySession() { }

    public static ConPtySession Start(string exe, string args, string cwd, IReadOnlyDictionary<string, string?> env, short cols, short rows)
    {
        var s = new ConPtySession();
        try { s.Launch(exe, args, cwd, env, cols, rows); }
        catch { s.Dispose(); throw; }
        return s;
    }

    private void Launch(string exe, string args, string cwd, IReadOnlyDictionary<string, string?> env, short cols, short rows)
    {
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");

        var hr = CreatePseudoConsole(new Coord(cols, rows), inRead, outWrite, 0, out _hpc);
        // The pseudo console now owns these ends; closing our copies is required for a clean shutdown.
        CloseHandle(inRead);
        CloseHandle(outWrite);
        if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole failed (needs Windows 10 1809 or newer)");

        _input = new FileStream(new SafeFileHandle(inWrite, true), FileAccess.Write, 4096, false);
        _output = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read, 4096, false);

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attrList = Marshal.AllocHGlobal(size);
        var envBlock = IntPtr.Zero;
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!UpdateProcThreadAttribute(attrList, 0, ProcThreadAttributePseudoConsole, _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var si = new StartupInfoEx { lpAttributeList = attrList };
            si.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            envBlock = BuildEnvironment(env);
            var cmd = new StringBuilder($"\"{exe}\" {args}");

            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    envBlock, cwd, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start {Path.GetFileName(exe)}");

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            _process = Process.GetProcessById(pi.dwProcessId);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                int code = 0; try { code = _process.ExitCode; } catch { /* already gone */ }
                Exited?.Invoke(code);
                Task.Run(Dispose); // closing the pseudo console lets the reader finish
            };
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
        }

        new Thread(ReadLoop) { IsBackground = true, Name = "conpty-reader" }.Start();
    }

    private static IntPtr BuildEnvironment(IReadOnlyDictionary<string, string?> overrides)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            merged[(string)e.Key] = (string?)e.Value ?? "";
        foreach (var (k, v) in overrides)
        {
            if (v is null) merged.Remove(k); else merged[k] = v;
        }
        var sb = new StringBuilder();
        foreach (var (k, v) in merged) sb.Append(k).Append('=').Append(v).Append('\0');
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[8192];
        try
        {
            int n;
            while (_output is not null && (n = _output.Read(buffer, 0, buffer.Length)) > 0)
            {
                var count = decoder.GetChars(buffer, 0, n, chars, 0);
                if (count > 0) Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (IOException) { /* pipe closed during shutdown */ }
        catch (ObjectDisposedException) { }
    }

    public void Write(string text)
    {
        if (_input is null || Volatile.Read(ref _disposed) == 1) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeGate)
        {
            try { _input.Write(bytes, 0, bytes.Length); _input.Flush(); } catch (IOException) { } catch (ObjectDisposedException) { }
        }
    }

    public void Resize(short cols, short rows)
    {
        if (_hpc != IntPtr.Zero && Volatile.Read(ref _disposed) == 0 && cols > 0 && rows > 0)
            ResizePseudoConsole(_hpc, new Coord(cols, rows));
    }

    public void Kill()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Kill();
        var hpc = _hpc; _hpc = IntPtr.Zero;
        // ClosePseudoConsole can wait for pending output, so never call it on the UI thread.
        Task.Run(() =>
        {
            if (hpc != IntPtr.Zero) ClosePseudoConsole(hpc);
            try { _input?.Dispose(); } catch { }
            try { _output?.Dispose(); } catch { }
        });
    }
}
