/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace UaLens.Tests.Desktop;

internal static class DesktopInteraction
{
    public static Window Owner =>
        ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow!;

    public static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public static T Control<T>(Control root, string name) where T : Control
    {
        return NameScope.GetNameScope(root)?.Find<T>(name) ??
            root.GetLogicalDescendants().OfType<T>().SingleOrDefault(control => control.Name == name) ??
            throw new InvalidOperationException($"The {root.GetType().Name} has no {name} control.");
    }

    public static async Task ChangedAsync(AvaloniaObject source, Func<bool> complete, Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (complete())
            {
                completion.TrySetResult();
            }
        }
        source.PropertyChanged += Changed;
        try
        {
            await action().ConfigureAwait(true);
            if (complete())
            {
                completion.TrySetResult();
            }
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        }
        finally
        {
            source.PropertyChanged -= Changed;
        }
    }

    public static async Task<T> OpenedAsync<T>(Action action) where T : Window
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = Window.WindowOpenedEvent.AddClassHandler<T>((window, _) =>
            completion.TrySetResult(window));
        action();
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
    }

    public static async Task CollectionChangedAsync(
        INotifyCollectionChanged collection, Func<bool> complete, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (complete())
            {
                completion.TrySetResult();
            }
        }
        collection.CollectionChanged += Changed;
        try
        {
            action();
            if (complete())
            {
                completion.TrySetResult();
            }
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        }
        finally
        {
            collection.CollectionChanged -= Changed;
        }
    }

    public static async Task ModelChangedAsync(
        INotifyPropertyChanged model, Func<bool> complete, Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (complete())
            {
                completion.TrySetResult();
            }
        }
        model.PropertyChanged += Changed;
        try
        {
            await action().ConfigureAwait(true);
            if (complete())
            {
                completion.TrySetResult();
            }
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
        }
        finally
        {
            model.PropertyChanged -= Changed;
        }
    }
}
