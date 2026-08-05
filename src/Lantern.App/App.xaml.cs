using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lantern.App.Services;

namespace Lantern.App;

public partial class App : Application
{
    private static readonly ExceptionDialogGate ExceptionDialogGate = new();

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (!ExceptionDialogGate.TryEnter())
        {
            return;
        }

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LANternControl",
            "unhandled-error.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(
                logPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{eventArgs.Exception}");
        }
        catch
        {
            // Error reporting must never trigger another dispatcher exception.
        }

        try
        {
            MessageBox.Show(
                $"{eventArgs.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"Details were saved to:{Environment.NewLine}{logPath}",
                "LANtern Control",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // A failing system dialog must not recursively open more dialogs.
        }
    }
}
