namespace ExpansorTexto;

internal static class Program
{
    private const string MutexName = "Local\\ExpansorTexto_Desktop_SingleInstance";
    private const string ShowEventName = "Local\\ExpansorTexto_Desktop_ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool created);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!created)
        {
            // Já existe uma instância: pede a ela que mostre a janela e encerra silenciosamente.
            try { showSignal.Set(); } catch { }
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Error("Exceção não tratada na interface.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Exceção fatal não tratada.", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();

        bool startHidden = args.Any(a => string.Equals(a, MainForm.TrayArgument, StringComparison.OrdinalIgnoreCase));
        try
        {
            Application.Run(new MainForm(startHidden, showSignal));
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { }
        }
    }
}
