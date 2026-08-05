using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Lantern.Linux;

public sealed record SafeModePromptResult(bool EnableSafeMode, bool SuppressFuturePrompts);

public partial class SafeModePromptWindow : Window
{
    public SafeModePromptWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(eventArgs);
        }
    }

    private void EnableButton_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        Close(new SafeModePromptResult(true, DontAskAgainCheckBox.IsChecked == true));

    private void IgnoreButton_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        Close(new SafeModePromptResult(false, DontAskAgainCheckBox.IsChecked == true));
}
