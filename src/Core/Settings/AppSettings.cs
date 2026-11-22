// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Text.Json;

namespace Edex.Core.Settings;

/// <summary>Impostazioni dell'applicazione, convalidabili prima dell'uso.</summary>
public sealed record AppSettings
{
    /// <summary>Valore minimo accettabile di <see cref="FontSize"/>.</summary>
    public const double MinFontSize = 6;

    /// <summary>Valore massimo accettabile di <see cref="FontSize"/>.</summary>
    public const double MaxFontSize = 72;

    /// <summary>Intervallo minimo accettabile di <see cref="MonitorIntervalMs"/>.</summary>
    public const int MinMonitorIntervalMs = 100;

    /// <summary>Intervallo massimo accettabile di <see cref="MonitorIntervalMs"/>.</summary>
    public const int MaxMonitorIntervalMs = 60_000;

    /// <summary>Interprete da avviare.</summary>
    public string Shell { get; init; } = DefaultShell();

    /// <summary>Argomenti passati all'interprete.</summary>
    public List<string> ShellArgs { get; init; } = [];

    /// <summary>Directory iniziale, oppure <c>null</c> per la directory corrente.</summary>
    public string? StartDirectory { get; init; }

    /// <summary>Nome del tema attivo.</summary>
    public string Theme { get; init; } = "tron";

    /// <summary>Dimensione del carattere in punti.</summary>
    public double FontSize { get; init; } = 14;

    /// <summary>Numero di righe conservate nello scrollback.</summary>
    public int Scrollback { get; init; } = 2000;

    /// <summary>Mostra i file nascosti nel browser.</summary>
    public bool ShowHiddenFiles { get; init; }

    /// <summary>Host usato per il controllo della connettività.</summary>
    public string PingHost { get; init; } = "one.one.one.one";

    /// <summary>Intervallo di aggiornamento del monitor in millisecondi.</summary>
    public int MonitorIntervalMs { get; init; } = 1000;

    /// <summary>Mostra la tastiera a schermo.</summary>
    public bool ShowKeyboard { get; init; } = true;

    /// <summary>Layout di tastiera, oppure <c>auto</c> per la rilevazione automatica.</summary>
    public string KeyboardLayout { get; init; } = "auto";

    /// <summary>Abilita gli effetti sonori.</summary>
    public bool Sounds { get; init; } = true;

    /// <summary>Abilita i suoni della tastiera.</summary>
    public bool KeyboardSounds { get; init; } = true;

    /// <summary>Volume degli effetti sonori, tra 0 e 1.</summary>
    public double SoundVolume { get; init; } = 0.4;

    /// <summary>Abilita la geolocalizzazione online.</summary>
    public bool OnlineGeolocation { get; init; } = true;

    /// <summary>
    /// Restituisce i problemi di validazione: una lista vuota significa che le
    /// impostazioni sono utilizzabili. Non modifica lo stato e non lancia eccezioni.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];
        if (string.IsNullOrWhiteSpace(Shell))
        {
            problems.Add("Shell non può essere vuoto.");
        }

        if (IsOutOfRange(FontSize, MinFontSize, MaxFontSize))
        {
            problems.Add($"FontSize deve essere tra {MinFontSize} e {MaxFontSize} (valore: {FontSize}).");
        }

        if (Scrollback <= 0)
        {
            problems.Add($"Scrollback deve essere positivo (valore: {Scrollback}).");
        }

        if (MonitorIntervalMs is < MinMonitorIntervalMs or > MaxMonitorIntervalMs)
        {
            problems.Add($"MonitorIntervalMs deve essere tra {MinMonitorIntervalMs} e {MaxMonitorIntervalMs} (valore: {MonitorIntervalMs}).");
        }

        if (IsOutOfRange(SoundVolume, 0, 1))
        {
            problems.Add($"SoundVolume deve essere tra 0 e 1 (valore: {SoundVolume}).");
        }

        if (string.IsNullOrWhiteSpace(Theme))
        {
            problems.Add("Theme non può essere vuoto.");
        }

        if (StartDirectory is { Length: > 0 } start && !Path.IsPathFullyQualified(start))
        {
            problems.Add("StartDirectory deve essere un percorso assoluto quando specificato.");
        }

        return problems;
    }

    private static bool IsOutOfRange(double value, double min, double max) =>
        !double.IsFinite(value) || value < min || value > max;

    /// <summary>Interprete predefinito per la piattaforma corrente.</summary>
    public static string DefaultShell() =>
        DefaultShell(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("SHELL"));

    internal static string DefaultShell(string? pathVariable, string? shellVariable)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsShell(pathVariable);
        }

        return string.IsNullOrWhiteSpace(shellVariable) ? UnixShell() : shellVariable;
    }

    private static string WindowsShell(string? pathVariable)
    {
        var onPath = (pathVariable ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "pwsh.exe")));
        return onPath ? "pwsh.exe" : "powershell.exe";
    }

    private static string UnixShell() => OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
}

/// <summary>Lettura e scrittura delle impostazioni su file JSON, con scrittura atomica.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Directory contenente il file di impostazioni.</summary>
    public string Directory { get; }

    /// <summary>Percorso del file di impostazioni.</summary>
    public string FilePath => Path.Combine(Directory, "settings.json");

    /// <summary>Directory contenente i temi personalizzati.</summary>
    public string ThemesDirectory => Path.Combine(Directory, "themes");

    /// <summary>Crea lo store nella directory predefinita dell'utente.</summary>
    public SettingsStore() : this(null)
    {
    }

    /// <summary>
    /// Crea lo store nella directory indicata, oppure in quella predefinita
    /// quando <paramref name="directory"/> è <c>null</c>.
    /// </summary>
    public SettingsStore(string? directory)
    {
        Directory = directory
            ?? Environment.GetEnvironmentVariable("EDEX_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "eDEX-UI-Next");
    }

    /// <summary>
    /// Legge le impostazioni con semantica best-effort: in caso di file assente,
    /// corrotto o non leggibile registra l'evento e restituisce i valori predefiniti.
    /// Per una lettura che non nasconda gli errori usare <see cref="LoadStrict"/>.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceInformation($"SettingsStore.Load: uso i valori predefiniti, lettura di '{FilePath}' fallita: {e.GetType().Name}: {e.Message}");
        }
        return new AppSettings();
    }

    /// <summary>
    /// Legge le impostazioni e convalida i valori. Solleva
    /// <see cref="InvalidOperationException"/> con l'eccezione originale come
    /// InnerException quando il file non è leggibile, e segnala tutti i valori
    /// non validi invece di restituire impostazioni inutilizzabili.
    /// </summary>
    /// <exception cref="InvalidOperationException">Lettura o validazione non riuscita.</exception>
    public AppSettings LoadStrict()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceInformation($"SettingsStore.LoadStrict: lettura di '{FilePath}' fallita: {e.GetType().Name}: {e.Message}");
            throw new InvalidOperationException($"Impossibile leggere le impostazioni da '{FilePath}'.", e);
        }

        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            var message = $"Impostazioni non valide in '{FilePath}': {string.Join("; ", problems)}";
            Trace.TraceInformation($"SettingsStore.LoadStrict: {message}");
            throw new InvalidOperationException(message);
        }
        return settings;
    }

    /// <summary>Salva le impostazioni, sostituendo atomicamente il file esistente.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        System.IO.Directory.CreateDirectory(Directory);
        System.IO.Directory.CreateDirectory(ThemesDirectory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>
    /// Convalida le impostazioni prima di scriverle: evita di persistere valori
    /// che <see cref="LoadStrict"/> rifiuterebbe al prossimo avvio.
    /// </summary>
    /// <exception cref="InvalidOperationException">Le impostazioni contengono valori non validi.</exception>
    public void SaveValidated(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            var message = $"Impostazioni non valide, nulla è stato scritto: {string.Join("; ", problems)}";
            Trace.TraceInformation($"SettingsStore.SaveValidated: {message}");
            throw new InvalidOperationException(message);
        }
        Save(settings);
    }
}
