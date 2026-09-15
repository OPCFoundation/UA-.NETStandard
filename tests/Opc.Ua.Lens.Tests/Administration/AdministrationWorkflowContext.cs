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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Administration;

/// <summary>
/// Reuses A's controlled connection, without creating an Application or a session.
/// Every cooperating-tool request must be explicitly arranged by the test.
/// </summary>
internal sealed class AdministrationWorkflowContext : IAsyncDisposable
{
    public AdministrationWorkflowContext(ApplicationConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            ConnectionContext.Backend.Setup(b => b.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(configuration);
                });
        }
        Workspace.SetupProperty(w => w.EndpointUrl, string.Empty);
        Workspace.SetupProperty(w => w.CurrentRegisteredApp);
        Workspace.SetupGet(w => w.ActiveDocument).Returns((IPlugin?)null);
        Workspace.SetupGet(w => w.SelectedNode).Returns((NodeViewModel?)null);
        Workspace.SetupGet(w => w.IsAddressSpaceVisible).Returns(false);
        Workspace.SetupGet(w => w.ResourceMonitor).Returns((UaLens.Diagnostics.ResourceMonitorHost?)null);
        var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns((string category) =>
            category == "Documents" ? Log : ConnectionContext.Telemetry.CreateLogger(category));
        var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
        telemetry.SetupGet(t => t.LoggerFactory).Returns(factory.Object);
        telemetry.SetupGet(t => t.ActivitySource).Returns(ConnectionContext.Telemetry.ActivitySource);
        telemetry.Setup(t => t.CreateMeter()).Returns(() => ConnectionContext.Telemetry.CreateMeter());
        Telemetry = telemetry.Object;
        Host = new PluginHost(
            Workspace.Object, ConnectionContext.Connection, ConnectionContext.Browser, Telemetry);
    }

    public DesktopConnectionContext ConnectionContext { get; } = new();
    public Mock<IPluginWorkspace> Workspace { get; } = new(MockBehavior.Strict);
    public AdministrationLogCapture Log { get; } = new();
    public ITelemetryContext Telemetry { get; }
    public PluginHost Host { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Host.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await ConnectionContext.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class AdministrationLogCapture : ILogger
{
    public sealed record Entry(
        LogLevel Level,
        EventId Event,
        Exception? Exception,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State);

    public IReadOnlyList<Entry> Entries => m_entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        KeyValuePair<string, object?>[] values =
            state is IEnumerable<KeyValuePair<string, object?>> structured ? structured.ToArray() : [];
        m_entries.Enqueue(new Entry(logLevel, eventId, exception, formatter(state, exception), values));
    }

    private readonly ConcurrentQueue<Entry> m_entries = new();
}
