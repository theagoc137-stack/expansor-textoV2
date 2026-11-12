using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using WinTimer = System.Windows.Forms.Timer;

namespace ExpansorTexto;

public sealed partial class MainForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 71;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002;
    private const uint VK_F10 = 0x79;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ExpansorTexto";
    internal const string TrayArgument = "--tray";

    // _settings só é lido/alterado na thread de interface. O motor recebe cópias imutáveis (EngineConfig).
    private readonly AppSettings _settings;
    private EngineConfig _config;
    private KeyboardEngine? _engine;
    private bool _engineFailed;
    private bool _engineStartRequested;

    private bool _allowVisible;
    private readonly EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;

    private bool _exitRequested;
    private bool _loadingControls;
    private bool _refreshingRules;
    private bool _selectionQueued;
    private bool _dirty;
    private bool _balloonShown;
    private bool _persistFailureNotified;
    private string? _editingTrigger;

    private readonly WinTimer _saveTimer = new() { Interval = 600 };
    private readonly WinTimer _noticeTimer = new() { Interval = 3500 };
    private readonly WinTimer _healthTimer = new() { Interval = 180_000 };

    private readonly Icon _iconActive;
    private readonly Icon _iconPaused;
    private NotifyIcon _tray = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _trayToggle = null!;

    public MainForm(bool startHidden = false, EventWaitHandle? showSignal = null)
    {
        _allowVisible = !startHidden;
        _showSignal = showSignal;

        // Escala por DPI: sem isso as alturas/larguras absolutas ficam cortadas em telas 125%/150%.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Expansor de Texto";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 630);
        Size = new Size(1060, 760);
        BackColor = _page;
        ForeColor = _ink;
        Font = F("Segoe UI", 9F);
        DoubleBuffered = true;

        _settings = SettingsStore.Load();
        _config = EngineConfig.From(_settings);
        _iconActive = AppIconFactory.Create(false);
        _iconPaused = AppIconFactory.Create(true);
        Icon = _iconActive;

        BuildInterface();
        LoadSettingsIntoControls();
        CreateTray();
        RefreshRules(null);
        UpdateReplacementInfo();

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); PersistNow(); };
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); _notice.Text = ""; };
        _healthTimer.Tick += (_, _) => _engine?.Reinstall("verificação periódica", false);

        _pet.Attach(this);
        UpdateStatus();
        SyncStartupRegistration();
        RegisterShowSignal();

        AppLog.Info("Aplicativo iniciado (versão " + (typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "?") +
                    "). Criador: Theago Oliveira. Configuração: " + SettingsStore.FilePath);

        // Início minimizado na bandeja: cria o handle sem exibir a janela (necessário para o atalho global).
        if (startHidden) _ = Handle;
    }

    // ------------------------------------------------------------ ciclo de vida

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(_allowVisible && value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_F10))
            AppLog.Warn($"Não foi possível registrar Ctrl+Alt+F10 (erro {Marshal.GetLastWin32Error()}). A combinação pode estar em uso por outro programa.");

        // O hook é instalado depois que o loop de mensagens começa (BeginInvoke), evitando atrasar o teclado
        // do sistema durante a construção da janela.
        if (!_engineStartRequested)
        {
            _engineStartRequested = true;
            BeginInvoke(new Action(StartEngine));
        }
    }

    private void StartEngine()
    {
        if (IsDisposed) return;
        try
        {
            _engine = new KeyboardEngine(_config);
            _engineFailed = false;
            _healthTimer.Start();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            _engine = null;
            _engineFailed = true;
            AppLog.Error("Falha ao iniciar o monitor de teclado.", ex);
            MessageBox.Show("Não foi possível iniciar o monitoramento global de teclado.\n\n" + ex.Message +
                            "\n\nO programa continuará aberto, mas as substituições não funcionarão.",
                "Expansor de Texto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateStatus();

        if (SettingsStore.LoadFailed)
        {
            string where = SettingsStore.BackupPath is null ? "" : "\n\nUma cópia do arquivo original foi salva em:\n" + SettingsStore.BackupPath;
            MessageBox.Show("O arquivo de configuração não pôde ser lido e foi iniciado com valores padrão." + where,
                "Expansor de Texto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon
            or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            PostToUi(() => _engine?.Reinstall("sessão reativada", true));
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            PostToUi(() => _engine?.Reinstall("retomada do sistema", true));
    }

    private void PostToUi(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
        }
        catch (InvalidOperationException) { }
        catch (ObjectDisposedException) { }
    }

    private void RegisterShowSignal()
    {
        if (_showSignal is null) return;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, timedOut) =>
        {
            if (!timedOut) PostToUi(ShowMainWindow);
        }, null, Timeout.Infinite, false);
    }

    private void ShowMainWindow()
    {
        _allowVisible = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide(); // o mascote para sozinho: o controle deixa de ser Visible
            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(1500, "Expansor de Texto", "O programa continua ativo na bandeja do Windows.", ToolTipIcon.Info);
            }
        }
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _saveTimer.Stop();
        PersistNow();
        if (IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
        AppLog.Info("Aplicativo encerrado.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _showWait?.Unregister(null);
            _saveTimer.Dispose();
            _noticeTimer.Dispose();
            _healthTimer.Dispose();
            _engine?.Dispose();
            _engine = null;
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
            _trayMenu?.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _iconActive.Dispose();
            _iconPaused.Dispose();
            foreach (var bitmap in _bitmaps) bitmap.Dispose();
            foreach (var font in _fonts.Values) font.Dispose();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            _enabled.Checked = !_enabled.Checked;
            _tray.ShowBalloonTip(900, "Expansor de Texto",
                _enabled.Checked ? "Substituição ativada." : "Substituição pausada.", ToolTipIcon.Info);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            SaveRule();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------ bandeja e status

    private void CreateTray()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Abrir Expansor de Texto", null, (_, _) => ShowMainWindow());
        _trayToggle = new ToolStripMenuItem("Pausar", null, (_, _) => _enabled.Checked = !_enabled.Checked);
        _trayMenu.Items.Add(_trayToggle);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Sair", null, (_, _) => { _exitRequested = true; Close(); });

        _tray = new NotifyIcon
        {
            Icon = _iconActive,
            Text = "Expansor de Texto — ativo",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void UpdateStatus()
    {
        bool active = _settings.Enabled;
        if (_engineFailed)
        {
            _status.Text = "● SEM MONITOR";
            _status.ForeColor = Color.FromArgb(190, 60, 60);
        }
        else
        {
            _status.Text = active ? "● ATIVO" : "● PAUSADO";
            _status.ForeColor = active ? Color.FromArgb(34, 145, 111) : Color.FromArgb(194, 126, 42);
        }
        if (_tray is not null)
        {
            _tray.Icon = active ? _iconActive : _iconPaused;
            _tray.Text = active ? "Expansor de Texto — ativo" : "Expansor de Texto — pausado";
            _trayToggle.Text = active ? "Pausar" : "Ativar";
        }
    }

    private void ShowNotice(string text, bool warning = false)
    {
        _notice.ForeColor = warning ? Color.FromArgb(194, 126, 42) : Color.FromArgb(34, 145, 111);
        _notice.Text = text;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ------------------------------------------------------------ configuração

    private void LoadSettingsIntoControls()
    {
        _loadingControls = true;
        try
        {
            _enabled.Checked = _settings.Enabled;
            _space.Checked = _settings.Delimiters.Space;
            _enter.Checked = _settings.Delimiters.Enter;
            _tab.Checked = _settings.Delimiters.Tab;
            _punctuation.Text = EngineConfig.SanitizePunctuation(_settings.Delimiters.Punctuation);
            _delay.Value = Math.Clamp(_settings.DelayMs, 0, 100);
            _startup.Checked = _settings.RunAtStartup;
        }
        finally { _loadingControls = false; }
    }

    private void PublishConfig()
    {
        _config = EngineConfig.From(_settings);
        _engine?.UpdateConfig(_config);
    }

    private bool PersistNow()
    {
        _saveTimer.Stop();
        try
        {
            SettingsStore.Save(_settings);
            _persistFailureNotified = false;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Não foi possível salvar config.json.", ex);
            if (!_persistFailureNotified && _tray is not null)
            {
                _persistFailureNotified = true;
                _tray.ShowBalloonTip(3000, "Expansor de Texto",
                    "Não foi possível salvar as configurações em disco. Consulte o log.", ToolTipIcon.Warning);
            }
            return false;
        }
    }

    private void SchedulePersist()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnBehaviorChanged()
    {
        if (_loadingControls) return;
        _settings.Delimiters.Space = _space.Checked;
        _settings.Delimiters.Enter = _enter.Checked;
        _settings.Delimiters.Tab = _tab.Checked;
        _settings.Delimiters.Punctuation = EngineConfig.SanitizePunctuation(_punctuation.Text);
        _settings.DelayMs = (int)_delay.Value;
        PublishConfig();
        SchedulePersist(); // gravação adiada: evita escrever em disco a cada tecla digitada no campo
        UpdateReplacementInfo();
    }

    private void OnPunctuationLeave()
    {
        string clean = EngineConfig.SanitizePunctuation(_punctuation.Text);
        if (clean == _punctuation.Text) return;
        _loadingControls = true;
        _punctuation.Text = clean;
        _loadingControls = false;
    }

    private void OnEnabledChanged()
    {
        if (_loadingControls) return;
        _settings.Enabled = _enabled.Checked;
        PublishConfig();
        PersistNow();
        UpdateStatus();
        AppLog.Info(_enabled.Checked ? "Monitoramento ativado." : "Monitoramento pausado.");
    }

    // ------------------------------------------------------------ inicialização com o Windows

    private static string StartupCommand()
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        return $"\"{exe}\" {TrayArgument}";
    }

    private static void SetStartupRegistration(bool enable)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("Não foi possível abrir a chave de inicialização do Windows.");
        if (enable) key.SetValue(RunValue, StartupCommand());
        else key.DeleteValue(RunValue, false);
    }

    /// <summary>Se a opção está ativa, atualiza o caminho registrado (o executável pode ter sido movido).</summary>
    private void SyncStartupRegistration()
    {
        if (!_settings.RunAtStartup) return;
        try { SetStartupRegistration(true); }
        catch (Exception ex) { AppLog.Error("Não foi possível atualizar a inicialização com o Windows.", ex); }
    }

    private void OnStartupChanged()
    {
        if (_loadingControls) return;
        bool want = _startup.Checked;
        try
        {
            SetStartupRegistration(want);
            _settings.RunAtStartup = want;
            PersistNow();
            AppLog.Info(want ? "Inicialização com o Windows ativada." : "Inicialização com o Windows desativada.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao alterar a inicialização com o Windows.", ex);
            _loadingControls = true;
            _startup.Checked = !want;
            _loadingControls = false;
            MessageBox.Show("Não foi possível alterar a inicialização automática: " + ex.Message,
                "Configuração", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------ lista e editor de atalhos

    private static string BuildPreview(string replacement)
    {
        string preview = replacement.Replace("\r\n", " ¶ ").Replace("\n", " ¶ ").Replace("\r", " ¶ ").Replace("\t", " → ");
        if (preview.Length <= 76) return preview;
        int cut = char.IsHighSurrogate(preview[72]) ? 72 : 73; // não corta emoji ao meio
        return preview[..cut] + "…";
    }

    private ShortcutRule? FindRuleByTrigger(string? trigger)
        => trigger is null ? null
            : _settings.Rules.FirstOrDefault(r => string.Equals(r.Trigger, trigger, StringComparison.OrdinalIgnoreCase));

    /// <summary>Atualiza somente a lista. Nunca altera o editor (a pesquisa não pode apagar o que está sendo digitado).</summary>
    private void RefreshRules(string? selectTrigger)
    {
        _refreshingRules = true;
        try
        {
            string filter = _search.Text.Trim();
            _grid.Rows.Clear();
            int selectedRow = -1;
            foreach (var rule in _settings.Rules
                         .Where(r => filter.Length == 0
                                     || r.Trigger.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                     || r.Replacement.Contains(filter, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(r => r.Trigger, StringComparer.OrdinalIgnoreCase))
            {
                int row = _grid.Rows.Add(rule.Trigger, BuildPreview(rule.Replacement));
                _grid.Rows[row].Tag = rule.Trigger;
                if (selectTrigger is not null && string.Equals(rule.Trigger, selectTrigger, StringComparison.OrdinalIgnoreCase))
                    selectedRow = row;
            }

            _grid.ClearSelection();
            if (selectedRow >= 0)
            {
                _grid.CurrentCell = _grid.Rows[selectedRow].Cells[0];
                _grid.Rows[selectedRow].Selected = true;
            }
            else
            {
                _grid.CurrentCell = null;
            }

            int total = _settings.Rules.Count;
            string noun = total == 1 ? "atalho cadastrado" : "atalhos cadastrados";
            _count.Text = filter.Length == 0 ? $"{total} {noun}" : $"{_grid.Rows.Count} de {total} {noun}";
        }
        finally { _refreshingRules = false; }
    }

    private void OnGridSelectionChanged()
    {
        if (_refreshingRules || _loadingControls || _selectionQueued) return;
        // Adiado: abrir diálogos ou alterar a grade dentro do próprio evento causa reentrância no DataGridView.
        _selectionQueued = true;
        BeginInvoke(new Action(HandleGridSelection));
    }

    private void HandleGridSelection()
    {
        _selectionQueued = false;
        if (_grid.SelectedRows.Count == 0) return;
        if (_grid.SelectedRows[0].Tag is not string trigger) return;
        if (string.Equals(trigger, _editingTrigger, StringComparison.OrdinalIgnoreCase)) return;

        if (_dirty && !ConfirmDiscard())
        {
            RefreshRules(_editingTrigger); // devolve a seleção ao atalho que está sendo editado
            return;
        }
        var rule = FindRuleByTrigger(trigger);
        if (rule is not null) LoadRuleIntoEditor(rule);
    }

    private bool ConfirmDiscard()
    {
        var answer = MessageBox.Show("Há alterações não salvas no editor. Deseja descartá-las?",
            "Alterações não salvas", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return answer == DialogResult.Yes;
    }

    private void LoadRuleIntoEditor(ShortcutRule rule)
    {
        _loadingControls = true;
        try
        {
            _editingTrigger = rule.Trigger;
            _trigger.Text = rule.Trigger;
            _replacement.Text = EngineConfig.NormalizeNewlines(rule.Replacement).Replace("\n", "\r\n");
            _dirty = false;
        }
        finally { _loadingControls = false; }
        UpdateReplacementInfo();
    }

    private void NewShortcut()
    {
        if (_dirty && !ConfirmDiscard()) return;
        _loadingControls = true;
        try
        {
            _editingTrigger = null;
            _dirty = false;
            _trigger.Clear();
            _replacement.Clear();
            _grid.ClearSelection();
            _grid.CurrentCell = null;
        }
        finally { _loadingControls = false; }
        UpdateReplacementInfo();
        _trigger.Focus();
    }

    private void OnEditorChanged()
    {
        if (_loadingControls) return;
        _dirty = true;
        UpdateReplacementInfo();
    }

    private void UpdateReplacementInfo()
    {
        if (_replacementInfo is null) return;
        int length = _replacement.Text.Replace("\r\n", "\n").Length;
        double seconds = length * (double)Math.Max(_settings.DelayMs, 0) / 1000.0;
        string duration = seconds < 1 ? "< 1 s" : $"≈ {Math.Ceiling(seconds).ToString(CultureInfo.InvariantCulture)} s";
        _replacementInfo.Text = $"{length} caractere(s) • {duration} para inserir";
    }

    private void SaveRule()
    {
        string trigger = _trigger.Text.Trim();
        string replacement = EngineConfig.NormalizeNewlines(_replacement.Text);

        if (trigger.Length == 0) { ShowValidation("Informe um atalho, por exemplo ;ola.", _trigger); return; }
        if (trigger.Any(char.IsWhiteSpace)) { ShowValidation("O atalho não pode conter espaços, Enter ou Tab.", _trigger); return; }
        if (trigger.Any(char.IsSurrogate)) { ShowValidation("O atalho deve usar apenas caracteres comuns (sem emojis).", _trigger); return; }
        if (trigger.Length > 64) { ShowValidation("O atalho deve ter no máximo 64 caracteres.", _trigger); return; }
        if (replacement.Length == 0) { ShowValidation("Informe o texto que substituirá o atalho.", _replacement); return; }

        var existing = FindRuleByTrigger(trigger);
        var editing = FindRuleByTrigger(_editingTrigger);
        if (existing is not null && !ReferenceEquals(existing, editing))
        {
            MessageBox.Show("Esse atalho já está cadastrado. Escolha outro ou edite o registro existente.",
                "Atalho duplicado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _trigger.Focus();
            return;
        }

        if (editing is not null)
        {
            editing.Trigger = trigger;
            editing.Replacement = replacement;
        }
        else
        {
            _settings.Rules.Add(new ShortcutRule { Trigger = trigger, Replacement = replacement });
        }

        _editingTrigger = trigger;
        _dirty = false;
        PublishConfig();
        bool saved = PersistNow();
        RefreshRules(trigger);
        AppLog.Info("Atalho cadastrado/atualizado (total: " + _settings.Rules.Count + ").");

        if (!saved)
        {
            MessageBox.Show("O atalho vale nesta sessão, mas não foi possível gravar o arquivo de configuração. " +
                            "Consulte o log em " + AppLog.FilePath + ".",
                "Falha ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (EndsWithDelimiter(trigger))
        {
            ShowNotice("Salvo. Atenção: o atalho termina com um caractere delimitador e não será acionado.", true);
        }
        else ShowNotice("Atalho salvo.");
    }

    private bool EndsWithDelimiter(string trigger) => _config.IsPunctuation(trigger[^1]);

    private void DeleteRule()
    {
        var rule = FindRuleByTrigger(_editingTrigger);
        if (rule is null)
        {
            ShowNotice("Selecione um atalho na lista para excluir.", true);
            return;
        }
        var answer = MessageBox.Show($"Excluir o atalho {rule.Trigger}?", "Confirmar exclusão",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        _settings.Rules.Remove(rule);
        PublishConfig();
        PersistNow();

        _loadingControls = true;
        try
        {
            _editingTrigger = null;
            _dirty = false;
            _trigger.Clear();
            _replacement.Clear();
        }
        finally { _loadingControls = false; }

        RefreshRules(null);
        UpdateReplacementInfo();
        AppLog.Info("Atalho excluído (total: " + _settings.Rules.Count + ").");
        ShowNotice("Atalho excluído.");
    }

    private void ShowValidation(string message, Control focus)
    {
        MessageBox.Show(message, "Verifique os dados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
