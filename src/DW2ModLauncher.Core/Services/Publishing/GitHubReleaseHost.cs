using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DW2ModLauncher.Core.Services.Publishing
{
    /// <summary>The result of running a program.</summary>
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Runs a program and waits for it. Behind an interface so release tests need no GitHub.</summary>
    public interface IProcessRunner
    {
        /// <summary>Throws <see cref="Win32Exception"/> when the program cannot be started (not installed).</summary>
        ProcessResult Run(string program, IReadOnlyList<string> arguments);
    }

    public class SystemProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string program, IReadOnlyList<string> arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(program)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using (Process process = Process.Start(info))
            {
                process.StandardInput.Close();
                // Both streams are read at once, or a full pipe on one side stalls the program.
                System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return new ProcessResult { ExitCode = process.ExitCode, Output = output, Error = error.Result };
            }
        }
    }

    /// <summary>A release could not be made or read; the message says what to do about it.</summary>
    public class ReleaseHostException : Exception
    {
        public ReleaseHostException(string message) : base(message) { }
    }

    /// <summary>
    /// Makes and reads a mod's releases on GitHub by running the GitHub command-line tool (<c>gh</c>), which uses the login the player already
    /// has on this machine: the launcher never sees or stores a credential. Every failure carries gh's own message.
    /// </summary>
    public class GitHubReleaseHost
    {
        public const string Program = "gh";
        /// <summary>The topic the catalog uses to find release repositories.</summary>
        public const string CatalogTopic = "dw2-mod";
        private static readonly Regex RepoPattern = new Regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

        private readonly IProcessRunner runner;

        public GitHubReleaseHost(IProcessRunner runner = null)
        {
            this.runner = runner ?? new SystemProcessRunner();
        }

        /// <summary>Whether <paramref name="repo"/> is written OWNER/NAME.</summary>
        public static bool IsRepo(string repo) { return repo != null && RepoPattern.IsMatch(repo); }

        /// <summary>Runs gh. A missing tool and a failing command both become a <see cref="ReleaseHostException"/> that says what happened.</summary>
        private ProcessResult Gh(params string[] arguments)
        {
            ProcessResult result;
            try { result = runner.Run(Program, arguments); }
            catch (Win32Exception)
            {
                throw new ReleaseHostException("GitHub's command-line tool (gh) was not found. Install it from https://cli.github.com, then run \"gh auth login\".");
            }
            return result;
        }

        private ProcessResult GhOrThrow(string doing, params string[] arguments)
        {
            ProcessResult result = Gh(arguments);
            if (result.ExitCode != 0) throw new ReleaseHostException(doing + " failed: " + Message(result));
            return result;
        }

        private static string Message(ProcessResult result)
        {
            string text = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            return (text ?? "").Trim();
        }

        private static void CheckRepo(string repo)
        {
            if (!IsRepo(repo)) throw new ReleaseHostException("The release repository \"" + repo + "\" is not written OWNER/NAME.");
        }

        /// <summary>Throws when gh is missing or not logged in to github.com.</summary>
        public void CheckReady()
        {
            ProcessResult status = Gh("auth", "status", "--hostname", "github.com");
            if (status.ExitCode != 0) throw new ReleaseHostException("gh is not logged in to GitHub. Run \"gh auth login\" and try again. (" + Message(status) + ")");
        }

        /// <summary>True when the repository has a release with this tag; false when it has none. Any other failure (no such repository, no access, no network) throws.</summary>
        public bool ReleaseExists(string repo, string tag)
        {
            CheckRepo(repo);
            ProcessResult result = Gh("release", "view", tag, "--repo", repo);
            if (result.ExitCode == 0) return true;
            if (Message(result).IndexOf("release not found", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            throw new ReleaseHostException("Looking up release " + tag + " in " + repo + " failed: " + Message(result));
        }

        /// <summary>Whether the repository carries the catalog topic; the catalog only lists mods from repositories that do (or that its owner listed by hand).</summary>
        public bool HasCatalogTopic(string repo)
        {
            CheckRepo(repo);
            ProcessResult result = GhOrThrow("Reading the topics of " + repo, "repo", "view", repo, "--json", "repositoryTopics");
            using (JsonDocument document = JsonDocument.Parse(result.Output))
            {
                if (!document.RootElement.TryGetProperty("repositoryTopics", out JsonElement topics) || topics.ValueKind != JsonValueKind.Array) return false;
                return topics.EnumerateArray().Any(t => t.TryGetProperty("name", out JsonElement name) && string.Equals(name.GetString(), CatalogTopic, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Creates the release and uploads the package's files. The release is not marked latest: a mod's releases are not versions of one product.</summary>
        public void Create(string repo, ReleasePackage package)
        {
            CheckRepo(repo);
            List<string> arguments = new List<string> { "release", "create", package.Tag };
            arguments.AddRange(package.Files);
            arguments.AddRange(new[] { "--repo", repo, "--title", package.Title, "--notes-file", package.NotesPath, "--latest=false" });
            GhOrThrow("Creating release " + package.Tag + " in " + repo, arguments.ToArray());
        }

        /// <summary>Deletes the release and its tag. Replacing a release this way resets its download count.</summary>
        public void Delete(string repo, string tag)
        {
            CheckRepo(repo);
            GhOrThrow("Deleting release " + tag + " in " + repo, "release", "delete", tag, "--repo", repo, "--yes", "--cleanup-tag");
        }

        /// <summary>Downloads the release's zip into <paramref name="folder"/> and returns its path.</summary>
        public string DownloadZip(string repo, string tag, string folder)
        {
            CheckRepo(repo);
            Directory.CreateDirectory(folder);
            string name = tag + ".zip";
            GhOrThrow("Downloading release " + tag + " from " + repo, "release", "download", tag, "--repo", repo, "--pattern", name, "--dir", folder, "--clobber");
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) throw new ReleaseHostException("Release " + tag + " in " + repo + " has no " + name + ".");
            return path;
        }
    }
}
