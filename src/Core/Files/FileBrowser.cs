// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public enum FileEntryKind { Directory, File, Link }

public sealed record FileEntry(string Name, string FullPath, FileEntryKind Kind, long Size, DateTime Modified, bool Hidden);

public sealed record DirectoryListing(string Path, IReadOnlyList<FileEntry> Entries, string? Error);

/// <summary>Lists directories for the file browser panel. Never throws for IO errors: they are reported in <see cref="DirectoryListing.Error"/>.</summary>
public static class FileBrowser
{
    public static DirectoryListing List(string path, bool showHidden = false)
    {
        string full;
        try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExpandHome(path))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DirectoryListing(path, [], e.Message);
        }

        var entries = new List<FileEntry>();
        var parent = Directory.GetParent(full);
        if (parent is not null)
            entries.Add(new FileEntry("..", parent.FullName, FileEntryKind.Directory, 0, DateTime.MinValue, false));

        var items = Safe.Run(() => Enumerate(full, showHidden), null);
        if (items is null) return new DirectoryListing(full, entries, "Directory not found");
        entries.AddRange(items
            .OrderByDescending(e => e.Kind == FileEntryKind.Directory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
        return new DirectoryListing(full, entries, null);
    }

    /// <summary>The entries of a folder, or null when it does not exist.</summary>
    private static List<FileEntry>? Enumerate(string full, bool showHidden)
    {
        var dir = new DirectoryInfo(full);
        if (!dir.Exists) return null;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        var items = new List<FileEntry>();
        foreach (var info in dir.EnumerateFileSystemInfos("*", options))
        {
            bool hidden = info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);
            if (hidden && !showHidden) continue;
            var kind = info is DirectoryInfo ? FileEntryKind.Directory : info.LinkTarget is not null ? FileEntryKind.Link : FileEntryKind.File;
            long size = info is FileInfo f ? Safe.Run(() => f.Length, 0L) : 0;
            items.Add(new FileEntry(info.Name, info.FullName, kind, size, info.LastWriteTime, hidden));
        }
        return items;
    }

    public static string ExpandHome(string path)
    {
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return path;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
