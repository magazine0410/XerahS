#region License Information (GPL v3)
/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team
*/
#endregion License Information (GPL v3)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ShareX.ImageEditor.Presentation.ViewModels;
using XerahS.Common;

namespace XerahS.UI.Services;

/// <summary>
/// Hosts dialog view-models in <see cref="MainViewModel.ModalContent"/> (Add-from-Catalog pattern).
/// Completes awaiters on VM close or overlay dismiss (backdrop / Escape / CloseModal).
/// </summary>
public static class ModalDialogHost
{
    // The main window has one modal slot. A dialog opened from another dialog (for example the image effect
    // browser from the workflow editor) takes the slot, and gives it back to the dialog below it when it closes,
    // instead of closing that one too.
    private static readonly List<object> OpenDialogs = new();

    public static Task<T> ShowAsync<T>(
        object viewModel,
        Action<Action<T>> assignCloseCallback,
        T dismissResult,
        string debugSource)
    {
        var mainVm = ModalOpenService.ResolveHostViewModel();
        if (mainVm == null)
        {
            DebugHelper.WriteLine($"[{debugSource}] ModalDialogHost: host MainViewModel is null");
            return Task.FromResult(dismissResult);
        }

        EnsureMainWindowVisible();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        bool IsTopDialog() => OpenDialogs.Count > 0 && ReferenceEquals(OpenDialogs[^1], viewModel);

        void Finish(T result)
        {
            mainVm.PropertyChanged -= OnModalPropertyChanged;
            OpenDialogs.Remove(viewModel);
            tcs.TrySetResult(result);
        }

        void ShowDialogBelow()
        {
            if (OpenDialogs.Count > 0)
            {
                mainVm.ModalContent = OpenDialogs[^1];
                mainVm.IsModalOpen = true;
            }
        }

        void Complete(T result)
        {
            if (tcs.Task.IsCompleted) return;
            bool wasTop = IsTopDialog();
            Finish(result);
            if (!wasTop) return;

            if (OpenDialogs.Count > 0)
                ShowDialogBelow();
            else if (mainVm.ModalContent == viewModel)
                mainVm.CloseModalCommand.Execute(null);
        }

        void OnModalPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Only the dialog on top is dismissed by the backdrop, Escape, or CloseModal.
            if (e.PropertyName == nameof(MainViewModel.IsModalOpen) &&
                !mainVm.IsModalOpen &&
                !tcs.Task.IsCompleted &&
                IsTopDialog())
            {
                Finish(dismissResult);
                if (OpenDialogs.Count > 0)
                {
                    // CloseModal clears ModalContent after IsModalOpen, so restore the dialog below afterwards.
                    Dispatcher.UIThread.Post(ShowDialogBelow, DispatcherPriority.Send);
                }
            }
        }

        assignCloseCallback(Complete);
        OpenDialogs.Add(viewModel);
        mainVm.PropertyChanged += OnModalPropertyChanged;
        ModalOpenService.Open(mainVm, viewModel, debugSource);
        return tcs.Task;
    }

    public static Task ShowUntilClosedAsync(
        object viewModel,
        Action<Action> assignCloseCallback,
        string debugSource)
    {
        return ShowAsync(
            viewModel,
            set => assignCloseCallback(() => set(true)),
            dismissResult: true,
            debugSource);
    }

    public static void EnsureMainWindowVisible()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var main = desktop.MainWindow;
        if (main == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!main.IsVisible)
                main.Show();
            if (main.WindowState == WindowState.Minimized)
                main.WindowState = WindowState.Normal;
            main.Activate();
        }, DispatcherPriority.Send);
    }
}
