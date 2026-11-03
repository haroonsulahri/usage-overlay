using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace UsageOverlay.Services;

internal sealed record SelectedProject(string Id, string Name, string Root);
internal sealed record ProjectGitSnapshot(SelectedProject Project, int? Branches, DateTimeOffset? LastCommit, bool IsRepository, string? RepositoryRoot = null, bool MultipleRepositories = false);

internal static class ProjectGitStatus
{
    internal static SelectedProject? ReadSelectedProject(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var selection = root.GetProperty("selected-project");
            if (selection.GetProperty("type").GetString() != "local") return null;
            var id = selection.GetProperty("projectId").GetString();
            if (string.IsNullOrWhiteSpace(id)) return null;
            var project = root.GetProperty("local-projects").GetProperty(id);
            var roots = project.GetProperty("rootPaths");
            if (roots.ValueKind != JsonValueKind.Array || roots.GetArrayLength() != 1) return null;
            var directory = roots[0].GetString();
            var name = project.GetProperty("name").GetString();
            return string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name)
                ? null : new SelectedProject(id, name, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static async Task<ProjectGitSnapshot> ReadAsync(SelectedProject project)
    {
        var (repository, multiple) = await Task.Run(() => FindRepository(project.Root));
        if (repository is null) return new(project, null, null, false, MultipleRepositories: multiple);
        var branches = await RunGitAsync(repository, "for-each-ref", "--format=.", "refs/heads");
        var commit = await RunGitAsync(repository, "log", "-1", "--format=%ct");
        int? count = branches is null ? null : branches.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        DateTimeOffset? time = null;
        if (long.TryParse(commit?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            try { time = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { }
        }
        return new(project, count, time, true, repository, multiple);
    }

    private static (string? Repository, bool Multiple) FindRepository(string root)
    {
        static bool HasGit(string path) => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));
        if (HasGit(root)) return (root, false);
        try
        {
            var children = new DirectoryInfo(root).EnumerateDirectories()
                .Where(child => !child.Attributes.HasFlag(FileAttributes.ReparsePoint) && HasGit(child.FullName))
                .Select(child => child.FullName).ToArray();
            if (children.Length == 1) return (children[0], false);
            var app = children.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "app", StringComparison.OrdinalIgnoreCase));
            return (app, children.Length > 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return (null, false); }
    }

    private static async Task<string?> RunGitAsync(string directory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("--no-optional-locks");
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(directory);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); return null; }
            await error;
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static string Format(ProjectGitSnapshot snapshot, DateTimeOffset now)
    {
        var prefix = $"Project root: {snapshot.Project.Name}";
        if (!snapshot.IsRepository) return prefix + (snapshot.MultipleRepositories ? "\nMultiple Git repositories found" : "\nGit repository not found");
        if (snapshot.RepositoryRoot is { } root && !string.Equals(root, snapshot.Project.Root, StringComparison.OrdinalIgnoreCase))
            prefix += $"\nRepository: {Path.GetFileName(root)}" + (snapshot.MultipleRepositories ? " (of several)" : "");
        var branches = snapshot.Branches?.ToString(CultureInfo.InvariantCulture) ?? "—";
        var age = "—";
        if (snapshot.LastCommit is { } commit)
        {
            var elapsed = now - commit;
            age = elapsed < TimeSpan.Zero ? "in the future" : elapsed.TotalMinutes < 1 ? "<1 min" : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} min ago"
                : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} h ago" : $"{(int)elapsed.TotalDays} d ago";
        }
        return $"{prefix}\nBranches: {branches} · Last commit: {age}";
    }
}
