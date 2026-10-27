using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacTrafficLights;

public partial class OverlayWindow : Window
{
    private IntPtr _overlayHwnd;
    private IntPtr _currentTargetHwnd;
    private bool _hideOriginalRightButtons = true;
    private int _offsetX = 2;
    private int _offsetY = 2;
    private int _stripWidth = 140;

    private System.Windows.Media.Color _currentBgColor = System.Windows.Media.Color.FromRgb(32, 32, 32);
    private DateTime _lastColorSampleTime = DateTime.MinValue;
    private IntPtr _lastSampledHwnd = IntPtr.Zero;

    private readonly Dictionary<IntPtr, int> _modifiedWindowStyles = new();

    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private readonly List<IntPtr> _hooks = new();
    private DispatcherTimer? _watchdogTimer;

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "#32768"
    };

    public bool HideOriginalRightButtons
    {
        get => _hideOriginalRightButtons;
        set
        {
            _hideOriginalRightButtons = value;
            if (!value)
            {
                RestoreAllModifiedWindows();
            }
            else if (_currentTargetHwnd != IntPtr.Zero)
            {
                ApplyRightButtonHiding(_currentTargetHwnd);
            }
        }
    }

    public int OffsetX
    {
        get => _offsetX;
        set { _offsetX = value; UpdateTargetWindow(); }
    }

    public int OffsetY
    {
        get => _offsetY;
        set { _offsetY = value; UpdateTargetWindow(); }
    }

    public int StripWidth
    {
        get => _stripWidth;
        set
        {
            _stripWidth = value;
            Width = value;
            UpdateTargetWindow();
        }
    }

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OverlayWindow_Loaded;
        Closed += OverlayWindow_Closed;
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayHwnd = new WindowInteropHelper(this).Handle;

        int exStyle = NativeMethods.GetWindowLong(_overlayHwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            _overlayHwnd,
            NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        SetupWinEventHooks();

        _watchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _watchdogTimer.Tick += (_, _) => UpdateTargetWindow();
        _watchdogTimer.Start();

        UpdateTargetWindow();
    }

    private void SetupWinEventHooks()
    {
        _winEventDelegate = WinEventProc;

        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;

        _hooks.Add(NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags));

        _hooks.Add(NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags));

        _hooks.Add(NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags));
    }

    private void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (idObject != 0 && eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
            return;

        UpdateTargetWindow();
    }

    public void UpdateTargetWindow()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();

        if (fg == _overlayHwnd)
        {
            fg = _currentTargetHwnd;
        }

        if (!IsValidTargetWindow(fg))
        {
            HideOverlay();
            return;
        }

        _currentTargetHwnd = fg;

        if (_hideOriginalRightButtons)
        {
            ApplyRightButtonHiding(fg);
        }

        if (!TryGetWindowBounds(fg, out var rect))
        {
            HideOverlay();
            return;
        }

        bool isZoomed = NativeMethods.IsZoomed(fg);
        int extraMaximizedY = isZoomed ? 2 : 0;
        int extraMaximizedX = isZoomed ? 2 : 0;

        int targetX = rect.Left + _offsetX + extraMaximizedX;
        int targetY = rect.Top + _offsetY + extraMaximizedY;

        uint dpi = NativeMethods.GetDpiForWindow(fg);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;

        int desiredWidthDip = Math.Min(_stripWidth, Math.Max(85, (int)(rect.Width / scale) - 40));
        int widthPx = (int)Math.Round(desiredWidthDip * scale);
        int heightPx = (int)Math.Round(30 * scale);

        if (fg != _lastSampledHwnd || (DateTime.UtcNow - _lastColorSampleTime).TotalMilliseconds > 250)
        {
            SampleAndApplyTitlebarColor(rect, widthPx, heightPx);
            _lastSampledHwnd = fg;
            _lastColorSampleTime = DateTime.UtcNow;
        }

        if (Visibility != Visibility.Visible)
        {
            Show();
        }

        NativeMethods.SetWindowPos(
            _overlayHwnd,
            NativeMethods.HWND_TOPMOST,
            targetX,
            targetY,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void SampleAndApplyTitlebarColor(NativeMethods.RECT rect, int overlayWidthPx, int overlayHeightPx)
    {
        IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            var samples = new List<(byte R, byte G, byte B)>();

            int baseRightX = rect.Left + overlayWidthPx + 8;
            int maxSampleX = Math.Min(rect.Right - 140, baseRightX + 90);
            int sampleY1 = rect.Top + Math.Max(4, overlayHeightPx / 4);
            int sampleY2 = rect.Top + Math.Max(8, (overlayHeightPx * 3) / 4);

            for (int x = baseRightX; x <= maxSampleX; x += 18)
            {
                AddPixelSample(hdc, x, sampleY1, samples);
                AddPixelSample(hdc, x, sampleY2, samples);
            }

            if (samples.Count == 0)
            {
                int fallbackX = rect.Left + Math.Min(rect.Width / 2, overlayWidthPx + 10);
                AddPixelSample(hdc, fallbackX, rect.Top + 4, samples);
                AddPixelSample(hdc, fallbackX, rect.Top + 6, samples);
            }

            if (samples.Count > 0)
            {
                samples.Sort((a, b) => (a.R + a.G + a.B).CompareTo(b.R + b.G + b.B));
                var best = samples[samples.Count / 2];

                _currentBgColor = System.Windows.Media.Color.FromArgb(255, best.R, best.G, best.B);
                PillBorder.Background = new SolidColorBrush(_currentBgColor);
                PillBorder.BorderBrush = new SolidColorBrush(_currentBgColor);
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static void AddPixelSample(IntPtr hdc, int x, int y, List<(byte R, byte G, byte B)> list)
    {
        uint pixel = NativeMethods.GetPixel(hdc, x, y);
        if (pixel != 0xFFFFFFFF)
        {
            byte r = (byte)(pixel & 0x000000FF);
            byte g = (byte)((pixel & 0x0000FF00) >> 8);
            byte b = (byte)((pixel & 0x00FF0000) >> 16);
            list.Add((r, g, b));
        }
    }

    private bool IsValidTargetWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _overlayHwnd)
            return false;

        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;

        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        var classSb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, classSb, classSb.Capacity);
        string className = classSb.ToString();

        if (ExcludedClasses.Contains(className))
            return false;

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        if ((style & NativeMethods.WS_CHILD) != 0)
            return false;

        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false;

        bool hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        bool hasThickFrame = (style & NativeMethods.WS_THICKFRAME) != 0;

        if (!hasCaption && !hasThickFrame && !_modifiedWindowStyles.ContainsKey(hwnd))
            return false;

        if (TryGetWindowBounds(hwnd, out var rect))
        {
            if (rect.Width < 180 || rect.Height < 100)
                return false;
        }

        return true;
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out rect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0 && rect.Width > 0 && rect.Height > 0)
            return true;

        return NativeMethods.GetWindowRect(hwnd, out rect);
    }

    private void ApplyRightButtonHiding(IntPtr hwnd)
    {
        try
        {
            var classSb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classSb, classSb.Capacity);
            string className = classSb.ToString();

            if (className.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                return;

            int currentStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            if ((currentStyle & NativeMethods.WS_SYSMENU) != 0)
            {
                if (!_modifiedWindowStyles.ContainsKey(hwnd))
                {
                    _modifiedWindowStyles[hwnd] = currentStyle;
                }

                int newStyle = currentStyle & ~(int)NativeMethods.WS_SYSMENU;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, newStyle);
                NativeMethods.SetWindowPos(
                    hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED);
            }
        }
        catch
        {
        }
    }

    public void RestoreAllModifiedWindows()
    {
        foreach (var kvp in _modifiedWindowStyles.ToList())
        {
            IntPtr hwnd = kvp.Key;
            int origStyle = kvp.Value;

            if (NativeMethods.IsWindow(hwnd))
            {
                try
                {
                    int current = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, current | (origStyle & (int)NativeMethods.WS_SYSMENU));
                    NativeMethods.SetWindowPos(
                        hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                        NativeMethods.SWP_FRAMECHANGED);
                }
                catch
                {
                }
            }
        }
        _modifiedWindowStyles.Clear();
    }

    private void HideOverlay()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
    }

    private void Container_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetIconsVisibility(Visibility.Visible);
    }

    private void Container_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetIconsVisibility(Visibility.Collapsed);
    }

    private void PillBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_currentTargetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_currentTargetHwnd))
        {
            if (e.ClickCount == 2)
            {
                bool isMaximized = NativeMethods.IsZoomed(_currentTargetHwnd);
                int cmd = isMaximized ? NativeMethods.SC_RESTORE : NativeMethods.SC_MAXIMIZE;
                NativeMethods.PostMessage(_currentTargetHwnd, NativeMethods.WM_SYSCOMMAND, new IntPtr(cmd), IntPtr.Zero);
            }
            else
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.PostMessage(_currentTargetHwnd, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            }
        }
    }

    private void SetIconsVisibility(Visibility visibility)
    {
        SetButtonIconVisibility(CloseBtn, visibility);
        SetButtonIconVisibility(MinBtn, visibility);
        SetButtonIconVisibility(MaxBtn, visibility);
    }

    private static void SetButtonIconVisibility(System.Windows.Controls.Button btn, Visibility visibility)
    {
        if (btn.Template.FindName("IconContent", btn) is UIElement icon)
        {
            icon.Visibility = visibility;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTargetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_currentTargetHwnd))
        {
            NativeMethods.PostMessage(_currentTargetHwnd, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_CLOSE), IntPtr.Zero);
        }
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTargetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_currentTargetHwnd))
        {
            NativeMethods.PostMessage(_currentTargetHwnd, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_MINIMIZE), IntPtr.Zero);
        }
    }

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTargetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_currentTargetHwnd))
        {
            bool isMaximized = NativeMethods.IsZoomed(_currentTargetHwnd);
            int cmd = isMaximized ? NativeMethods.SC_RESTORE : NativeMethods.SC_MAXIMIZE;
            NativeMethods.PostMessage(_currentTargetHwnd, NativeMethods.WM_SYSCOMMAND, new IntPtr(cmd), IntPtr.Zero);
        }
    }

    private void OverlayWindow_Closed(object? sender, EventArgs e)
    {
        _watchdogTimer?.Stop();
        foreach (var hook in _hooks)
        {
            if (hook != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();
        RestoreAllModifiedWindows();
    }
}
