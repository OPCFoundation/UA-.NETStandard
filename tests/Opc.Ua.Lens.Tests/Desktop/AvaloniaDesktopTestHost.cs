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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

/// <summary>
/// One process-owned real desktop dispatcher. Bodies run serially with a temporary
/// desktop MainWindow. Callers close and await their controls' StopAsync before
/// releasing services; the final window sweep is a failure-path safety net.
/// </summary>
internal static class AvaloniaDesktopTestHost
{
    public static async Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (s_state.IsValueCreated && s_state.Value.Started.Task.IsCompletedSuccessfully &&
            Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("RunAsync bodies cannot nest another desktop scope.");
        }
        await s_serial.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartAsync().ConfigureAwait(false);
            HostState state = s_state.Value;
            ObjectDisposedException.ThrowIf(state.Stopped.Task.IsCompleted, typeof(AvaloniaDesktopTestHost));
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => _ = ExecuteAsync(body, completion));
            Task finished = await Task.WhenAny(completion.Task, state.Stopped.Task).ConfigureAwait(false);
            if (ReferenceEquals(finished, state.Stopped.Task))
            {
                await state.Stopped.Task.ConfigureAwait(false);
                throw new InvalidOperationException("The desktop dispatcher stopped before its body completed.");
            }
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            s_serial.Release();
        }
    }

    internal static Task StartAsync()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "The owned desktop thread supports Windows and Linux. Cocoa requires the process main thread.");
        }
        return s_state.Value.Started.Task;
    }

    internal static async Task StopAsync()
    {
        if (!s_state.IsValueCreated)
        {
            return;
        }
        await s_serial.WaitAsync().ConfigureAwait(false);
        try
        {
            await s_state.Value.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            s_serial.Release();
        }
    }

    private static async Task ExecuteAsync(Func<Task> body, TaskCompletionSource completion)
    {
        var errors = new List<Exception>();
        Application app = Application.Current!;
        var lifetime = s_state.Value.Lifetime!;
        Window? previousOwner = lifetime.MainWindow;
        var previousResources = app.Resources;
        var previousStyles = app.Styles.ToArray();
        var previousTheme = app.RequestedThemeVariant;
        var windows = new List<Window>();
        using IDisposable opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            window.ShowInTaskbar = false;
            windows.Add(window);
        });
        try
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(previousResources);
            app.Resources = resources;
            var owner = new Window
            {
                Title = "UaLens owned test desktop",
                Width = 1000,
                Height = 760,
                ShowInTaskbar = false
            };
            lifetime.MainWindow = owner;
            owner.Show();
            await body().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                try
                {
                    Window window = windows[i];
                    window.Close();
                    switch (window)
                    {
                        case WriteValueDialog write:
                            await write.StopAsync().ConfigureAwait(true);
                            break;
                        case MethodCallDialog call:
                            await call.StopAsync().ConfigureAwait(true);
                            break;
                        case ComplexValueElementDialog element:
                            await element.StopAsync().ConfigureAwait(true);
                            break;
                        case EditArrayDialog array:
                            await array.StopAsync().ConfigureAwait(true);
                            break;
                    }
                    if (window is IAsyncDisposable disposable)
                    {
                        await disposable.DisposeAsync().ConfigureAwait(true);
                    }
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            lifetime.MainWindow = previousOwner;
            app.Resources = previousResources;
            app.RequestedThemeVariant = previousTheme;
            app.Styles.Clear();
            foreach (var style in previousStyles)
            {
                app.Styles.Add(style);
            }
        }
        if (errors.Count == 0)
        {
            completion.SetResult();
        }
        else
        {
            completion.SetException(errors.Count == 1 ? errors[0] : new AggregateException(errors));
        }
    }

    private sealed class HostState : IAsyncDisposable
    {
        public HostState()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "UaLens test desktop" };
            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource LoopCancellation { get; } = new();
        public ClassicDesktopStyleApplicationLifetime? Lifetime { get; private set; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Stopped.Task.IsCompleted)
                {
                    LoopCancellation.Cancel();
                }
                await Stopped.Task.ConfigureAwait(false);
            }
            finally
            {
                LoopCancellation.Dispose();
            }
        }

        private void Run()
        {
            Exception? failure = null;
            try
            {
                if (Application.Current is not null)
                {
                    throw new InvalidOperationException("Avalonia was initialized outside the owned desktop thread.");
                }
                Lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithLifetime(Lifetime);
                Application.Current!.Styles.Add(new FluentTheme());
                Application.Current.Resources.MergedDictionaries.Add(new UaLens.Themes.DarkStandardTheme());
                _ = ItemColors.ForItemId(0);
                AvaloniaSynchronizationContext.InstallIfNeeded();
                Started.SetResult();
                Dispatcher.UIThread.MainLoop(LoopCancellation.Token);
            }
            catch (OperationCanceledException) when (LoopCancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                failure = error;
                Started.TrySetException(error);
            }
            finally
            {
                try
                {
                    if (Lifetime is not null)
                    {
                        Lifetime.MainWindow = null;
                        Lifetime.Dispose();
                    }
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                if (failure is null)
                {
                    Stopped.TrySetResult();
                }
                else
                {
                    Stopped.TrySetException(failure);
                }
            }
        }

    }

    private static readonly SemaphoreSlim s_serial = new(1, 1);
    private static readonly Lazy<HostState> s_state = new(() => new HostState());
}
