namespace ExpansorTexto;

public sealed class ShortcutRule
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
}

public sealed class DelimiterSettings
{
    public bool Space { get; set; } = true;
    public bool Enter { get; set; } = false;
    public bool Tab { get; set; } = false;
    public string Punctuation { get; set; } = ".,!?:;";
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public int DelayMs { get; set; } = 10;
    public bool RunAtStartup { get; set; } = false;
    public DelimiterSettings Delimiters { get; set; } = new();
    public List<ShortcutRule> Rules { get; set; } = new();
}

/// <summary>Regra imutável usada pelo motor de teclado.</summary>
internal sealed record ExpansionRule(string Trigger, string Replacement);

/// <summary>
/// Cópia imutável das configurações que o motor de teclado lê. É criada na thread de interface
/// e publicada por referência; o hook e a tarefa de expansão nunca tocam em AppSettings, então
/// não há locks nem cópias a cada tecla.
/// </summary>
internal sealed class EngineConfig
{
    private readonly HashSet<char> _punctuation;

    public bool Enabled { get; }
    public int DelayMs { get; }
    public bool Space { get; }
    public bool Enter { get; }
    public bool Tab { get; }
    public IReadOnlyDictionary<string, ExpansionRule> Rules { get; }

    private EngineConfig(bool enabled, int delayMs, bool space, bool enter, bool tab,
        HashSet<char> punctuation, Dictionary<string, ExpansionRule> rules)
    {
        Enabled = enabled;
        DelayMs = delayMs;
        Space = space;
        Enter = enter;
        Tab = tab;
        _punctuation = punctuation;
        Rules = rules;
    }

    public bool IsPunctuation(char c) => _punctuation.Contains(c);

    public static EngineConfig From(AppSettings settings)
    {
        var delimiters = settings.Delimiters ?? new DelimiterSettings();
        var punctuation = new HashSet<char>(SanitizePunctuation(delimiters.Punctuation));
        var rules = new Dictionary<string, ExpansionRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in settings.Rules ?? new List<ShortcutRule>())
        {
            if (string.IsNullOrEmpty(rule.Trigger) || string.IsNullOrEmpty(rule.Replacement)) continue;
            rules.TryAdd(rule.Trigger, new ExpansionRule(rule.Trigger, NormalizeNewlines(rule.Replacement)));
        }
        return new EngineConfig(settings.Enabled, Math.Clamp(settings.DelayMs, 0, 100),
            delimiters.Space, delimiters.Enter, delimiters.Tab, punctuation, rules);
    }

    /// <summary>Mantém apenas sinais de pontuação/símbolos: sem letras, dígitos, espaços, controles ou emojis.</summary>
    public static string SanitizePunctuation(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var seen = new HashSet<char>();
        var result = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c)) continue;
            if (seen.Add(c)) result.Append(c);
        }
        return result.ToString();
    }

    public static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
