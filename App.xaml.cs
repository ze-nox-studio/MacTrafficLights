using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MacTrafficLights;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlayWindow;
    private Forms.NotifyIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _overlayWindow = new OverlayWindow();
        _overlayWindow.Show();

        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrafficLightTrayIcon(),
            Text = "MacTrafficLights",
            Visible = true
        };

        var contextMenu = new Forms.ContextMenuStrip();

        var hideRightItem = new Forms.ToolStripMenuItem("Sağdaki Orijinal Tuşları Gizle")
        {
            Checked = _overlayWindow!.HideOriginalRightButtons,
            CheckOnClick = true
        };
        hideRightItem.CheckedChanged += (_, _) =>
        {
            if (_overlayWindow != null)
            {
                _overlayWindow.HideOriginalRightButtons = hideRightItem.Checked;
            }
        };

        var stripWidthMenu = new Forms.ToolStripMenuItem("Şerit Genişliği");

        var widthShort = new Forms.ToolStripMenuItem("Kısa (95 px)");
        widthShort.Click += (_, _) => SetStripWidth(95);

        var widthMedium = new Forms.ToolStripMenuItem("Normal (140 px)");
        widthMedium.Click += (_, _) => SetStripWidth(140);

        var widthWide = new Forms.ToolStripMenuItem("Geniş (220 px)");
        widthWide.Click += (_, _) => SetStripWidth(220);

        var widthExtraWide = new Forms.ToolStripMenuItem("Tam Başlık (320 px)");
        widthExtraWide.Click += (_, _) => SetStripWidth(320);

        stripWidthMenu.DropDownItems.Add(widthShort);
        stripWidthMenu.DropDownItems.Add(widthMedium);
        stripWidthMenu.DropDownItems.Add(widthWide);
        stripWidthMenu.DropDownItems.Add(widthExtraWide);

        var positionMenu = new Forms.ToolStripMenuItem("Konum");
        
        var posCompact = new Forms.ToolStripMenuItem("Sıfır Köşe (X: 2, Y: 2)");
        posCompact.Click += (_, _) => SetOffsets(2, 2);

        var posDefault = new Forms.ToolStripMenuItem("Standart (X: 6, Y: 4)");
        posDefault.Click += (_, _) => SetOffsets(6, 4);

        var posLower = new Forms.ToolStripMenuItem("İçeride (X: 10, Y: 8)");
        posLower.Click += (_, _) => SetOffsets(10, 8);

        positionMenu.DropDownItems.Add(posCompact);
        positionMenu.DropDownItems.Add(posDefault);
        positionMenu.DropDownItems.Add(posLower);

        var restoreItem = new Forms.ToolStripMenuItem("Pencereleri Sıfırla");
        restoreItem.Click += (_, _) => _overlayWindow?.RestoreAllModifiedWindows();

        var exitItem = new Forms.ToolStripMenuItem("Çıkış");
        exitItem.Click += (_, _) => ExitApp();

        contextMenu.Items.Add(new Forms.ToolStripLabel("MacTrafficLights") { Font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold) });
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(hideRightItem);
        contextMenu.Items.Add(stripWidthMenu);
        contextMenu.Items.Add(positionMenu);
        contextMenu.Items.Add(restoreItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void SetOffsets(int x, int y)
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.OffsetX = x;
            _overlayWindow.OffsetY = y;
        }
    }

    private void SetStripWidth(int width)
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.StripWidth = width;
        }
    }

    private static Icon CreateTrafficLightTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.FromArgb(230, 35, 35, 35));
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            using var redBrush = new SolidBrush(Color.FromArgb(255, 95, 86));
            using var yellowBrush = new SolidBrush(Color.FromArgb(255, 189, 46));
            using var greenBrush = new SolidBrush(Color.FromArgb(39, 201, 63));

            g.FillEllipse(redBrush, 6, 7, 8, 8);
            g.FillEllipse(yellowBrush, 18, 7, 8, 8);
            g.FillEllipse(greenBrush, 12, 17, 8, 8);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private void ExitApp()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_overlayWindow != null)
        {
            _overlayWindow.RestoreAllModifiedWindows();
            _overlayWindow.Close();
        }

        Shutdown();
    }
}
