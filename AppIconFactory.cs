using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ExpansorTexto;

internal static class AppIconFactory
{
    /// <summary>Ícone da bandeja/janela. Pausado: cinza com duas barras de pausa.</summary>
    public static Icon Create(bool paused)
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(paused ? Color.FromArgb(120, 132, 146) : Color.FromArgb(40, 132, 150));
            using var white = new Pen(Color.White, 2.2F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 8);
            g.DrawRoundedRectangle(white, new Rectangle(7, 9, 17, 15), 3);
            g.DrawLine(white, 11, 14, 20, 14);
            g.DrawLine(white, 11, 19, 17, 19);

            if (paused)
            {
                using var bars = new SolidBrush(Color.FromArgb(255, 200, 110));
                g.FillRectangle(bars, 21, 3, 3, 9);
                g.FillRectangle(bars, 26, 3, 3, 9);
            }
            else
            {
                using var sparkle = new Pen(Color.FromArgb(255, 225, 164), 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(sparkle, 24, 5, 24, 12);
                g.DrawLine(sparkle, 21, 8, 27, 8);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone(); // Clone copia o ícone; o HICON original é destruído abaixo
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
