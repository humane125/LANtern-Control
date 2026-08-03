using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Lantern.Linux;

public enum UpdatePromptChoice
{
    NotNow,
    Update,
    NeverAskAgain,
}
public partial class UpdatePromptWindow : Window
{
    public UpdatePromptWindow()
        : this(new Version(), new Version())
    {
    }

    public UpdatePromptWindow(Version installedVersion, Version availableVersion)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        ArgumentNullException.ThrowIfNull(availableVersion);
        InitializeComponent();
        InstalledVersionText.Text = $"v{installedVersion.ToString(3)}";
        AvailableVersionText.Text = $"v{availableVersion.ToString(3)}";
    }

    public UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.NotNow;

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(eventArgs);
        }
    }

    private void UpdateButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.Update;
        Close(Choice);
    }

    private void NotNowButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.NotNow;
        Close(Choice);
    }

    private void NeverAskAgainButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        Choice = UpdatePromptChoice.NeverAskAgain;
        Close(Choice);
    }
}
