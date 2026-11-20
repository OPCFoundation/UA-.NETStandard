/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.Diagnostics.Capture;
using UaLens.Workspace;

namespace UaLens.ViewModels;

/// <summary>
/// Live context passed to tool factories. Session and resource monitoring are
/// resolved on every access; neither a captured session nor the entire shell is exposed.
/// </summary>
internal sealed class PluginHost : IAsyncDisposable
{
    public PluginHost(
        IPluginWorkspace workspace,
        ConnectionService connection,
        BrowserViewModel browser,
        ITelemetryContext telemetry)
        : this(workspace, connection, browser, telemetry, null, null)
    {
    }

    public PluginHost(
        IPluginWorkspace workspace,
        ConnectionService connection,
        BrowserViewModel browser,
        ITelemetryContext telemetry,
        ICapabilityService? capabilities,
        IPluginFactory? factory,
        DiagnosticCaptureWorkspace? captures = null)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        Log = telemetry.CreateLogger("Documents");
        m_ownsCapabilities = capabilities is null;
        Capabilities = capabilities ?? new CapabilityService(connection, new SessionCapabilityProbe());
        Factory = factory ?? PluginFactory.Default;
        Captures = captures;
    }

    public IPluginWorkspace Workspace { get; }

    public ISession? Session => Connection.CurrentSession;

    public ConnectionService Connection { get; }

    public BrowserViewModel Browser { get; }

    public ITelemetryContext Telemetry { get; }

    public ILogger Log { get; }

    public ICapabilityService Capabilities { get; }

    public IPluginFactory Factory { get; }

    /// <summary>
    /// Gets the optional workspace-owned capture module; tool runtimes only register their borrowed transport taps.
    /// </summary>
    public DiagnosticCaptureWorkspace? Captures { get; }

    public ResourceMonitorHost? ResourceMonitor => Workspace.ResourceMonitor;

    public ValueTask DisposeAsync()
    {
        return m_ownsCapabilities ? Capabilities.DisposeAsync() : ValueTask.CompletedTask;
    }

    private readonly bool m_ownsCapabilities;
}
