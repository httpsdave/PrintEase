using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintEase.App.Models;
using PrintEase.App.Services;

namespace PrintEase.App;

public partial class MainWindow : Window
{
    private readonly PrinterManager _printerManager = new();
    private readonly DiagnosticsService _diagnostics = new();
    private List<PrinterDevice> _printers = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await DetectPrintersAsync();
    }

    private PrinterDevice? SelectedPrinter() => PrinterComboBox.SelectedItem as PrinterDevice;

    private void SetActionState(bool busy, string message)
    {
        ActionProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActionStatusText.Text = message;

        DetectPrintersButton.IsEnabled = !busy;
        AutoFixButton.IsEnabled = !busy;
        TestPrintButton.IsEnabled = !busy;
    }

    private async Task DetectPrintersAsync()
    {
        SetActionState(true, "Detecting printers...");
        try
        {
            var previousName = SelectedPrinter()?.Name;
            _printers = await Task.Run(() => _printerManager.GetPrinters().ToList());
            var onlinePrinters = _printers.Where(p => p.IsOnline).ToList();
            var likelyAvailablePrinters = _printers.Where(p => !p.IsOffline).ToList();
            var displayPrinters = onlinePrinters.Count > 0
                ? onlinePrinters
                : likelyAvailablePrinters.Count > 0
                    ? likelyAvailablePrinters
                    : _printers;

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
                return;
            }

            UpdatePrinterInfo(selected);
            var connection = selected.ConnectionType;
            StatusText.Text = $"Status: detected {selected.Name} via {connection}.";
            ActionStatusText.Text = $"Detected {selected.Name}.";
            _diagnostics.Info($"Detected printer {selected.Name} ({connection})");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: detection failed. {ex.Message}";
            ActionStatusText.Text = "Detection failed.";
            _diagnostics.Error("Printer detection failed", ex);
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
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: auto-fix failed. {ex.Message}";
            ActionStatusText.Text = "Auto-fix failed.";
            _diagnostics.Error("Auto-fix failed", ex);
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
            return;
        }

        SetActionState(true, "Sending test print...");
        try
        {
            await Task.Run(() => _printerManager.PrintTestPage(selected.Name, new PrintOptions()));
            StatusText.Text = "Status: test page sent.";
            ActionStatusText.Text = "Test page sent.";
            _diagnostics.Info($"Test print sent to {selected.Name}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Status: test print failed. {ex.Message}";
            ActionStatusText.Text = "Test print failed.";
            _diagnostics.Error("Test print failed", ex);
        }
        finally
        {
            SetActionState(false, ActionStatusText.Text);
        }
    }

    private async void DetectPrintersButton_Click(object sender, RoutedEventArgs e)
    {
        await DetectPrintersAsync();
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
        Close();
    }
}
