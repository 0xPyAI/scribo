using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Scribo;

public partial class MainWindow : Window
{
    private const int HOTKEY_ID_PRIMARY = 9001;
    private const int HOTKEY_ID_F8 = 9002;

    private OverlayWindow? _overlayWindow;
    private TrayHelper? _trayHelper;
    private AppSettings _settings = AppSettings.Load();
    private string _currentHotkeyName = "Alt + S";
    private string _currentHoldKey = "Alt";
    private ToolType? _previousToolBeforeHold = null;
    private bool _isHoldingKey = false;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardHookProc;
    private bool _isVertical = false;
    private bool _isInitialized = false;
    private int _activateMsg = 0;
    private bool _isKeystrokeVisualizerEnabled = false;
    private bool _isCursorHaloEnabled = false;
    private bool _isVanishingLaserEnabled = true;

    private static void Log(string msg)
    {
        Logger.Log(msg);
    }

    public MainWindow()
    {
        Log("MainWindow constructor starting");
        _currentHotkeyName = _settings.RecallHotkey;
        _currentHoldKey = _settings.HoldKey;
        _isVertical = _settings.IsVerticalOrientation;
        _isKeystrokeVisualizerEnabled = _settings.KeystrokesEnabled;
        _isCursorHaloEnabled = _settings.CursorHaloEnabled;
        _isVanishingLaserEnabled = _settings.VanishingLaserEnabled;

        InitializeComponent();
        _isInitialized = true;
        Log("MainWindow InitializeComponent complete");

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += (s, e) => Log("MainWindow Closed event fired!");
        KeyDown += MainWindow_KeyDown;
        LocationChanged += MainWindow_LocationChanged;
        SizeChanged += MainWindow_SizeChanged;
        Log("MainWindow constructor finished");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log("MainWindow_Loaded starting");
        // Position toolbar at top center of primary monitor
        Left = Math.Max(20, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2.0);
        Top = 24;
        Log($"MainWindow positioned at ({Left}, {Top}) ActualSize=({ActualWidth}x{ActualHeight})");

        // Create and display the fullscreen drawing overlay
        _overlayWindow = new OverlayWindow();
        _overlayWindow.Show();
        Log("OverlayWindow shown");

        // Make MainWindow owned by OverlayWindow at the Win32 level so it stays permanently on top in Z-order
        var mainHwnd = new WindowInteropHelper(this).Handle;
        var overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
        if (mainHwnd != IntPtr.Zero && overlayHwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(mainHwnd, NativeMethods.GWL_HWNDPARENT, overlayHwnd);
        }

        // Default: start with no feature selected (Cursor / pass-through mode)
        _overlayWindow.IsCursorHaloEnabled = _isCursorHaloEnabled;
        _overlayWindow.IsVanishingLaserEnabled = _isVanishingLaserEnabled;
        SwitchToCursorMode();
        UpdateOverlayExclusionZone();
        Log("Default tool set to Cursor (no feature selected)");

        // Initialize popup close handlers
        colorStylePopup.Closed += (s, e) => btnColorStyle.IsChecked = false;
        shapesPopup.Closed += ShapesPopup_Closed;
        thicknessPopup.Closed += (s, e) => btnThickness.IsChecked = false;

        // Automatically switch back to cursor mode once any shape is drawn
        _overlayWindow.ShapeDrawn += () =>
        {
            SwitchToCursorMode();
        };

        _overlayWindow.BoardPageChanged += (current, total) =>
        {
            txtBoardPage.Text = $"{current}/{total}";
        };

        _overlayWindow.PenColorAutoChanged += (color) =>
        {
            OnPenColorAutoChanged(color);
        };

        _overlayWindow.TextFormattingChanged += (family, size, bold, italic, underline) =>
        {
            OnTextFormattingChangedFromOverlay(family, size, bold, italic, underline);
        };

        // Auto-dismiss popups when mouse interacts with drawing canvas so drawing is immediate
        _overlayWindow.PreviewMouseDown += (s, e) =>
        {
            if (shapesPopup.IsOpen) shapesPopup.IsOpen = false;
            if (colorStylePopup.IsOpen) colorStylePopup.IsOpen = false;
            if (thicknessPopup.IsOpen) thicknessPopup.IsOpen = false;
            if (screenshotPopup.IsOpen) screenshotPopup.IsOpen = false;
            if (boardPopup.IsOpen) boardPopup.IsOpen = false;
            if (textPopup != null && textPopup.IsOpen) textPopup.IsOpen = false;
        };

        // Auto-dismiss popups when clicking elsewhere on toolbar or deactivating
        this.PreviewMouseDown += (s, e) =>
        {
            if (shapesPopup.IsOpen && !shapesContainer.IsMouseOver)
            {
                shapesPopup.IsOpen = false;
            }
            if (boardPopup.IsOpen && !boardContainer.IsMouseOver && !boardPopupBorder.IsMouseOver)
            {
                boardPopup.IsOpen = false;
            }
            if (textPopup != null && textPopup.IsOpen && !textContainer.IsMouseOver && !textPopupBorder.IsMouseOver)
            {
                textPopup.IsOpen = false;
            }
        };

        this.Deactivated += (s, e) =>
        {
            if (shapesPopup.IsOpen) shapesPopup.IsOpen = false;
            if (boardPopup.IsOpen) boardPopup.IsOpen = false;
            if (textPopup != null && textPopup.IsOpen && !textPopup.IsMouseOver && !textPopup.IsKeyboardFocusWithin)
            {
                textPopup.IsOpen = false;
            }
        };

        UpdateTextPreview();

        ApplyOrientation();
        InstallKeyboardHook();

        if (_overlayWindow != null)
        {
            _overlayWindow.KeyDown += (s, e) => MainWindow_KeyDown(s, e);
        }

        UpdateTooltipsWithHotkeys();

        Log("MainWindow_Loaded complete");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(HwndHook);

        _activateMsg = NativeMethods.RegisterWindowMessage(App.UNIQUE_ACTIVATE_MSG);

        // Initialize Win32 System Tray
        _trayHelper = new TrayHelper(hwnd, $"Scribo ({_currentHotkeyName} to show)");

        RegisterCurrentHotkey();
    }

    private void RegisterCurrentHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID_PRIMARY);
        NativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID_F8);

        // Always allow F8 as secondary backup
        NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID_F8, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F8);

        switch (_currentHotkeyName)
        {
            case "Alt + S":
                NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID_PRIMARY, NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_S);
                break;
            case "Ctrl + Shift + S":
                NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID_PRIMARY, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_S);
                break;
            case "Ctrl + Alt + D":
                NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID_PRIMARY, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, 0x44 /* VK_D */);
                break;
            case "F8":
                // F8 is already registered
                break;
        }

        // Update minimize button tooltip and tray tooltip with current hotkey
        btnMinimize.ToolTip = $"Minimize to System Tray ({_currentHotkeyName})";
        _trayHelper?.UpdateTip($"Scribo ({_currentHotkeyName} to show)");
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        Log("MainWindow_Closing fired! Stack: " + Environment.StackTrace);
        UninstallKeyboardHook();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID_PRIMARY);
        NativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID_F8);

        _trayHelper?.Dispose();
        _overlayWindow?.Close();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_activateMsg != 0 && msg == _activateMsg)
        {
            Log("Received instance activate message — restoring window to front");
            RestoreWindow();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_PRIMARY || id == HOTKEY_ID_F8)
            {
                // If minimized or hidden, restore immediately
                if (Visibility != Visibility.Visible || WindowState == WindowState.Minimized)
                {
                    RestoreWindow();
                }
                handled = true;
            }
        }
        else if (msg == TrayHelper.WM_TRAYCALLBACK)
        {
            int eventType = lParam.ToInt32();
            if (eventType == TrayHelper.WM_LBUTTONUP)
            {
                RestoreWindow();
                handled = true;
            }
            else if (eventType == TrayHelper.WM_RBUTTONUP)
            {
                ShowTrayContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowTrayContextMenu()
    {
        var menu = new ContextMenu();

        var itemRestore = new MenuItem { Header = "Show Scribo", FontWeight = FontWeights.Bold };
        itemRestore.Click += (s, e) => RestoreWindow();
        menu.Items.Add(itemRestore);

        var itemWhiteboard = new MenuItem { Header = "Whiteboard" };
        itemWhiteboard.Click += (s, e) =>
        {
            RestoreWindow();
            SetBoardMode(OverlayWindow.BoardMode.Whiteboard);
        };
        menu.Items.Add(itemWhiteboard);

        var itemClear = new MenuItem { Header = "Clear Screen" };
        itemClear.Click += (s, e) => _overlayWindow?.ClearCanvas();
        menu.Items.Add(itemClear);

        menu.Items.Add(new Separator());

        var itemSettings = new MenuItem { Header = "Settings..." };
        itemSettings.Click += (s, e) => OpenSettings();
        menu.Items.Add(itemSettings);

        menu.Items.Add(new Separator());

        var itemExit = new MenuItem { Header = "Exit" };
        itemExit.Click += (s, e) => Application.Current.Shutdown();
        menu.Items.Add(itemExit);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    public void RestoreWindow()
    {
        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            Topmost = true;
            Activate();

            _overlayWindow?.Show();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (_overlayWindow != null)
            {
                var overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
                if (hwnd != IntPtr.Zero && overlayHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT, overlayHwnd);
                }
            }
            NativeMethods.SetForegroundWindow(hwnd);
            UpdateOverlayExclusionZone();
        });
    }

    #region Toolbar Orientation
    public void SetOrientation(bool vertical)
    {
        if (_isVertical == vertical) return;
        _isVertical = vertical;
        ApplyOrientation();
    }

    private void ApplyOrientation()
    {
        var orientation = _isVertical ? Orientation.Vertical : Orientation.Horizontal;

        toolbarPanel.Orientation = orientation;
        toolbarPanel.HorizontalAlignment = HorizontalAlignment.Center;
        toolsPanel.Orientation = orientation;
        toolsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        actionsPanel.Orientation = orientation;
        actionsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        windowPanel.Orientation = orientation;
        windowPanel.HorizontalAlignment = HorizontalAlignment.Center;

        colorStyleContainer.HorizontalAlignment = HorizontalAlignment.Center;
        thicknessContainer.HorizontalAlignment = HorizontalAlignment.Center;
        shapesContainer.HorizontalAlignment = HorizontalAlignment.Center;
        if (shapesButtonPanel != null) shapesButtonPanel.Orientation = orientation;
        textContainer.HorizontalAlignment = HorizontalAlignment.Center;
        if (textButtonPanel != null) textButtonPanel.Orientation = orientation;
        screenshotContainer.HorizontalAlignment = HorizontalAlignment.Center;

        // Update separators between horizontal/vertical styles
        var sepStyle = _isVertical
            ? (Style)FindResource("VSeparatorStyle")
            : (Style)FindResource("HSeparatorStyle");

        sep0.Style = sepStyle;
        sep1.Style = sepStyle;
        sep3.Style = sepStyle;
        sep4.Style = sepStyle;

        // In vertical mode, collapse sep2 so Color and Thickness are grouped together without redundant gaps
        sep2.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
        sep2.Style = sepStyle;

        if (_isVertical)
        {
            // Compact padding for sleek vertical dock (removes horizontal and vertical white space gutters)
            toolbarBorder.Padding = new Thickness(4, 5, 4, 5);

            // Turn drag handle into compact horizontal grip at the top of the vertical toolbar
            dragHandleBorder.Margin = new Thickness(0, 1, 0, 3);
            dragHandleBorder.Padding = new Thickness(6, 3.5, 6, 3.5);
            dragDotsCol1.Orientation = Orientation.Horizontal;
            dragDotsCol1.Margin = new Thickness(0, 0, 0, 2);
            dragDotsCol2.Orientation = Orientation.Horizontal;
            dragHandleStack.Orientation = Orientation.Vertical;

            // Hide dropdown chevrons in vertical dock so all buttons are uniform, compact 32x32 squares
            if (btnShapesDropdown != null) btnShapesDropdown.Visibility = Visibility.Collapsed;
            if (btnBoardDropdown != null) btnBoardDropdown.Visibility = Visibility.Collapsed;
            if (btnTextDropdown != null) btnTextDropdown.Visibility = Visibility.Collapsed;
            if (chevronColor != null) chevronColor.Visibility = Visibility.Collapsed;
            if (chevronThickness != null) chevronThickness.Visibility = Visibility.Collapsed;
            if (chevronScreenshot != null) chevronScreenshot.Visibility = Visibility.Collapsed;

            btnColorStyle.Padding = new Thickness(6, 6, 6, 6);
            btnThickness.Padding = new Thickness(6, 6, 6, 6);
            btnScreenshot.Padding = new Thickness(7, 7, 7, 7);

            // Compact vertical margins: 3px between buttons (1.5px top + 1.5px bottom)
            var vBtnMargin = new Thickness(0, 1.5, 0, 1.5);
            foreach (UIElement child in toolsPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = vBtnMargin;
            }
            foreach (UIElement child in actionsPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = vBtnMargin;
            }
            foreach (UIElement child in windowPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = vBtnMargin;
            }
            colorStyleContainer.Margin = vBtnMargin;
            thicknessContainer.Margin = vBtnMargin;

            // Popups open to the right of the vertical toolbar
            shapesPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            shapesPopup.HorizontalOffset = 8;
            shapesPopup.VerticalOffset = 0;

            colorStylePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            colorStylePopup.HorizontalOffset = 8;
            colorStylePopup.VerticalOffset = 0;

            thicknessPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            thicknessPopup.HorizontalOffset = 8;
            thicknessPopup.VerticalOffset = 0;

            screenshotPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            screenshotPopup.HorizontalOffset = 8;
            screenshotPopup.VerticalOffset = 0;

            boardPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            boardPopup.HorizontalOffset = 8;
            boardPopup.VerticalOffset = 0;

            textPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            textPopup.HorizontalOffset = 8;
            textPopup.VerticalOffset = 0;
        }
        else
        {
            // Standard horizontal toolbar padding
            toolbarBorder.Padding = new Thickness(8, 5, 8, 5);

            // Restore vertical grip handle on the left
            dragHandleBorder.Margin = new Thickness(1, 0, 3, 0);
            dragHandleBorder.Padding = new Thickness(5, 6, 5, 6);
            dragDotsCol1.Orientation = Orientation.Vertical;
            dragDotsCol1.Margin = new Thickness(0, 0, 2.5, 0);
            dragDotsCol2.Orientation = Orientation.Vertical;
            dragHandleStack.Orientation = Orientation.Horizontal;

            // Restore chevrons in horizontal toolbar
            if (btnShapesDropdown != null) btnShapesDropdown.Visibility = Visibility.Visible;
            if (btnBoardDropdown != null) btnBoardDropdown.Visibility = Visibility.Visible;
            if (btnTextDropdown != null) btnTextDropdown.Visibility = Visibility.Visible;
            if (chevronColor != null) chevronColor.Visibility = Visibility.Visible;
            if (chevronThickness != null) chevronThickness.Visibility = Visibility.Visible;
            if (chevronScreenshot != null) chevronScreenshot.Visibility = Visibility.Visible;

            btnColorStyle.Padding = new Thickness(7, 5, 7, 5);
            btnThickness.Padding = new Thickness(7, 5, 7, 5);
            btnScreenshot.Padding = new Thickness(6, 6, 6, 6);

            // Standard horizontal margins: 4px between buttons (2px left + 2px right)
            var hBtnMargin = new Thickness(2, 0, 2, 0);
            foreach (UIElement child in toolsPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = hBtnMargin;
            }
            foreach (UIElement child in actionsPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = hBtnMargin;
            }
            foreach (UIElement child in windowPanel.Children)
            {
                if (child is FrameworkElement fe) fe.Margin = hBtnMargin;
            }
            colorStyleContainer.Margin = hBtnMargin;
            thicknessContainer.Margin = hBtnMargin;

            // Popups open below the horizontal toolbar
            shapesPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            shapesPopup.VerticalOffset = 8;
            shapesPopup.HorizontalOffset = 0;

            colorStylePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            colorStylePopup.VerticalOffset = 8;
            colorStylePopup.HorizontalOffset = 0;

            thicknessPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            thicknessPopup.VerticalOffset = 8;
            thicknessPopup.HorizontalOffset = 0;

            screenshotPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            screenshotPopup.VerticalOffset = 8;
            screenshotPopup.HorizontalOffset = 0;

            boardPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            boardPopup.VerticalOffset = 8;
            boardPopup.HorizontalOffset = 0;

            textPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            textPopup.VerticalOffset = 8;
            textPopup.HorizontalOffset = 0;
        }

        // Keep toolbar at its current user-dragged position, only clamp to prevent screen overflow
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            UpdateLayout();
            double maxLeft = Math.Max(10, SystemParameters.PrimaryScreenWidth - ActualWidth - 10);
            double maxTop = Math.Max(10, SystemParameters.PrimaryScreenHeight - ActualHeight - 10);
            Left = Math.Clamp(Left, 10, maxLeft);
            Top = Math.Clamp(Top, 10, maxTop);
            UpdateOverlayExclusionZone();
        });
    }
    #endregion

    #region Overlay Exclusion Zone
    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        UpdateOverlayExclusionZone();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverlayExclusionZone();
    }

    private void UpdateOverlayExclusionZone()
    {
        if (_overlayWindow == null) return;

        // Pass the toolbar's screen-space bounding rect to the overlay so it won't draw there
        var toolbarRect = new Rect(Left, Top, ActualWidth, ActualHeight);
        _overlayWindow.SetExclusionZone(toolbarRect);
    }
    #endregion

    #region Window Dragging
    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Safely ignored if button state changed before OS processed
            }
        }
    }
    #endregion

    #region Tool & Palette Selection
    private ToolType _currentShapeTool = ToolType.Rectangle;
    private void Tool_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            // Re-clicking active tool toggles it off and returns to cursor mode
            rb.IsChecked = false;
            e.Handled = true;
            SwitchToCursorMode();
        }
    }

    public void SwitchToCursorMode()
    {
        if (toolSelect != null) toolSelect.IsChecked = false;
        toolPen.IsChecked = false;
        toolHighlighter.IsChecked = false;
        toolLaser.IsChecked = false;
        if (toolText != null) toolText.IsChecked = false;
        if (toolBadge != null) toolBadge.IsChecked = false;
        toolShapes.IsChecked = false;
        toolEraser.IsChecked = false;
        shapesPopup.IsOpen = false;

        _overlayWindow?.SetTool(ToolType.Cursor);
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || _overlayWindow == null) return;

        shapesPopup.IsOpen = false;

        if (rb == toolSelect) _overlayWindow.SetTool(ToolType.Select);
        else if (rb == toolPen) _overlayWindow.SetTool(ToolType.Pen);
        else if (rb == toolHighlighter) _overlayWindow.SetTool(ToolType.Highlighter);
        else if (rb == toolLaser) _overlayWindow.SetTool(ToolType.Laser);
        else if (rb == toolText) _overlayWindow.SetTool(ToolType.Text);
        else if (rb == toolBadge) _overlayWindow.SetTool(ToolType.StepBadge);
        else if (rb == toolShapes) _overlayWindow.SetTool(_currentShapeTool);
        else if (rb == toolEraser) _overlayWindow.SetTool(ToolType.Eraser);
    }

    private void BtnShapesDropdown_Click(object sender, RoutedEventArgs e)
    {
        shapesPopup.IsOpen = !shapesPopup.IsOpen;
    }

    private void ToolShapes_RightClick(object sender, MouseButtonEventArgs e)
    {
        toolShapes.IsChecked = true;
        _overlayWindow?.SetTool(_currentShapeTool);
        shapesPopup.IsOpen = true;
        e.Handled = true;
    }

    private void ShapesPopup_Closed(object? sender, EventArgs e)
    {
        if (toolShapes.IsChecked == true && _overlayWindow != null)
        {
            _overlayWindow.SetTool(_currentShapeTool);
        }
    }

    private void BtnShapeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement elem || elem.Tag is not string shapeName) return;

        switch (shapeName)
        {
            case "Rectangle":
                SelectShape(ToolType.Rectangle);
                break;
            case "Circle":
                SelectShape(ToolType.Circle);
                break;
            case "Arrow":
                SelectShape(ToolType.Arrow);
                break;
            case "DoubleArrow":
                SelectShape(ToolType.DoubleArrow);
                break;
            case "Line":
                SelectShape(ToolType.Line);
                break;
        }

        shapesPopup.IsOpen = false;
    }

    private void SelectShape(ToolType shape)
    {
        _currentShapeTool = shape;
        toolShapes.IsChecked = true;
        UpdateShapeIcon(shape);
        _overlayWindow?.SetTool(shape);

        if (btnShapeRect != null) btnShapeRect.IsChecked = (shape == ToolType.Rectangle);
        if (btnShapeCircle != null) btnShapeCircle.IsChecked = (shape == ToolType.Circle);
        if (btnShapeArrow != null) btnShapeArrow.IsChecked = (shape == ToolType.Arrow);
        if (btnShapeDoubleArrow != null) btnShapeDoubleArrow.IsChecked = (shape == ToolType.DoubleArrow);
        if (btnShapeLine != null) btnShapeLine.IsChecked = (shape == ToolType.Line);
    }

    private void UpdateShapeIcon(ToolType shape)
    {
        var fgBinding = new Binding("Foreground") { Source = toolShapes };

        if (shape == ToolType.Rectangle)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = 14,
                Fill = Brushes.Transparent,
                StrokeThickness = 2,
                SnapsToDevicePixels = true
            };
            rect.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fgBinding);
            shapeIconPresenter.Content = rect;
        }
        else if (shape == ToolType.Circle)
        {
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = Brushes.Transparent,
                StrokeThickness = 2,
                SnapsToDevicePixels = true
            };
            ellipse.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fgBinding);
            shapeIconPresenter.Content = ellipse;
        }
        else if (shape == ToolType.Arrow)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3.5 14.5L14.5 3.5M7.5 5.5L14.5 3.5L12.5 10.5"),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fgBinding);
            shapeIconPresenter.Content = path;
        }
        else if (shape == ToolType.DoubleArrow)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M2 12L12 2M6 2L2 2L2 6M8 12L12 12L12 8"),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fgBinding);
            shapeIconPresenter.Content = path;
        }
        else if (shape == ToolType.Line)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = 2,
                Y1 = 12,
                X2 = 12,
                Y2 = 2,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 14,
                Height = 14,
                SnapsToDevicePixels = true
            };
            line.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, fgBinding);
            shapeIconPresenter.Content = line;
        }

        // Keep selection state in mini-bar flyout synced
        if (btnShapeRect != null) btnShapeRect.IsChecked = shape == ToolType.Rectangle;
        if (btnShapeCircle != null) btnShapeCircle.IsChecked = shape == ToolType.Circle;
        if (btnShapeArrow != null) btnShapeArrow.IsChecked = shape == ToolType.Arrow;
        if (btnShapeDoubleArrow != null) btnShapeDoubleArrow.IsChecked = shape == ToolType.DoubleArrow;
        if (btnShapeLine != null) btnShapeLine.IsChecked = shape == ToolType.Line;
    }

    #region Text Formatting & Typography
    private void BtnTextDropdown_Click(object sender, RoutedEventArgs e)
    {
        ToggleTextPopup();
    }

    private void ToolText_RightClick(object sender, MouseButtonEventArgs e)
    {
        toolText.IsChecked = true;
        _overlayWindow?.SetTool(ToolType.Text);
        ToggleTextPopup();
        e.Handled = true;
    }

    private void ToggleTextPopup()
    {
        bool wasOpen = textPopup.IsOpen;
        CloseAllPopupsImmediately();
        textPopup.IsOpen = !wasOpen;
        if (textPopup.IsOpen)
        {
            UpdateTextPreview();
        }
    }

    private void ComboFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTextPreview();
    }

    private void ComboFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTextPreview();
    }

    private void BtnFontDec_Click(object sender, RoutedEventArgs e)
    {
        if (comboFontSize == null) return;
        int curIdx = comboFontSize.SelectedIndex;
        if (curIdx > 0)
        {
            comboFontSize.SelectedIndex = curIdx - 1;
        }
    }

    private void BtnFontInc_Click(object sender, RoutedEventArgs e)
    {
        if (comboFontSize == null) return;
        int curIdx = comboFontSize.SelectedIndex;
        if (curIdx < comboFontSize.Items.Count - 1)
        {
            comboFontSize.SelectedIndex = curIdx + 1;
        }
    }

    private void TextStyleToggle_Click(object sender, RoutedEventArgs e)
    {
        UpdateTextPreview();
    }

    private void UpdateTextPreview()
    {
        if (txtFontPreview == null) return;
        var family = GetSelectedFontFamily();
        double size = GetSelectedFontSize();
        bool isBold = btnTextBold?.IsChecked == true;
        bool isItalic = btnTextItalic?.IsChecked == true;
        bool isUnderline = btnTextUnderline?.IsChecked == true;

        txtFontPreview.FontFamily = family;
        txtFontPreview.FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
        txtFontPreview.FontStyle = isItalic ? FontStyles.Italic : FontStyles.Normal;
        txtFontPreview.TextDecorations = isUnderline ? TextDecorations.Underline : null;

        var activeBrush = activeColorBadge?.Fill ?? Brushes.Black;
        txtFontPreview.Foreground = activeBrush;

        _overlayWindow?.SetTextFormatting(family, size, isBold, isItalic, isUnderline);
    }

    private FontFamily GetSelectedFontFamily()
    {
        if (comboFontFamily?.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string fontName)
                return new FontFamily(fontName);
            if (item.Content is string content)
                return new FontFamily(content);
        }
        return new FontFamily("Segoe UI");
    }

    private double GetSelectedFontSize()
    {
        if (comboFontSize?.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (double.TryParse(tagStr, out double size))
                return size;
        }
        return 24.0;
    }

    private void OnTextFormattingChangedFromOverlay(FontFamily family, double size, bool isBold, bool isItalic, bool isUnderline)
    {
        if (btnTextBold != null) btnTextBold.IsChecked = isBold;
        if (btnTextItalic != null) btnTextItalic.IsChecked = isItalic;
        if (btnTextUnderline != null) btnTextUnderline.IsChecked = isUnderline;

        // Sync font family selection if present in list
        if (comboFontFamily != null)
        {
            string famName = family.Source;
            foreach (ComboBoxItem item in comboFontFamily.Items)
            {
                if (item.Tag is string t && string.Equals(t, famName, StringComparison.OrdinalIgnoreCase))
                {
                    comboFontFamily.SelectedItem = item;
                    break;
                }
            }
        }

        // Sync font size selection if present
        if (comboFontSize != null)
        {
            int roundedSize = (int)Math.Round(size);
            foreach (ComboBoxItem item in comboFontSize.Items)
            {
                if (item.Tag is string t && int.TryParse(t, out int s) && s == roundedSize)
                {
                    comboFontSize.SelectedItem = item;
                    break;
                }
            }
        }

        UpdateTextPreview();
    }
    #endregion

    private void Color_Select(object sender)
    {
        if (sender is not RadioButton rb || _overlayWindow == null) return;

        Color color;
        if (rb.Tag is string hex)
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        else if (rb.Background is SolidColorBrush scb)
        {
            color = scb.Color;
        }
        else
        {
            return;
        }

        rb.IsChecked = true;
        _overlayWindow.SetColor(color);
        activeColorBadge.Fill = new SolidColorBrush(color);
        UpdateTextPreview();

        // If currently in cursor or eraser mode, switch to pen so user can draw immediately
        bool isDrawingActive = (toolSelect != null && toolSelect.IsChecked == true) ||
                               toolPen.IsChecked == true ||
                               toolHighlighter.IsChecked == true ||
                               toolLaser.IsChecked == true ||
                               (toolText != null && toolText.IsChecked == true) ||
                               (toolBadge != null && toolBadge.IsChecked == true) ||
                               toolShapes.IsChecked == true;

        if (!isDrawingActive || toolEraser.IsChecked == true)
        {
            toolEraser.IsChecked = false;
            toolPen.IsChecked = true;
            _overlayWindow.SetTool(ToolType.Pen);
        }

        colorStylePopup.IsOpen = false;
        btnColorStyle.IsChecked = false;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        Color_Select(sender);
    }

    private void Color_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Color_Select(sender);
        e.Handled = true;
    }

    private void BtnColorStyle_Click(object sender, RoutedEventArgs e)
    {
        colorStylePopup.IsOpen = btnColorStyle.IsChecked == true;
    }

    private void ColorStylePopup_Closed(object? sender, EventArgs e)
    {
        btnColorStyle.IsChecked = false;
    }
    #endregion

    #region Stroke Thickness Popover & Slider
    private bool _isUpdatingSizeSlider = false;

    private void BtnThickness_Click(object sender, RoutedEventArgs e)
    {
        thicknessPopup.IsOpen = btnThickness.IsChecked == true;
    }

    private void UpdateThicknessPreview(double size)
    {
        if (activeThicknessDot != null)
        {
            double d = Math.Clamp(size * 0.8, 3.0, 13.0);
            activeThicknessDot.Width = d;
            activeThicknessDot.Height = d;
        }
        if (btnThickness != null)
        {
            btnThickness.ToolTip = $"Stroke Thickness ({(int)size}px)";
        }
    }

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized || _isUpdatingSizeSlider) return;
        if (thicknessSlider == null || txtThickness == null) return;

        double size = Math.Round(thicknessSlider.Value);
        txtThickness.Text = $"{(int)size}px";
        _overlayWindow?.SetSize(size);
        UpdateThicknessPreview(size);

        // Update presets if size matches exactly
        _isUpdatingSizeSlider = true;
        if (sizeFine != null) sizeFine.IsChecked = Math.Abs(size - 2) < 0.5;
        if (sizeMedium != null) sizeMedium.IsChecked = Math.Abs(size - 5) < 0.5;
        if (sizeBold != null) sizeBold.IsChecked = Math.Abs(size - 12) < 0.5;
        _isUpdatingSizeSlider = false;
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        string? sizeStr = null;
        if (sender is RadioButton rb && rb.Tag is string s1) sizeStr = s1;
        else if (sender is Button btn && btn.Tag is string s2) sizeStr = s2;

        if (sizeStr != null && double.TryParse(sizeStr, out double s))
        {
            _isUpdatingSizeSlider = true;
            thicknessSlider.Value = s;
            txtThickness.Text = $"{(int)s}px";
            UpdateThicknessPreview(s);
            _isUpdatingSizeSlider = false;

            _overlayWindow?.SetSize(s);
        }
    }
    #endregion



    #region Actions
    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.Undo();
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.Redo();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.ClearCanvas();
    }

    private void BtnToggleEye_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow != null)
        {
            bool isVisible = _overlayWindow.ToggleInkVisibility();
            eyeIcon.Opacity = isVisible ? 1.0 : 0.4;
        }
    }

    private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
    {
        screenshotPopup.IsOpen = true;
    }

    private void CloseAllPopupsImmediately()
    {
        HidePopupHwnd(screenshotPopup);
        HidePopupHwnd(shapesPopup);
        HidePopupHwnd(colorStylePopup);
        HidePopupHwnd(thicknessPopup);
        HidePopupHwnd(boardPopup);
        HidePopupHwnd(textPopup);

        screenshotPopup.IsOpen = false;
        shapesPopup.IsOpen = false;
        colorStylePopup.IsOpen = false;
        thicknessPopup.IsOpen = false;
        boardPopup.IsOpen = false;
        textPopup.IsOpen = false;
    }

    private static void HidePopupHwnd(System.Windows.Controls.Primitives.Popup popup)
    {
        try
        {
            if (popup.Child != null)
            {
                var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                if (source != null && source.Handle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(source.Handle, NativeMethods.SW_HIDE);
                }
            }
        }
        catch { }
    }

    private void BtnCopyClipboard_Click(object sender, RoutedEventArgs e)
    {
        CloseAllPopupsImmediately();
        var bmp = ScreenCaptureHelper.CaptureScreen(this);
        if (bmp != null)
        {
            ScreenCaptureHelper.CopyToClipboard(bmp);
            _trayHelper?.ShowBalloon("Screenshot Copied", "Screenshot with your drawings has been copied to the clipboard.");
        }
    }

    private void BtnBoard_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null) return;
        var targetMode = _overlayWindow.CurrentBoardMode == OverlayWindow.BoardMode.Transparent
            ? _overlayWindow.LastBoardStyle
            : OverlayWindow.BoardMode.Transparent;

        SetBoardMode(targetMode);
    }

    private void BtnBoard_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleBoardPopup();
    }

    private void BtnBoardDropdown_Click(object sender, RoutedEventArgs e)
    {
        ToggleBoardPopup();
    }

    private void ToggleBoardPopup()
    {
        bool wasOpen = boardPopup.IsOpen;
        CloseAllPopupsImmediately();
        boardPopup.IsOpen = !wasOpen;
    }

    private void SetBoardMode(OverlayWindow.BoardMode mode)
    {
        if (_overlayWindow == null) return;
        Log($"SetBoardMode mode={mode}");
        _overlayWindow.SetBoardMode(mode);
        UpdateBoardUi(mode);
    }

    private void UpdateBoardUi(OverlayWindow.BoardMode mode)
    {
        if (_overlayWindow == null) return;
        bool isBoardActive = mode != OverlayWindow.BoardMode.Transparent;
        boardPagePanel.Visibility = isBoardActive ? Visibility.Visible : Visibility.Collapsed;

        if (isBoardActive)
        {
            btnBoard.ToolTip = $"Board Mode: {mode} ({_settings.KeyBoard}) — Click for Desktop / Arrow for Styles";
            boardIcon.Fill = mode switch
            {
                OverlayWindow.BoardMode.Greenboard => new SolidColorBrush(Color.FromRgb(27, 77, 62)),
                OverlayWindow.BoardMode.Grid => new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                OverlayWindow.BoardMode.Ruled => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                OverlayWindow.BoardMode.Dots => new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                OverlayWindow.BoardMode.Frosted => new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))
            };

            // If currently in cursor / pass-through mode, automatically switch to Pen so user can write on the board immediately
            bool isDrawingActive = toolPen.IsChecked == true ||
                                   toolHighlighter.IsChecked == true ||
                                   toolLaser.IsChecked == true ||
                                   toolEraser.IsChecked == true ||
                                   (toolBadge != null && toolBadge.IsChecked == true) ||
                                   toolShapes.IsChecked == true ||
                                   (toolSelect != null && toolSelect.IsChecked == true) ||
                                   (toolText != null && toolText.IsChecked == true);

            if (!isDrawingActive)
            {
                toolPen.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Pen);
            }

            txtBoardPage.Text = $"{_overlayWindow.CurrentBoardPage}/{_overlayWindow.TotalBoardPages}";
        }
        else
        {
            btnBoard.ToolTip = $"Board Mode: Desktop ({_settings.KeyBoard}) — Click for Whiteboard";
            boardIcon.SetBinding(Shape.FillProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        }
    }

    private void BtnBoardStyle_Select(object sender)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            if (Enum.TryParse<OverlayWindow.BoardMode>(tag, out var mode))
            {
                SetBoardMode(mode);
                boardPopup.IsOpen = false;
            }
        }
    }

    private void BtnBoardStyle_Click(object sender, RoutedEventArgs e) => BtnBoardStyle_Select(sender);

    private void BtnBoardStyle_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        BtnBoardStyle_Select(sender);
        e.Handled = true;
    }

    private void BtnBoardSplit_Select(object sender)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            if (Enum.TryParse<OverlayWindow.BoardSplitMode>(tag, out var splitMode))
            {
                _overlayWindow?.SetBoardSplitMode(splitMode);
                boardPopup.IsOpen = false;
            }
        }
    }

    private void BtnBoardSplit_Click(object sender, RoutedEventArgs e) => BtnBoardSplit_Select(sender);

    private void BtnBoardSplit_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        BtnBoardSplit_Select(sender);
        e.Handled = true;
    }

    private void BtnPrevBoardPage_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.PreviousBoardPage();
    }

    private void BtnNextBoardPage_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.NextBoardPage();
    }

    private void BtnAddBoardPage_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.AddBoardPage();
    }

    private void OnPenColorAutoChanged(Color color)
    {
        activeColorBadge.Fill = new SolidColorBrush(color);
        UpdateTextPreview();
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        foreach (var child in FindVisualChildren<RadioButton>(colorStylePopup))
        {
            if (child.Tag is string tag && string.Equals(tag, hex, StringComparison.OrdinalIgnoreCase))
            {
                child.IsChecked = true;
                break;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var childOfChild in FindVisualChildren<T>(child))
                yield return childOfChild;
        }
    }

    private void BtnSaveToPc_Click(object sender, RoutedEventArgs e)
    {
        CloseAllPopupsImmediately();
        var bmp = ScreenCaptureHelper.CaptureScreen(this);
        if (bmp != null)
        {
            bool saved = ScreenCaptureHelper.SaveToFile(bmp, this);
            if (saved)
            {
                _trayHelper?.ShowBalloon("Screenshot Saved", "Screenshot with your drawings has been saved to your PC.");
            }
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        var settingsWin = new SettingsWindow(_settings)
        {
            Owner = this
        };
        settingsWin.SettingsSaved += (updatedSettings) =>
        {
            _settings = updatedSettings;
            _currentHotkeyName = _settings.RecallHotkey;
            _currentHoldKey = _settings.HoldKey;
            _isKeystrokeVisualizerEnabled = _settings.KeystrokesEnabled;
            _isCursorHaloEnabled = _settings.CursorHaloEnabled;
            _isVanishingLaserEnabled = _settings.VanishingLaserEnabled;

            if (_overlayWindow != null)
            {
                _overlayWindow.IsCursorHaloEnabled = _isCursorHaloEnabled;
                _overlayWindow.IsVanishingLaserEnabled = _isVanishingLaserEnabled;
            }

            RegisterCurrentHotkey();
            SetOrientation(_settings.IsVerticalOrientation);
            UpdateTooltipsWithHotkeys();
        };
        settingsWin.ShowDialog();
    }

    private void UpdateTooltipsWithHotkeys()
    {
        if (toolSelect != null) toolSelect.ToolTip = $"Select & Move ({_settings.KeySelect}) — Click or lasso to select, then drag to move";
        if (toolPen != null) toolPen.ToolTip = $"Pen ({_settings.KeyPen}) — Click to draw, click again for cursor mode";
        if (toolHighlighter != null) toolHighlighter.ToolTip = $"Highlighter ({_settings.KeyHighlighter}) — Click to draw, click again for cursor mode";
        if (toolLaser != null) toolLaser.ToolTip = $"Laser Pointer ({_settings.KeyLaser}) — Click to draw, click again for cursor mode";
        if (toolText != null) toolText.ToolTip = $"Text Tool ({_settings.KeyText}) — Click to type, arrow for font & styles";
        if (toolBadge != null) toolBadge.ToolTip = $"Step Badge ({_settings.KeyStepBadge}) — Click to place numbered steps. Right-click to reset";
        if (toolShapes != null) toolShapes.ToolTip = $"Shapes ({_settings.KeyShapes}) — Click to draw shape, arrow for shapes";
        if (toolEraser != null) toolEraser.ToolTip = $"Stroke Eraser ({_settings.KeyEraser}) — Click to erase, click again for cursor mode";
        if (btnBoard != null) btnBoard.ToolTip = $"Board Mode ({_settings.KeyBoard}) — Click for Whiteboard, arrow for styles";
        if (btnSpotlight != null) btnSpotlight.ToolTip = $"Screen Spotlight ({_settings.KeySpotlight}) — Dim background and highlight cursor";
        if (btnMagnifier != null) btnMagnifier.ToolTip = $"Screen Magnifier ({_settings.KeyMagnifier}) — 2× Live zoom loupe following cursor";
        if (btnToggleEye != null) btnToggleEye.ToolTip = $"Hide / Show Drawings ({_settings.KeyToggleInk})";
        if (btnClear != null) btnClear.ToolTip = $"Clear Canvas ({_settings.KeyClear})";
        if (btnScreenshot != null) btnScreenshot.ToolTip = $"Screenshot ({_settings.KeyScreenshot}) — Copy to Clipboard or Save to PC";
        if (btnMinimize != null) btnMinimize.ToolTip = $"Minimize to System Tray ({_currentHotkeyName})";
    }

    private void BtnSpotlight_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null) return;
        _overlayWindow.IsSpotlightActive = btnSpotlight.IsChecked == true;
    }

    private void BtnMagnifier_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null) return;
        _overlayWindow.IsMagnifierActive = btnMagnifier.IsChecked == true;
    }

    private void BtnFillShape_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayWindow == null) return;
        _overlayWindow.IsShapeFilled = btnFillShape.IsChecked == true;
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        _overlayWindow?.Hide();

        // Show Windows notification balloon
        if (_settings.ShowNotification)
        {
            _trayHelper?.ShowBalloon(
                "Scribo Minimized",
                $"Scribo is in the system tray. Press {_currentHotkeyName} anywhere or click the tray icon to restore.");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void ToggleOrSelectTool(RadioButton? rb, ToolType toolType)
    {
        if (rb == null) return;
        if (rb.IsChecked == true)
        {
            SwitchToCursorMode();
        }
        else
        {
            rb.IsChecked = true;
            _overlayWindow?.SetTool(toolType);
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept tool hotkeys when typing into an inline text note
        if (_overlayWindow?.IsEditingText == true)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Z)
        {
            _overlayWindow?.Undo();
            e.Handled = true;
            return;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Y)
        {
            _overlayWindow?.Redo();
            e.Handled = true;
            return;
        }
        else if ((key == Key.Delete || key == Key.Back) && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_overlayWindow != null && _overlayWindow.DeleteSelectedStrokes())
            {
                e.Handled = true;
                return;
            }
        }

        // Configurable tool hotkeys
        if (key == AppSettings.ParseKey(_settings.KeyPen, Key.P))
        {
            ToggleOrSelectTool(toolPen, ToolType.Pen);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyHighlighter, Key.H))
        {
            ToggleOrSelectTool(toolHighlighter, ToolType.Highlighter);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyLaser, Key.L))
        {
            ToggleOrSelectTool(toolLaser, ToolType.Laser);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyText, Key.T))
        {
            ToggleOrSelectTool(toolText, ToolType.Text);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyStepBadge, Key.N))
        {
            ToggleOrSelectTool(toolBadge, ToolType.StepBadge);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyShapes, Key.R))
        {
            if (toolShapes.IsChecked == true)
            {
                SwitchToCursorMode();
            }
            else
            {
                toolShapes.IsChecked = true;
                _overlayWindow?.SetTool(_currentShapeTool);
            }
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyEraser, Key.E))
        {
            ToggleOrSelectTool(toolEraser, ToolType.Eraser);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeySelect, Key.S))
        {
            ToggleOrSelectTool(toolSelect, ToolType.Select);
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyBoard, Key.B))
        {
            BtnBoard_Click(btnBoard, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeySpotlight, Key.F))
        {
            btnSpotlight.IsChecked = btnSpotlight.IsChecked != true;
            BtnSpotlight_Click(btnSpotlight, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyMagnifier, Key.M))
        {
            btnMagnifier.IsChecked = btnMagnifier.IsChecked != true;
            BtnMagnifier_Click(btnMagnifier, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyToggleInk, Key.V))
        {
            BtnToggleEye_Click(btnToggleEye, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyClear, Key.K) || (Keyboard.Modifiers == ModifierKeys.Control && key == Key.K))
        {
            _overlayWindow?.ClearCanvas();
            e.Handled = true;
        }
        else if (key == AppSettings.ParseKey(_settings.KeyScreenshot, Key.C))
        {
            BtnScreenshot_Click(btnScreenshot, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (key == Key.A)
        {
            SelectShape(ToolType.Arrow);
            e.Handled = true;
        }
        else if (key == Key.D)
        {
            SelectShape(ToolType.DoubleArrow);
            e.Handled = true;
        }
        else if (key == Key.I)
        {
            SelectShape(ToolType.Line);
            e.Handled = true;
        }
    }
    #endregion

    #region Momentary Hold-to-Select & Keystroke Visualizer Keyboard Hook
    private void InstallKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero) return;
        _keyboardHookProc = LowLevelKeyboardCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = NativeMethods.GetModuleHandle(curModule?.ModuleName);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
        Log($"Keyboard hook installed: handle={_keyboardHookHandle}");
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            Log("Keyboard hook uninstalled");
        }
    }

    private bool IsHoldKey(uint vk)
    {
        return _currentHoldKey switch
        {
            "Alt" => vk == NativeMethods.VK_MENU || vk == NativeMethods.VK_LMENU || vk == NativeMethods.VK_RMENU,
            "Ctrl" => vk == NativeMethods.VK_CONTROL || vk == NativeMethods.VK_LCONTROL || vk == NativeMethods.VK_RCONTROL,
            "Shift" => vk == NativeMethods.VK_SHIFT || vk == NativeMethods.VK_LSHIFT || vk == NativeMethods.VK_RSHIFT,
            "Space" => vk == NativeMethods.VK_SPACE,
            _ => false
        };
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                ProcessKeystrokeForVisualizer(vk);
            }

            if (IsHoldKey(vk))
            {
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    Dispatcher.BeginInvoke(EnterHoldSelectMode);
                }
                else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    Dispatcher.BeginInvoke(ExitHoldSelectMode);
                }
            }
            else if (_isHoldingKey)
            {
                // If a modifier combo like Alt+Tab or Alt+F4 occurs while holding, cancel hold-select
                if (vk == 0x09 /* Tab */ || vk == 0x1B /* Esc */ || vk == 0x73 /* F4 */)
                {
                    Dispatcher.BeginInvoke(ExitHoldSelectMode);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void ProcessKeystrokeForVisualizer(uint vk)
    {
        if (!_isKeystrokeVisualizerEnabled || _overlayWindow == null) return;

        // Skip standalone modifier keys by themselves
        if (vk == 0x11 || vk == 0x12 || vk == 0x10 || vk == 0x5B || vk == 0x5C ||
            vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5)
        {
            return;
        }

        bool ctrl = (NativeMethods.GetKeyState(0x11 /* VK_CONTROL */) & 0x8000) != 0;
        bool alt = (NativeMethods.GetKeyState(0x12 /* VK_MENU */) & 0x8000) != 0;
        bool shift = (NativeMethods.GetKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0;
        bool win = (NativeMethods.GetKeyState(0x5B /* VK_LWIN */) & 0x8000) != 0 ||
                   (NativeMethods.GetKeyState(0x5C /* VK_RWIN */) & 0x8000) != 0;

        string keyName = GetFriendlyKeyName(vk);
        if (string.IsNullOrEmpty(keyName)) return;

        var keys = new List<string>();
        if (win) keys.Add("Win");
        if (ctrl) keys.Add("Ctrl");
        if (alt) keys.Add("Alt");
        if (shift) keys.Add("Shift");
        keys.Add(keyName);

        Dispatcher.BeginInvoke(() => _overlayWindow.ShowKeystrokes(keys));
    }

    private static string GetFriendlyKeyName(uint vk)
    {
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => ""
        };
    }

    private void EnterHoldSelectMode()
    {
        if (_isHoldingKey) return;
        if (Visibility != Visibility.Visible || _overlayWindow == null) return;
        if (_overlayWindow.CurrentTool == ToolType.Cursor) return; // Don't interrupt desktop pass-through

        _isHoldingKey = true;
        _previousToolBeforeHold = _overlayWindow.CurrentTool;

        if (_previousToolBeforeHold != ToolType.Select)
        {
            toolSelect.IsChecked = true;
            _overlayWindow.SetTool(ToolType.Select);
        }
    }

    private void ExitHoldSelectMode()
    {
        if (!_isHoldingKey) return;
        _isHoldingKey = false;

        // If mouse left button is currently pressed (e.g. user is actively dragging a shape),
        // wait until mouse release to restore tool cleanly
        if (Mouse.LeftButton == MouseButtonState.Pressed && _overlayWindow != null)
        {
            MouseButtonEventHandler? onMouseUp = null;
            onMouseUp = (s, e) =>
            {
                _overlayWindow.PreviewMouseLeftButtonUp -= onMouseUp;
                if (!_isHoldingKey)
                {
                    RestorePreviousTool();
                }
            };
            _overlayWindow.PreviewMouseLeftButtonUp += onMouseUp;
            return;
        }

        RestorePreviousTool();
    }

    private void RestorePreviousTool()
    {
        if (_previousToolBeforeHold == null || _overlayWindow == null) return;

        var target = _previousToolBeforeHold.Value;
        _previousToolBeforeHold = null;

        if (target == ToolType.Select) return;

        switch (target)
        {
            case ToolType.Pen:
                toolPen.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Pen);
                break;
            case ToolType.Highlighter:
                toolHighlighter.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Highlighter);
                break;
            case ToolType.Laser:
                toolLaser.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Laser);
                break;
            case ToolType.Text:
                if (toolText != null) toolText.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Text);
                break;
            case ToolType.StepBadge:
                if (toolBadge != null) toolBadge.IsChecked = true;
                _overlayWindow.SetTool(ToolType.StepBadge);
                break;
            case ToolType.Rectangle:
            case ToolType.Circle:
            case ToolType.Arrow:
            case ToolType.DoubleArrow:
            case ToolType.Line:
                SelectShape(target);
                break;
            case ToolType.Eraser:
                toolEraser.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Eraser);
                break;
            default:
                toolPen.IsChecked = true;
                _overlayWindow.SetTool(ToolType.Pen);
                break;
        }
    }
    #endregion
}
