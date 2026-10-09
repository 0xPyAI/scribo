using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scribo;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _initialIsVertical;
    private Button? _activeRecordingButton;

    public event Action<AppSettings>? SettingsSaved;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _initialIsVertical = settings.IsVerticalOrientation;

        // Presentation options
        chkKeystrokes.IsChecked = settings.KeystrokesEnabled;
        chkCursorHalo.IsChecked = settings.CursorHaloEnabled;
        chkVanishingLaser.IsChecked = settings.VanishingLaserEnabled;

        // Minimize options
        chkTray.IsChecked = settings.MinimizeToTray;
        chkNotify.IsChecked = settings.ShowNotification;

        // Recall Hotkey
        for (int i = 0; i < comboHotkey.Items.Count; i++)
        {
            if (comboHotkey.Items[i] is ComboBoxItem item && item.Content.ToString()!.Contains(settings.RecallHotkey))
            {
                comboHotkey.SelectedIndex = i;
                break;
            }
        }

        // Hold-to-Select key
        for (int i = 0; i < comboHoldKey.Items.Count; i++)
        {
            if (comboHoldKey.Items[i] is ComboBoxItem item && item.Content.ToString()!.StartsWith(settings.HoldKey, StringComparison.OrdinalIgnoreCase))
            {
                comboHoldKey.SelectedIndex = i;
                break;
            }
        }

        // Orientation
        if (settings.IsVerticalOrientation)
        {
            radioVertical.IsChecked = true;
            radioHorizontal.IsChecked = false;
        }
        else
        {
            radioHorizontal.IsChecked = true;
            radioVertical.IsChecked = false;
        }

        // Tool Shortcuts
        SetButtonKey(btnKeyPen, settings.KeyPen, "P");
        SetButtonKey(btnKeyHighlighter, settings.KeyHighlighter, "H");
        SetButtonKey(btnKeyLaser, settings.KeyLaser, "L");
        SetButtonKey(btnKeyText, settings.KeyText, "T");
        SetButtonKey(btnKeyStepBadge, settings.KeyStepBadge, "N");
        SetButtonKey(btnKeyShapes, settings.KeyShapes, "R");
        SetButtonKey(btnKeyEraser, settings.KeyEraser, "E");
        SetButtonKey(btnKeySelect, settings.KeySelect, "S");
        SetButtonKey(btnKeyBoard, settings.KeyBoard, "B");
        SetButtonKey(btnKeySpotlight, settings.KeySpotlight, "F");
        SetButtonKey(btnKeyMagnifier, settings.KeyMagnifier, "M");
        SetButtonKey(btnKeyToggleInk, settings.KeyToggleInk, "V");
        SetButtonKey(btnKeyClear, settings.KeyClear, "K");
        SetButtonKey(btnKeyScreenshot, settings.KeyScreenshot, "C");
    }

    private static void SetButtonKey(Button btn, string keyStr, string defaultStr)
    {
        string finalKey = string.IsNullOrWhiteSpace(keyStr) ? defaultStr : keyStr;
        btn.Content = finalKey;
        btn.Tag = finalKey;
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            if (_activeRecordingButton != null && _activeRecordingButton != btn)
            {
                // Revert previous button
                _activeRecordingButton.Content = _activeRecordingButton.Tag?.ToString() ?? "";
                _activeRecordingButton.ClearValue(Border.BorderBrushProperty);
                _activeRecordingButton.ClearValue(Border.BackgroundProperty);
            }

            _activeRecordingButton = btn;
            btn.Content = "Press key...";
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(79, 70, 229));
            btn.Background = new SolidColorBrush(Color.FromRgb(238, 242, 255));
            btn.Focus();
        }
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_activeRecordingButton != null)
        {
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                // Cancel
                _activeRecordingButton.Content = _activeRecordingButton.Tag?.ToString() ?? "";
                _activeRecordingButton.ClearValue(Border.BorderBrushProperty);
                _activeRecordingButton.ClearValue(Border.BackgroundProperty);
                _activeRecordingButton = null;
                return;
            }

            // Ignore modifier keys by themselves
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            string keyName = key.ToString();
            _activeRecordingButton.Content = keyName;
            _activeRecordingButton.Tag = keyName;
            _activeRecordingButton.ClearValue(Border.BorderBrushProperty);
            _activeRecordingButton.ClearValue(Border.BackgroundProperty);
            _activeRecordingButton = null;
        }
    }

    private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        comboHotkey.SelectedIndex = 0; // Alt + S
        comboHoldKey.SelectedIndex = 0; // Alt

        SetButtonKey(btnKeyPen, "P", "P");
        SetButtonKey(btnKeyHighlighter, "H", "H");
        SetButtonKey(btnKeyLaser, "L", "L");
        SetButtonKey(btnKeyText, "T", "T");
        SetButtonKey(btnKeyStepBadge, "N", "N");
        SetButtonKey(btnKeyShapes, "R", "R");
        SetButtonKey(btnKeyEraser, "E", "E");
        SetButtonKey(btnKeySelect, "S", "S");
        SetButtonKey(btnKeyBoard, "B", "B");
        SetButtonKey(btnKeySpotlight, "F", "F");
        SetButtonKey(btnKeyMagnifier, "M", "M");
        SetButtonKey(btnKeyToggleInk, "V", "V");
        SetButtonKey(btnKeyClear, "K", "K");
        SetButtonKey(btnKeyScreenshot, "C", "C");

        if (_activeRecordingButton != null)
        {
            _activeRecordingButton.ClearValue(Border.BorderBrushProperty);
            _activeRecordingButton.ClearValue(Border.BackgroundProperty);
            _activeRecordingButton = null;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Save Recall Hotkey
        if (comboHotkey.SelectedItem is ComboBoxItem item)
        {
            string content = item.Content.ToString()!;
            if (content.Contains("Alt + S")) _settings.RecallHotkey = "Alt + S";
            else if (content.Contains("F8")) _settings.RecallHotkey = "F8";
            else if (content.Contains("Ctrl + Shift + S")) _settings.RecallHotkey = "Ctrl + Shift + S";
            else if (content.Contains("Ctrl + Alt + D")) _settings.RecallHotkey = "Ctrl + Alt + D";
        }

        // Save Hold-to-Select key
        if (comboHoldKey.SelectedItem is ComboBoxItem holdItem)
        {
            string content = holdItem.Content.ToString()!;
            if (content.StartsWith("Alt")) _settings.HoldKey = "Alt";
            else if (content.StartsWith("Ctrl")) _settings.HoldKey = "Ctrl";
            else if (content.StartsWith("Shift")) _settings.HoldKey = "Shift";
            else if (content.StartsWith("Space")) _settings.HoldKey = "Space";
            else if (content.StartsWith("Disabled")) _settings.HoldKey = "Disabled";
        }

        // Save Tool Shortcuts
        _settings.KeyPen = btnKeyPen.Tag?.ToString() ?? "P";
        _settings.KeyHighlighter = btnKeyHighlighter.Tag?.ToString() ?? "H";
        _settings.KeyLaser = btnKeyLaser.Tag?.ToString() ?? "L";
        _settings.KeyText = btnKeyText.Tag?.ToString() ?? "T";
        _settings.KeyStepBadge = btnKeyStepBadge.Tag?.ToString() ?? "N";
        _settings.KeyShapes = btnKeyShapes.Tag?.ToString() ?? "R";
        _settings.KeyEraser = btnKeyEraser.Tag?.ToString() ?? "E";
        _settings.KeySelect = btnKeySelect.Tag?.ToString() ?? "S";
        _settings.KeyBoard = btnKeyBoard.Tag?.ToString() ?? "B";
        _settings.KeySpotlight = btnKeySpotlight.Tag?.ToString() ?? "F";
        _settings.KeyMagnifier = btnKeyMagnifier.Tag?.ToString() ?? "M";
        _settings.KeyToggleInk = btnKeyToggleInk.Tag?.ToString() ?? "V";
        _settings.KeyClear = btnKeyClear.Tag?.ToString() ?? "K";
        _settings.KeyScreenshot = btnKeyScreenshot.Tag?.ToString() ?? "C";

        // Save Orientation
        _settings.IsVerticalOrientation = radioVertical.IsChecked == true;

        // Save Presentation options
        _settings.KeystrokesEnabled = chkKeystrokes.IsChecked ?? false;
        _settings.CursorHaloEnabled = chkCursorHalo.IsChecked ?? false;
        _settings.VanishingLaserEnabled = chkVanishingLaser.IsChecked ?? true;

        // Save Minimize options
        _settings.MinimizeToTray = chkTray.IsChecked ?? true;
        _settings.ShowNotification = chkNotify.IsChecked ?? true;

        // Persist to disk
        _settings.Save();

        // Notify MainWindow
        SettingsSaved?.Invoke(_settings);

        Close();
    }
}
