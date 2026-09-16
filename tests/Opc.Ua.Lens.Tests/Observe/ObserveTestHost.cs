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
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Plugins.Gds;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Observe;

/// <summary>
/// Builds a disconnected <see cref="PluginHost"/> for the Observe document tests. It
/// wires a real telemetry context, a fresh disconnected <see cref="ConnectionService"/>
/// and a browser, exactly as the production factory does, so the plug-in lifecycle and
/// configuration seams are exercised without a live server.
/// </summary>
internal sealed class ObserveTestHost : IAsyncDisposable
{
    public ObserveTestHost()
    {
        Telemetry = new AppTelemetryContext(new LogRingBuffer(capacity: 32));
        Connection = new ConnectionService(Telemetry);
        Host = new PluginHost(new FakeWorkspace(), Connection, new BrowserViewModel(Telemetry, Connection), Telemetry);
    }

    public AppTelemetryContext Telemetry { get; }

    public ConnectionService Connection { get; }

    public PluginHost Host { get; }

    public IServiceMessageContext MessageContext => ServiceMessageContext.Create(Telemetry);

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class FakeWorkspace : ObservableObject, IPluginWorkspace
    {
        public IPlugin? ActiveDocument => null;
        public string EndpointUrl { get; set; } = string.Empty;
        public RegisteredApplicationContext? CurrentRegisteredApp { get; set; }
        public NodeViewModel? SelectedNode => null;
        public bool IsAddressSpaceVisible => false;
        public ResourceMonitorHost? ResourceMonitor => null;

        public Task<IPlugin> OpenToolAsync(
            PluginKind kind,
            EndpointDescription? discoveryEndpoint = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The Observe document tests do not open cooperating tools.");
    }
}
