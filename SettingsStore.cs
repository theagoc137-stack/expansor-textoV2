using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ExpansorTexto;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Mantém acentos e emojis legíveis no arquivo local.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Folder { get; } = ResolveFolder();
    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    /// <summary>True se o config.json existia mas não pôde ser lido (foi feita cópia de segurança).</summary>
    public static bool LoadFailed { get; private set; }
    public static string? BackupPath { get; private set; }

    private static string ResolveFolder()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
        return Path.Combine(root, "ExpansorTexto");
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            string json = ReadAllTextWithRetry(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (loaded is null) throw new JsonException("Arquivo de configuração vazio.");
            return Normalize(loaded);
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao ler config.json. Será usada a configuração padrão.", ex);
            LoadFailed = true;
            BackupUnreadableFile();
            return new AppSettings();
        }
    }

    private static string ReadAllTextWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) when (attempt < 3) { Thread.Sleep(150); }
        }
    }

    private static AppSettings Normalize(AppSettings s)
    {
        s.Delimiters ??= new DelimiterSettings();
        s.Delimiters.Punctuation ??= ".,!?:;";
        s.DelayMs = Math.Clamp(s.DelayMs, 0, 100);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<ShortcutRule>();
        foreach (var r in s.Rules ?? new List<ShortcutRule>())
        {
            if (r is null) continue;
            string trigger = (r.Trigger ?? "").Trim();
            if (trigger.Length == 0 || trigger.Any(char.IsWhiteSpace)) continue;
            if (!seen.Add(trigger)) continue; // duplicados (sem diferenciar maiúsculas): mantém o primeiro
            rules.Add(new ShortcutRule { Trigger = trigger, Replacement = r.Replacement ?? "" });
        }
        s.Rules = rules;
        return s;
    }

    private static void BackupUnreadableFile()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string backup = Path.Combine(Folder, $"config.invalido-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, backup, true);
            BackupPath = backup;
        }
        catch (Exception ex)
        {
            AppLog.Error("Não foi possível copiar o config.json ilegível.", ex);
        }
    }

    /// <summary>Grava em arquivo temporário, força flush em disco e troca o original (mantendo config.json.bak).</summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        string json = JsonSerializer.Serialize(settings, Options);

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }
}
