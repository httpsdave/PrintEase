using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Forms = System.Windows.Forms;
using PrintEase.App.Models;
using PrintEase.App.Services;

namespace PrintEase.App;

public partial class MainWindow : Window
{
    private readonly PrinterManager _printerManager = new();
    private readonly DiagnosticsService _diagnostics = new();
    private readonly DispatcherTimer _trayInactivityTimer = new();
    private List<PrinterDevice> _printers = new();
    private Forms.NotifyIcon? _trayIcon;
    private bool _closeToTrayEnabled;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;

        _trayInactivityTimer.Interval = TimeSpan.FromMinutes(10);
        _trayInactivityTimer.Tick += TrayInactivityTimer_Tick;

        EnsureTrayIcon();
    }

    private PrinterDevice? SelectedPrinter() => PrinterComboBox.SelectedItem as PrinterDevice;

    private void SetActionState(bool busy, string message)
    {
        ActionProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActionStatusText.Text = message;

        DetectPrintersButton.IsEnabled = !busy;
        AutoFixButton.IsEnabled = !busy;
        AutoFixHelpButton.IsEnabled = !busy;
        TestPrintButton.IsEnabled = !busy;
        TestPrintHelpButton.IsEnabled = !busy;
    }

    private void ShowActionResult(string title, string message, bool success)
    {
        System.Windows.MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task DetectPrintersAsync(bool showPopup = false)
    {
        SetActionState(true, "Detecting printers...");
        try
        {
            var previousName = SelectedPrinter()?.Name;
            _printers = await Task.Run(() => _printerManager.GetPrinters().ToList());
            var displayPrinters = _printers;

            PrinterComboBox.ItemsSource = displayPrinters;

            var selected = displayPrinters.FirstOrDefault(p => string.Equals(p.Name, previousName, StringComparison.OrdinalIgnoreCase))
                ?? displayPrinters.FirstOrDefault(p => p.IsDefault)
                ?? displayPrinters.FirstOrDefault();

            PrinterComboBox.SelectedItem = selected;

            if (selected is null)
            {
                PrinterInfoText.Text = "No printer detected. Check power, cable/Wi-Fi, and printer driver.";
                StatusText.Text = "Status: no printers found.";
                ActionStatusText.Text = "No printers found.";
                if (showPopup)
                {
                    ShowActionResult(
                        "Detect Printers",
                        "No printers were found. Check power, USB/Wi-Fi connection, and printer drivers, then try again.",
                        success: false);
                }
                return;
            }

            UpdatePrinterInfo(selected);
            var connection = selected.ConnectionType;
            var onlineCount = _printers.Count(p => p.IsOnline);
            var offlineCount = _printers.Count(p => p.IsOffline);
            var unknownCount = _printers.Count - onlineCount - offlineCount;
            StatusText.Text = $"Status: selected {selected.Name} via {connection} ({selected.OnlineStatus.ToLowerInvariant()}).";
            ActionStatusText.Text = $"Detected {_printers.Count} printers: {onlineCount} online, {offlineCount} offline, {unknownCount} unknown.";
            _diagnostics.Info($"Detected printer {selected.Name} ({connection})");

            if (showPopup)
            {
                ShowActionResult(
                    "Detect Printers",
                    $"Detection complete. Found {_printers.Count} printer(s). Selected: {selected.Name}.",
                    success: true);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: detection failed. {ex.Message}";
            ActionStatusText.Text = "Detection failed.";
            _diagnostics.Error("Printer detection failed", ex);

            if (showPopup)
            {
                ShowActionResult(
                    "Detect Printers",
                    $"Printer detection failed: {ex.Message}",
                    success: false);
            }
        }
        finally
        {
            SetActionState(false, ActionStatusText.Text);
        }
    }

    private void UpdatePrinterInfo(PrinterDevice printer)
    {
        var defaultTag = printer.IsDefault ? "default printer" : "not default";
        PrinterInfoText.Text =
            $"Printer: {printer.Name}\n" +
            $"Connection: {printer.ConnectionType}\n" +
            $"Port: {printer.PortName}\n" +
            $"State: {printer.OnlineStatus} ({defaultTag})";
    }

    private async void AutoFixButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPrinter();
        if (selected is null)
        {
            StatusText.Text = "Status: select a printer, then run Auto Fix.";
            ShowActionResult(
                "Auto Fix And Connect",
                "No printer selected. Detect or choose a printer first, then run Auto Fix And Connect.",
                success: false);
            return;
        }

        SetActionState(true, "Auto-fix and connect running...");
        try
        {
            StatusText.Text = "Status: running recovery steps...";

            var healthyBefore = _printerManager.TestConnection(selected.Name, out var beforeReason);
            if (healthyBefore)
            {
                StatusText.Text = "Status: printer already connected and healthy.";
                _diagnostics.Info($"Auto-fix skipped; printer already healthy: {selected.Name}");
                ActionStatusText.Text = "Printer already healthy.";
                ShowActionResult(
                    "Auto Fix And Connect",
                    $"{selected.Name} is already connected and healthy. No repair steps were required.",
                    success: true);
                return;
            }

            // Recovery order: restart spooler, resume queue, clear stuck jobs, detect again, re-test.
            var spoolerResult = await _printerManager.RestartSpoolerAsync();

            try
            {
                _printerManager.ResumeQueue(selected.Name);
            }
            catch
            {
                // Queue may not be pausable/resumable on some drivers.
            }

            try
            {
                _printerManager.CancelAllJobsFast(selected.Name);
            }
            catch
            {
                // If queue cleanup fails, continue with remaining recovery steps.
            }

            await DetectPrintersAsync();
            selected = SelectedPrinter() ?? selected;

            var healthyAfter = _printerManager.TestConnection(selected.Name, out var afterReason);
            StatusText.Text = healthyAfter
                ? $"Status: connected. {afterReason}"
                : $"Status: still not connected. {afterReason}";
            ActionStatusText.Text = healthyAfter
                ? "Connected after auto-fix."
                : "Auto-fix complete, still disconnected.";

            _diagnostics.Info(
                $"Auto-fix for {selected.Name}: before='{beforeReason}', after='{afterReason}', spooler='{spoolerResult.Replace("\n", " ", StringComparison.Ordinal)}'");

            ShowActionResult(
                "Auto Fix And Connect",
                healthyAfter
                    ? $"Auto-fix completed successfully for {selected.Name}.\n\nResult: {afterReason}"
                    : $"Auto-fix completed but connection is still failing for {selected.Name}.\n\nLatest status: {afterReason}",
                success: healthyAfter);

            if (!healthyAfter)
            {
                ShowManualReconnectGuidance(selected.Name, afterReason);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: auto-fix failed. {ex.Message}";
            ActionStatusText.Text = "Auto-fix failed.";
            _diagnostics.Error("Auto-fix failed", ex);
            ShowActionResult(
                "Auto Fix And Connect",
                $"Auto-fix failed: {ex.Message}",
                success: false);
        }
        finally
        {
            SetActionState(false, ActionStatusText.Text);
        }
    }

    private async void TestPrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPrinter();
        if (selected is null)
        {
            StatusText.Text = "Status: select a printer before test print.";
            ShowActionResult(
                "Test Print",
                "No printer selected. Detect or choose a printer before sending a test page.",
                success: false);
            return;
        }

        SetActionState(true, "Sending test print...");
        try
        {
            if (!_printerManager.TestConnection(selected.Name, out var preflightReason))
            {
                StatusText.Text = $"Status: test print blocked. {preflightReason}";
                ActionStatusText.Text = "Test print blocked; printer is offline.";
                ShowActionResult(
                    "Test Print",
                    $"Cannot send test page to {selected.Name}.\n\nReason: {preflightReason}",
                    success: false);
                return;
            }

            await Task.Run(() => _printerManager.PrintTestPage(selected.Name, new PrintOptions()));
            StatusText.Text = "Status: test page sent.";
            ActionStatusText.Text = "Test page sent.";
            _diagnostics.Info($"Test print sent to {selected.Name}");
            ShowActionResult(
                "Test Print",
                $"Test page sent successfully to {selected.Name}.",
                success: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: test print failed. {ex.Message}";
            ActionStatusText.Text = "Test print failed.";
            _diagnostics.Error("Test print failed", ex);
            ShowActionResult(
                "Test Print",
                $"Test print failed for {selected.Name}: {ex.Message}",
                success: false);
        }
        finally
        {
            SetActionState(false, ActionStatusText.Text);
        }
    }

    private async void DetectPrintersButton_Click(object sender, RoutedEventArgs e)
    {
        await DetectPrintersAsync(showPopup: true);
    }

    private void AutoFixHelpButton_Click(object sender, RoutedEventArgs e)
    {
        const string message =
            "Auto Fix And Connect runs these automated steps:\n\n" +
            "1. Checks current printer connection health.\n" +
            "2. Restarts the Windows Print Spooler service.\n" +
            "3. Attempts to resume the printer queue.\n" +
            "4. Clears stuck print jobs when possible.\n" +
            "5. Re-detects printers and refreshes selection.\n" +
            "6. Re-tests connection and reports final status.\n" +
            "7. If still failing, remove/forget the printer in Windows and add it again.";

        System.Windows.MessageBox.Show(message, "What Auto Fix And Connect Does", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowManualReconnectGuidance(string printerName, string latestStatus)
    {
        var response = System.Windows.MessageBox.Show(
            $"Auto-fix finished but {printerName} is still failing.\n\n" +
            $"Latest status: {latestStatus}\n\n" +
            "Recommended next step:\n" +
            "1. Open Windows printer settings.\n" +
            "2. Remove/forget this printer.\n" +
            "3. Add the printer again, then run Detect Printers and Test Print.\n\n" +
            "Open Windows Printers settings now?",
            "Manual Reconnect Recommended",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (response == MessageBoxResult.Yes)
        {
            _printerManager.OpenPrintersSettings();
            StatusText.Text = "Status: opened Windows Printers settings for remove/re-add workflow.";
            ActionStatusText.Text = "Manual reconnect step opened.";
        }
    }

    private void TestPrintHelpButton_Click(object sender, RoutedEventArgs e)
    {
        const string message =
            "Test Print performs these automated steps:\n\n" +
            "1. Uses the currently selected printer.\n" +
            "2. Sends a Windows test page request with default options.\n" +
            "3. Reports whether the request succeeded or failed.\n\n" +
            "If nothing prints, check printer paper/ink state and queue errors.";

        System.Windows.MessageBox.Show(message, "What Test Print Does", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrinterComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!PrinterComboBox.IsDropDownOpen)
        {
            PrinterComboBox.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void PrinterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedPrinter();
        if (selected is null)
        {
            return;
        }

        UpdatePrinterInfo(selected);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_closeToTrayEnabled)
        {
            HideToTray();
            return;
        }

        _allowExit = true;
        Close();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void CloseToTrayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _closeToTrayEnabled = true;
        CloseToTrayMenuItem.IsChecked = true;
        CloseExitMenuItem.IsChecked = false;
        StatusText.Text = "Status: close action set to minimize to tray.";
    }

    private void CloseExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _closeToTrayEnabled = false;
        CloseToTrayMenuItem.IsChecked = false;
        CloseExitMenuItem.IsChecked = true;
        _trayInactivityTimer.Stop();
        StatusText.Text = "Status: close action set to exit application.";
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            DisposeTrayIcon();
            return;
        }

        if (_closeToTrayEnabled)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        EnsureTrayIcon();

        ShowInTaskbar = false;
        Hide();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            _trayIcon.BalloonTipTitle = "PrintEase running in tray";
            _trayIcon.BalloonTipText = "PrintEase will auto-close after 10 minutes of inactivity.";
            _trayIcon.ShowBalloonTip(2000);
        }

        _trayInactivityTimer.Stop();
        _trayInactivityTimer.Start();
    }

    private void RestoreFromTray()
    {
        _trayInactivityTimer.Stop();

        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    private void TrayInactivityTimer_Tick(object? sender, EventArgs e)
    {
        _trayInactivityTimer.Stop();
        _diagnostics.Info("Auto-shutdown triggered after 10 minutes in tray.");
        _allowExit = true;
        Close();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _trayInactivityTimer.Stop();
            _allowExit = true;
            Close();
        }));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = ResolveTrayIcon(),
            Text = "PrintEase",
            ContextMenuStrip = menu,
            Visible = false
        };

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private static System.Drawing.Icon ResolveTrayIcon()
    {
        try
        {
            var baseDirExe = Path.Combine(AppContext.BaseDirectory, "PrintEase.App.exe");
            var candidatePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? (File.Exists(baseDirExe) ? baseDirExe : null);

            if (!string.IsNullOrWhiteSpace(candidatePath) && File.Exists(candidatePath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(candidatePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall back to a safe system icon if executable icon resolution fails.
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void DisposeTrayIcon()
    {
        _trayInactivityTimer.Stop();

        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void SupportLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Failed to open support link", ex);
            StatusText.Text = "Status: unable to open support link.";
        }
    }
}
