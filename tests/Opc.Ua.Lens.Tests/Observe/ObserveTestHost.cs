// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
