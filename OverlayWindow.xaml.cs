using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Scribo;

public enum ToolType
{
    Cursor,
    Select,
    Pen,
    Highlighter,
    Laser,
    Arrow,
    DoubleArrow,
    Line,
    Rectangle,
    Circle,
    Text,
    StepBadge,
    Eraser
}

public partial class OverlayWindow : Window
{
    public event Action? ShapeDrawn;

    private ToolType _currentTool = ToolType.Cursor;
    public ToolType CurrentTool => _currentTool;
    private Color _currentColor = (Color)ColorConverter.ConvertFromString("#FFE600");
    public Color CurrentColor => _currentColor;
    private double _currentSize = 5.0;

    // Undo / Redo history
    private readonly Stack<StrokeCollection> _undoStack = new();
    private readonly Stack<StrokeCollection> _redoStack = new();
    private bool _isPerformingHistoryAction;

    // Shape drawing
    private bool _isDrawingShape;
    private Point _shapeStartPoint;
    private Point _lastMovePoint;
    private Shape? _previewShape;

    // Board Mode (Transparent, Whiteboard, Grid, Ruled, Dots, Greenboard, Frosted)
    public enum BoardMode
    {
        Transparent,
        Whiteboard,
        Grid,
        Ruled,
        Dots,
        Greenboard,
        Frosted
    }

    public enum BoardSplitMode
    {
        Full,
        SplitRight,
        SplitLeft
    }

    private BoardMode _currentBoardMode = BoardMode.Transparent;
    public BoardMode CurrentBoardMode => _currentBoardMode;

    private BoardMode _lastBoardStyle = BoardMode.Whiteboard;
    public BoardMode LastBoardStyle => _lastBoardStyle;

    private BoardSplitMode _splitMode = BoardSplitMode.Full;
    public BoardSplitMode SplitMode => _splitMode;

    public event Action<int, int>? BoardPageChanged;
    public event Action<Color>? PenColorAutoChanged;

    private class BoardPageData
    {
        public StrokeCollection Strokes { get; set; } = new();
        public List<UIElement> Children { get; set; } = new();
    }

    private readonly List<BoardPageData> _boardPages = new();
    private int _currentPageIndex = 0;

    private StrokeCollection _desktopStrokes = new();
    private List<UIElement> _desktopChildren = new();

    public int CurrentBoardPage => _currentPageIndex + 1;
    public int TotalBoardPages => Math.Max(1, _boardPages.Count);

    // Numbered Step Badge Counter
    private int _stepBadgeNumber = 1;
    public void ResetStepBadgeNumber() => _stepBadgeNumber = 1;

    // Inline Text Tool & Typography Formatting
    private TextBox? _activeTextBox;
    public bool IsEditingText => _activeTextBox != null && _activeTextBox.IsKeyboardFocusWithin;
    private FontFamily _textFontFamily = new FontFamily("Segoe UI");
    private double _textFontSize = 24.0;
    private bool _isTextBold = true;
    private bool _isTextItalic = false;
    private bool _isTextUnderline = false;
    public event Action<FontFamily, double, bool, bool, bool>? TextFormattingChanged;

    public FontFamily CurrentFontFamily => _textFontFamily;
    public double CurrentFontSize => _textFontSize;
    public bool IsTextBold => _isTextBold;
    public bool IsTextItalic => _isTextItalic;
    public bool IsTextUnderline => _isTextUnderline;

    // Toolbar exclusion zone (screen coordinates)
    private Rect _exclusionZone = Rect.Empty;
    private bool _hasWindowRegion = false;

    // Presentation Aids & Focus Tools State
    private bool _isSpotlightActive;
    public bool IsSpotlightActive
    {
        get => _isSpotlightActive;
        set
        {
            if (_isSpotlightActive == value) return;
            _isSpotlightActive = value;
            spotlightCanvas.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) EnsureCursorTracking();
            else CheckCursorTracking();
        }
    }

    private bool _isMagnifierActive;
    public bool IsMagnifierActive
    {
        get => _isMagnifierActive;
        set
        {
            if (_isMagnifierActive == value) return;
            _isMagnifierActive = value;
            magnifierCanvas.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value) EnsureCursorTracking();
            else CheckCursorTracking();
        }
    }

    private bool _isCursorHaloEnabled;
    public bool IsCursorHaloEnabled
    {
        get => _isCursorHaloEnabled;
        set
        {
            if (_isCursorHaloEnabled == value) return;
            _isCursorHaloEnabled = value;
            cursorHalo.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value)
            {
                cursorEffectsCanvas.Visibility = Visibility.Visible;
                EnsureCursorTracking();
            }
            else
            {
                CheckCursorTracking();
            }
        }
    }

    public bool IsShapeFilled { get; set; } = false;
    public bool IsVanishingLaserEnabled { get; set; } = true;

    public OverlayWindow()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            // Start in cursor / pass-through mode until a drawing tool is chosen
            SetInteractive(false);
            SaveUndoState();
        };

        inkCanvas.StrokeCollected += InkCanvas_StrokeCollected;
        inkCanvas.StrokeErased += (s, e) => SaveUndoState();
        inkCanvas.SelectionMoved += (s, e) => SaveUndoState();
        inkCanvas.SelectionResized += (s, e) => SaveUndoState();

        SizeChanged += (s, e) =>
        {
            if (_splitMode != BoardSplitMode.Full && _currentBoardMode != BoardMode.Transparent)
            {
                ApplyBoardMode();
            }
        };

        Closing += (s, e) =>
        {
            _cursorTrackingTimer?.Stop();
            _cursorTrackingTimer = null;
            _keystrokeHideTimer?.Stop();
            _keystrokeHideTimer = null;
        };
    }

    /// <summary>
    /// Sets the toolbar exclusion zone by punching a real hole in the overlay window
    /// using Win32 SetWindowRgn. The OS itself won't deliver mouse events to this area.
    /// The rect is in screen coordinates.
    /// </summary>
    public void SetExclusionZone(Rect toolbarScreenRect)
    {
        _exclusionZone = toolbarScreenRect;
        ApplyWindowRegion();
    }

    private void ApplyWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // In Board Mode, the board must remain a solid, unbroken background!
        // Punching a window region hole cuts through the board and exposes the user's desktop screen.
        // Therefore, we only apply exclusion region clipping in Transparent desktop mode.
        if (_currentBoardMode != BoardMode.Transparent || _exclusionZone == Rect.Empty || _exclusionZone.Width <= 0 || _exclusionZone.Height <= 0)
        {
            if (_hasWindowRegion)
            {
                // Reset to full window region (solid fullscreen board, zero holes)
                NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
                _hasWindowRegion = false;
            }
            return;
        }

        // Get DPI scaling factor
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        // Full overlay region (in device pixels)
        int fullW = (int)(Width * dpiX);
        int fullH = (int)(Height * dpiY);
        IntPtr fullRgn = NativeMethods.CreateRectRgn(0, 0, fullW, fullH);

        // Toolbar exclusion rect (convert from screen coords to overlay-local coords, then to device pixels)
        int exLeft = (int)((_exclusionZone.Left - Left) * dpiX);
        int exTop = (int)((_exclusionZone.Top - Top) * dpiY);
        int exRight = (int)((_exclusionZone.Right - Left) * dpiX);
        int exBottom = (int)((_exclusionZone.Bottom - Top) * dpiY);

        IntPtr excludeRgn = NativeMethods.CreateRectRgn(exLeft, exTop, exRight, exBottom);

        // Subtract the toolbar rect from the full region
        IntPtr resultRgn = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        NativeMethods.CombineRgn(resultRgn, fullRgn, excludeRgn, NativeMethods.RGN_DIFF);

        // Apply the region — SetWindowRgn takes ownership, so don't delete resultRgn
        NativeMethods.SetWindowRgn(hwnd, resultRgn, true);
        _hasWindowRegion = true;

        // Clean up temp regions
        NativeMethods.DeleteObject(fullRgn);
        NativeMethods.DeleteObject(excludeRgn);
    }

    public void SetInteractive(bool interactive)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

        if (interactive)
        {
            Visibility = Visibility.Visible;
            long newStyle = style & ~NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

            inkCanvas.IsHitTestVisible = true;
            inkCanvas.Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
        }
        else
        {
            // Cursor / Pass-through mode:
            // Mouse clicks pass straight through to desktop and underlying apps!
            Visibility = Visibility.Visible;
            long newStyle = style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);

            inkCanvas.IsHitTestVisible = false;
            inkCanvas.Background = Brushes.Transparent;
            shapePreviewCanvas.Children.Clear();
            _isDrawingShape = false;
        }
    }

    #region Board Mode (Transparent, Whiteboard, Grid, Ruled, Dots, Greenboard, Frosted)
    private static Brush? _gridBrush;
    private static Brush? _ruledBrush;
    private static Brush? _dotBrush;

    private static Brush GetGridBrush()
    {
        if (_gridBrush != null) return _gridBrush;
        var db = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 32, 32),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 32, 32),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1.0); // Slate-300 (#CBD5E1) - crisp & visible
        linePen.Freeze();
        var geom = new GeometryGroup();
        geom.Children.Add(new LineGeometry(new Point(0, 0), new Point(32, 0)));
        geom.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 32)));
        geom.Freeze();
        group.Children.Add(new GeometryDrawing(null, linePen, geom));
        group.Freeze();
        db.Drawing = group;
        db.Freeze();
        _gridBrush = db;
        return db;
    }

    private static Brush GetRuledBrush()
    {
        if (_ruledBrush != null) return _ruledBrush;
        var db = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 100, 36),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 100, 36),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 100, 36))));
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(147, 197, 253)), 1.2); // Notebook soft blue (#93C5FD)
        linePen.Freeze();
        group.Children.Add(new GeometryDrawing(null, linePen, new LineGeometry(new Point(0, 0), new Point(100, 0))));
        group.Freeze();
        db.Drawing = group;
        db.Freeze();
        _ruledBrush = db;
        return db;
    }

    private static Brush GetDotBrush()
    {
        if (_dotBrush != null) return _dotBrush;
        var db = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 28, 28),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 28, 28),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
        var dotGeom = new EllipseGeometry(new Point(14, 14), 2.0, 2.0);
        dotGeom.Freeze();
        var dotBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate-400 (#94A3B8)
        dotBrush.Freeze();
        group.Children.Add(new GeometryDrawing(dotBrush, null, dotGeom));
        group.Freeze();
        db.Drawing = group;
        db.Freeze();
        _dotBrush = db;
        return db;
    }

    public BoardMode CycleBoardMode()
    {
        var target = _currentBoardMode == BoardMode.Transparent ? _lastBoardStyle : BoardMode.Transparent;
        SetBoardMode(target);
        return _currentBoardMode;
    }

    public void SetBoardMode(BoardMode mode)
    {
        bool wasTransparent = _currentBoardMode == BoardMode.Transparent;
        bool isNowTransparent = mode == BoardMode.Transparent;

        if (wasTransparent && !isNowTransparent)
        {
            // Transitioning from Desktop to Board mode:
            // Save current desktop annotations
            CommitActiveText();
            _desktopStrokes = new StrokeCollection(inkCanvas.Strokes.Select(s => s.Clone()));
            _desktopChildren = inkCanvas.Children.OfType<UIElement>().ToList();

            // Ensure at least 1 board page exists
            if (_boardPages.Count == 0)
            {
                _boardPages.Add(new BoardPageData());
                _currentPageIndex = 0;
            }

            // Load active board page
            LoadCurrentBoardPage();
        }
        else if (!wasTransparent && isNowTransparent)
        {
            // Transitioning from Board to Desktop mode:
            // Save current board page
            CommitActiveText();
            SaveCurrentBoardPage();

            // Restore desktop annotations
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();
            foreach (var s in _desktopStrokes)
                inkCanvas.Strokes.Add(s.Clone());
            foreach (var elem in _desktopChildren)
                inkCanvas.Children.Add(elem);

            _undoStack.Clear();
            _redoStack.Clear();
            SaveUndoState();
        }

        if (!isNowTransparent)
        {
            _lastBoardStyle = mode;
        }

        _currentBoardMode = mode;
        ApplyBoardMode();

        // Check Smart Pen Auto-Contrast
        ApplySmartPenColor(mode);

        BoardPageChanged?.Invoke(CurrentBoardPage, TotalBoardPages);
    }

    public void SetBoardSplitMode(BoardSplitMode splitMode)
    {
        _splitMode = splitMode;
        ApplyBoardMode();
    }

    public void ApplyBoardMode()
    {
        if (_currentBoardMode == BoardMode.Transparent)
        {
            boardBackdrop.Background = Brushes.Transparent;
            boardBackdrop.Visibility = Visibility.Collapsed;
        }
        else
        {
            boardBackdrop.Visibility = Visibility.Visible;
            boardBackdrop.Background = _currentBoardMode switch
            {
                BoardMode.Whiteboard => Brushes.White,
                BoardMode.Grid => GetGridBrush(),
                BoardMode.Ruled => GetRuledBrush(),
                BoardMode.Dots => GetDotBrush(),
                BoardMode.Greenboard => new SolidColorBrush(Color.FromRgb(27, 77, 62)),
                BoardMode.Frosted => new SolidColorBrush(Color.FromArgb(220, 248, 250, 252)),
                _ => Brushes.White
            };

            double targetWidth = ActualWidth > 0 ? ActualWidth : Width;
            if (_splitMode == BoardSplitMode.SplitRight)
            {
                boardBackdrop.HorizontalAlignment = HorizontalAlignment.Right;
                boardBackdrop.Width = targetWidth / 2;
                boardBackdrop.BorderThickness = new Thickness(2, 0, 0, 0);
                boardBackdrop.BorderBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
            else if (_splitMode == BoardSplitMode.SplitLeft)
            {
                boardBackdrop.HorizontalAlignment = HorizontalAlignment.Left;
                boardBackdrop.Width = targetWidth / 2;
                boardBackdrop.BorderThickness = new Thickness(0, 0, 2, 0);
                boardBackdrop.BorderBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
            else
            {
                boardBackdrop.HorizontalAlignment = HorizontalAlignment.Stretch;
                boardBackdrop.Width = double.NaN;
                boardBackdrop.BorderThickness = new Thickness(0);
            }
        }

        ApplyWindowRegion();
    }

    private void ApplySmartPenColor(BoardMode mode)
    {
        if (mode == BoardMode.Greenboard)
        {
            // If pen color is dark, auto switch to clean white
            double brightness = (_currentColor.R * 0.299 + _currentColor.G * 0.587 + _currentColor.B * 0.114) / 255.0;
            if (brightness < 0.45)
            {
                SetColor(Colors.White);
                PenColorAutoChanged?.Invoke(Colors.White);
            }
        }
        else if (mode != BoardMode.Transparent)
        {
            // Whiteboard, Grid, Ruled, Dots, Frosted: if pen is pure white, auto switch to carbon black
            if (_currentColor.R > 230 && _currentColor.G > 230 && _currentColor.B > 230)
            {
                var darkColor = Color.FromRgb(15, 23, 42); // Carbon Black
                SetColor(darkColor);
                PenColorAutoChanged?.Invoke(darkColor);
            }
        }
    }

    private void SaveCurrentBoardPage()
    {
        if (_currentPageIndex >= 0 && _currentPageIndex < _boardPages.Count)
        {
            var page = _boardPages[_currentPageIndex];
            page.Strokes = new StrokeCollection(inkCanvas.Strokes.Select(s => s.Clone()));
            page.Children = inkCanvas.Children.OfType<UIElement>().ToList();
        }
    }

    private void LoadCurrentBoardPage()
    {
        if (_currentPageIndex >= 0 && _currentPageIndex < _boardPages.Count)
        {
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();
            var page = _boardPages[_currentPageIndex];
            foreach (var s in page.Strokes)
                inkCanvas.Strokes.Add(s.Clone());
            foreach (var elem in page.Children)
                inkCanvas.Children.Add(elem);

            _undoStack.Clear();
            _redoStack.Clear();
            SaveUndoState();
        }
    }

    public void NextBoardPage()
    {
        if (_currentBoardMode == BoardMode.Transparent) return;
        CommitActiveText();
        SaveCurrentBoardPage();

        if (_currentPageIndex + 1 >= _boardPages.Count)
        {
            _boardPages.Add(new BoardPageData());
        }
        _currentPageIndex++;
        LoadCurrentBoardPage();
        BoardPageChanged?.Invoke(CurrentBoardPage, TotalBoardPages);
    }

    public void PreviousBoardPage()
    {
        if (_currentBoardMode == BoardMode.Transparent) return;
        if (_currentPageIndex <= 0) return;

        CommitActiveText();
        SaveCurrentBoardPage();
        _currentPageIndex--;
        LoadCurrentBoardPage();
        BoardPageChanged?.Invoke(CurrentBoardPage, TotalBoardPages);
    }

    public void AddBoardPage()
    {
        if (_currentBoardMode == BoardMode.Transparent) return;
        CommitActiveText();
        SaveCurrentBoardPage();
        _currentPageIndex++;
        _boardPages.Insert(_currentPageIndex, new BoardPageData());
        LoadCurrentBoardPage();
        BoardPageChanged?.Invoke(CurrentBoardPage, TotalBoardPages);
    }
    #endregion

    #region Text & Step Badges
    public void CommitActiveText()
    {
        if (_activeTextBox == null) return;
        var text = _activeTextBox.Text.Trim();
        var left = InkCanvas.GetLeft(_activeTextBox);
        var top = InkCanvas.GetTop(_activeTextBox);
        var fontFamily = _activeTextBox.FontFamily;
        var fontSize = _activeTextBox.FontSize;
        var fontWeight = _activeTextBox.FontWeight;
        var fontStyle = _activeTextBox.FontStyle;
        var textDecorations = _activeTextBox.TextDecorations;
        var color = _activeTextBox.Foreground;

        inkCanvas.Children.Remove(_activeTextBox);
        _activeTextBox = null;

        if (!string.IsNullOrEmpty(text))
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                TextDecorations = textDecorations,
                Foreground = color
            };
            var textBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2),
                Child = textBlock
            };
            textBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && _currentTool == ToolType.Text)
                {
                    var origBlock = textBorder.Child as TextBlock;
                    var origText = origBlock?.Text ?? "";
                    var origFamily = origBlock?.FontFamily ?? _textFontFamily;
                    var origSize = origBlock?.FontSize ?? _textFontSize;
                    var origWeight = origBlock?.FontWeight ?? (_isTextBold ? FontWeights.Bold : FontWeights.Normal);
                    var origStyle = origBlock?.FontStyle ?? (_isTextItalic ? FontStyles.Italic : FontStyles.Normal);
                    var origDeco = origBlock?.TextDecorations;

                    inkCanvas.Children.Remove(textBorder);
                    ShowInlineTextInput(new Point(InkCanvas.GetLeft(textBorder), InkCanvas.GetTop(textBorder)));
                    if (_activeTextBox != null)
                    {
                        _activeTextBox.Text = origText;
                        _activeTextBox.FontFamily = origFamily;
                        _activeTextBox.FontSize = origSize;
                        _activeTextBox.FontWeight = origWeight;
                        _activeTextBox.FontStyle = origStyle;
                        _activeTextBox.TextDecorations = origDeco;
                        _activeTextBox.SelectAll();
                    }
                    e.Handled = true;
                }
            };
            InkCanvas.SetLeft(textBorder, left);
            InkCanvas.SetTop(textBorder, top);
            inkCanvas.Children.Add(textBorder);
            SaveUndoState();
        }
    }

    private void ShowInlineTextInput(Point pt)
    {
        CommitActiveText();

        var tb = new TextBox
        {
            FontFamily = _textFontFamily,
            FontSize = _textFontSize,
            FontWeight = _isTextBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _isTextItalic ? FontStyles.Italic : FontStyles.Normal,
            TextDecorations = _isTextUnderline ? TextDecorations.Underline : null,
            Foreground = new SolidColorBrush(_currentColor),
            CaretBrush = new SolidColorBrush(_currentColor),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 59, 130, 246)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(2),
            MinWidth = 80,
            AcceptsReturn = true
        };

        InkCanvas.SetLeft(tb, pt.X);
        InkCanvas.SetTop(tb, pt.Y);

        tb.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0))
            {
                CommitActiveText();
                e.Handled = true;
            }
            else if (e.Key == Key.B && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isTextBold = !_isTextBold;
                tb.FontWeight = _isTextBold ? FontWeights.Bold : FontWeights.Normal;
                TextFormattingChanged?.Invoke(_textFontFamily, _textFontSize, _isTextBold, _isTextItalic, _isTextUnderline);
                e.Handled = true;
            }
            else if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isTextItalic = !_isTextItalic;
                tb.FontStyle = _isTextItalic ? FontStyles.Italic : FontStyles.Normal;
                TextFormattingChanged?.Invoke(_textFontFamily, _textFontSize, _isTextBold, _isTextItalic, _isTextUnderline);
                e.Handled = true;
            }
            else if (e.Key == Key.U && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isTextUnderline = !_isTextUnderline;
                tb.TextDecorations = _isTextUnderline ? TextDecorations.Underline : null;
                TextFormattingChanged?.Invoke(_textFontFamily, _textFontSize, _isTextBold, _isTextItalic, _isTextUnderline);
                e.Handled = true;
            }
        };

        tb.LostFocus += (s, e) => CommitActiveText();

        _activeTextBox = tb;
        inkCanvas.Children.Add(tb);
        tb.Focus();
    }

    public void SetTextFormatting(FontFamily fontFamily, double fontSize, bool isBold, bool isItalic, bool isUnderline)
    {
        _textFontFamily = fontFamily;
        _textFontSize = fontSize;
        _isTextBold = isBold;
        _isTextItalic = isItalic;
        _isTextUnderline = isUnderline;

        if (_activeTextBox != null)
        {
            _activeTextBox.FontFamily = _textFontFamily;
            _activeTextBox.FontSize = _textFontSize;
            _activeTextBox.FontWeight = _isTextBold ? FontWeights.Bold : FontWeights.Normal;
            _activeTextBox.FontStyle = _isTextItalic ? FontStyles.Italic : FontStyles.Normal;
            _activeTextBox.TextDecorations = _isTextUnderline ? TextDecorations.Underline : null;
        }

        var selectedElements = inkCanvas.GetSelectedElements();
        if (selectedElements.Count > 0)
        {
            bool anyUpdated = false;
            foreach (var elem in selectedElements)
            {
                if (elem is Border border && border.Child is TextBlock tb)
                {
                    tb.FontFamily = _textFontFamily;
                    tb.FontSize = _textFontSize;
                    tb.FontWeight = _isTextBold ? FontWeights.Bold : FontWeights.Normal;
                    tb.FontStyle = _isTextItalic ? FontStyles.Italic : FontStyles.Normal;
                    tb.TextDecorations = _isTextUnderline ? TextDecorations.Underline : null;
                    anyUpdated = true;
                }
            }
            if (anyUpdated) SaveUndoState();
        }
    }

    private Border CreateStepBadgeElement(int number, Point position)
    {
        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(_currentColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        InkCanvas.SetLeft(badge, position.X - 14);
        InkCanvas.SetTop(badge, position.Y - 14);
        return badge;
    }
    #endregion

    public void SetTool(ToolType tool)
    {
        CommitActiveText();
        _currentTool = tool;
        if (tool == ToolType.Cursor)
        {
            SetInteractive(false);
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.Select(new StrokeCollection());
            return;
        }

        SetInteractive(true);
        if (tool == ToolType.Select)
        {
            inkCanvas.EditingMode = InkCanvasEditingMode.Select;
            return;
        }

        inkCanvas.Select(new StrokeCollection());
        UpdateDrawingAttributes();
    }

    public void SetColor(Color color)
    {
        _currentColor = color;
        UpdateDrawingAttributes();

        var selected = inkCanvas.GetSelectedStrokes();
        var selectedElements = inkCanvas.GetSelectedElements();
        if (selected.Count > 0 || selectedElements.Count > 0)
        {
            SaveUndoState();
            foreach (var stroke in selected)
            {
                var da = stroke.DrawingAttributes.Clone();
                da.Color = stroke.DrawingAttributes.IsHighlighter
                    ? Color.FromArgb(120, color.R, color.G, color.B)
                    : color;
                stroke.DrawingAttributes = da;
            }
            foreach (UIElement elem in selectedElements)
            {
                if (elem is Border border)
                {
                    if (border.Child is TextBlock tb)
                    {
                        tb.Foreground = new SolidColorBrush(color);
                    }
                    else
                    {
                        border.Background = new SolidColorBrush(color);
                    }
                }
            }
        }

        if (_activeTextBox != null)
        {
            _activeTextBox.Foreground = new SolidColorBrush(color);
            _activeTextBox.CaretBrush = new SolidColorBrush(color);
        }
    }

    public void SetSize(double size)
    {
        _currentSize = size;
        UpdateDrawingAttributes();

        var selected = inkCanvas.GetSelectedStrokes();
        var selectedElements = inkCanvas.GetSelectedElements();
        if (selected.Count > 0 || selectedElements.Count > 0)
        {
            SaveUndoState();
            foreach (var stroke in selected)
            {
                var da = stroke.DrawingAttributes.Clone();
                da.Width = size;
                da.Height = size;
                stroke.DrawingAttributes = da;
            }
            foreach (UIElement elem in selectedElements)
            {
                if (elem is Border border && border.Child is TextBlock tb)
                {
                    tb.FontSize = Math.Max(16, size * 3.0);
                }
            }
        }
    }

    private void UpdateDrawingAttributes()
    {
        var da = new DrawingAttributes
        {
            Color = _currentColor,
            Width = _currentSize,
            Height = _currentSize,
            FitToCurve = true,
            IsHighlighter = _currentTool == ToolType.Highlighter
        };

        if (_currentTool == ToolType.Highlighter)
        {
            da.Color = Color.FromArgb(120, _currentColor.R, _currentColor.G, _currentColor.B);
            da.Width = _currentSize * 2.5;
            da.Height = _currentSize * 2.5;
        }
        else if (_currentTool == ToolType.Laser)
        {
            da.Color = Color.FromArgb(230, 244, 63, 94);
            da.Width = Math.Max(4.0, _currentSize);
            da.Height = Math.Max(4.0, _currentSize);
        }

        inkCanvas.DefaultDrawingAttributes = da;

        if (_currentTool == ToolType.Eraser)
        {
            inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        }
        else if (_currentTool == ToolType.Select)
        {
            inkCanvas.EditingMode = InkCanvasEditingMode.Select;
        }
        else if (_currentTool == ToolType.Arrow || _currentTool == ToolType.DoubleArrow ||
                 _currentTool == ToolType.Line || _currentTool == ToolType.Rectangle ||
                 _currentTool == ToolType.Circle || _currentTool == ToolType.Text ||
                 _currentTool == ToolType.StepBadge)
        {
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
        }
        else
        {
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }
    }

    #region Shape & Interaction Drawing
    private void InkCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Guard against strokes or clicks starting over the toolbar area
        if (_exclusionZone != Rect.Empty)
        {
            var pos = e.GetPosition(this);
            var screenPt = new Point(Left + pos.X, Top + pos.Y);
            if (_exclusionZone.Contains(screenPt))
            {
                e.Handled = true;
                return;
            }
        }


        if (_currentTool == ToolType.Text)
        {
            Point pt = e.GetPosition(inkCanvas);
            ShowInlineTextInput(pt);
            e.Handled = true;
            return;
        }

        if (_currentTool == ToolType.StepBadge)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                _stepBadgeNumber = 1;
                e.Handled = true;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pt = e.GetPosition(inkCanvas);
                var badge = CreateStepBadgeElement(_stepBadgeNumber, pt);
                inkCanvas.Children.Add(badge);
                SaveUndoState();
                _stepBadgeNumber++;
                e.Handled = true;
                return;
            }
        }

        bool isShape = _currentTool == ToolType.Arrow || _currentTool == ToolType.DoubleArrow ||
                       _currentTool == ToolType.Line || _currentTool == ToolType.Rectangle ||
                       _currentTool == ToolType.Circle;

        if (!isShape) return;

        inkCanvas.Select(new StrokeCollection());
        _isDrawingShape = true;
        _shapeStartPoint = e.GetPosition(shapePreviewCanvas);
        _lastMovePoint = _shapeStartPoint;

        shapePreviewCanvas.Children.Clear();
        var brush = new SolidColorBrush(_currentColor);
        var fillBrush = IsShapeFilled
            ? new SolidColorBrush(Color.FromArgb(60, _currentColor.R, _currentColor.G, _currentColor.B))
            : Brushes.Transparent;

        if (_currentTool == ToolType.Rectangle)
        {
            _previewShape = new System.Windows.Shapes.Rectangle
            {
                Stroke = brush,
                StrokeThickness = _currentSize,
                Fill = fillBrush,
                RadiusX = 0,
                RadiusY = 0
            };
        }
        else if (_currentTool == ToolType.Circle)
        {
            _previewShape = new System.Windows.Shapes.Ellipse
            {
                Stroke = brush,
                StrokeThickness = _currentSize,
                Fill = fillBrush
            };
        }
        else if (_currentTool == ToolType.Arrow || _currentTool == ToolType.DoubleArrow || _currentTool == ToolType.Line)
        {
            _previewShape = new System.Windows.Shapes.Line
            {
                Stroke = brush,
                StrokeThickness = _currentSize,
                X1 = _shapeStartPoint.X,
                Y1 = _shapeStartPoint.Y,
                X2 = _shapeStartPoint.X,
                Y2 = _shapeStartPoint.Y
            };
        }

        if (_previewShape != null)
        {
            shapePreviewCanvas.Children.Add(_previewShape);
        }
        e.Handled = true;
    }

    private void InkCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {

        if (!_isDrawingShape || _previewShape == null) return;

        Point current = e.GetPosition(shapePreviewCanvas);

        if (Keyboard.IsKeyDown(Key.Space))
        {
            double deltaX = current.X - _lastMovePoint.X;
            double deltaY = current.Y - _lastMovePoint.Y;
            _shapeStartPoint.X += deltaX;
            _shapeStartPoint.Y += deltaY;
        }
        _lastMovePoint = current;

        if ((_currentTool == ToolType.Arrow || _currentTool == ToolType.DoubleArrow || _currentTool == ToolType.Line)
            && _previewShape is System.Windows.Shapes.Line line)
        {
            line.X1 = _shapeStartPoint.X;
            line.Y1 = _shapeStartPoint.Y;
            line.X2 = current.X;
            line.Y2 = current.Y;
        }
        else
        {
            double x = Math.Min(_shapeStartPoint.X, current.X);
            double y = Math.Min(_shapeStartPoint.Y, current.Y);
            double w = Math.Abs(current.X - _shapeStartPoint.X);
            double h = Math.Abs(current.Y - _shapeStartPoint.Y);

            System.Windows.Controls.Canvas.SetLeft(_previewShape, x);
            System.Windows.Controls.Canvas.SetTop(_previewShape, y);
            _previewShape.Width = Math.Max(1, w);
            _previewShape.Height = Math.Max(1, h);
        }
    }

    private void InkCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {

        if (!_isDrawingShape) return;
        _isDrawingShape = false;
        shapePreviewCanvas.Children.Clear();

        Point endPoint = e.GetPosition(shapePreviewCanvas);
        var da = inkCanvas.DefaultDrawingAttributes.Clone();

        if (IsShapeFilled && (_currentTool == ToolType.Rectangle || _currentTool == ToolType.Circle))
        {
            double x = Math.Min(_shapeStartPoint.X, endPoint.X);
            double y = Math.Min(_shapeStartPoint.Y, endPoint.Y);
            double w = Math.Abs(endPoint.X - _shapeStartPoint.X);
            double h = Math.Abs(endPoint.Y - _shapeStartPoint.Y);

            if (w > 3 && h > 3)
            {
                UIElement filledElement;
                if (_currentTool == ToolType.Rectangle)
                {
                    filledElement = new System.Windows.Shapes.Rectangle
                    {
                        Width = w,
                        Height = h,
                        Stroke = new SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        Fill = new SolidColorBrush(Color.FromArgb(60, _currentColor.R, _currentColor.G, _currentColor.B))
                    };
                }
                else
                {
                    filledElement = new System.Windows.Shapes.Ellipse
                    {
                        Width = w,
                        Height = h,
                        Stroke = new SolidColorBrush(_currentColor),
                        StrokeThickness = _currentSize,
                        Fill = new SolidColorBrush(Color.FromArgb(60, _currentColor.R, _currentColor.G, _currentColor.B))
                    };
                }
                InkCanvas.SetLeft(filledElement, x);
                InkCanvas.SetTop(filledElement, y);
                inkCanvas.Children.Add(filledElement);
                SaveUndoState();
                ShapeDrawn?.Invoke();
            }
        }
        else
        {
            Stroke? newStroke = null;
            if (_currentTool == ToolType.Arrow)
            {
                newStroke = ShapeHelper.CreateArrowStroke(_shapeStartPoint, endPoint, da);
            }
            else if (_currentTool == ToolType.DoubleArrow)
            {
                newStroke = ShapeHelper.CreateDoubleArrowStroke(_shapeStartPoint, endPoint, da);
            }
            else if (_currentTool == ToolType.Line)
            {
                newStroke = ShapeHelper.CreateLineStroke(_shapeStartPoint, endPoint, da);
            }
            else if (_currentTool == ToolType.Rectangle)
            {
                newStroke = ShapeHelper.CreateRectangleStroke(_shapeStartPoint, endPoint, da);
            }
            else if (_currentTool == ToolType.Circle)
            {
                newStroke = ShapeHelper.CreateEllipseStroke(_shapeStartPoint, endPoint, da);
            }

            if (newStroke != null)
            {
                inkCanvas.Strokes.Add(newStroke);
                SaveUndoState();
                ShapeDrawn?.Invoke();
            }
        }
        e.Handled = true;
    }

    public bool DeleteSelectedStrokes()
    {
        var selected = inkCanvas.GetSelectedStrokes();
        var selectedElements = inkCanvas.GetSelectedElements();

        if (selected.Count > 0 || selectedElements.Count > 0)
        {
            SaveUndoState();
            foreach (var s in selected)
            {
                inkCanvas.Strokes.Remove(s);
            }
            foreach (UIElement elem in selectedElements)
            {
                inkCanvas.Children.Remove(elem);
            }
            inkCanvas.Select(new StrokeCollection());
            return true;
        }
        return false;
    }
    #endregion

    #region Stroke Events & Laser Pointer
    private void InkCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        SaveUndoState();

        if (_currentTool == ToolType.Laser && IsVanishingLaserEnabled)
        {
            var stroke = e.Stroke;
            FadeAndRemoveLaserStroke(stroke);
        }
    }

    private void FadeAndRemoveLaserStroke(Stroke stroke)
    {
        var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        delayTimer.Tick += (s, args) =>
        {
            delayTimer.Stop();
            var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            int steps = 18; // ~630ms smooth fade
            int currentStep = 0;
            var originalColor = stroke.DrawingAttributes.Color;

            fadeTimer.Tick += (fs, fargs) =>
            {
                currentStep++;
                if (currentStep >= steps)
                {
                    fadeTimer.Stop();
                    if (inkCanvas.Strokes.Contains(stroke))
                    {
                        inkCanvas.Strokes.Remove(stroke);
                    }
                }
                else
                {
                    double ratio = 1.0 - ((double)currentStep / steps);
                    byte newAlpha = (byte)Math.Clamp(originalColor.A * ratio, 0, 255);
                    var da = stroke.DrawingAttributes.Clone();
                    da.Color = Color.FromArgb(newAlpha, originalColor.R, originalColor.G, originalColor.B);
                    stroke.DrawingAttributes = da;
                }
            };
            fadeTimer.Start();
        };
        delayTimer.Start();
    }
    #endregion

    #region Global Cursor Tracking (Spotlight, Magnifier, Cursor Halo & Ripple)
    private DispatcherTimer? _cursorTrackingTimer;
    private bool _wasLButtonDown = false;

    private void EnsureCursorTracking()
    {
        if (_cursorTrackingTimer != null) return;
        _cursorTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(20) // 50 FPS
        };
        _cursorTrackingTimer.Tick += CursorTrackingTimer_Tick;
        _cursorTrackingTimer.Start();
    }

    private void CheckCursorTracking()
    {
        if (!_isSpotlightActive && !_isMagnifierActive && !_isCursorHaloEnabled)
        {
            _cursorTrackingTimer?.Stop();
            _cursorTrackingTimer = null;
        }
    }

    private void CursorTrackingTimer_Tick(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double localX = (pt.X - Left * dpiX) / dpiX;
        double localY = (pt.Y - Top * dpiY) / dpiY;

        // 1. Spotlight Tracking
        if (_isSpotlightActive)
        {
            spotlightScreenRect.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            spotlightHole.Center = new Point(localX, localY);
            Canvas.SetLeft(spotlightRing, localX - 140);
            Canvas.SetTop(spotlightRing, localY - 140);
        }

        // 2. Magnifier / Live Zoom Loupe Tracking
        if (_isMagnifierActive)
        {
            var bmp = CaptureZoomRegion(pt.X, pt.Y, 120, 120);
            if (bmp != null)
            {
                magnifierImage.Source = bmp;
            }

            double loupeX = localX + 28;
            double loupeY = localY - 120;
            if (loupeX + 250 > ActualWidth) loupeX = localX - 268;
            if (loupeY < 10) loupeY = localY + 28;

            Canvas.SetLeft(magnifierBorder, Math.Clamp(loupeX, 10, Math.Max(10, ActualWidth - 260)));
            Canvas.SetTop(magnifierBorder, Math.Clamp(loupeY, 10, Math.Max(10, ActualHeight - 260)));
        }

        // 3. Cursor Halo & Click Ripple Tracking
        if (_isCursorHaloEnabled)
        {
            Canvas.SetLeft(cursorHalo, localX - 22);
            Canvas.SetTop(cursorHalo, localY - 22);

            bool isDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            if (isDown && !_wasLButtonDown)
            {
                TriggerClickRipple(localX, localY);
            }
            _wasLButtonDown = isDown;
        }
    }

    public void TriggerClickRipple(double x, double y)
    {
        cursorEffectsCanvas.Visibility = Visibility.Visible;
        clickRipple.Visibility = Visibility.Visible;
        Canvas.SetLeft(clickRipple, x - 10);
        Canvas.SetTop(clickRipple, y - 10);

        var scaleAnim = new DoubleAnimation(1.0, 3.5, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var opacityAnim = new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        opacityAnim.Completed += (s, e) =>
        {
            clickRipple.Visibility = Visibility.Collapsed;
        };

        rippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        rippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        clickRipple.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }

    private BitmapSource? CaptureZoomRegion(int screenX, int screenY, int width, int height)
    {
        IntPtr hdcScreen = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return null;

            hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
            hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
            hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

            NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, screenX - width / 2, screenY - height / 2, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            NativeMethods.SelectObject(hdcMem, hOld);
            hOld = IntPtr.Zero;

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
            {
                NativeMethods.SelectObject(hdcMem, hOld);
            }
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }
            if (hdcMem != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(hdcMem);
            }
            if (hdcScreen != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
    }
    #endregion

    #region Keystroke Visualizer HUD
    private DispatcherTimer? _keystrokeHideTimer;

    public void ShowKeystrokes(IReadOnlyList<string> keys)
    {
        if (keys == null || keys.Count == 0) return;

        keystrokeBadgesPanel.Children.Clear();

        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                var plusText = new TextBlock
                {
                    Text = "+",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // #94A3B8
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                keystrokeBadgesPanel.Children.Add(plusText);
            }

            var keyBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // #1E293B
                BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)), // #475569
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 0,
                    ShadowDepth = 3,
                    Direction = 270,
                    Color = Color.FromRgb(15, 23, 42),
                    Opacity = 0.6
                },
                Child = new TextBlock
                {
                    Text = keys[i],
                    FontSize = 14,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)), // #F8FAFC
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            keystrokeBadgesPanel.Children.Add(keyBadge);
        }

        keystrokeHud.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        keystrokeHud.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _keystrokeHideTimer?.Stop();
        _keystrokeHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _keystrokeHideTimer.Tick += (s, e) =>
        {
            _keystrokeHideTimer.Stop();
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (fs, fe) => keystrokeHud.Visibility = Visibility.Collapsed;
            keystrokeHud.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        _keystrokeHideTimer.Start();
    }
    #endregion

    #region Undo / Redo / Clear / Snapshot / Visibility
    private void SaveUndoState()
    {
        if (_isPerformingHistoryAction) return;
        _undoStack.Push(new StrokeCollection(inkCanvas.Strokes.Select(s => s.Clone())));
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count > 1)
        {
            _isPerformingHistoryAction = true;
            var current = _undoStack.Pop();
            _redoStack.Push(current);

            var previous = _undoStack.Peek();
            inkCanvas.Strokes.Clear();
            inkCanvas.Strokes.Add(new StrokeCollection(previous.Select(s => s.Clone())));
            _isPerformingHistoryAction = false;
        }
    }

    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            _isPerformingHistoryAction = true;
            var next = _redoStack.Pop();
            _undoStack.Push(next);

            inkCanvas.Strokes.Clear();
            inkCanvas.Strokes.Add(new StrokeCollection(next.Select(s => s.Clone())));
            _isPerformingHistoryAction = false;
        }
    }

    public void ClearCanvas()
    {
        if (inkCanvas.Strokes.Count > 0 || inkCanvas.Children.Count > 0)
        {
            SaveUndoState();
            inkCanvas.Strokes.Clear();
            inkCanvas.Children.Clear();
            _stepBadgeNumber = 1;

            if (_currentBoardMode != BoardMode.Transparent && _currentPageIndex >= 0 && _currentPageIndex < _boardPages.Count)
            {
                _boardPages[_currentPageIndex].Strokes.Clear();
                _boardPages[_currentPageIndex].Children.Clear();
            }
        }
    }

    public bool ToggleInkVisibility()
    {
        if (inkCanvas.Visibility == Visibility.Visible)
        {
            inkCanvas.Visibility = Visibility.Collapsed;
            return false;
        }
        else
        {
            inkCanvas.Visibility = Visibility.Visible;
            return true;
        }
    }

    public bool TakeSnapshot()
    {
        try
        {
            int w = (int)inkCanvas.ActualWidth;
            int h = (int)inkCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return false;

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(inkCanvas);

            Clipboard.SetImage(rtb);
            return true;
        }
        catch
        {
            return false;
        }
    }
    #endregion
}
