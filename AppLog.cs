using System.Text;

namespace ExpansorTexto;

/// <summary>
/// Log técnico local. NUNCA recebe texto digitado, atalhos, textos de substituição ou títulos de janela;
/// apenas eventos, contagens, códigos de erro e nomes de processo.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(SettingsStore.Folder, "expansor.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("AVISO", message);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex is not null)
            message += $" [{ex.GetType().Name}, HResult 0x{ex.HResult:X8}: {Truncate(ex.Message, 300)}]";
        Write("ERRO", message);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(SettingsStore.Folder, "expansor.old.log"), true);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {level,-5}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Falha de log nunca deve derrubar o aplicativo nem o hook.
        }
    }
}
