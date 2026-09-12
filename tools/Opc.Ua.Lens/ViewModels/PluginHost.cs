/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
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
        IPluginFactory? factory)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        Log = telemetry.CreateLogger("Documents");
        m_ownsCapabilities = capabilities is null;
        Capabilities = capabilities ?? new CapabilityService(connection, new SessionCapabilityProbe());
        Factory = factory ?? PluginFactory.Default;
    }

    public IPluginWorkspace Workspace { get; }

    public ISession? Session => Connection.CurrentSession;

    public ConnectionService Connection { get; }

    public BrowserViewModel Browser { get; }

    public ITelemetryContext Telemetry { get; }

    public ILogger Log { get; }

    public ICapabilityService Capabilities { get; }

    public IPluginFactory Factory { get; }

    public ResourceMonitorHost? ResourceMonitor => Workspace.ResourceMonitor;

    public ValueTask DisposeAsync()
    {
        return m_ownsCapabilities ? Capabilities.DisposeAsync() : ValueTask.CompletedTask;
    }

    private readonly bool m_ownsCapabilities;
}
