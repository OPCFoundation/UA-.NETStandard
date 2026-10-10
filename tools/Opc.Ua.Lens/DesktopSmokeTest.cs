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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using ScottPlot.Avalonia;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens;

/// <summary>
/// Offline artifact check using the shipped composition, desktop backend,
/// compiled resources, document factory and chart renderer. Not a UI test runner.
/// </summary>
internal sealed partial class DesktopSmokeTest
{
    public Task Completion => m_completion
        ?? throw new InvalidOperationException("The desktop smoke window never opened.");

    public void Start(
        MainWindow window,
        MainViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (m_completion is not null)
        {
            throw new InvalidOperationException("The desktop smoke check has already started.");
        }
        m_completion = RunAsync(window, viewModel, desktop);
    }

    private static async Task RunAsync(
        MainWindow window,
        MainViewModel viewModel,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        ILogger logger = viewModel.Telemetry.CreateLogger("DesktopSmoke");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int exitCode = 1;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            LogStarting(logger);
            Application application = Application.Current
                ?? throw new InvalidOperationException("The desktop application is not initialized.");
            foreach (string key in s_resourceKeys)
            {
                if (!application.TryGetResource(key, application.ActualThemeVariant, out object? value)
                    || value is not ISolidColorBrush)
                {
                    throw new InvalidOperationException($"The compiled theme resource '{key}' did not load.");
                }
            }

            IPlugin document = await viewModel.OpenToolAsync(
                PluginKind.SubscriptionBench, cancellationToken: timeout.Token).ConfigureAwait(true);
            Control view = document.View
                ?? throw new InvalidOperationException("The factory document has no desktop view.");
            // An animation tick can precede the shell's queued host-visibility
            // update. Require actual attachment before checking the rendered view.
            await WaitForAttachmentAsync(window, view, timeout.Token).ConfigureAwait(true);
            await NextFrameAsync(window, timeout.Token).ConfigureAwait(true);
            window.UpdateLayout();
            if (!window.IsVisible || !window.GetVisualDescendants().Contains(view))
            {
                throw new InvalidOperationException("The factory document was not attached to the desktop shell.");
            }
            AvaPlot chart = view.GetVisualDescendants().OfType<AvaPlot>().Single();
            if (!chart.IsVisible || chart.Bounds.Width <= 0 || chart.Bounds.Height <= 0)
            {
                throw new InvalidOperationException("The document chart has no visible desktop layout.");
            }

            // Render deterministic data through the actual document's plot. This
            // loads the shipped native Skia/font dependencies even on a quiet,
            // disconnected bench, rather than only constructing a managed plot.
            chart.Plot.Add.Scatter(s_x, s_y);
            chart.Plot.Axes.AutoScale();
            chart.Plot.RenderInMemory(640, 360);
            chart.Refresh();
            await NextFrameAsync(window, timeout.Token).ConfigureAwait(true);
            LogRendered(logger);
            exitCode = 0;
        }
        finally
        {
            try
            {
                await window.DisposeAsync().ConfigureAwait(true);
            }
            finally
            {
                // Even a failed check must release the event loop, so Program
                // can observe the faulted task and await DI disposal. The outer
                // artifact runner also bounds failures inside native code.
                desktop.Shutdown(exitCode);
            }
        }
        if (!viewModel.Workspace.IsClosing || viewModel.Tabs.Count != 0)
        {
            throw new InvalidOperationException("The desktop workspace did not drain.");
        }
        LogDrained(logger);
    }

    private static async Task WaitForAttachmentAsync(
        Window window,
        Control view,
        CancellationToken cancellationToken)
    {
        var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnAttached(object? sender, VisualTreeAttachmentEventArgs args) => attached.TrySetResult();
        view.AttachedToVisualTree += OnAttached;
        try
        {
            if (!window.GetVisualDescendants().Contains(view))
            {
                await attached.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            view.AttachedToVisualTree -= OnAttached;
        }
    }

    private static async Task NextFrameAsync(Window window, CancellationToken cancellationToken)
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => frame.TrySetResult());
        await frame.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting offline desktop artifact smoke check.")]
    private static partial void LogStarting(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Compiled resources, factory document and desktop chart rendered.")]
    private static partial void LogRendered(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Desktop artifact smoke workspace drained.")]
    private static partial void LogDrained(ILogger logger);

    private Task? m_completion;
    private static readonly string[] s_resourceKeys = ["AppBg", "TextPrimary", "TextOnAccent"];
    private static readonly double[] s_x = [0, 1, 2, 3];
    private static readonly double[] s_y = [0, 1, -1, 0];
}
