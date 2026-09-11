/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NUnit.Framework;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

/// <summary>
/// Owns a shell, its workspace, modal windows and isolated workspace files.
/// Assertions observe real platform windows and awaited dispatcher frames.
/// </summary>
internal sealed class DesktopWindowScope : IAsyncDisposable
{
    private DesktopWindowScope(string directory, IStorageProvider? storageProvider)
    {
        DirectoryPath = directory;
        Appearance = new AppearancePreferences(Path.Combine(directory, "appearance.json"));
        ViewModel = new MainViewModel();
        Window = new MainWindow(ViewModel, Appearance, storageProvider: storageProvider);
        Window.Closed += (_, _) => m_closed.TrySetResult();
    }

    public MainWindow Window { get; }
    public MainViewModel ViewModel { get; }
    public AppearancePreferences Appearance { get; }
    public string DirectoryPath { get; }
    public Task Closed => m_closed.Task;

    public static async Task<DesktopWindowScope> OpenAsync(IStorageProvider? storageProvider = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        string directory = Path.GetFullPath(Path.Combine("TestResults", "lens-desktop", Guid.NewGuid().ToString("N")));
        var scope = new DesktopWindowScope(directory, storageProvider);
        Directory.CreateDirectory(directory);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnThemeChanged() => loaded.TrySetResult();
        ThemeManager.ThemeChanged += OnThemeChanged;
        try
        {
            scope.Window.Width = scope.Window.MinWidth;
            scope.Window.Height = scope.Window.MinHeight;
            scope.Window.Show();
            await loaded.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            await FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(scope.Window.TryGetPlatformHandle()?.Handle, Is.Not.Null.And.Not.EqualTo(IntPtr.Zero),
                "A native platform window is required; headless control construction is not a desktop run.");
            Assert.That(scope.Window.Screens, Is.Not.Null, "The platform has no display service.");
            Assert.That(scope.Window.Screens!.All, Is.Not.Empty, "No platform display was detected.");
            string? expectedScale = Environment.GetEnvironmentVariable("UALENS_DESKTOP_EXPECTED_SCALE");
            if (expectedScale is not null)
            {
                double scale = double.Parse(expectedScale, System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(scope.Window.RenderScaling, Is.EqualTo(scale).Within(0.01),
                    "The requested DPI matrix cell does not match the actual platform render scale.");
            }
            TestContext.Progress.WriteLine(
                $"Desktop: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
                $"client={scope.Window.ClientSize}; scale={scope.Window.RenderScaling}");
            return scope;
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(true);
            throw;
        }
        finally
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }
    }

    public static async Task FrameAsync(Window window)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => rendered.TrySetResult());
        await rendered.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
        window.UpdateLayout();
    }

    public static async Task ChangeAsync(INotifyPropertyChanged source, Func<bool> condition, Action action)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (condition())
            {
                changed.TrySetResult();
            }
        }
        source.PropertyChanged += OnChanged;
        try
        {
            action();
            if (condition())
            {
                changed.TrySetResult();
            }
            await changed.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
        }
        finally
        {
            source.PropertyChanged -= OnChanged;
        }
    }

    public static async Task ControlChangeAsync(AvaloniaObject source, Func<bool> condition, Action action)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (condition())
            {
                changed.TrySetResult();
            }
        }
        source.PropertyChanged += OnChanged;
        try
        {
            action();
            if (condition())
            {
                changed.TrySetResult();
            }
            await changed.Task.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
        }
        finally
        {
            source.PropertyChanged -= OnChanged;
        }
    }

    public static void Click(Button button)
    {
        Assert.That(button.IsEffectivelyVisible, Is.True, $"{button.Name} is not visible.");
        Assert.That(button.IsEffectivelyEnabled, Is.True, $"{button.Name} is not enabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public static KeyEventArgs Key(
        InputElement source,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None,
        bool keyUp = false)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = keyUp ? InputElement.KeyUpEvent : InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers
        };
        source.RaiseEvent(args);
        return args;
    }

    public async ValueTask DisposeAsync()
    {
        await Window.DisposeAsync().AsTask().WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
        if (!Closed.IsCompleted)
        {
            Window.Close();
            await Closed.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
        }
        Assert.That(Window.OwnedWindows, Is.Empty, "A task-owned window outlived its shell.");
        Directory.Delete(DirectoryPath, recursive: true);
    }

    private readonly TaskCompletionSource m_closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
