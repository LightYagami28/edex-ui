// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public enum FileEntryKind { Directory, File, Link }

public sealed record FileEntry(string Name, string FullPath, FileEntryKind Kind, long Size, DateTime Modified, bool Hidden);

public sealed record DirectoryListing(string Path, IReadOnlyList<FileEntry> Entries, string? Error);

public static class FileBrowser
{
    private const int HomePrefixLength = 2;
    private const double BytesPerUnit = 1024;

    public static DirectoryListing List(string path) => List(path, false);

    public static DirectoryListing List(string path, bool showHidden)
    {
        string full;
        try
        {
            full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExpandHome(path)));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DirectoryListing(path, [], e.Message);
        }

        return ListResolved(full, showHidden);
    }

    private static DirectoryListing ListResolved(string full, bool showHidden)
    {
        var entries = ParentEntries(full);
        var items = Safe.Run(() => Enumerate(full, showHidden), null);
        if (items is null)
        {
            return new DirectoryListing(full, entries, "Directory not found");
        }

        entries.AddRange(items
            .OrderByDescending(e => e.Kind == FileEntryKind.Directory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
        return new DirectoryListing(full, entries, null);
    }

    private static List<FileEntry> ParentEntries(string full)
    {
        var parent = Directory.GetParent(full);
        return parent is null ? [] : [new FileEntry("..", parent.FullName, FileEntryKind.Directory, 0, DateTime.MinValue, false)];
    }

    private static List<FileEntry>? Enumerate(string full, bool showHidden)
    {
        var dir = new DirectoryInfo(full);
        if (!dir.Exists)
        {
            return null;
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        return dir.EnumerateFileSystemInfos("*", options)
            .Where(info => showHidden || !IsHidden(info))
            .Select(ToEntry)
            .ToList();
    }

    private static bool IsHidden(FileSystemInfo info) =>
        info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);

    private static FileEntry ToEntry(FileSystemInfo info)
    {
        long size = info is FileInfo f ? Safe.Run(() => f.Length, 0L) : 0;
        return new FileEntry(info.Name, info.FullName, GetKind(info), size, info.LastWriteTime, IsHidden(info));
    }

    public static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[HomePrefixLength..]);
        }

        return path;
    }

    private static FileEntryKind GetKind(FileSystemInfo info)
    {
        if (info is DirectoryInfo)
        {
            return FileEntryKind.Directory;
        }

        if (info.LinkTarget is not null)
        {
            return FileEntryKind.Link;
        }

        return FileEntryKind.File;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= BytesPerUnit && unit < units.Length - 1)
        {
            value /= BytesPerUnit;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
