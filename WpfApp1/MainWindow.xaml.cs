using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFontStyles = System.Windows.FontStyles;
using WpfFontWeights = System.Windows.FontWeights;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Ttrad
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")]
        private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int RGN_DIFF = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private long seed = (780705L + 870114L + 131230L + 160609L + 210904L) * 260425L;

        private WpfColor penColor = Colors.Orange;
        private WpfColor textColor = Colors.Red;
        private double currentThickness = 4;
        private bool isDrawingEnabled = false;
        private bool isErasing = false;
        private bool isTextMode = false;
        private string savePath = "";
        private string appFolder = "";
        private string settingsPath = "";
        private string textSavePath = "";
        private string projectsFolder = "";
        private string currentProjectPath = "";
        private bool _isDirty = false;
        private IntPtr hwnd;

        private WinForms.NotifyIcon? notifyIcon;
        private WinForms.ContextMenuStrip? settingsMenu;
        private bool isExiting = false;

        private bool isErasingActive = false;
        private WpfPoint lastEraserPoint;

        private string currentLanguage = "ru";
        private AppStrings strings = new AppStrings();

        private string currentFontFamily = "Segoe UI";
        private double currentFontSize = 16;
        private bool currentBold = false;
        private bool currentItalic = false;

        private Canvas? textCanvas;
        private List<FrameworkElement> textElements = new List<FrameworkElement>();
        private Window? textInputWindow;
        private WpfTextBox? currentTextBox;
        private WpfPoint textInsertPoint;

        private Canvas? _editingContainer;
        private WpfTextBlock? _editingTextBlock;

        private WinForms.ToolStripMenuItem? thicknessMenuItem;
        private WinForms.ToolStripMenuItem? colorMenuItem;
        private WinForms.ToolStripMenuItem? undoMenuItem;

        private bool firstRunHintShown = false;
        private bool escHintShown = false;
        private Window? trayHintWindow = null;
        private Window? _escHintWindow = null;

        private IntPtr _currentRegion = IntPtr.Zero;

        // Undo
        private List<Stroke> _penUndoStack = new List<Stroke>();
        private List<List<Stroke>> _eraserUndoStack = new List<List<Stroke>>();
        private const int MaxUndoSteps = 10;

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const string VersionUrl = "https://raw.githubusercontent.com/TimurVG/ttrad/main/version.json";

        public MainWindow()
        {
            InitializeComponent();
            appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ttrad");
            Directory.CreateDirectory(appFolder);
            savePath = Path.Combine(appFolder, "drawing.json");
            settingsPath = Path.Combine(appFolder, "settings.json");
            textSavePath = Path.Combine(appFolder, "text.json");
            projectsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ttrad Projects");
            Directory.CreateDirectory(projectsFolder);
            LoadSettings();
            this.SourceInitialized += OnSourceInitialized;
            this.Closing += OnClosing;
            this.PreviewKeyDown += OnPreviewKeyDown;
            CreateTextCanvas();
            UpdatePen();
            DrawingCanvas.Strokes.StrokesChanged += OnStrokesChanged;
            DrawingCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
            DrawingCanvas.MouseMove += OnCanvasMouseMove;
            DrawingCanvas.MouseLeftButtonUp += OnCanvasMouseUp;
            DrawingCanvas.StrokeCollected += OnStrokeCollected;
            DrawingCanvas.MouseEnter += (s, e) => { this.Activate(); this.Focus(); };
            UpdateWindowBounds();
            LoadDrawing();
            LoadTextElements();
            SetupTrayIcon();
            SetDrawingState(false);
            UpdateTrayIcon();
            Task.Run(() => CheckForUpdates(false));
        }

        private void CreateTextCanvas()
        {
            textCanvas = new Canvas { IsHitTestVisible = false, Background = System.Windows.Media.Brushes.Transparent };
            Canvas.SetZIndex(textCanvas, 500);
            textCanvas.MouseLeftButtonDown += TextCanvas_MouseLeftButtonDown;
            ((Grid)this.Content).Children.Add(textCanvas);
        }

        private void TextCanvas_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (!isDrawingEnabled || !isTextMode) return;
            var hitElement = e.OriginalSource as DependencyObject;
            bool clickedOnText = false;
            while (hitElement != null)
            {
                if (hitElement is WpfTextBlock) { clickedOnText = true; break; }
                hitElement = VisualTreeHelper.GetParent(hitElement);
            }
            if (!clickedOnText)
            {
                textInsertPoint = e.GetPosition(textCanvas);
                ShowTextInputWindow();
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        currentLanguage = s.Language ?? "ru";
                        firstRunHintShown = s.FirstRunHintShown;
                        escHintShown = s.EscHintShown;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(new AppSettings
                {
                    Language = currentLanguage,
                    FirstRunHintShown = firstRunHintShown,
                    EscHintShown = escHintShown
                }));
            }
            catch { }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        { hwnd = new WindowInteropHelper(this).Handle; int ex = GetWindowLong(hwnd, GWL_EXSTYLE); SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT); }

        private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Activate();
                this.Focus();

                if (_escHintWindow != null) { _escHintWindow.Close(); _escHintWindow = null; }
                if (textInputWindow != null && textInputWindow.IsVisible) { InsertText(); textInputWindow.Close(); }
                if (isDrawingEnabled)
                {
                    isDrawingEnabled = false; isTextMode = false; isErasing = false;
                    DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                    SetWindowTransparent(true);
                    UpdateWindowRegion(false);
                    DrawingCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    isErasingActive = false;
                    if (textCanvas != null) textCanvas.IsHitTestVisible = false;
                    UpdatePen();
                    UpdateTrayIcon();
                    e.Handled = true;
                }
            }
        }

        private void UpdateWindowBounds()
        { this.Left = SystemParameters.VirtualScreenLeft; this.Top = SystemParameters.VirtualScreenTop; this.Width = SystemParameters.VirtualScreenWidth; this.Height = SystemParameters.VirtualScreenHeight; }

        private void UpdateWindowRegion(bool excludeTaskbar)
        {
            if (hwnd == IntPtr.Zero) return;

            if (_currentRegion != IntPtr.Zero)
            {
                DeleteObject(_currentRegion);
                _currentRegion = IntPtr.Zero;
            }

            if (!excludeTaskbar)
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            if (source != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            double left = this.Left * dpiScaleX;
            double top = this.Top * dpiScaleY;
            double width = this.Width * dpiScaleX;
            double height = this.Height * dpiScaleY;
            IntPtr fullRegion = CreateRectRgn(0, 0, (int)width, (int)height);

            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                RECT taskbarRect;
                if (GetWindowRect(taskbarHwnd, out taskbarRect))
                {
                    int taskbarX = (int)(taskbarRect.Left - left);
                    int taskbarY = (int)(taskbarRect.Top - top);
                    int taskbarW = taskbarRect.Right - taskbarRect.Left;
                    int taskbarH = taskbarRect.Bottom - taskbarRect.Top;

                    IntPtr taskbarRegion = CreateRectRgn(taskbarX, taskbarY, taskbarX + taskbarW, taskbarY + taskbarH);
                    CombineRgn(fullRegion, fullRegion, taskbarRegion, RGN_DIFF);
                    DeleteObject(taskbarRegion);
                }
            }

            SetWindowRgn(hwnd, fullRegion, true);
            _currentRegion = fullRegion;
        }

        private void SetDrawingState(bool enabled)
        {
            isDrawingEnabled = enabled;
            if (enabled)
            {
                if (isErasing)
                {
                    DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                    DrawingCanvas.Cursor = System.Windows.Input.Cursors.Cross;
                    if (textCanvas != null) textCanvas.IsHitTestVisible = false;
                }
                else if (isTextMode)
                {
                    DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                    SetWindowTransparent(false);
                    if (textCanvas != null) textCanvas.IsHitTestVisible = true;
                    this.Cursor = System.Windows.Input.Cursors.IBeam;
                    DrawingCanvas.Cursor = System.Windows.Input.Cursors.IBeam;
                }
                else
                {
                    DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    DrawingCanvas.Cursor = System.Windows.Input.Cursors.Pen;
                    if (textCanvas != null) textCanvas.IsHitTestVisible = false;
                }
                SetWindowTransparent(false);
                UpdateWindowRegion(true);
                this.Topmost = true; this.Activate(); this.Focus(); this.Topmost = false;

                if (!escHintShown)
                {
                    escHintShown = true;
                    SaveSettings();

                    string toolName;
                    if (isErasing) toolName = currentLanguage == "ru" ? "ластика" : "eraser";
                    else if (isTextMode) toolName = currentLanguage == "ru" ? "текста" : "text";
                    else toolName = currentLanguage == "ru" ? "карандаша" : "pen";

                    var hintWindow = new Window
                    {
                        Title = "",
                        Width = 420,
                        Height = 70,
                        WindowStyle = WindowStyle.None,
                        Topmost = true,
                        ShowInTaskbar = false,
                        AllowsTransparency = true,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)),
                        ResizeMode = ResizeMode.NoResize,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Focusable = false,
                        ShowActivated = false
                    };

                    var textBlock = new WpfTextBlock
                    {
                        Text = currentLanguage == "ru"
                            ? $"Нажми ESC, чтобы выключить режим {toolName}\nи вернуть обычный курсор. Окно исчезнет через 30 сек."
                            : $"Press ESC to exit {toolName} mode\nand restore normal cursor. Window will close in 30 sec.",
                        FontFamily = new WpfFontFamily("Segoe UI"),
                        FontSize = 13,
                        FontWeight = WpfFontWeights.SemiBold,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(15, 10, 15, 10)
                    };

                    hintWindow.Content = textBlock;
                    var helper = new WindowInteropHelper(hintWindow);
                    helper.EnsureHandle();
                    int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                    SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);

                    var screen = WinForms.Screen.PrimaryScreen;
                    var workingArea = screen.WorkingArea;

                    hintWindow.Show();
                    hintWindow.Left = workingArea.Right - hintWindow.Width - 20;
                    hintWindow.Top = workingArea.Bottom - hintWindow.Height - 50;
                    _escHintWindow = hintWindow;

                    var closeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    closeTimer.Tick += (s2, args2) =>
                    {
                        if (_escHintWindow != null)
                        {
                            _escHintWindow.Close();
                            _escHintWindow = null;
                        }
                        closeTimer.Stop();
                    };
                    closeTimer.Start();
                }
            }
            else
            {
                DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
                SetWindowTransparent(true);
                UpdateWindowRegion(false);
                DrawingCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                isErasingActive = false; isTextMode = false;
                if (textCanvas != null) textCanvas.IsHitTestVisible = false;
                if (_escHintWindow != null) { _escHintWindow.Close(); _escHintWindow = null; }
            }
            UpdatePen();
        }

        private void UpdateTrayIcon()
        {
            if (notifyIcon == null) return;
            strings.SetLanguage(currentLanguage);

            if (isDrawingEnabled)
            {
                notifyIcon.Text = "";
            }
            else
            {
                notifyIcon.Text = $"{strings.AppName} — {strings.Disabled}\n{strings.LeftClick} | {strings.RightClick} | {strings.EscExit}";
            }

            var oldIcon = notifyIcon.Icon;
            notifyIcon.Icon = CreateTrayIcon(isDrawingEnabled, isErasing, isTextMode);
            if (oldIcon != null)
            {
                oldIcon.Dispose();
            }
        }

        private void DrawFallbackIcon(Graphics g, System.Drawing.Color lightColor, System.Drawing.Color darkColor,
            bool enabled, bool eraser, bool textMode)
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            if (!enabled)
            {
                using (var br = new SolidBrush(darkColor)) g.FillPie(br, 0, 0, 31, 31, 90, 180);
                using (var br = new SolidBrush(lightColor)) g.FillPie(br, 0, 0, 31, 31, 270, 180);
            }
            else if (eraser)
            {
                int x = 8, y = 2, w = 16, h = 28, midY = y + h / 2;
                using (var br = new SolidBrush(lightColor)) g.FillRectangle(br, x, y, w, midY - y);
                using (var br = new SolidBrush(darkColor)) g.FillRectangle(br, x, midY, w, h - (midY - y));
            }
            else if (textMode)
            {
                using (var br = new SolidBrush(darkColor)) g.FillRectangle(br, 2, 2, 28, 28);
                using (var font = new Font("Arial", 20, System.Drawing.FontStyle.Bold))
                {
                    var ts = g.MeasureString("T", font);
                    using (var brush = new SolidBrush(lightColor))
                        g.DrawString("T", font, brush, 2 + (28 - ts.Width) / 2, 2 + (28 - ts.Height) / 2 + 2);
                }
            }
            else
            {
                var pts = new System.Drawing.Point[] { new(16, 2), new(3, 28), new(29, 28) };
                using (var br = new SolidBrush(lightColor)) g.FillPolygon(br, pts);
                var tipPts = new System.Drawing.Point[] { new(16, 2), new(11, 12), new(21, 12) };
                using (var br = new SolidBrush(darkColor)) g.FillPolygon(br, tipPts);
            }
        }

        private System.Drawing.Icon CreateTrayIcon(bool enabled, bool eraser, bool textMode = false)
        {
            int size = 32;
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                    string resourceName = "";
                    if (!enabled) resourceName = "Ttrad.icon_off_32.png";
                    else if (eraser) resourceName = "Ttrad.icon_eraser_32.png";
                    else if (textMode) resourceName = "Ttrad.icon_text_32.png";
                    else resourceName = "Ttrad.icon_pen_32.png";

                    bool loadedFromResource = false;
                    try
                    {
                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                using (var original = new Bitmap(stream))
                                    g.DrawImage(original, 0, 0, 32, 32);
                                loadedFromResource = true;
                            }
                        }
                    }
                    catch { }

                    if (!loadedFromResource)
                    {
                        System.Drawing.Color lightColor = System.Drawing.Color.FromArgb(0xe0, 0x93, 0x17);
                        System.Drawing.Color darkColor = System.Drawing.Color.FromArgb(0x33, 0x3f, 0x4b);
                        DrawFallbackIcon(g, lightColor, darkColor, enabled, eraser, textMode);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
        }

        private void SetWindowTransparent(bool transparent)
        { if (hwnd == IntPtr.Zero) return; int ex = GetWindowLong(hwnd, GWL_EXSTYLE); if (transparent) SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT); else SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_TRANSPARENT); }

        private void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
        {
            _penUndoStack.Add(e.Stroke);
            if (_penUndoStack.Count > MaxUndoSteps)
                _penUndoStack.RemoveAt(0);
            if (undoMenuItem != null) undoMenuItem.Enabled = true;
            _eraserUndoStack.Clear();
            _isDirty = true;
        }

        private void OnCanvasMouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (!isDrawingEnabled) return;
            this.Activate(); this.Focus();

            if (isErasing && !isTextMode)
            {
                var snapshot = new List<Stroke>();
                foreach (Stroke s in DrawingCanvas.Strokes)
                    snapshot.Add(s.Clone());
                _eraserUndoStack.Add(snapshot);
                if (_eraserUndoStack.Count > MaxUndoSteps)
                    _eraserUndoStack.RemoveAt(0);
                if (undoMenuItem != null) undoMenuItem.Enabled = true;
                _penUndoStack.Clear();
                _isDirty = true;

                isErasingActive = true;
                lastEraserPoint = e.GetPosition(DrawingCanvas);
                EraseAtPoint(lastEraserPoint);
            }
        }

        private void OnCanvasMouseMove(object sender, WpfMouseEventArgs e)
        { if (!isDrawingEnabled || !isErasing || !isErasingActive || isTextMode) return; WpfPoint pos = e.GetPosition(DrawingCanvas); if (Math.Sqrt(Math.Pow(pos.X - lastEraserPoint.X, 2) + Math.Pow(pos.Y - lastEraserPoint.Y, 2)) > currentThickness / 2) { EraseAtPoint(pos); lastEraserPoint = pos; } }

        private void OnCanvasMouseUp(object sender, WpfMouseButtonEventArgs e)
        {
            if (isErasing)
            {
                isErasingActive = false;
                this.Focus();
                SaveDrawing();
            }
        }

        private void UndoLastAction()
        {
            if (_eraserUndoStack.Count > 0)
            {
                var snapshot = _eraserUndoStack[_eraserUndoStack.Count - 1];
                _eraserUndoStack.RemoveAt(_eraserUndoStack.Count - 1);

                DrawingCanvas.Strokes.Clear();
                foreach (Stroke s in snapshot)
                    DrawingCanvas.Strokes.Add(s);
                SaveDrawing();
                _isDirty = true;
            }
            else if (_penUndoStack.Count > 0)
            {
                var lastStroke = _penUndoStack[_penUndoStack.Count - 1];
                _penUndoStack.RemoveAt(_penUndoStack.Count - 1);

                if (DrawingCanvas.Strokes.Contains(lastStroke))
                    DrawingCanvas.Strokes.Remove(lastStroke);
                SaveDrawing();
                _isDirty = true;
            }

            if (undoMenuItem != null)
                undoMenuItem.Enabled = _penUndoStack.Count > 0 || _eraserUndoStack.Count > 0;
        }

        private void EraseAtPoint(WpfPoint point)
        { double r = currentThickness * 1.5; var rem = new List<Stroke>(); var add = new List<Stroke>(); foreach (Stroke s in DrawingCanvas.Strokes) { var sp = s.StylusPoints; var before = new List<StylusPoint>(); var after = new List<StylusPoint>(); bool found = false, erasing = false; foreach (var p in sp) { if (Math.Sqrt(Math.Pow(p.X - point.X, 2) + Math.Pow(p.Y - point.Y, 2)) <= r) { erasing = true; found = true; } else { if (erasing) erasing = false; if (!found) before.Add(p); else after.Add(p); } } if (found) { rem.Add(s); if (before.Count >= 2) { var ns = new Stroke(new StylusPointCollection(before)); ns.DrawingAttributes = s.DrawingAttributes.Clone(); add.Add(ns); } if (after.Count >= 2) { var ns = new Stroke(new StylusPointCollection(after)); ns.DrawingAttributes = s.DrawingAttributes.Clone(); add.Add(ns); } } } foreach (var s in rem) DrawingCanvas.Strokes.Remove(s); foreach (var s in add) DrawingCanvas.Strokes.Add(s); }

        private void UpdatePen() { DrawingCanvas.DefaultDrawingAttributes = new DrawingAttributes { Color = isErasing ? Colors.Transparent : penColor, Width = currentThickness, Height = currentThickness, FitToCurve = true, IsHighlighter = false }; }
        private void OnStrokesChanged(object? sender, System.Windows.Ink.StrokeCollectionChangedEventArgs e) { if (!isErasing) SaveDrawing(); }
        private void SaveDrawing()
        { try { var data = DrawingCanvas.Strokes.Select(s => new StrokeData { Points = s.StylusPoints.Select(p => new WpfPoint(p.X, p.Y)).ToList(), Color = s.DrawingAttributes.Color.ToString(), Width = s.DrawingAttributes.Width, Height = s.DrawingAttributes.Height }).ToList(); File.WriteAllText(savePath, JsonSerializer.Serialize(data)); } catch { } }
        private void LoadDrawing()
        { try { if (!File.Exists(savePath)) return; var data = JsonSerializer.Deserialize<List<StrokeData>>(File.ReadAllText(savePath)); if (data == null) return; foreach (var d in data) { var pts = new System.Windows.Input.StylusPointCollection(); foreach (var p in d.Points) pts.Add(new System.Windows.Input.StylusPoint(p.X, p.Y)); var s = new Stroke(pts); s.DrawingAttributes = new DrawingAttributes { Color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(d.Color), Width = d.Width, Height = d.Height }; DrawingCanvas.Strokes.Add(s); } } catch { } }

        // ==================== ПРОЕКТЫ ====================

        private bool HasUnsavedWork()
        {
            return _isDirty;
        }

        private bool ConfirmSaveBeforeAction(string action)
        {
            if (!HasUnsavedWork()) return true;
            var result = WinForms.MessageBox.Show(
                currentLanguage == "ru"
                    ? $"У вас есть несохранённые заметки. Сохранить перед {action}?"
                    : $"You have unsaved notes. Save before {action}?",
                "Ttrad",
                WinForms.MessageBoxButtons.YesNoCancel,
                WinForms.MessageBoxIcon.Question);
            if (result == WinForms.DialogResult.Yes)
            {
                SaveProject();
                return true;
            }
            return result == WinForms.DialogResult.No;
        }

        private void NewProject()
        {
            if (!ConfirmSaveBeforeAction(currentLanguage == "ru" ? "созданием нового" : "creating new"))
                return;

            DrawingCanvas.Strokes.Clear();
            foreach (var el in textElements)
                textCanvas?.Children.Remove(el);
            textElements.Clear();
            _penUndoStack.Clear();
            _eraserUndoStack.Clear();
            if (undoMenuItem != null) undoMenuItem.Enabled = false;
            currentProjectPath = "";
            _isDirty = false;
            SaveDrawing();
            SaveTextElements();
        }

        private void SaveProject()
        {
            if (string.IsNullOrEmpty(currentProjectPath))
            {
                SaveProjectAs();
                return;
            }
            SaveProjectToFile(currentProjectPath);
        }

        private void SaveProjectAs()
        {
            var dialog = new WinForms.SaveFileDialog
            {
                Title = currentLanguage == "ru" ? "Сохранить проект как" : "Save Project As",
                Filter = "Ttrad Project (*.ttrad)|*.ttrad",
                DefaultExt = "ttrad",
                InitialDirectory = projectsFolder,
                FileName = currentLanguage == "ru" ? "Мои заметки.ttrad" : "My Notes.ttrad"
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                currentProjectPath = dialog.FileName;
                SaveProjectToFile(currentProjectPath);
            }
        }

        private void SaveProjectToFile(string path)
        {
            try
            {
                var drawingData = DrawingCanvas.Strokes.Select(s => new StrokeData
                {
                    Points = s.StylusPoints.Select(p => new WpfPoint(p.X, p.Y)).ToList(),
                    Color = s.DrawingAttributes.Color.ToString(),
                    Width = s.DrawingAttributes.Width,
                    Height = s.DrawingAttributes.Height
                }).ToList();

                var textData = new List<TextElementData>();
                foreach (Canvas container in textElements)
                {
                    if (container.Children.Count == 0) continue;
                    var tb = container.Children[0] as WpfTextBlock;
                    if (tb != null)
                        textData.Add(new TextElementData
                        {
                            Text = tb.Text,
                            X = Canvas.GetLeft(container),
                            Y = Canvas.GetTop(container),
                            FontFamily = tb.FontFamily.Source,
                            FontSize = tb.FontSize,
                            FontStyle = tb.FontStyle.ToString(),
                            FontWeight = tb.FontWeight.ToString(),
                            Color = ((SolidColorBrush)tb.Foreground).Color.ToString()
                        });
                }

                var project = new ProjectData
                {
                    Drawing = drawingData,
                    Text = textData,
                    VirtualScreenWidth = SystemParameters.VirtualScreenWidth,
                    VirtualScreenHeight = SystemParameters.VirtualScreenHeight
                };

                var json = JsonSerializer.Serialize(project);
                using (var fs = new FileStream(path, FileMode.Create))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("data.json");
                    using (var writer = new StreamWriter(entry.Open()))
                        writer.Write(json);
                }
                _isDirty = false;
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(
                    (currentLanguage == "ru" ? "Ошибка сохранения: " : "Save error: ") + ex.Message,
                    "Ttrad", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        private void OpenProject()
        {
            if (!ConfirmSaveBeforeAction(currentLanguage == "ru" ? "открытием другого" : "opening another"))
                return;

            var dialog = new WinForms.OpenFileDialog
            {
                Title = currentLanguage == "ru" ? "Открыть проект" : "Open Project",
                Filter = "Ttrad Project (*.ttrad)|*.ttrad",
                DefaultExt = "ttrad",
                InitialDirectory = projectsFolder
            };
            if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;

            try
            {
                ProjectData? project = null;
                using (var fs = new FileStream(dialog.FileName, FileMode.Open))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry("data.json");
                    if (entry != null)
                    {
                        using (var reader = new StreamReader(entry.Open()))
                            project = JsonSerializer.Deserialize<ProjectData>(reader.ReadToEnd());
                    }
                }

                if (project == null)
                {
                    WinForms.MessageBox.Show(
                        currentLanguage == "ru" ? "Файл повреждён." : "File is corrupted.",
                        "Ttrad", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                    return;
                }

                double currentW = SystemParameters.VirtualScreenWidth;
                double currentH = SystemParameters.VirtualScreenHeight;
                bool needsShift = false;
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var d in project.Drawing)
                {
                    foreach (var p in d.Points)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                }
                foreach (var t in project.Text)
                {
                    if (t.X < minX) minX = t.X;
                    if (t.Y < minY) minY = t.Y;
                    if (t.X > maxX) maxX = t.X;
                    if (t.Y > maxY) maxY = t.Y;
                }

                if (minX == double.MaxValue) { minX = 0; minY = 0; maxX = 0; maxY = 0; }

                double shiftX = 0, shiftY = 0;
                if (minX < 0 || maxX >= currentW || minY < 0 || maxY >= currentH)
                    needsShift = true;

                if (needsShift)
                {
                    var shiftResult = WinForms.MessageBox.Show(
                        currentLanguage == "ru"
                            ? "Заметки были созданы на другом разрешении. Сдвинуть в видимую область?"
                            : "Notes were created on a different resolution. Shift to visible area?",
                        "Ttrad",
                        WinForms.MessageBoxButtons.YesNo,
                        WinForms.MessageBoxIcon.Question);
                    if (shiftResult == WinForms.DialogResult.Yes)
                    {
                        shiftX = 50 - minX;
                        shiftY = 50 - minY;
                    }
                }

                DrawingCanvas.Strokes.Clear();
                foreach (var el in textElements)
                    textCanvas?.Children.Remove(el);
                textElements.Clear();
                _penUndoStack.Clear();
                _eraserUndoStack.Clear();
                if (undoMenuItem != null) undoMenuItem.Enabled = false;

                foreach (var d in project.Drawing)
                {
                    var pts = new System.Windows.Input.StylusPointCollection();
                    foreach (var p in d.Points)
                        pts.Add(new System.Windows.Input.StylusPoint(p.X + shiftX, p.Y + shiftY));
                    var s = new Stroke(pts);
                    s.DrawingAttributes = new DrawingAttributes
                    {
                        Color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(d.Color),
                        Width = d.Width,
                        Height = d.Height
                    };
                    DrawingCanvas.Strokes.Add(s);
                }

                foreach (var t in project.Text)
                {
                    var textBlock = new WpfTextBlock
                    {
                        Text = t.Text,
                        FontFamily = new WpfFontFamily(t.FontFamily),
                        FontSize = t.FontSize,
                        FontStyle = t.FontStyle == "Italic" ? WpfFontStyles.Italic : WpfFontStyles.Normal,
                        FontWeight = t.FontWeight == "Bold" ? WpfFontWeights.Bold : WpfFontWeights.Normal,
                        Foreground = new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(t.Color)),
                        Background = System.Windows.Media.Brushes.Transparent,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 400,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    var contextMenu = new WpfContextMenu();
                    var editItem = new WpfMenuItem { Header = strings.Edit };
                    editItem.Click += (s, e) => EditTextBlock(textBlock);
                    var deleteItem = new WpfMenuItem { Header = strings.Delete };
                    deleteItem.Click += (s, e) => DeleteTextBlock(textBlock);
                    contextMenu.Items.Add(editItem); contextMenu.Items.Add(deleteItem);
                    textBlock.ContextMenu = contextMenu;
                    textBlock.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { EditTextBlock(textBlock); e.Handled = true; } };
                    var container = new Canvas();
                    Canvas.SetLeft(container, t.X + shiftX);
                    Canvas.SetTop(container, t.Y + shiftY);
                    container.Children.Add(textBlock);
                    container.Background = System.Windows.Media.Brushes.Transparent;
                    MakeTextDraggable(container, textBlock);
                    textCanvas?.Children.Add(container);
                    textElements.Add(container);
                }

                currentProjectPath = dialog.FileName;
                _isDirty = false;
                SaveDrawing();
                SaveTextElements();
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(
                    (currentLanguage == "ru" ? "Ошибка открытия: " : "Open error: ") + ex.Message,
                    "Ttrad", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        // ==================== /ПРОЕКТЫ ====================

        // ==================== ОБНОВЛЕНИЯ ====================

        private async Task CheckForUpdates(bool showUpToDate)
        {
            try
            {
                var json = await _httpClient.GetStringAsync(VersionUrl);
                var data = JsonSerializer.Deserialize<VersionData>(json);
                if (data == null) return;

                int currentVersion = int.Parse(strings.AppVersion);
                int latestVersion = int.Parse(data.Version);

                if (latestVersion > currentVersion)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var result = WinForms.MessageBox.Show(
                            currentLanguage == "ru"
                                ? $"Доступна новая версия {data.Version}! Текущая: {strings.AppVersion}. Открыть страницу загрузки?"
                                : $"New version {data.Version} available! Current: {strings.AppVersion}. Open download page?",
                            "Ttrad - " + (currentLanguage == "ru" ? "Обновление" : "Update"),
                            WinForms.MessageBoxButtons.YesNo,
                            WinForms.MessageBoxIcon.Information);
                        if (result == WinForms.DialogResult.Yes)
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(data.Url) { UseShellExecute = true });
                    });
                }
                else if (showUpToDate)
                {
                    Dispatcher.Invoke(() =>
                    {
                        WinForms.MessageBox.Show(
                            currentLanguage == "ru" ? "У вас последняя версия." : "You have the latest version.",
                            "Ttrad",
                            WinForms.MessageBoxButtons.OK,
                            WinForms.MessageBoxIcon.Information);
                    });
                }
            }
            catch
            {
                if (showUpToDate)
                {
                    Dispatcher.Invoke(() =>
                    {
                        WinForms.MessageBox.Show(
                            currentLanguage == "ru" ? "Не удалось проверить обновления." : "Failed to check for updates.",
                            "Ttrad",
                            WinForms.MessageBoxButtons.OK,
                            WinForms.MessageBoxIcon.Warning);
                    });
                }
            }
        }

        // ==================== /ОБНОВЛЕНИЯ ====================

        private void ShowTextInputWindow()
        {
            if (textInputWindow != null && textInputWindow.IsVisible) textInputWindow.Close();
            textInputWindow = new Window
            {
                Title = strings.Text,
                Width = 450,
                Height = 350,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = true,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Math.Max(0, textInsertPoint.X),
                Top = Math.Max(0, textInsertPoint.Y)
            };
            textInputWindow.Closed += (s, e) => { _editingContainer = null; _editingTextBlock = null; };
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var settingsPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var fontCombo = new WpfComboBox { Width = 120, Margin = new Thickness(0, 0, 5, 0) }; fontCombo.Items.Add("Arial"); fontCombo.Items.Add("Times New Roman"); fontCombo.Items.Add("Segoe UI"); fontCombo.SelectedItem = currentFontFamily;
            var sizeCombo = new WpfComboBox { Width = 60, Margin = new Thickness(0, 0, 5, 0) }; sizeCombo.Items.Add("8"); sizeCombo.Items.Add("10"); sizeCombo.Items.Add("12"); sizeCombo.Items.Add("14"); sizeCombo.Items.Add("16"); sizeCombo.Items.Add("18"); sizeCombo.Items.Add("20"); sizeCombo.Items.Add("24"); sizeCombo.Items.Add("28"); sizeCombo.Items.Add("36"); sizeCombo.Items.Add("48"); sizeCombo.Items.Add("72"); sizeCombo.SelectedItem = ((int)currentFontSize).ToString();
            var boldToggle = new WpfCheckBox { Content = "B", FontWeight = WpfFontWeights.Bold, Margin = new Thickness(0, 0, 5, 0) }; boldToggle.IsChecked = currentBold;
            var italicToggle = new WpfCheckBox { Content = "I", FontStyle = WpfFontStyles.Italic, Margin = new Thickness(0, 0, 10, 0) }; italicToggle.IsChecked = currentItalic;
            var colorPicker = new WpfComboBox { Width = 80 }; colorPicker.Items.Add("Красный"); colorPicker.Items.Add("Зелёный"); colorPicker.Items.Add("Синий"); colorPicker.Items.Add("Жёлтый"); colorPicker.Items.Add("Оранжевый"); colorPicker.Items.Add("Фиолетовый"); colorPicker.Items.Add("Белый"); colorPicker.Items.Add("Чёрный");
            if (textColor == Colors.Red) colorPicker.SelectedIndex = 0; else if (textColor == Colors.Green) colorPicker.SelectedIndex = 1; else if (textColor == Colors.Blue) colorPicker.SelectedIndex = 2; else if (textColor == Colors.Yellow) colorPicker.SelectedIndex = 3; else if (textColor == Colors.Orange) colorPicker.SelectedIndex = 4; else if (textColor == Colors.Purple) colorPicker.SelectedIndex = 5; else if (textColor == Colors.White) colorPicker.SelectedIndex = 6; else if (textColor == Colors.Black) colorPicker.SelectedIndex = 7; else colorPicker.SelectedIndex = 0;
            settingsPanel.Children.Add(fontCombo); settingsPanel.Children.Add(sizeCombo); settingsPanel.Children.Add(boldToggle); settingsPanel.Children.Add(italicToggle); settingsPanel.Children.Add(colorPicker);
            Grid.SetRow(settingsPanel, 0); grid.Children.Add(settingsPanel);
            var label = new WpfTextBlock { Text = strings.EnterText, Margin = new Thickness(0, 5, 0, 5) }; Grid.SetRow(label, 1); grid.Children.Add(label);
            var textBox = new WpfTextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10),
                MinHeight = 100,
                MinWidth = 200,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 208, 208))
            };
            Grid.SetRow(textBox, 2); grid.Children.Add(textBox);
            void ApplyStyle() { textBox.FontFamily = new WpfFontFamily(fontCombo.SelectedItem?.ToString() ?? "Segoe UI"); textBox.FontSize = double.Parse(sizeCombo.SelectedItem?.ToString() ?? "16"); textBox.FontWeight = boldToggle.IsChecked == true ? WpfFontWeights.Bold : WpfFontWeights.Normal; textBox.FontStyle = italicToggle.IsChecked == true ? WpfFontStyles.Italic : WpfFontStyles.Normal; string colorName = colorPicker.SelectedItem?.ToString() ?? "Красный"; WpfColor color = colorName switch { "Красный" => Colors.Red, "Зелёный" => Colors.Green, "Синий" => Colors.Blue, "Жёлтый" => Colors.Yellow, "Оранжевый" => Colors.Orange, "Фиолетовый" => Colors.Purple, "Белый" => Colors.White, "Чёрный" => Colors.Black, _ => Colors.Red }; textBox.Foreground = new SolidColorBrush(color); }
            fontCombo.SelectionChanged += (s, e) => ApplyStyle(); sizeCombo.SelectionChanged += (s, e) => ApplyStyle(); boldToggle.Checked += (s, e) => ApplyStyle(); boldToggle.Unchecked += (s, e) => ApplyStyle(); italicToggle.Checked += (s, e) => ApplyStyle(); italicToggle.Unchecked += (s, e) => ApplyStyle(); colorPicker.SelectionChanged += (s, e) => ApplyStyle(); ApplyStyle();
            currentTextBox = textBox;
            currentTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { SaveTextSettings(fontCombo, sizeCombo, boldToggle, italicToggle, colorPicker); InsertText(); textInputWindow.Close(); } };
            var btnPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var okBtn = new WpfButton { Content = "OK", Width = 80, Height = 25, Margin = new Thickness(0, 0, 10, 0) }; okBtn.Click += (s, e) => { SaveTextSettings(fontCombo, sizeCombo, boldToggle, italicToggle, colorPicker); InsertText(); textInputWindow.Close(); };
            var cancelBtn = new WpfButton { Content = "Cancel", Width = 80, Height = 25 }; cancelBtn.Click += (s, e) => textInputWindow.Close();
            btnPanel.Children.Add(okBtn); btnPanel.Children.Add(cancelBtn); Grid.SetRow(btnPanel, 3); grid.Children.Add(btnPanel);
            textInputWindow.Content = grid; textInputWindow.Show(); currentTextBox.Focus();
        }

        private void SaveTextSettings(WpfComboBox fontCombo, WpfComboBox sizeCombo, WpfCheckBox boldToggle, WpfCheckBox italicToggle, WpfComboBox colorPicker)
        { currentFontFamily = fontCombo.SelectedItem?.ToString() ?? "Segoe UI"; currentFontSize = double.Parse(sizeCombo.SelectedItem?.ToString() ?? "16"); currentBold = boldToggle.IsChecked == true; currentItalic = italicToggle.IsChecked == true; string colorName = colorPicker.SelectedItem?.ToString() ?? "Красный"; textColor = colorName switch { "Красный" => Colors.Red, "Зелёный" => Colors.Green, "Синий" => Colors.Blue, "Жёлтый" => Colors.Yellow, "Оранжевый" => Colors.Orange, "Фиолетовый" => Colors.Purple, "Белый" => Colors.White, "Чёрный" => Colors.Black, _ => Colors.Red }; }

        private void MakeTextDraggable(Canvas container, WpfTextBlock textBlock)
        {
            bool isDragging = false; WpfPoint clickOffset; WpfPoint dragStartPoint; bool hasDragged = false; const double dragThreshold = 5.0;
            container.Cursor = System.Windows.Input.Cursors.SizeAll;
            container.MouseLeftButtonDown += (s, e) => { if (!isDrawingEnabled || !isTextMode) return; dragStartPoint = e.GetPosition(textCanvas); clickOffset = e.GetPosition(container); isDragging = false; hasDragged = false; container.CaptureMouse(); e.Handled = true; };
            container.MouseMove += (s, e) => { if (!container.IsMouseCaptured) return; var currentPoint = e.GetPosition(textCanvas); var diff = currentPoint - dragStartPoint; if (!hasDragged && (Math.Abs(diff.X) > dragThreshold || Math.Abs(diff.Y) > dragThreshold)) { hasDragged = true; isDragging = true; } if (isDragging) { var newPos = e.GetPosition(textCanvas); Canvas.SetLeft(container, newPos.X - clickOffset.X); Canvas.SetTop(container, newPos.Y - clickOffset.Y); } };
            container.MouseLeftButtonUp += (s, e) => { if (container.IsMouseCaptured) { container.ReleaseMouseCapture(); if (isDragging && hasDragged) { SaveTextElements(); } } };
        }

        private void InsertText()
        {
            if (currentTextBox == null || string.IsNullOrWhiteSpace(currentTextBox.Text)) return;
            if (_editingContainer != null) { textCanvas?.Children.Remove(_editingContainer); textElements.Remove(_editingContainer); _editingContainer = null; _editingTextBlock = null; }
            var textBlock = new WpfTextBlock { Text = currentTextBox.Text, FontFamily = new WpfFontFamily(currentFontFamily), FontSize = currentFontSize, FontStyle = currentItalic ? WpfFontStyles.Italic : WpfFontStyles.Normal, FontWeight = currentBold ? WpfFontWeights.Bold : WpfFontWeights.Normal, Foreground = new SolidColorBrush(textColor), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Cursor = System.Windows.Input.Cursors.Hand };
            var contextMenu = new WpfContextMenu(); var editItem = new WpfMenuItem { Header = strings.Edit }; editItem.Click += (s, e) => EditTextBlock(textBlock); var deleteItem = new WpfMenuItem { Header = strings.Delete }; deleteItem.Click += (s, e) => DeleteTextBlock(textBlock); contextMenu.Items.Add(editItem); contextMenu.Items.Add(deleteItem); textBlock.ContextMenu = contextMenu;
            textBlock.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { EditTextBlock(textBlock); e.Handled = true; } };
            var container = new Canvas(); Canvas.SetLeft(container, textInsertPoint.X); Canvas.SetTop(container, textInsertPoint.Y); container.Children.Add(textBlock); container.Background = System.Windows.Media.Brushes.Transparent;
            MakeTextDraggable(container, textBlock);
            textCanvas?.Children.Add(container); textElements.Add(container); currentTextBox = null;
            _isDirty = true;
            SaveTextElements();
        }

        private void EditTextBlock(WpfTextBlock textBlock)
        {
            if (!isTextMode) return;
            var container = textBlock.Parent as Canvas; if (container == null) return;
            double x = Canvas.GetLeft(container), y = Canvas.GetTop(container); textInsertPoint = new WpfPoint(x, y);
            currentFontFamily = textBlock.FontFamily.Source; currentFontSize = textBlock.FontSize; currentBold = textBlock.FontWeight == WpfFontWeights.Bold; currentItalic = textBlock.FontStyle == WpfFontStyles.Italic; textColor = ((SolidColorBrush)textBlock.Foreground).Color;
            _editingContainer = container; _editingTextBlock = textBlock;
            ShowTextInputWindow();
            if (currentTextBox != null) currentTextBox.Text = textBlock.Text; currentTextBox?.SelectAll();
        }

        private void DeleteTextBlock(WpfTextBlock textBlock)
        {
            if (WinForms.MessageBox.Show("Удалить эту заметку?", "Подтверждение", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) == WinForms.DialogResult.Yes)
            { var container = textBlock.Parent as Canvas; if (container != null) { textCanvas?.Children.Remove(container); textElements.Remove(container); SaveTextElements(); _isDirty = true; } }
        }

        private void SaveTextElements()
        { try { var textData = new List<TextElementData>(); foreach (Canvas container in textElements) { if (container.Children.Count == 0) continue; var textBlock = container.Children[0] as WpfTextBlock; if (textBlock != null) textData.Add(new TextElementData { Text = textBlock.Text, X = Canvas.GetLeft(container), Y = Canvas.GetTop(container), FontFamily = textBlock.FontFamily.Source, FontSize = textBlock.FontSize, FontStyle = textBlock.FontStyle.ToString(), FontWeight = textBlock.FontWeight.ToString(), Color = ((SolidColorBrush)textBlock.Foreground).Color.ToString() }); } File.WriteAllText(textSavePath, JsonSerializer.Serialize(textData)); } catch { } }

        private void LoadTextElements()
        { try { if (!File.Exists(textSavePath)) return; var textData = JsonSerializer.Deserialize<List<TextElementData>>(File.ReadAllText(textSavePath)); if (textData == null) return; foreach (var data in textData) { var textBlock = new WpfTextBlock { Text = data.Text, FontFamily = new WpfFontFamily(data.FontFamily), FontSize = data.FontSize, FontStyle = data.FontStyle == "Italic" ? WpfFontStyles.Italic : WpfFontStyles.Normal, FontWeight = data.FontWeight == "Bold" ? WpfFontWeights.Bold : WpfFontWeights.Normal, Foreground = new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(data.Color)), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Cursor = System.Windows.Input.Cursors.Hand }; var contextMenu = new WpfContextMenu(); var editItem = new WpfMenuItem { Header = strings.Edit }; editItem.Click += (s, e) => EditTextBlock(textBlock); var deleteItem = new WpfMenuItem { Header = strings.Delete }; deleteItem.Click += (s, e) => DeleteTextBlock(textBlock); contextMenu.Items.Add(editItem); contextMenu.Items.Add(deleteItem); textBlock.ContextMenu = contextMenu; textBlock.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { EditTextBlock(textBlock); e.Handled = true; } }; var container = new Canvas(); Canvas.SetLeft(container, data.X); Canvas.SetTop(container, data.Y); container.Children.Add(textBlock); container.Background = System.Windows.Media.Brushes.Transparent; MakeTextDraggable(container, textBlock); textCanvas?.Children.Add(container); textElements.Add(container); } } catch { } }

        private void SetupTrayIcon()
        {
            notifyIcon = new WinForms.NotifyIcon();
            strings.SetLanguage(currentLanguage);
            notifyIcon.Text = $"{strings.AppName} — {strings.Disabled}\n{strings.LeftClick} | {strings.RightClick} | {strings.EscExit}";
            notifyIcon.Visible = true;

            if (!firstRunHintShown)
            {
                firstRunHintShown = true;
                SaveSettings();

                WinForms.MessageBox.Show(
                    "Программа работает в фоне.\nИщи значок в области уведомлений\n(правый нижний угол, рядом с часами).\nВозможно, он скрыт под значком ʌ — нажми на него.\n\nЛКМ по значку — переключение инструментов\nПКМ по значку — настройки\n\nПодробная инструкция — в меню настроек (ПКМ → Справка).\n\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\nProgram is running in background.\nLook for the icon in the notification area\n(bottom-right corner, near the clock).\nIt may be hidden under the ʌ icon — click it.\n\nLeft Click — switch tools\nRight Click — settings\n\nFull guide — in settings menu (Right Click → Help).",
                    "Ttrad", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);

                var screen = WinForms.Screen.PrimaryScreen;
                var workingArea = screen.WorkingArea;
                var darkBg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
                var whiteColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                var lightGrayColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224));
                var lightAccent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe0, 0x93, 0x17));
                var darkAccent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x3f, 0x4b));
                var separatorColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.Margin = new Thickness(18, 14, 18, 8);

                var circleGrid = new Grid { Width = 24, Height = 24, HorizontalAlignment = WpfHorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
                var leftHalf = new System.Windows.Shapes.Ellipse { Width = 24, Height = 24, Fill = lightAccent, Clip = new RectangleGeometry(new Rect(0, 0, 12, 24)) };
                var rightHalf = new System.Windows.Shapes.Ellipse { Width = 24, Height = 24, Fill = darkAccent, Clip = new RectangleGeometry(new Rect(12, 0, 12, 24)) };
                circleGrid.Children.Add(leftHalf); circleGrid.Children.Add(rightHalf);
                Grid.SetRow(circleGrid, 0); grid.Children.Add(circleGrid);

                var smallIcon = new Grid { Width = 14, Height = 14, Margin = new Thickness(2, 0, 2, 0) };
                var smallLeft = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = lightAccent, Clip = new RectangleGeometry(new Rect(0, 0, 7, 14)) };
                var smallRight = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = darkAccent, Clip = new RectangleGeometry(new Rect(7, 0, 7, 14)) };
                smallIcon.Children.Add(smallLeft); smallIcon.Children.Add(smallRight);

                var textBlock = new WpfTextBlock
                {
                    FontFamily = new WpfFontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = lightGrayColor,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                textBlock.Inlines.Add(new Run("Ищи такой значок в области уведомлений.\n") { FontWeight = WpfFontWeights.SemiBold });
                textBlock.Inlines.Add(new Run("Он может быть скрыт под значком "));
                textBlock.Inlines.Add(new Run("ʌ") { FontSize = 16, FontWeight = WpfFontWeights.Bold, Foreground = whiteColor });
                textBlock.Inlines.Add(new Run(" — нажми на него.\n"));
                textBlock.Inlines.Add(new Run("Нажми на "));
                textBlock.Inlines.Add(new InlineUIContainer(smallIcon));
                textBlock.Inlines.Add(new Run(" ПРАВОЙ кнопкой мыши.\n\n") { FontWeight = WpfFontWeights.SemiBold });
                textBlock.Inlines.Add(new Run("—  —  —  —  —\n") { Foreground = separatorColor });
                textBlock.Inlines.Add(new Run("Look for this icon in the notification area.\n") { FontWeight = WpfFontWeights.SemiBold });
                textBlock.Inlines.Add(new Run("It may be hidden under the "));
                textBlock.Inlines.Add(new Run("ʌ") { FontSize = 16, FontWeight = WpfFontWeights.Bold, Foreground = whiteColor });
                textBlock.Inlines.Add(new Run(" icon — click it.\n"));
                textBlock.Inlines.Add(new Run("Right-click on the "));
                textBlock.Inlines.Add(new InlineUIContainer(new Grid
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(2, 0, 2, 0),
                    Children =
                    {
                        new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = lightAccent, Clip = new RectangleGeometry(new Rect(0, 0, 7, 14)) },
                        new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = darkAccent, Clip = new RectangleGeometry(new Rect(7, 0, 7, 14)) }
                    }
                }));
                textBlock.Inlines.Add(new Run(" icon.") { FontWeight = WpfFontWeights.SemiBold });
                Grid.SetRow(textBlock, 1); grid.Children.Add(textBlock);

                var arrowCanvas = new Canvas { Width = 20, Height = 12, HorizontalAlignment = WpfHorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
                var line1 = new System.Windows.Shapes.Line { X1 = 10, Y1 = 0, X2 = 10, Y2 = 8, Stroke = lightGrayColor, StrokeThickness = 1.5 };
                var line2 = new System.Windows.Shapes.Line { X1 = 5, Y1 = 5, X2 = 10, Y2 = 8, Stroke = lightGrayColor, StrokeThickness = 1.5 };
                var line3 = new System.Windows.Shapes.Line { X1 = 15, Y1 = 5, X2 = 10, Y2 = 8, Stroke = lightGrayColor, StrokeThickness = 1.5 };
                arrowCanvas.Children.Add(line1); arrowCanvas.Children.Add(line2); arrowCanvas.Children.Add(line3);
                Grid.SetRow(arrowCanvas, 2); grid.Children.Add(arrowCanvas);

                trayHintWindow = new Window
                {
                    Title = "",
                    Width = 400,
                    Height = 280,
                    WindowStyle = WindowStyle.None,
                    Topmost = true,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = darkBg,
                    Content = grid,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                trayHintWindow.Show();
                trayHintWindow.Left = workingArea.Right - trayHintWindow.Width - 10;
                trayHintWindow.Top = workingArea.Bottom - trayHintWindow.Height - 5;

                var closeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                closeTimer.Tick += (s2, args2) => { trayHintWindow?.Close(); trayHintWindow = null; closeTimer.Stop(); };
                closeTimer.Start();
            }

            notifyIcon.MouseClick += (s, e) =>
            {
                if (trayHintWindow != null) { trayHintWindow.Close(); trayHintWindow = null; }
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    if (!isDrawingEnabled) { isErasing = false; isTextMode = false; isDrawingEnabled = true; }
                    else if (!isErasing && !isTextMode) { isErasing = true; isTextMode = false; }
                    else if (isErasing) { isErasing = false; isTextMode = true; }
                    else if (isTextMode) { isErasing = false; isTextMode = false; isDrawingEnabled = false; }
                    UpdatePen(); SetDrawingState(isDrawingEnabled); UpdateTrayIcon();
                }
            };
            settingsMenu = new WinForms.ContextMenuStrip();
            SetupSettingsMenu();
            notifyIcon.ContextMenuStrip = settingsMenu;
        }

        private void SetupSettingsMenu()
        {
            if (settingsMenu == null) return;
            settingsMenu.Items.Clear();
            settingsMenu.AutoClose = false;

            var applyCloseItem = new WinForms.ToolStripMenuItem(strings.ApplyAndClose);
            applyCloseItem.Click += (s, e) => { settingsMenu?.Close(); };
            settingsMenu.Items.Add(applyCloseItem);
            settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var penItem = new WinForms.ToolStripMenuItem();
            penItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; pe.Graphics.Clear(mi.BackColor); try { var a = System.Reflection.Assembly.GetExecutingAssembly(); using (var st = a.GetManifestResourceStream("Ttrad.icon_pen_20.png")) { if (st != null) using (var bmp = new Bitmap(st)) pe.Graphics.DrawImage(bmp, 10, 3, 18, 18); } } catch { } pe.Graphics.DrawString(strings.Pen, new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Black, 35, 7); };
            penItem.Click += (s, e) => { isErasing = false; isTextMode = false; UpdatePen(); isDrawingEnabled = true; SetDrawingState(true); UpdateTrayIcon(); settingsMenu?.Close(); };
            settingsMenu.Items.Add(penItem);

            var eraserItem = new WinForms.ToolStripMenuItem();
            eraserItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; pe.Graphics.Clear(mi.BackColor); try { var a = System.Reflection.Assembly.GetExecutingAssembly(); using (var st = a.GetManifestResourceStream("Ttrad.icon_eraser_20.png")) { if (st != null) using (var bmp = new Bitmap(st)) pe.Graphics.DrawImage(bmp, 10, 3, 18, 18); } } catch { } pe.Graphics.DrawString(strings.Eraser, new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Black, 35, 7); };
            eraserItem.Click += (s, e) => { isErasing = true; isTextMode = false; UpdatePen(); isDrawingEnabled = true; SetDrawingState(true); UpdateTrayIcon(); settingsMenu?.Close(); };
            settingsMenu.Items.Add(eraserItem);

            var textItem = new WinForms.ToolStripMenuItem();
            textItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; pe.Graphics.Clear(mi.BackColor); try { var a = System.Reflection.Assembly.GetExecutingAssembly(); using (var st = a.GetManifestResourceStream("Ttrad.icon_text_20.png")) { if (st != null) using (var bmp = new Bitmap(st)) pe.Graphics.DrawImage(bmp, 10, 3, 18, 18); } } catch { } pe.Graphics.DrawString(strings.Text, new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Black, 35, 7); };
            textItem.Click += (s, e) => { isErasing = false; isTextMode = true; UpdatePen(); isDrawingEnabled = true; SetDrawingState(true); UpdateTrayIcon(); settingsMenu?.Close(); };
            settingsMenu.Items.Add(textItem);

            var offItem = new WinForms.ToolStripMenuItem();
            offItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality; pe.Graphics.Clear(mi.BackColor); try { var a = System.Reflection.Assembly.GetExecutingAssembly(); using (var st = a.GetManifestResourceStream("Ttrad.icon_off_20.png")) { if (st != null) using (var bmp = new Bitmap(st)) pe.Graphics.DrawImage(bmp, 10, 3, 18, 18); } } catch { } pe.Graphics.DrawString(strings.Off, new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Black, 35, 7); };
            offItem.Click += (s, e) => { isDrawingEnabled = false; isTextMode = false; _penUndoStack.Clear(); _eraserUndoStack.Clear(); if (undoMenuItem != null) undoMenuItem.Enabled = false; SetDrawingState(false); UpdateTrayIcon(); settingsMenu?.Close(); };
            settingsMenu.Items.Add(offItem);
            settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            undoMenuItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Отменить" : "Undo");
            undoMenuItem.Enabled = false;
            undoMenuItem.Click += (s, e) => { UndoLastAction(); };
            settingsMenu.Items.Add(undoMenuItem);
            settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            thicknessMenuItem = new WinForms.ToolStripMenuItem();
            thicknessMenuItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; pe.Graphics.Clear(mi.BackColor); using (var font = new System.Drawing.Font("Segoe UI", 9)) { string text = strings.Thickness + ":"; var ts = pe.Graphics.MeasureString(text, font); float ty = (mi.Height - ts.Height) / 2; pe.Graphics.DrawString(text, font, System.Drawing.Brushes.Black, 35, ty); float sx = 100 + (float)currentThickness / 2; float len = 40 - (float)currentThickness * 0.9f; float ex = sx + len; int ly = mi.Height / 2; using (var p = new System.Drawing.Pen(System.Drawing.Color.Black, (float)currentThickness)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; pe.Graphics.DrawLine(p, sx, ly, ex, ly); } } pe.Graphics.DrawString("▶", new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Gray, mi.Width - 25, (mi.Height - 12) / 2); };
            var thicknessMenu = new WinForms.ToolStripMenuItem(); thicknessMenu.DropDownItems.Add(thicknessMenuItem);
            foreach (var t in new[] { 2, 4, 6, 8, 12, 16, 20 }) { var it = CreateThicknessItem(t); it.Click += (s, e) => { currentThickness = t; UpdatePen(); thicknessMenuItem?.Invalidate(); }; thicknessMenuItem.DropDownItems.Add(it); }
            settingsMenu.Items.Add(thicknessMenuItem);

            colorMenuItem = new WinForms.ToolStripMenuItem();
            colorMenuItem.Paint += (s, pe) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || pe.Graphics == null) return; pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; pe.Graphics.Clear(mi.BackColor); using (var font = new System.Drawing.Font("Segoe UI", 9)) { string label = currentLanguage == "ru" ? "Цвет:" : "Color:"; var ts = pe.Graphics.MeasureString(label, font); float ty = (mi.Height - ts.Height) / 2; pe.Graphics.DrawString(label, font, System.Drawing.Brushes.Black, 35, ty); int sq = 14; float sqx = 100; float sqy = (mi.Height - sq) / 2; using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(penColor.R, penColor.G, penColor.B))) pe.Graphics.FillRectangle(brush, sqx, sqy, sq, sq); } pe.Graphics.DrawString("▶", new System.Drawing.Font("Segoe UI", 9), System.Drawing.Brushes.Gray, mi.Width - 25, (mi.Height - 12) / 2); };
            var colorMenu = new WinForms.ToolStripMenuItem(); colorMenu.DropDownItems.Add(colorMenuItem);
            var colors = new (string Name, WpfColor Color)[] { ("Красный", Colors.Red), ("Зелёный", Colors.Green), ("Синий", Colors.Blue), ("Жёлтый", Colors.Yellow), ("Оранжевый", Colors.Orange), ("Фиолетовый", Colors.Purple), ("Белый", Colors.White), ("Чёрный", Colors.Black) };
            foreach (var (name, color) in colors) { var item = CreateColorItem(name, color); item.Click += (s, e) => { penColor = color; UpdatePen(); colorMenuItem?.Invalidate(); }; colorMenuItem.DropDownItems.Add(item); }
            settingsMenu.Items.Add(colorMenuItem);
            settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var newProjectItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Новый" : "New");
            newProjectItem.Click += (s, e) => { NewProject(); };
            settingsMenu.Items.Add(newProjectItem);

            var saveProjectItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Сохранить" : "Save");
            saveProjectItem.Click += (s, e) => { SaveProject(); };
            settingsMenu.Items.Add(saveProjectItem);

            var saveAsProjectItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Сохранить как..." : "Save As...");
            saveAsProjectItem.Click += (s, e) => { SaveProjectAs(); };
            settingsMenu.Items.Add(saveAsProjectItem);

            var openProjectItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Открыть..." : "Open...");
            openProjectItem.Click += (s, e) => { OpenProject(); };
            settingsMenu.Items.Add(openProjectItem);

            var checkUpdatesItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Проверить обновления" : "Check for Updates");
            checkUpdatesItem.Click += (s, e) => { Task.Run(() => CheckForUpdates(true)); };
            settingsMenu.Items.Add(checkUpdatesItem);

            settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var helpItem = new WinForms.ToolStripMenuItem(currentLanguage == "ru" ? "Справка" : "Help");
            helpItem.Click += (s, e) =>
            {
                if (currentLanguage == "ru")
                    WinForms.MessageBox.Show(
                        "ЛКМ по иконке в трее — переключение инструментов\nПКМ по иконке — настройки\n\nКарандаш — рисуй по экрану\nЛастик — стирай нарисованное (толщина меняется)\n\nТекст:\n   • ЛКМ по пустому месту — ввод текста\n   • ЛКМ + удержание по тексту — перетаскивание\n   • ПКМ по тексту — редактировать / удалить\n\nОтменить — отмена последнего действия\n\nНовый/Сохранить/Открыть — проекты (.ttrad)\nПроверить обновления — новая версия\n\nESC — выключить любой режим",
                        "Ttrad - Справка",
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Information);
                else
                    WinForms.MessageBox.Show(
                        "Left Click on tray icon — switch tools\nRight Click on tray icon — settings\n\nPen — draw on screen\nEraser — erase drawings (thickness adjustable)\n\nText:\n   • Left Click on empty space — add text\n   • Left Click + hold on text — drag\n   • Right Click on text — edit / delete\n\nUndo — restore last action\n\nNew/Save/Open — projects (.ttrad)\nCheck for Updates — new version\n\nESC — exit any mode",
                        "Ttrad - Help",
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Information);
            };
            settingsMenu.Items.Add(helpItem);

            var langMenu = new WinForms.ToolStripMenuItem(strings.Language);
            var ru = new WinForms.ToolStripMenuItem("Русский") { Checked = currentLanguage == "ru" }; ru.Click += (s, e) => { currentLanguage = "ru"; SaveSettings(); UpdateTrayIcon(); SetupSettingsMenu(); };
            var en = new WinForms.ToolStripMenuItem("English") { Checked = currentLanguage == "en" }; en.Click += (s, e) => { currentLanguage = "en"; SaveSettings(); UpdateTrayIcon(); SetupSettingsMenu(); };
            langMenu.DropDownItems.Add(ru); langMenu.DropDownItems.Add(en); settingsMenu.Items.Add(langMenu); settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var clearItem = new WinForms.ToolStripMenuItem(strings.ClearAll);
            clearItem.Click += (s, e) =>
            {
                if (HasUnsavedWork())
                {
                    var saveResult = WinForms.MessageBox.Show(
                        currentLanguage == "ru" ? "Сохранить текущие заметки перед очисткой?" : "Save current notes before clearing?",
                        "Ttrad",
                        WinForms.MessageBoxButtons.YesNoCancel,
                        WinForms.MessageBoxIcon.Question);
                    if (saveResult == WinForms.DialogResult.Yes)
                        SaveProject();
                    else if (saveResult == WinForms.DialogResult.Cancel)
                        return;
                }
                if (WinForms.MessageBox.Show(strings.ClearConfirm, strings.AppName, WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Warning) == WinForms.DialogResult.Yes)
                {
                    DrawingCanvas.Strokes.Clear();
                    foreach (var el in textElements) textCanvas?.Children.Remove(el);
                    textElements.Clear();
                    _penUndoStack.Clear();
                    _eraserUndoStack.Clear();
                    if (undoMenuItem != null) undoMenuItem.Enabled = false;
                    _isDirty = false;
                    SaveDrawing();
                    SaveTextElements();
                }
            };
            settingsMenu.Items.Add(clearItem); settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var startupItem = new WinForms.ToolStripMenuItem(strings.Startup); startupItem.Checked = IsStartupEnabled();
            startupItem.Click += (s, e) => { startupItem.Checked = !startupItem.Checked; SetStartup(startupItem.Checked); };
            settingsMenu.Items.Add(startupItem); settingsMenu.Items.Add(new WinForms.ToolStripSeparator());

            var exitItem = new WinForms.ToolStripMenuItem(strings.Exit);
            exitItem.Click += (s, e) =>
            {
                if (_isDirty)
                {
                    var saveResult = WinForms.MessageBox.Show(
                        currentLanguage == "ru" ? "Сохранить заметки перед выходом?" : "Save notes before exit?",
                        "Ttrad",
                        WinForms.MessageBoxButtons.YesNoCancel,
                        WinForms.MessageBoxIcon.Question);
                    if (saveResult == WinForms.DialogResult.Yes)
                        SaveProject();
                    else if (saveResult == WinForms.DialogResult.Cancel)
                        return;
                }
                if (WinForms.MessageBox.Show(strings.ExitConfirm, strings.AppName, WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) == WinForms.DialogResult.Yes)
                {
                    isExiting = true;
                    notifyIcon?.Dispose();
                    WpfApplication.Current.Shutdown();
                }
            };
            settingsMenu.Items.Add(exitItem);
        }

        private WinForms.ToolStripMenuItem CreateThicknessItem(double t)
        {
            var it = new WinForms.ToolStripMenuItem(); it.Text = "";
            it.Paint += (s, e) => { var mi = s as WinForms.ToolStripMenuItem; if (mi == null || e.Graphics == null) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.Clear(mi.BackColor); int y = mi.Height / 2; float sx = 10 + (float)t / 2; float len = 40 - (float)t * 0.9f; float ex = sx + len; using (var p = new System.Drawing.Pen(System.Drawing.Color.Black, (float)t)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; e.Graphics.DrawLine(p, sx, y, ex, y); } };
            return it;
        }

        private WinForms.ToolStripMenuItem CreateColorItem(string name, WpfColor color)
        {
            var item = new WinForms.ToolStripMenuItem(); item.Text = ""; item.ToolTipText = name;
            item.Paint += (sender, e) => { var mi = sender as WinForms.ToolStripMenuItem; if (mi == null || e.Graphics == null) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.Clear(mi.BackColor); int sq = 16, x = 10, y = (mi.Height - sq) / 2; using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(color.R, color.G, color.B))) e.Graphics.FillRectangle(brush, x, y, sq, sq); };
            return item;
        }

        private bool IsStartupEnabled()
        { try { using var k = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true); return k?.GetValue("Ttrad") != null; } catch { return false; } }

        private void SetStartup(bool en)
        { try { using var k = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true); if (en) k?.SetValue("Ttrad", $"\"{System.Reflection.Assembly.GetExecutingAssembly().Location}\""); else k?.DeleteValue("Ttrad", false); } catch { } }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!isExiting)
            {
                if (_isDirty)
                {
                    var result = WinForms.MessageBox.Show(
                        currentLanguage == "ru" ? "Сохранить заметки перед выходом?" : "Save notes before exit?",
                        "Ttrad",
                        WinForms.MessageBoxButtons.YesNoCancel,
                        WinForms.MessageBoxIcon.Question);
                    if (result == WinForms.DialogResult.Yes)
                        SaveProject();
                    else if (result == WinForms.DialogResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                SaveDrawing();
                SaveTextElements();
            }
            if (_currentRegion != IntPtr.Zero)
            {
                DeleteObject(_currentRegion);
                _currentRegion = IntPtr.Zero;
            }
            notifyIcon?.Dispose();
        }

        private class StrokeData { public List<WpfPoint> Points { get; set; } = new(); public string Color { get; set; } = ""; public double Width { get; set; } public double Height { get; set; } }
        private class TextElementData { public string Text { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string FontFamily { get; set; } = "Segoe UI"; public double FontSize { get; set; } = 16; public string FontStyle { get; set; } = "Normal"; public string FontWeight { get; set; } = "Normal"; public string Color { get; set; } = "#FFFF0000"; }
        private class ProjectData
        {
            public List<StrokeData> Drawing { get; set; } = new();
            public List<TextElementData> Text { get; set; } = new();
            public double VirtualScreenWidth { get; set; }
            public double VirtualScreenHeight { get; set; }
        }
        private class VersionData
        {
            public string Version { get; set; } = "";
            public string Url { get; set; } = "";
        }
        private class AppSettings { public string? Language { get; set; } public bool FirstRunHintShown { get; set; } public bool EscHintShown { get; set; } }

        private class AppStrings
        {
            public string AppName { get; set; } = "Ttrad";
            public string Pen { get; set; } = "Карандаш";
            public string Eraser { get; set; } = "Ластик";
            public string Text { get; set; } = "Текст";
            public string EnterText { get; set; } = "Введите текст:";
            public string Edit { get; set; } = "Редактировать";
            public string Delete { get; set; } = "Удалить";
            public string On { get; set; } = "ON";
            public string Off { get; set; } = "Выключено";
            public string Disabled { get; set; } = "Отключено";
            public string LeftClick { get; set; } = "ЛКМ — переключить";
            public string RightClick { get; set; } = "ПКМ — настройки";
            public string EscExit { get; set; } = "ESC — выкл";
            public string Language { get; set; } = "Язык";
            public string Thickness { get; set; } = "Толщина";
            public string PencilColor { get; set; } = "Цвет карандаша";
            public string ApplyAndClose { get; set; } = "Применить и закрыть";
            public string ClearAll { get; set; } = "Очистить всё";
            public string ClearConfirm { get; set; } = "Будут удалены все рисунки и весь текст. Продолжить?";
            public string Startup { get; set; } = "Запускать с Windows";
            public string Exit { get; set; } = "Выход";
            public string ExitConfirm { get; set; } = "Выйти из Ttrad?";
            public string AppVersion { get; set; } = "18";

            public void SetLanguage(string l)
            {
                if (l == "ru")
                {
                    AppName = "Ttrad"; Pen = "Карандаш"; Eraser = "Ластик"; Text = "Текст";
                    EnterText = "Введите текст:"; Edit = "Редактировать"; Delete = "Удалить";
                    On = "ON"; Off = "Выключено"; Disabled = "Отключено";
                    LeftClick = "ЛКМ — переключить"; RightClick = "ПКМ — настройки"; EscExit = "ESC — выкл";
                    Language = "Язык"; Thickness = "Толщина"; PencilColor = "Цвет карандаша";
                    ApplyAndClose = "Применить и закрыть"; ClearAll = "Очистить всё";
                    ClearConfirm = "Будут удалены все рисунки и весь текст. Продолжить?";
                    Startup = "Запускать с Windows"; Exit = "Выход"; ExitConfirm = "Выйти из Ttrad?";
                    AppVersion = "18";
                }
                else
                {
                    AppName = "Ttrad"; Pen = "Pen"; Eraser = "Eraser"; Text = "Text";
                    EnterText = "Enter text:"; Edit = "Edit"; Delete = "Delete";
                    On = "ON"; Off = "OFF"; Disabled = "Disabled";
                    LeftClick = "Left Click — Switch"; RightClick = "Right Click — Settings"; EscExit = "ESC — Exit";
                    Language = "Language"; Thickness = "Thickness"; PencilColor = "Pencil Color";
                    ApplyAndClose = "Apply and Close"; ClearAll = "Clear All";
                    ClearConfirm = "All drawings and text will be deleted. Continue?";
                    Startup = "Run on Windows Startup"; Exit = "Exit"; ExitConfirm = "Exit Ttrad?";
                    AppVersion = "18";
                }
            }
        }
    }
}