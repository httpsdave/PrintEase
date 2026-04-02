using System.Configuration;
using System.Data;
using System.Windows;

namespace PrintEase.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            System.Windows.MessageBox.Show($"Startup crash:\n{e.Exception}", "PrintEase Error");
            e.Handled = false;
        };
        
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.Windows.MessageBox.Show($"Unhandled exception:\n{e.ExceptionObject}", "PrintEase Error");
        };
    }
}

