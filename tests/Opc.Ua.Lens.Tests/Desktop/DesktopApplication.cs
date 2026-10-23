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
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;

namespace UaLens.Tests.Desktop;

/// <summary>
/// Owns the real platform dispatcher for the explicitly selected desktop tests.
/// NUnit workers never block while the UI thread runs its native message loop.
/// </summary>
internal sealed class DesktopApplication : IAsyncDisposable
{
    private DesktopApplication()
    {
        var thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "UaLens desktop regression dispatcher"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
    }

    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(Func<Task> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        DesktopApplication application = s_instance.Value;
        await application.m_started.Task.WaitAsync(Timeout).ConfigureAwait(false);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await test().ConfigureAwait(true);
                completed.TrySetResult();
            }
            catch (Exception error)
            {
                completed.TrySetException(error);
            }
        });
        Task finished = await Task.WhenAny(completed.Task, application.m_exited.Task)
            .WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        await finished.ConfigureAwait(false);
        if (!completed.Task.IsCompleted)
        {
            throw new InvalidOperationException("The native dispatcher exited before the desktop test completed.");
        }
        await completed.Task.ConfigureAwait(false);
    }

    public static async Task StopAsync()
    {
        if (s_instance.IsValueCreated)
        {
            await s_instance.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await m_stop.CancelAsync().ConfigureAwait(false);
            await m_exited.Task.WaitAsync(Timeout).ConfigureAwait(false);
        }
        finally
        {
            m_stop.Dispose();
        }
    }

    private void RunMessageLoop()
    {
        try
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("Desktop regression supports Windows and Linux X11.");
            }
            if (OperatingSystem.IsLinux() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                throw new InvalidOperationException("Desktop regression requires a reachable X11 DISPLAY.");
            }
            if (Application.Current is not null)
            {
                throw new InvalidOperationException(
                    "Run desktop tests in a separate test process; Avalonia was already initialized.");
            }
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .SetupWithoutStarting();
            AvaloniaSynchronizationContext.InstallIfNeeded();
            Dispatcher.UIThread.Post(() => m_started.TrySetResult());
            Dispatcher.UIThread.MainLoop(m_stop.Token);
        }
        catch (Exception error)
        {
            m_started.TrySetException(error);
            m_exited.TrySetException(error);
        }
        finally
        {
            m_exited.TrySetResult();
        }
    }

    private readonly CancellationTokenSource m_stop = new();
    private readonly TaskCompletionSource m_started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource m_exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly Lazy<DesktopApplication> s_instance = new(() => new DesktopApplication());
}
