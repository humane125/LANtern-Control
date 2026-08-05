using System.Windows;
using System.Windows.Input;

namespace Lantern.App;

public partial class SafeModePromptWindow : Window
{
    public SafeModePromptWindow()
    {
        InitializeComponent();
    }

    public bool EnableSafeMode { get; private set; }

    public bool SuppressFuturePrompts => DontAskAgainCheckBox.IsChecked == true;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void EnableButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        EnableSafeMode = true;
        DialogResult = true;
    }

    private void IgnoreButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        EnableSafeMode = false;
        DialogResult = false;
    }
}
