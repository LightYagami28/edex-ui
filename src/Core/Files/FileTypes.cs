// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public enum FileCategory
{
    Folder, Link, Text, Code, Data, Image, Audio, Video, Archive, Pdf, Document, Spreadsheet, Presentation, Executable, Font, Disk, Other,
}

/// <summary>Classifies files by extension for icons and the built-in viewers.</summary>
public static class FileTypes
{
    private static readonly Dictionary<string, FileCategory> ByExtension = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory category, string list)
        {
            foreach (var ext in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map["." + ext] = category;
        }
        Add(FileCategory.Text, "txt md markdown rst log nfo readme license changelog tex");
        Add(FileCategory.Code, "c h cc cpp cxx hpp hh cs fs vb java kt kts scala go rs swift m mm py rb pl pm php js mjs cjs ts tsx jsx vue svelte " +
            "lua r dart zig nim hs ml ex exs erl clj sh bash zsh fish ps1 psm1 bat cmd css scss sass less html htm sql gradle cmake makefile dockerfile");
        Add(FileCategory.Data, "json jsonc xml yaml yml toml ini cfg conf csv tsv props targets csproj fsproj slnx sln editorconfig gitignore env lock plist");
        Add(FileCategory.Image, "png jpg jpeg gif bmp webp ico svg tif tiff heic avif psd raw");
        Add(FileCategory.Audio, "wav mp3 ogg oga flac aac m4a opus wma mid midi aiff");
        Add(FileCategory.Video, "mp4 m4v mkv webm avi mov wmv flv mpg mpeg 3gp");
        Add(FileCategory.Archive, "zip 7z rar tar gz tgz bz2 xz zst lz4 cab jar war apk nupkg deb rpm");
        Add(FileCategory.Pdf, "pdf");
        Add(FileCategory.Document, "doc docx odt rtf epub pages");
        Add(FileCategory.Spreadsheet, "xls xlsx ods numbers");
        Add(FileCategory.Presentation, "ppt pptx odp key");
        Add(FileCategory.Executable, "exe msi dll so dylib app appimage com sys elf bin");
        Add(FileCategory.Font, "ttf otf woff woff2 fon");
        Add(FileCategory.Disk, "iso img dmg vhd vhdx vmdk qcow2");
        return map;
    }

    public static FileCategory Classify(string name, FileEntryKind kind = FileEntryKind.File)
    {
        if (kind == FileEntryKind.Directory) return FileCategory.Folder;
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext))
        {
            // Well-known extensionless files.
            var lower = name.ToLowerInvariant();
            if (lower is "makefile" or "dockerfile" or "jenkinsfile" or "rakefile" or "gemfile") return FileCategory.Code;
            if (lower is "readme" or "license" or "copying" or "authors" or "changelog") return FileCategory.Text;
            if (name.StartsWith('.')) return FileCategory.Data;
            return kind == FileEntryKind.Link ? FileCategory.Link : FileCategory.Other;
        }
        if (ByExtension.TryGetValue(ext, out var category)) return category;
        if (name.StartsWith('.') && name.IndexOf('.', 1) < 0) return FileCategory.Data;
        return kind == FileEntryKind.Link ? FileCategory.Link : FileCategory.Other;
    }

    /// <summary>Short upper-case label drawn on generic icons, e.g. "PDF".</summary>
    public static string Label(string name)
    {
        var ext = Path.GetExtension(name).TrimStart('.');
        return ext.Length is > 0 and <= 4 ? ext.ToUpperInvariant() : "";
    }

    /// <summary>True for files the text viewer can show.</summary>
    public static bool IsTextLike(FileCategory category) => category is FileCategory.Text or FileCategory.Code or FileCategory.Data;
}
