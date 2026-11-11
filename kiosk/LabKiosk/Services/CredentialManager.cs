using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabKiosk.Services;

/// <summary>
/// Interacts with the native Windows Credential Manager (advapi32.dll) to query and manage
/// Antigravity AI (Google) and GitHub account credentials without requiring user re-entry.
/// </summary>
public static class CredentialManager
{
    private const uint CRED_TYPE_GENERIC = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string? filter, int flags, out uint count, out IntPtr credentialArrayPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>
    /// Reads the authenticated Google Account email from Antigravity's stored credential (gemini:antigravity).
    /// Falls back to session logs if Credential Manager is not yet populated.
    /// </summary>
    public static string? GetAntigravityGoogleEmail()
    {
        try
        {
            if (CredRead("gemini:antigravity", CRED_TYPE_GENERIC, 0, out var credPtr) && credPtr != IntPtr.Zero)
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                    {
                        var bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                        var json = Encoding.UTF8.GetString(bytes);

                        try
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("id_token_email", out var emailProp))
                            {
                                var email = emailProp.GetString();
                                if (!string.IsNullOrWhiteSpace(email)) return email.Trim();
                            }
                        }
                        catch { /* not valid JSON, proceed to regex fallback */ }

                        var match = Regex.Match(json, @"[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+");
                        if (match.Success) return match.Value.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(cred.UserName))
                    {
                        var userMatch = Regex.Match(cred.UserName, @"[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+");
                        if (userMatch.Success) return userMatch.Value.Trim();
                    }
                }
                finally
                {
                    CredFree(credPtr);
                }
            }
        }
        catch { /* best effort */ }

        // Fallback: Check session logs (%TEMP%\agy_session.log, antigravity-cli/log/cli-*.log, antigravity/cli.log)
        try
        {
            var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidateLogs = new List<string>();
            var tempLog = Path.Combine(Path.GetTempPath(), "agy_session.log");
            if (File.Exists(tempLog)) candidateLogs.Add(tempLog);

            var mainCliLog = Path.Combine(userDir, ".gemini", "antigravity", "cli.log");
            if (File.Exists(mainCliLog)) candidateLogs.Add(mainCliLog);

            var logDir = Path.Combine(userDir, ".gemini", "antigravity-cli", "log");
            if (Directory.Exists(logDir))
            {
                candidateLogs.AddRange(
                    new DirectoryInfo(logDir)
                        .GetFiles("cli-*.log")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(3)
                        .Select(f => f.FullName));
            }

            foreach (var logPath in candidateLogs)
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? email = null;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var m = Regex.Match(line, @"(?:applyAuthResult:\s*email=|signed-in\s+user:\s*)([a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+)", RegexOptions.IgnoreCase);
                    if (m.Success) email = m.Groups[1].Value.Trim();
                }
                if (!string.IsNullOrWhiteSpace(email)) return email;
            }
        }
        catch { /* best effort */ }

        return null;
    }

    /// <summary>
    /// Clears the Antigravity Google OAuth credential so the participant can sign in with a different account.
    /// </summary>
    public static bool ClearAntigravityGoogleCredential()
    {
        try
        {
            return CredDelete("gemini:antigravity", CRED_TYPE_GENERIC, 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Discovers the active GitHub username from Git Credential Manager, git config, or GitHub CLI.
    /// </summary>
    public static string? GetGitHubUsername()
    {
        // 1. Direct query: git:https://github.com
        try
        {
            if (CredRead("git:https://github.com", CRED_TYPE_GENERIC, 0, out var credPtr) && credPtr != IntPtr.Zero)
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    var user = cred.UserName;
                    if (IsValidGitHubUsername(user)) return user!.Trim();
                }
                finally
                {
                    CredFree(credPtr);
                }
            }
        }
        catch { /* best effort */ }

        // 2. Enumerate generic credentials for GitHub targets (e.g. GitHub - https://api.github.com/{user})
        try
        {
            if (CredEnumerate(null, 0, out var count, out var arrayPtr) && arrayPtr != IntPtr.Zero)
            {
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var ptr = Marshal.ReadIntPtr(arrayPtr, i * IntPtr.Size);
                        if (ptr == IntPtr.Zero) continue;
                        var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                        var target = cred.TargetName ?? "";

                        if (target.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                        {
                            var user = cred.UserName;
                            if (IsValidGitHubUsername(user)) return user!.Trim();

                            // Look for GitHub - https://api.github.com/{username} in TargetName
                            var m = Regex.Match(target, @"api\.github\.com[/:]([a-zA-Z0-9-_]+)");
                            if (m.Success && IsValidGitHubUsername(m.Groups[1].Value))
                                return m.Groups[1].Value.Trim();
                        }
                    }
                }
                finally
                {
                    CredFree(arrayPtr);
                }
            }
        }
        catch { /* best effort */ }

        // 3. Query git config github.user
        try
        {
            var gitUser = RunProcessCaptureOutput("git.exe", "config --get github.user");
            if (IsValidGitHubUsername(gitUser)) return gitUser!.Trim();
        }
        catch { /* best effort */ }

        // 4. Query gh CLI if available
        try
        {
            var ghUser = RunProcessCaptureOutput("gh.exe", "api user --jq .login");
            if (IsValidGitHubUsername(ghUser)) return ghUser!.Trim();
        }
        catch { /* best effort */ }

        return null;
    }

    private static bool IsValidGitHubUsername(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim().TrimStart('@');
        if (trimmed.Length == 0 || trimmed.Length > 39) return false;
        if (trimmed.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Personal Access Token", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("token", StringComparison.OrdinalIgnoreCase))
            return false;
        return Regex.IsMatch(trimmed, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?$");
    }

    private static string? RunProcessCaptureOutput(string fileName, string args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var outText = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(1500);
            return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(outText) ? outText : null;
        }
        catch
        {
            return null;
        }
    }
}

