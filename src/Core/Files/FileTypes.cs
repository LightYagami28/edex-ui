// SPDX-License-Identifier: GPL-3.0-only
namespace Edex.Core.Files;

public enum FileCategory
{
    Folder, Link, Text, Code, Data, Image, Audio, Video, Archive, Pdf, Document, Spreadsheet, Presentation, Executable, Font, Disk, Other,
}

public static class FileTypes
{
    private const int MaxLabelLength = 4;

    private static readonly (FileCategory Category, string Extensions)[] ExtensionTable =
    [
        (FileCategory.Text, "txt md markdown rst log nfo readme license changelog tex"),
        (FileCategory.Code, "c h cc cpp cxx hpp hh cs fs vb java kt kts scala go rs swift m mm py rb pl pm php js mjs cjs ts tsx jsx vue svelte " +
            "lua r dart zig nim hs ml ex exs erl clj sh bash zsh fish ps1 psm1 bat cmd css scss sass less html htm sql gradle cmake makefile dockerfile"),
        (FileCategory.Data, "json jsonc xml yaml yml toml ini cfg conf csv tsv props targets csproj fsproj slnx sln editorconfig gitignore env lock plist"),
        (FileCategory.Image, "png jpg jpeg gif bmp webp ico svg tif tiff heic avif psd raw"),
        (FileCategory.Audio, "wav mp3 ogg oga flac aac m4a opus wma mid midi aiff"),
        (FileCategory.Video, "mp4 m4v mkv webm avi mov wmv flv mpg mpeg 3gp"),
        (FileCategory.Archive, "zip 7z rar tar gz tgz bz2 xz zst lz4 cab jar war apk nupkg deb rpm"),
        (FileCategory.Pdf, "pdf"),
        (FileCategory.Document, "doc docx odt rtf epub pages"),
        (FileCategory.Spreadsheet, "xls xlsx ods numbers"),
        (FileCategory.Presentation, "ppt pptx odp key"),
        (FileCategory.Executable, "exe msi dll so dylib app appimage com sys elf bin"),
        (FileCategory.Font, "ttf otf woff woff2 fon"),
        (FileCategory.Disk, "iso img dmg vhd vhdx vmdk qcow2"),
    ];

    private static readonly Dictionary<string, FileCategory> ByExtension = Build();

    private static readonly Dictionary<string, FileCategory> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["makefile"] = FileCategory.Code, ["dockerfile"] = FileCategory.Code, ["jenkinsfile"] = FileCategory.Code,
        ["rakefile"] = FileCategory.Code, ["gemfile"] = FileCategory.Code,
        ["readme"] = FileCategory.Text, ["license"] = FileCategory.Text, ["copying"] = FileCategory.Text,
        ["authors"] = FileCategory.Text, ["changelog"] = FileCategory.Text,
    };

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, extensions) in ExtensionTable)
        {
            foreach (var ext in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                map["." + ext] = category;
            }
        }
        return map;
    }

    public static FileCategory Classify(string name) => Classify(name, FileEntryKind.File);

    public static FileCategory Classify(string name, FileEntryKind kind)
    {
        if (kind == FileEntryKind.Directory)
        {
            return FileCategory.Folder;
        }

        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext))
        {
            return ClassifyExtensionless(name, kind);
        }

        return ByExtension.TryGetValue(ext, out var category) ? category : ClassifyUnknown(name, kind);
    }

    private static FileCategory ClassifyUnknown(string name, FileEntryKind kind) =>
        name.StartsWith('.') && name.IndexOf('.', 1) < 0 ? FileCategory.Data : Fallback(kind);

    private static FileCategory ClassifyExtensionless(string name, FileEntryKind kind)
    {
        if (ByName.TryGetValue(name, out var category))
        {
            return category;
        }

        return name.StartsWith('.') ? FileCategory.Data : Fallback(kind);
    }

    private static FileCategory Fallback(FileEntryKind kind) => kind == FileEntryKind.Link ? FileCategory.Link : FileCategory.Other;

    public static string Label(string name)
    {
        var ext = Path.GetExtension(name).TrimStart('.');
        return ext.Length is > 0 and <= MaxLabelLength ? ext.ToUpperInvariant() : "";
    }

    public static bool IsTextLike(FileCategory category) => category is FileCategory.Text or FileCategory.Code or FileCategory.Data;
}
