#region License Information (GPL v3)
/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team
*/
#endregion License Information (GPL v3)

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XerahS.UI.ViewModels;

namespace XerahS.UI.Views;

public partial class SimplePromptView : UserControl
{
    public SimplePromptView()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// As ShareX's prompts: the text box takes the focus with its text selected, so typing replaces it and Enter confirms;
    /// a prompt without input focuses its default button, so Enter confirms and Escape cancels there too.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
    }

    private void FocusInitialControl()
    {
        if (DataContext is not SimplePromptViewModel vm || !IsLoaded) return;
        if (vm.ShowInput)
        {
            TextBox? box = this.FindControl<TextBox>(vm.IsPassword ? "PasswordTextBox" : "InputTextBox");
            if (box?.Focus() == true)
            {
                box.SelectAll();
            }
            return;
        }

        this.FindControl<Button>("PrimaryButton")?.Focus();
    }
}
