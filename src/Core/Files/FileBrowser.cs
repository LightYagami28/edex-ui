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

        try
        {
            var dir = new DirectoryInfo(full);
            if (!dir.Exists) return new DirectoryListing(full, entries, "Directory not found");
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
            var items = new List<FileEntry>();
            foreach (var info in dir.EnumerateFileSystemInfos("*", options))
            {
                bool hidden = info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);
                if (hidden && !showHidden) continue;
                var kind = info.LinkTarget is not null ? FileEntryKind.Link
                    : info is DirectoryInfo ? FileEntryKind.Directory : FileEntryKind.File;
                long size = info is FileInfo f && kind == FileEntryKind.File ? SafeLength(f) : 0;
                bool isDir = info is DirectoryInfo;
                items.Add(new FileEntry(info.Name, info.FullName, isDir ? FileEntryKind.Directory : kind, size, info.LastWriteTime, hidden));
            }
            entries.AddRange(items
                .OrderByDescending(e => e.Kind == FileEntryKind.Directory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
            return new DirectoryListing(full, entries, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DirectoryListing(full, entries, e.Message);
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch (IOException) { return 0; }
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
