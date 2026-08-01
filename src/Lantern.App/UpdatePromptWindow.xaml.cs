using System.Windows;
using System.Windows.Input;

namespace Lantern.App;

public enum UpdatePromptChoice
{
    NotNow,
    Update,
    NeverAskAgain,
}

public partial class UpdatePromptWindow : Window
{
    public UpdatePromptWindow(Version installedVersion, Version availableVersion)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        ArgumentNullException.ThrowIfNull(availableVersion);
        InitializeComponent();
        InstalledVersionText.Text = $"v{installedVersion.ToString(3)}";
        AvailableVersionText.Text = $"v{availableVersion.ToString(3)}";
    }

    public UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.NotNow;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void UpdateButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.Update;
        Close();
    }

    private void NotNowButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.NotNow;
        Close();
    }

    private void NeverAskAgainButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.NeverAskAgain;
        Close();
    }
}
