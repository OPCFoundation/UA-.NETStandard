/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

namespace UaLens.Tests.Administration;

/// <summary>
/// Builds a disconnected <see cref="PluginHost"/> for the administration tools so
/// their offline configuration, persistence and lifetime seams can be exercised
/// without a server, a window or any UI-testing dependency.
/// </summary>
internal sealed class AdministrationTestContext : IAsyncDisposable
{
    public AdministrationTestContext()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(capacity: 32));
        Connection = new ConnectionService(telemetry);
        Workspace = new TestPluginWorkspace();
        Host = new PluginHost(Workspace, Connection, new BrowserViewModel(telemetry, Connection), telemetry);
    }

    public ConnectionService Connection { get; }

    public TestPluginWorkspace Workspace { get; }

    public PluginHost Host { get; }

    /// <summary>
    /// Creates an administration document offline. The workspace is disconnected,
    /// so no session-dependent work runs until a connection notification is delivered.
    /// </summary>
    public IPlugin Create(PluginKind kind)
        => PluginRegistry.For(kind).Factory(Host);

    public ValueTask DisposeAsync()
        => Connection.DisposeAsync();

    /// <summary>
    /// Minimal cooperating-tools context: it never opens documents, mutates the
    /// session or exposes a resource monitor.
    /// </summary>
    internal sealed class TestPluginWorkspace : ObservableObject, IPluginWorkspace
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
            => throw new NotSupportedException("The administration tests do not open cooperating tools.");
    }
}
