using System.Drawing.Drawing2D;
using WinTimer = System.Windows.Forms.Timer;

namespace ExpansorTexto;

/// <summary>
/// Mascote vetorial no rodapé. Acompanha o cursor (posição horizontal em relação à janela e direção do
/// olhar) enquanto a janela principal está visível. Fica restrito à própria área, é transparente ao mouse
/// (WM_NCHITTEST → HTTRANSPARENT) e a animação para sozinha quando a janela é ocultada.
/// </summary>
internal sealed class MousePetControl : Control
{
    private const int WM_NCHITTEST = 0x0084;
    private const int ActiveIntervalMs = 33;   // ~30 quadros/s enquanto se move
    private const int IdleIntervalMs = 100;    // ~10 quadros/s em repouso (só a respiração)
    private const int MinimizedIntervalMs = 500;

    private readonly WinTimer _timer = new() { Interval = IdleIntervalMs };
    private Form? _owner;
    private bool _initialized;
    private float _petX, _targetX, _gazeX, _gazeY, _phase;

    // Recursos GDI+ criados uma única vez (o desenho usa coordenadas de um quadro de 56x44 px).
    private readonly GraphicsPath _bodyPath = new();
    private readonly SolidBrush _shadow = new(Color.FromArgb(24, 35, 54, 67));
    private readonly SolidBrush _body = new(Color.FromArgb(68, 166, 177));
    private readonly SolidBrush _belly = new(Color.FromArgb(223, 246, 244));
    private readonly SolidBrush _earInner = new(Color.FromArgb(177, 224, 222));
    private readonly SolidBrush _eyeWhite = new(Color.White);
    private readonly SolidBrush _pupil = new(Color.FromArgb(32, 48, 59));
    private readonly SolidBrush _nose = new(Color.FromArgb(224, 133, 137));
    private readonly Pen _outline = new(Color.FromArgb(34, 119, 133), 1.4F);
    private readonly Pen _tail = new(Color.FromArgb(40, 132, 150), 3F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly Pen _smile = new(Color.FromArgb(47, 78, 85), 1.1F);
    private readonly Pen _sparkle = new(Color.FromArgb(242, 177, 88), 1.5F);

    private static readonly PointF[] LeftEar = { new(8, 12), new(9, 1), new(18, 8) };
    private static readonly PointF[] RightEar = { new(27, 8), new(37, 1), new(37, 13) };
    private static readonly PointF[] LeftEarInner = { new(10, 8), new(11, 4), new(15, 9) };
    private static readonly PointF[] RightEarInner = { new(30, 9), new(35, 4), new(35, 9) };

    public MousePetControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        _bodyPath.AddBezier(5, 13, 5, 2, 14, 1, 21, 5);
        _bodyPath.AddBezier(21, 5, 29, 0, 39, 5, 39, 15);
        _bodyPath.AddBezier(39, 15, 41, 30, 33, 36, 22, 36);
        _bodyPath.AddBezier(22, 36, 10, 37, 3, 29, 5, 13);
        _bodyPath.CloseFigure();

        _timer.Tick += (_, _) => Step();
    }

    /// <summary>Associa a janela principal. Iniciar/parar é automático conforme a visibilidade.</summary>
    public void Attach(Form owner)
    {
        _owner = owner;
        UpdateTimerState();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateTimerState();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        base.OnHandleDestroyed(e);
    }

    private void UpdateTimerState()
    {
        // Visible só é true quando a janela e todos os pais estão visíveis; ao ocultar a janela a animação para.
        bool shouldRun = _owner is not null && !DesignMode && Visible;
        if (shouldRun && !_timer.Enabled) _timer.Start();
        else if (!shouldRun && _timer.Enabled) _timer.Stop();
    }

    private void SetInterval(int ms)
    {
        if (_timer.Interval != ms) _timer.Interval = ms;
    }

    private void Step()
    {
        var owner = _owner;
        if (owner is null || !Visible || owner.IsDisposed) { _timer.Stop(); return; }
        if (owner.WindowState == FormWindowState.Minimized) { SetInterval(MinimizedIntervalMs); return; }

        float scale = DeviceDpi / 96F;
        Point cursor = Cursor.Position;               // coordenadas de tela
        Rectangle window = owner.Bounds;              // janela de nível superior: também em tela
        float normalized = window.Width > 0
            ? Math.Clamp((cursor.X - window.Left) / (float)window.Width, 0F, 1F)
            : 0.5F;

        float petWidth = 52F * scale;
        float margin = 4F * scale;
        float travel = Math.Max(0F, Width - petWidth - 2F * margin);
        _targetX = margin + travel * normalized;
        if (!_initialized) { _petX = _targetX; _initialized = true; }

        // Olhar: direção do cursor em relação ao centro do mascote.
        Point center = PointToScreen(new Point((int)(_petX + petWidth / 2F), Height / 2));
        float targetGazeX = Math.Clamp((cursor.X - center.X) / 300F, -1F, 1F);
        float targetGazeY = Math.Clamp((cursor.Y - center.Y) / 300F, -1F, 1F);

        float oldX = _petX, oldGx = _gazeX, oldGy = _gazeY;
        _petX += (_targetX - _petX) * 0.14F;
        _gazeX += (targetGazeX - _gazeX) * 0.22F;
        _gazeY += (targetGazeY - _gazeY) * 0.22F;

        bool moving = Math.Abs(_petX - oldX) > 0.05F || Math.Abs(_gazeX - oldGx) > 0.004F || Math.Abs(_gazeY - oldGy) > 0.004F;
        _phase += moving ? 0.12F : 0.30F; // em repouso, quadros mais espaçados, passo maior
        SetInterval(moving ? ActiveIntervalMs : IdleIntervalMs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = DeviceDpi / 96F;
        float x = Math.Clamp(_petX, 2F * s, Math.Max(2F * s, Width - 54F * s));
        float bob = (float)Math.Sin(_phase) * 1.6F * s;
        float y = Math.Max(2F * s, (Height - 42F * s) / 2F + bob);

        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(s, s);
        DrawPet(g);
        g.Restore(state);
    }

    private void DrawPet(Graphics g)
    {
        g.FillEllipse(_shadow, 5, 34, 34, 6);
        g.DrawBezier(_tail, 34, 27, 43, 31, 43, 17, 39, 17);

        g.FillPath(_body, _bodyPath);
        g.DrawPath(_outline, _bodyPath);
        g.FillEllipse(_belly, 14, 23, 16, 10);

        g.FillPolygon(_body, LeftEar);
        g.FillPolygon(_body, RightEar);
        g.FillPolygon(_earInner, LeftEarInner);
        g.FillPolygon(_earInner, RightEarInner);

        float ex = _gazeX * 1.5F, ey = _gazeY * 1F;
        g.FillEllipse(_eyeWhite, 12, 13, 7, 8);
        g.FillEllipse(_eyeWhite, 27, 13, 7, 8);
        g.FillEllipse(_pupil, 14 + ex, 15 + ey, 3.5F, 4.5F);
        g.FillEllipse(_pupil, 29 + ex, 15 + ey, 3.5F, 4.5F);

        g.FillEllipse(_nose, 21, 22, 4, 3);
        g.DrawArc(_smile, 18, 22, 6, 6, 15, 145);
        g.DrawArc(_smile, 22, 22, 6, 6, 20, 145);

        g.DrawLine(_sparkle, 45, 5, 45, 13);
        g.DrawLine(_sparkle, 41, 9, 49, 9);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = new IntPtr(-1); // HTTRANSPARENT: não intercepta cliques nem foco
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _bodyPath.Dispose();
            _shadow.Dispose(); _body.Dispose(); _belly.Dispose(); _earInner.Dispose();
            _eyeWhite.Dispose(); _pupil.Dispose(); _nose.Dispose();
            _outline.Dispose(); _tail.Dispose(); _smile.Dispose(); _sparkle.Dispose();
        }
        base.Dispose(disposing);
    }
}
