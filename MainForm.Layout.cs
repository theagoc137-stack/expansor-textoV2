using System.Globalization;

namespace ExpansorTexto;

public sealed partial class MainForm
{
    private readonly Color _page = Color.FromArgb(246, 248, 251);
    private readonly Color _card = Color.White;
    private readonly Color _ink = Color.FromArgb(37, 48, 63);
    private readonly Color _muted = Color.FromArgb(112, 124, 140);
    private readonly Color _accent = Color.FromArgb(40, 132, 150);
    private readonly Color _line = Color.FromArgb(226, 232, 239);

    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly TextBox _trigger = new();
    private readonly TextBox _replacement = new();
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _space = new();
    private readonly CheckBox _enter = new();
    private readonly CheckBox _tab = new();
    private readonly TextBox _punctuation = new();
    private readonly NumericUpDown _delay = new();
    private readonly CheckBox _startup = new();
    private readonly Label _status = new();
    private readonly Label _count = new();
    private readonly Label _notice = new();
    private readonly Label _replacementInfo = new();
    private readonly MousePetControl _pet = new();
    private readonly ToolTip _tips = new();

    // Fontes e bitmaps criados uma única vez e descartados em Dispose.
    private readonly Dictionary<string, Font> _fonts = new();
    private readonly List<Bitmap> _bitmaps = new();

    private Font F(string family, float size, FontStyle style = FontStyle.Regular)
    {
        string key = family + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + (int)style;
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = new Font(family, size, style);
            _fonts[key] = font;
        }
        return font;
    }

    private Bitmap MakeIcon(IconKind kind, Color color)
    {
        int size = Math.Max(16, (int)Math.Round(16 * DeviceDpi / 96.0));
        var bitmap = VectorIcons.Create(kind, color, size);
        _bitmaps.Add(bitmap);
        return bitmap;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 12),
            BackColor = _page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildWorkspace(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        // Eventos ligados só depois de tudo montado (evita disparos durante a construção).
        _search.TextChanged += (_, _) => RefreshRules(_editingTrigger);
        _grid.SelectionChanged += (_, _) => OnGridSelectionChanged();
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { DeleteRule(); e.Handled = true; } };
        _trigger.TextChanged += (_, _) => OnEditorChanged();
        _replacement.TextChanged += (_, _) => OnEditorChanged();
        _trigger.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _replacement.Focus(); e.SuppressKeyPress = true; e.Handled = true; }
        };
        _space.CheckedChanged += (_, _) => OnBehaviorChanged();
        _enter.CheckedChanged += (_, _) => OnBehaviorChanged();
        _tab.CheckedChanged += (_, _) => OnBehaviorChanged();
        _punctuation.TextChanged += (_, _) => OnBehaviorChanged();
        _punctuation.Leave += (_, _) => OnPunctuationLeave();
        _delay.ValueChanged += (_, _) => OnBehaviorChanged();
        _startup.CheckedChanged += (_, _) => OnStartupChanged();
        _enabled.CheckedChanged += (_, _) => OnEnabledChanged();
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _page };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var brand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 7, 0, 0), BackColor = _page
        };
        brand.Controls.Add(new LogoMark { Size = new Size(48, 48), Margin = new Padding(0, 0, 12, 0) });

        var titleStack = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, Size = new Size(350, 52), Margin = new Padding(0), BackColor = _page
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        titleStack.Controls.Add(new Label
        {
            Text = "Expansor de Texto", Font = F("Segoe UI Semibold", 16F), ForeColor = _ink,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        titleStack.Controls.Add(new Label
        {
            Text = "Seus atalhos, em qualquer campo de texto", Font = F("Segoe UI", 8.5F), ForeColor = _muted,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        brand.Controls.Add(titleStack);
        header.Controls.Add(brand, 0, 0);

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, 17, 0, 0), BackColor = _page
        };
        _enabled.Text = "Substituição ativa";
        _enabled.AutoSize = true;
        _enabled.Font = F("Segoe UI Semibold", 9F);
        _enabled.ForeColor = _ink;
        _enabled.Margin = new Padding(8, 2, 0, 0);
        _status.AutoSize = true;
        _status.Font = F("Segoe UI", 8.5F);
        _status.ForeColor = _accent;
        _status.Margin = new Padding(0, 4, 10, 0);
        statusPanel.Controls.Add(_enabled);
        statusPanel.Controls.Add(_status);
        header.Controls.Add(statusPanel, 1, 0);
        return header;
    }

    // Duas colunas em TableLayoutPanel (em vez de SplitContainer): SplitterDistance definido antes de a
    // janela ter o tamanho final é limitado ao tamanho provisório do controle e deixa a lista estreita.
    private Control BuildWorkspace()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _page };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = BuildShortcutCard();
        left.Margin = new Padding(0, 0, 6, 0);
        var right = BuildEditorCard();
        right.Margin = new Padding(6, 0, 0, 0);
        table.Controls.Add(left, 0, 0);
        table.Controls.Add(right, 1, 0);
        return table;
    }

    private Control BuildShortcutCard()
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16), BackColor = _card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _card };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(SectionTitle("Seus atalhos", IconKind.Keyboard), 0, 0);
        var add = CreateButton("Novo", true, IconKind.Plus);
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(0);
        add.Click += (_, _) => NewShortcut();
        heading.Controls.Add(add, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        var searchRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _card, Margin = new Padding(0, 5, 0, 7)
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(IconBox(IconKind.Search, _muted), 0, 0);
        _search.Dock = DockStyle.Fill;
        _search.Font = F("Segoe UI", 9F);
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.PlaceholderText = "Buscar atalho ou texto...";
        _search.Margin = new Padding(0, 4, 0, 0);
        searchRow.Controls.Add(_search, 1, 0);
        layout.Controls.Add(searchRow, 0, 1);

        ConfigureGrid();
        layout.Controls.Add(_grid, 0, 2);

        _count.Text = "0 atalhos cadastrados";
        _count.Dock = DockStyle.Fill;
        _count.ForeColor = _muted;
        _count.Font = F("Segoe UI", 8F);
        _count.TextAlign = ContentAlignment.BottomLeft;
        layout.Controls.Add(_count, 0, 3);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildEditorCard()
    {
        var card = CreateCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(18), BackColor = _card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));

        layout.Controls.Add(SectionTitle("Criar ou editar atalho", IconKind.Pencil), 0, 0);

        var triggerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = _card };
        triggerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        triggerPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        triggerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        triggerPanel.Controls.Add(FieldLabel("Atalho"), 0, 0);
        StyleTextBox(_trigger);
        _trigger.PlaceholderText = ";ola";
        _trigger.MaxLength = 64;
        _trigger.Dock = DockStyle.Fill;
        triggerPanel.Controls.Add(_trigger, 0, 1);
        layout.Controls.Add(triggerPanel, 0, 1);

        var textHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _card };
        textHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        textHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        textHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        textHeader.Controls.Add(FieldLabel("Texto que será inserido"), 0, 0);
        _replacementInfo.Dock = DockStyle.Fill;
        _replacementInfo.Font = F("Segoe UI", 8F);
        _replacementInfo.ForeColor = _muted;
        _replacementInfo.TextAlign = ContentAlignment.MiddleRight;
        textHeader.Controls.Add(_replacementInfo, 1, 0);
        layout.Controls.Add(textHeader, 0, 2);

        _replacement.Multiline = true;
        _replacement.AcceptsReturn = true;
        _replacement.AcceptsTab = true;
        _replacement.MaxLength = 5000;
        _replacement.ScrollBars = ScrollBars.Vertical;
        _replacement.Dock = DockStyle.Fill;
        _replacement.Font = F("Segoe UI", 9.5F);
        _replacement.BorderStyle = BorderStyle.FixedSingle;
        _replacement.BackColor = Color.FromArgb(252, 253, 255);
        _replacement.Margin = new Padding(0, 4, 0, 8);
        layout.Controls.Add(_replacement, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(0, 7, 0, 0), BackColor = _card
        };
        var save = CreateButton("Salvar", true, IconKind.Check);
        save.Width = 112;
        save.Click += (_, _) => SaveRule();
        var clear = CreateButton("Limpar", false, IconKind.Close);
        clear.Width = 100;
        clear.Click += (_, _) => NewShortcut();
        var delete = CreateButton("Excluir", false, IconKind.Trash);
        delete.Width = 100;
        delete.Click += (_, _) => DeleteRule();
        _notice.AutoSize = true;
        _notice.Font = F("Segoe UI", 8.5F);
        _notice.Margin = new Padding(8, 8, 0, 0);
        actions.Controls.AddRange(new Control[] { save, clear, delete, _notice });
        layout.Controls.Add(actions, 0, 4);

        layout.Controls.Add(new Panel { Dock = DockStyle.Fill, Height = 1, BackColor = _line }, 0, 5);

        var behavior = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = _card, Padding = new Padding(0, 8, 0, 0)
        };
        behavior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        behavior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        behavior.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        behavior.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        behavior.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        behavior.Controls.Add(FieldLabel("Teclas que finalizam o atalho"), 0, 0);
        behavior.Controls.Add(FieldLabel("Ritmo de digitação"), 1, 0);

        var delimiters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = _card, Margin = new Padding(0)
        };
        ConfigureCheck(_space, "Espaço");
        ConfigureCheck(_enter, "Enter");
        ConfigureCheck(_tab, "Tab");
        delimiters.Controls.AddRange(new Control[] { _space, _enter, _tab });
        behavior.Controls.Add(delimiters, 0, 1);

        var delayPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = _card, Margin = new Padding(0)
        };
        _delay.Minimum = 0;
        _delay.Maximum = 100;
        _delay.Value = 10;
        _delay.Width = 64;
        _delay.Margin = new Padding(0, 2, 4, 0);
        delayPanel.Controls.Add(_delay);
        delayPanel.Controls.Add(new Label { Text = "ms", AutoSize = true, ForeColor = _muted, Margin = new Padding(0, 6, 0, 0) });
        behavior.Controls.Add(delayPanel, 1, 1);

        var punctuationPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = _card, Margin = new Padding(0)
        };
        punctuationPanel.Controls.Add(new Label { Text = "Pontuação:", AutoSize = true, ForeColor = _muted, Margin = new Padding(0, 6, 7, 0) });
        _punctuation.Width = 150;
        _punctuation.MaxLength = 40;
        _punctuation.Margin = new Padding(0, 1, 0, 0);
        _punctuation.BorderStyle = BorderStyle.FixedSingle;
        punctuationPanel.Controls.Add(_punctuation);
        behavior.Controls.Add(punctuationPanel, 0, 2);

        _startup.Text = "Iniciar com Windows";
        _startup.AutoSize = true;
        _startup.ForeColor = _ink;
        _startup.Font = F("Segoe UI", 8.5F);
        _startup.Margin = new Padding(0, 3, 0, 0);
        behavior.Controls.Add(_startup, 1, 2);
        layout.Controls.Add(behavior, 0, 6);

        SetupTips();
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = _page, Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var creator = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = _page };
        creator.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        creator.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        creator.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        creator.Controls.Add(new Label
        {
            Text = "Criado por Theago Oliveira", Dock = DockStyle.Fill, Font = F("Segoe UI Semibold", 9F),
            ForeColor = _ink, TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        creator.Controls.Add(new Label
        {
            Text = "Expansor de Texto • Windows", Dock = DockStyle.Fill, Font = F("Segoe UI", 8F),
            ForeColor = _muted, TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        footer.Controls.Add(creator, 0, 0);

        footer.Controls.Add(new Label
        {
            Text = "Ctrl + Alt + F10  •  Pausar / ativar\nFechar a janela mantém o app na bandeja.",
            Dock = DockStyle.Fill, ForeColor = _muted, Font = F("Segoe UI", 8F), TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);

        // Área reservada ao mascote: ele nunca sai daqui e é transparente ao mouse.
        _pet.Dock = DockStyle.Fill;
        _pet.Margin = new Padding(0);
        _pet.BackColor = _page;
        footer.Controls.Add(_pet, 2, 0);
        return footer;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = _card;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = _line;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = _muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = F("Segoe UI Semibold", 8.5F);
        _grid.ColumnHeadersHeight = 34;
        _grid.DefaultCellStyle.BackColor = _card;
        _grid.DefaultCellStyle.ForeColor = _ink;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 242, 245);
        _grid.DefaultCellStyle.SelectionForeColor = _ink;
        _grid.DefaultCellStyle.Font = F("Segoe UI", 8.5F);
        _grid.DefaultCellStyle.Padding = new Padding(5, 3, 5, 3);
        _grid.RowTemplate.Height = 36;
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Trigger", HeaderText = "ATALHO", FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Preview", HeaderText = "TEXTO", FillWeight = 70, SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void SetupTips()
    {
        _tips.SetToolTip(_punctuation, "Sinais que também concluem um atalho. Exemplo: . , ! ? : ;\nLetras, números e espaços são ignorados.");
        _tips.SetToolTip(_delay, "Padrão: 10 ms, valor que funcionou nos testes relatados (Bloco de Notas, Word e Citrix/Salesforce).\nOutros ambientes podem exigir ajuste. 0 ms causou texto incorreto no Citrix.");
        _tips.SetToolTip(_enter, "Cuidado: em chats, Enter pode enviar a mensagem. Começa desativado.");
        _tips.SetToolTip(_tab, "Tab pode mudar o campo selecionado. Começa desativado.");
        _tips.SetToolTip(_replacement, "Quebras de linha são inseridas com a tecla Enter. Em chats, isso pode enviar a mensagem.");
    }

    private Panel CreateCard()
        => new() { Dock = DockStyle.Fill, BackColor = _card, Padding = new Padding(0), BorderStyle = BorderStyle.FixedSingle };

    private Control SectionTitle(string text, IconKind kind)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _card };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(IconBox(kind, _accent), 0, 0);
        row.Controls.Add(new Label
        {
            Text = text, Dock = DockStyle.Fill, Font = F("Segoe UI Semibold", 10F), ForeColor = _ink,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        return row;
    }

    private PictureBox IconBox(IconKind kind, Color color)
        => new()
        {
            Image = MakeIcon(kind, color),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Dock = DockStyle.Fill,
            BackColor = _card,
            Margin = new Padding(0)
        };

    private Label FieldLabel(string text)
        => new()
        {
            Text = text, Dock = DockStyle.Fill, Font = F("Segoe UI Semibold", 8.5F), ForeColor = _muted,
            TextAlign = ContentAlignment.MiddleLeft
        };

    private Button CreateButton(string text, bool primary, IconKind icon)
    {
        var button = new Button
        {
            Text = text,
            Image = MakeIcon(icon, primary ? Color.White : _ink),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? _accent : Color.FromArgb(246, 248, 251),
            ForeColor = primary ? Color.White : _ink,
            Font = F("Segoe UI Semibold", 8.5F),
            Cursor = Cursors.Hand,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = _line;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(32, 116, 133) : Color.FromArgb(235, 241, 246);
        return button;
    }

    private void StyleTextBox(TextBox textBox)
    {
        textBox.Font = F("Segoe UI", 9.5F);
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.FromArgb(252, 253, 255);
        textBox.Margin = new Padding(0, 3, 0, 0);
    }

    private void ConfigureCheck(CheckBox check, string text)
    {
        check.Text = text;
        check.AutoSize = true;
        check.ForeColor = _ink;
        check.Font = F("Segoe UI", 8.5F);
        check.Margin = new Padding(0, 4, 14, 0);
    }
}
