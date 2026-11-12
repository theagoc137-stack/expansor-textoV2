using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ExpansorTexto;

/// <summary>
/// Captura global com WH_KEYBOARD_LL e expansão com SendInput Unicode (sem área de transferência).
///
/// Modelo de threads:
///  - Hook: roda na thread de interface (que bombeia mensagens). É a ÚNICA dona de _buffer, _pending e
///    do estado de tecla morta. Nunca bloqueia: só lê um snapshot imutável (EngineConfig).
///  - Expansão: uma tarefa dedicada envia as teclas com o atraso configurado. Comunica-se com o hook
///    apenas por _expanding (Interlocked), _expandDeadline e a fila de digitação antecipada (com lock).
/// </summary>
internal sealed class KeyboardEngine : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10, LLKHF_LOWER_IL_INJECTED = 0x02;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

    private const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14;
    private const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_PRIOR = 0x21, VK_NEXT = 0x22;
    private const int VK_END = 0x23, VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    private const int VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int VK_PROCESSKEY = 0xE5, VK_PACKET = 0xE7;

    private const int BufferLimit = 128;
    private const long IdleResetMs = 10_000;
    private const long PendingTimeoutMs = 1_500;
    private const int TypeAheadLimit = 64;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public UIntPtr dwExtraInfo;
    }

    // IMPORTANTE: a união precisa conter MOUSEINPUT (o maior membro) para que sizeof(INPUT) seja 40 bytes
    // em x64. Com apenas KEYBDINPUT o tamanho seria 32 e o SendInput falharia com ERROR_INVALID_PARAMETER.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    private sealed record PendingExpansion(
        ExpansionRule Rule, string Delimiter, int DelimiterVk, int TypedLength,
        int DelayMs, IntPtr Foreground, long Tick);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly HookProc _hookProc;      // referência mantida para o GC não coletar o delegate
    private EngineConfig _config;
    private IntPtr _hook;
    private volatile bool _disposed;

    // --- Estado pertencente exclusivamente à thread do hook ---
    private readonly StringBuilder _buffer = new();
    private PendingExpansion? _pending;
    private bool _hasDead;
    private uint _deadVk, _deadScan;
    private byte[] _deadState = new byte[256];
    private IntPtr _lastForeground, _lastFocus;
    private long _lastInputTick;
    private int _activeDelimiterVk = -1;
    private int _hookErrors;

    // --- Estado compartilhado com a tarefa de expansão ---
    private int _expanding;
    private long _expandDeadline;
    private readonly object _taLock = new();
    private readonly StringBuilder _typeAhead = new();

    public KeyboardEngine(EngineConfig config)
    {
        _config = config;
        _hookProc = HookCallback;
        if (!Install(out int error))
            throw new InvalidOperationException($"Não foi possível instalar o monitor de teclado (erro do Windows {error}).");
        AppLog.Info("Monitoramento global de teclado instalado.");
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void UpdateConfig(EngineConfig config) => Volatile.Write(ref _config, config);

    private bool Install(out int error)
    {
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        error = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (_hook == IntPtr.Zero)
            AppLog.Error($"SetWindowsHookEx falhou. Erro do Windows: {error}.");
        return _hook != IntPtr.Zero;
    }

    /// <summary>
    /// Reinstala o hook (chamar na thread de interface). O Windows pode remover hooks silenciosamente
    /// (timeout, bloqueio/desbloqueio de sessão, suspensão). Só reinstala quando não há expansão em curso.
    /// </summary>
    public bool Reinstall(string reason, bool logSuccess)
    {
        if (_disposed) return false;
        if (Volatile.Read(ref _expanding) != 0 || _pending is not null) return false;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        ResetBuffer();
        bool ok = Install(out _);
        if (ok && logSuccess) AppLog.Info("Monitor de teclado reinstalado (" + reason + ").");
        return ok;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    // ------------------------------------------------------------------ hook

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _disposed) return CallNextHookEx(_hook, nCode, wParam, lParam);
        try
        {
            if (HandleKey(wParam, lParam)) return new IntPtr(1);
        }
        catch (Exception ex)
        {
            // Uma exceção não tratada atravessando o hook derrubaria o processo.
            if (Interlocked.Increment(ref _hookErrors) <= 5) AppLog.Error("Erro no tratamento de tecla.", ex);
            _pending = null;
            ResetBuffer();
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Retorna true quando a tecla deve ser engolida (não chega ao aplicativo de destino).</summary>
    private bool HandleKey(IntPtr wParam, IntPtr lParam)
    {
        var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Ignora toda entrada injetada (inclusive a nossa): evita recursão.
        if ((key.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0) return false;

        int message = unchecked((int)wParam.ToInt64());
        bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
        bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
        if (!down && !up) return false;

        int vk = (int)key.vkCode;
        var config = Volatile.Read(ref _config);
        long now = Environment.TickCount64;

        // 1) Expansão em andamento: engole teclas físicas pressionadas (evita mistura com o texto
        //    injetado), mas deixa os "soltar tecla" passarem para não deixar modificadores presos.
        if (Volatile.Read(ref _expanding) != 0)
        {
            if (now > Interlocked.Read(ref _expandDeadline))
            {
                Interlocked.Exchange(ref _expanding, 0);
                AppLog.Warn("Expansão excedeu o tempo previsto; bloqueio de teclado liberado por segurança.");
            }
            else
            {
                if (down && vk != _activeDelimiterVk) QueueTypeAhead(key, vk);
                return down;
            }
        }

        // 2) Pausado: não observa nada e não deixa o buffer contaminado.
        if (!config.Enabled)
        {
            if (_buffer.Length > 0 || _pending is not null) { _pending = null; ResetBuffer(); }
            return false;
        }

        // 3) Delimitador aguardando ser solto.
        var pending = _pending;
        if (pending is not null)
        {
            if (now - pending.Tick > PendingTimeoutMs)
            {
                _pending = null;
                ResetBuffer();
                AppLog.Warn("Delimitador pendente não foi solto a tempo; expansão cancelada.");
            }
            else if (vk == pending.DelimiterVk)
            {
                if (up) { _pending = null; StartExpansion(pending); }
                return true; // keydown repetido (auto-repeat) ou keyup do delimitador
            }
            else if (down)
            {
                // Digitação rápida (rollover): outra tecla desceu antes de soltar o delimitador.
                _pending = null;
                StartExpansion(pending);
                QueueTypeAhead(key, vk);
                return true;
            }
            else return false; // keyup de outra tecla
        }

        if (!down) return false;
        return ProcessKeyDown(key, vk, config, now);
    }

    private bool ProcessKeyDown(KBDLLHOOKSTRUCT key, int vk, EngineConfig config, long now)
    {
        if (IsModifierOrLock(vk)) return false;

        GetForegroundContext(out IntPtr foreground, out IntPtr focus, out IntPtr layout);
        if (foreground != _lastForeground || focus != _lastFocus || now - _lastInputTick > IdleResetMs)
            ResetBuffer();
        _lastForeground = foreground;
        _lastFocus = focus;
        _lastInputTick = now;

        bool ctrl = IsDown(VK_CONTROL), alt = IsDown(VK_MENU);
        bool win = IsDown(VK_LWIN) || IsDown(VK_RWIN);
        // Ctrl+Alt juntos = AltGr (ABNT2: / ? € etc.), permitido. Qualquer outro atalho zera o buffer.
        if (win || ((ctrl || alt) && !(ctrl && alt))) { ResetBuffer(); return false; }

        if (vk == VK_PACKET || vk == VK_PROCESSKEY || IsNavigationKey(vk)) { ResetBuffer(); return false; }

        if (vk == VK_BACK)
        {
            if (_hasDead) _hasDead = false;
            else if (_buffer.Length > 0) _buffer.Length--;
            return false;
        }

        bool isSpace = vk == VK_SPACE, isEnter = vk == VK_RETURN, isTab = vk == VK_TAB;
        if (isSpace || isEnter || isTab)
        {
            bool enabled = isSpace ? config.Space : isEnter ? config.Enter : config.Tab;
            // Delimitador desativado (ou logo após tecla morta): passa normalmente, mas encerra a palavra.
            if (_hasDead || !enabled) { ResetBuffer(); return false; }

            string delimiter = isSpace ? " " : isEnter ? "\n" : "\t";
            if (TryStartPending(config, delimiter, vk, foreground, now)) return true;
            ResetBuffer();
            return false;
        }

        if (!TryTranslate(key, layout, out string chars)) return false;

        if (chars.Length == 1 && config.IsPunctuation(chars[0]))
        {
            if (TryStartPending(config, chars, vk, foreground, now)) return true;
            // Pontuação sem atalho: encerra a palavra anterior, mas mantém o sinal como possível início
            // de um novo atalho (ex.: ;ola). Prefixos só de pontuação (::x, ;;x) são acumulados.
            if (!BufferHasOnlyPunctuation(config)) ResetBuffer();
            Append(chars);
            return false;
        }

        Append(chars);
        return false;
    }

    private bool TryStartPending(EngineConfig config, string delimiter, int vk, IntPtr foreground, long now)
    {
        if (_buffer.Length == 0) return false;
        if (!config.Rules.TryGetValue(_buffer.ToString(), out var rule)) return false;
        _pending = new PendingExpansion(rule, delimiter, vk, _buffer.Length, config.DelayMs, foreground, now);
        return true; // o keydown do delimitador é engolido; ele é reinserido depois do texto
    }

    private static bool IsModifierOrLock(int vk)
        => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL or 0x90 or 0x91 || (vk >= 0xA0 && vk <= 0xA5);

    private static bool IsNavigationKey(int vk)
        => vk is VK_ESCAPE or VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN or VK_HOME or VK_END
            or VK_PRIOR or VK_NEXT or VK_DELETE or VK_INSERT || (vk >= 0x70 && vk <= 0x87);

    private bool BufferHasOnlyPunctuation(EngineConfig config)
    {
        for (int i = 0; i < _buffer.Length; i++)
            if (!config.IsPunctuation(_buffer[i])) return false;
        return true;
    }

    private void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _buffer.Append(text);
        if (_buffer.Length > BufferLimit) _buffer.Remove(0, _buffer.Length - BufferLimit);
    }

    private void ResetBuffer() { _buffer.Clear(); _hasDead = false; }

    // ------------------------------------------------- tradução (ABNT2 / teclas mortas)

    private static byte[] BuildKeyState()
    {
        var state = new byte[256];
        if (IsDown(VK_SHIFT)) state[VK_SHIFT] = 0x80;
        if (IsDown(VK_CONTROL)) state[VK_CONTROL] = 0x80;
        if (IsDown(VK_MENU)) state[VK_MENU] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) state[VK_CAPITAL] = 1;
        return state;
    }

    /// <summary>
    /// Converte a tecla no(s) caractere(s) que o aplicativo de destino vai receber, usando o layout da
    /// janela ativa. Teclas mortas (´ ` ~ ^ ¨ no ABNT2) são lembradas e combinadas com a tecla seguinte,
    /// então ´ + a vira "á" no buffer, igual ao que aparece no destino.
    /// </summary>
    private bool TryTranslate(KBDLLHOOKSTRUCT key, IntPtr layout, out string text)
    {
        text = "";
        byte[] state = BuildKeyState();
        var output = new StringBuilder(8);

        if (_hasDead)
        {
            _hasDead = false;
            // Recria o estado da tecla morta pendente (flags 0 altera o estado) e consome com a tecla atual.
            ToUnicodeEx(_deadVk, _deadScan, _deadState, output, output.Capacity, 0, layout);
            output.Clear();
            int composed = ToUnicodeEx(key.vkCode, key.scanCode, state, output, output.Capacity, 0, layout);
            if (composed < 0)
            {
                RememberDeadKey(key, state);
                FlushKeyboardState(layout); // evita deixar estado de tecla morta no sistema
                return false;
            }
            if (composed == 0) return false;
            text = output.ToString(0, Math.Min(composed, output.Length));
            return text.Length > 0;
        }

        // 0x4: não altera o estado do teclado (Windows 10 1607+).
        int count = ToUnicodeEx(key.vkCode, key.scanCode, state, output, output.Capacity, 0x4, layout);
        if (count < 0) { RememberDeadKey(key, state); return false; }
        if (count == 0) return false;
        text = output.ToString(0, Math.Min(count, output.Length));
        return text.Length > 0;
    }

    private void RememberDeadKey(KBDLLHOOKSTRUCT key, byte[] state)
    {
        _hasDead = true;
        _deadVk = key.vkCode;
        _deadScan = key.scanCode;
        _deadState = (byte[])state.Clone();
    }

    private static void FlushKeyboardState(IntPtr layout)
    {
        var sb = new StringBuilder(8);
        ToUnicodeEx(VK_SPACE, 0x39, new byte[256], sb, sb.Capacity, 0, layout);
    }

    private static void GetForegroundContext(out IntPtr foreground, out IntPtr focus, out IntPtr layout)
    {
        foreground = GetForegroundWindow();
        focus = IntPtr.Zero;
        uint threadId = 0;
        if (foreground != IntPtr.Zero)
        {
            threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(threadId, ref info)) focus = info.hwndFocus;
        }
        layout = GetKeyboardLayout(threadId);
    }

    // ------------------------------------------------- digitação durante a expansão

    private void QueueTypeAhead(KBDLLHOOKSTRUCT key, int vk)
    {
        if (IsModifierOrLock(vk)) return;

        string? add = null;
        if (vk != VK_BACK)
        {
            GetForegroundContext(out _, out _, out IntPtr layout);
            bool translated = TryTranslate(key, layout, out string chars);
            _hasDead = false; // uma tecla morta digitada durante a expansão é descartada
            if (!translated) return;
            add = chars;
        }

        lock (_taLock)
        {
            if (Volatile.Read(ref _expanding) == 0) return;
            if (add is null)
            {
                if (_typeAhead.Length > 0) _typeAhead.Length--;
            }
            else if (_typeAhead.Length + add.Length <= TypeAheadLimit)
            {
                _typeAhead.Append(add);
            }
        }
    }

    // ------------------------------------------------------------- expansão

    private void StartExpansion(PendingExpansion pending)
    {
        ResetBuffer(); // o atalho digitado será consumido
        lock (_taLock) _typeAhead.Clear();
        _activeDelimiterVk = pending.DelimiterVk;
        long estimate = (long)(pending.TypedLength + pending.Rule.Replacement.Length + 4) * (pending.DelayMs + 20) + 4000;
        Interlocked.Exchange(ref _expandDeadline, Environment.TickCount64 + estimate);
        Interlocked.Exchange(ref _expanding, 1);
        try
        {
            Task.Factory.StartNew(() => Expand(pending), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _expanding, 0);
            AppLog.Error("Não foi possível iniciar a tarefa de expansão.", ex);
        }
    }

    private void Expand(PendingExpansion pending)
    {
        try { RunExpansion(pending); }
        catch (Exception ex) { AppLog.Error("Erro durante a expansão.", ex); }
        finally { FinishExpansion(pending.DelayMs); }
    }

    private void RunExpansion(PendingExpansion p)
    {
        // Se Shift/Ctrl/Alt ainda estiverem pressionados (ex.: delimitador digitado com Shift ou AltGr),
        // o Backspace/Enter injetado seria interpretado como atalho. Aguarda a liberação (máx. 600 ms).
        if (!WaitForModifiersRelease(600))
            AppLog.Warn("Modificadores ainda pressionados após 600 ms; a expansão prosseguiu.");

        if (GetForegroundWindow() != p.Foreground)
        {
            AppLog.Warn("Expansão cancelada: a janela ativa mudou antes do envio.");
            return;
        }

        int delay = p.DelayMs;
        for (int i = 0; i < p.TypedLength; i++)
        {
            if (_disposed) return;
            if (!SendVirtualKey(VK_BACK))
            {
                AppLog.Error("SendInput falhou ao apagar o atalho.");
                if (i == 0) SendDelimiter(p); // nada foi alterado: devolve o delimitador engolido
                return;
            }
            if (delay > 0) Thread.Sleep(delay);
        }

        if (!SendText(p.Rule.Replacement, delay))
        {
            AppLog.Error("SendInput falhou ao inserir o texto.");
            return;
        }

        SendDelimiter(p);
        AppLog.Info($"Expansão concluída em {ProcessNameOf(p.Foreground)}: {p.TypedLength} caractere(s) apagado(s), " +
                    $"{p.Rule.Replacement.Length} inserido(s), atraso {delay} ms, delimitador preservado.");
    }

    private void FinishExpansion(int delay)
    {
        try
        {
            while (!_disposed)
            {
                string chunk;
                lock (_taLock)
                {
                    if (_typeAhead.Length == 0)
                    {
                        Interlocked.Exchange(ref _expanding, 0);
                        return;
                    }
                    chunk = _typeAhead.ToString();
                    _typeAhead.Clear();
                }
                if (!SendText(chunk, delay)) break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Erro ao reenviar teclas digitadas durante a expansão.", ex);
        }
        lock (_taLock) _typeAhead.Clear();
        Interlocked.Exchange(ref _expanding, 0);
    }

    private static bool WaitForModifiersRelease(int timeoutMs)
    {
        long limit = Environment.TickCount64 + timeoutMs;
        while (IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN))
        {
            if (Environment.TickCount64 >= limit) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    private static string ProcessNameOf(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "processo desconhecido";
        }
    }

    // ------------------------------------------------------------ SendInput

    /// <summary>Envia o texto com o atraso configurado após cada caractere (comportamento validado na PoC).</summary>
    private bool SendText(string text, int delayMs)
    {
        text = EngineConfig.NormalizeNewlines(text);
        for (int i = 0; i < text.Length; i++)
        {
            if (_disposed) return false;
            char ch = text[i];
            bool ok;
            if (ch == '\n') ok = SendVirtualKey(VK_RETURN);
            else if (ch == '\t') ok = SendVirtualKey(VK_TAB);
            else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // Emoji/caracteres fora do BMP: as duas metades vão no mesmo SendInput, sem atraso entre elas.
                ok = SendUnicodePair(ch, text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(ch) || char.IsControl(ch)) continue; // surrogate solto ou controle: ignora
            else ok = SendUnicodeChar(ch);

            if (!ok) return false;
            if (delayMs > 0) Thread.Sleep(delayMs);
        }
        return true;
    }

    private static void SendDelimiter(PendingExpansion p)
    {
        if (p.Delimiter == "\n") SendVirtualKey(VK_RETURN);
        else if (p.Delimiter == "\t") SendVirtualKey(VK_TAB);
        else if (p.Delimiter == " ") SendVirtualKey(VK_SPACE);
        else foreach (char ch in p.Delimiter) SendUnicodeChar(ch);
    }

    private static bool SendVirtualKey(int vk)
        => SendChecked(new[] { MakeInput((ushort)vk, 0, 0), MakeInput((ushort)vk, 0, KEYEVENTF_KEYUP) });

    private static bool SendUnicodeChar(char ch)
        => SendChecked(new[]
        {
            MakeInput(0, ch, KEYEVENTF_UNICODE),
            MakeInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)
        });

    private static bool SendUnicodePair(char high, char low)
        => SendChecked(new[]
        {
            MakeInput(0, high, KEYEVENTF_UNICODE),
            MakeInput(0, high, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
            MakeInput(0, low, KEYEVENTF_UNICODE),
            MakeInput(0, low, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)
        });

    private static INPUT MakeInput(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    private static bool SendChecked(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == inputs.Length) return true;
        AppLog.Warn($"SendInput aceitou {sent} de {inputs.Length} eventos. Erro do Windows: {Marshal.GetLastWin32Error()}.");
        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ------------------------------------------------------------- P/Invoke

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr hMod, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyboardState, StringBuilder buffer, int bufferSize, uint flags, IntPtr keyboardLayout);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
