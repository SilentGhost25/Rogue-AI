using System.Diagnostics;
using System.Text.Json;
using ControlPlane.Data;
using Microsoft.Extensions.Configuration;

namespace ControlPlane.Services;

/// <summary>
/// Manages per-participant Git branches and syncs commits upstream to GitHub (Prince649294u83/Rogue-AI-competition.git).
/// Operates asynchronously on the server so network push operations never block evaluation or participant scoring.
/// </summary>
public sealed class GitMirrorService
{
    private readonly Database _db;
    private readonly string _remoteUrl;
    private readonly string? _gitHubToken;
    private readonly bool _disabled;

    public GitMirrorService(IConfiguration config, Database db)
    {
        _db = db;
        _remoteUrl = config["Breach:GitMirrorUrl"]
                     ?? Environment.GetEnvironmentVariable("GIT_MIRROR_URL")
                     ?? "";

        _gitHubToken = config["Breach:GitHubToken"]
                       ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                       ?? Environment.GetEnvironmentVariable("GH_TOKEN");

        _disabled = string.Equals(config["Breach:DisableGitMirror"], "true", StringComparison.OrdinalIgnoreCase);
    }

    public string RemoteUrl => _remoteUrl;
    public bool HasToken => !string.IsNullOrWhiteSpace(_gitHubToken);

    /// <summary>
    /// Commits the current workspace snapshot to the participant branch and attempts to push to GitHub.
    /// </summary>
    public async Task<bool> SyncBranchAsync(string participantId, string workspacePath, string commitMessage)
    {
        if (_disabled || string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            return false;

        return await Task.Run(() =>
        {
            try
            {
                var gitignore = Path.Combine(workspacePath, ".gitignore");
                if (!File.Exists(gitignore))
                {
                    File.WriteAllText(gitignore, "node_modules/\n.breach-backup/\n*.db\n*.db-wal\n*.db-shm\n*.sqlite*\n*.log\neval_*.db\n");
                }

                var gitDir = Path.Combine(workspacePath, ".git");
                if (!Directory.Exists(gitDir))
                {
                    RunGit(workspacePath, "init", 5000);
                    RunGit(workspacePath, $"config user.name \"Participant {participantId}\"", 3000);
                    RunGit(workspacePath, $"config user.email \"participant-{participantId}@rogue.internal\"", 3000);
                }

                var branch = $"participant/{participantId}";
                RunGit(workspacePath, $"checkout -B {branch}", 5000);
                RunGit(workspacePath, "add -A", 10000);

                var safeMsg = commitMessage.Replace("\"", "'");
                RunGit(workspacePath, $"commit -m \"{safeMsg}\" --allow-empty", 5000);

                if (!string.IsNullOrWhiteSpace(_gitHubToken) && !string.IsNullOrWhiteSpace(_remoteUrl))
                {
                    var pushUrl = FormatAuthUrl(_remoteUrl, _gitHubToken);
                    RunGit(workspacePath, "remote remove origin", 3000);
                    RunGit(workspacePath, $"remote add origin \"{pushUrl}\"", 3000);

                    var pushRes = RunGit(workspacePath, $"push -u origin {branch} --force", 15000);
                    if (pushRes.Success)
                    {
                        _db.Log("GIT_PUSH_SUCCESS", participantId, payloadJson: JsonSerializer.Serialize(new
                        {
                            branch,
                            commit = commitMessage,
                            repo = _remoteUrl
                        }));
                        return true;
                    }
                    else
                    {
                        _db.Log("GIT_PUSH_DEFERRED", participantId, payloadJson: JsonSerializer.Serialize(new
                        {
                            branch,
                            reason = pushRes.Error.Length > 200 ? pushRes.Error[..200] : pushRes.Error
                        }));
                        return false;
                    }
                }
                else
                {
                    _db.Log("GIT_COMMITTED_LOCAL", participantId, payloadJson: JsonSerializer.Serialize(new
                    {
                        branch,
                        commit = commitMessage
                    }));
                    return true;
                }
            }
            catch (Exception ex)
            {
                _db.Log("GIT_ERROR", participantId, payloadJson: JsonSerializer.Serialize(new
                {
                    error = ex.Message
                }));
                return false;
            }
        });
    }

    private static string FormatAuthUrl(string remoteUrl, string token)
    {
        if (remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://x-access-token:" + token + "@" + remoteUrl["https://".Length..];
        }
        return remoteUrl;
    }

    private static (bool Success, string Output, string Error) RunGit(string workingDir, string arguments, int timeoutMs)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (proc.WaitForExit(timeoutMs))
            {
                stdoutTask.Wait(500);
                stderrTask.Wait(500);
                return (proc.ExitCode == 0, stdoutTask.Result, stderrTask.Result);
            }

            try { proc.Kill(); } catch { }
            return (false, "", "Git command timed out after " + timeoutMs + "ms");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }
}
