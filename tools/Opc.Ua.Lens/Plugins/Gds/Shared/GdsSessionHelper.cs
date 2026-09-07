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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Gds;

/// <summary>
/// Pure-static helpers shared by the GDS-flavoured plug-ins
/// (<c>GdsPushPlugin</c>, <c>GdsManagementPlugin</c>) for the small
/// portions of their secondary-session plumbing that have zero coupling
/// to per-plug-in state:
///
/// <list type="bullet">
///   <item><see cref="IsOuterSuitable"/> / <see cref="IsOuterInsecure"/> —
///         predicates over the outer Connection-pane <see cref="ManagedSession"/>
///         used to decide whether the outer can be piggy-backed on or
///         whether the Connect button should advertise "Connect securely…".</item>
///   <item><see cref="SafeDisconnectAndDisposeAsync"/> — defensive disconnect +
///         dispose used during reconnect / Disconnect / tab dispose.</item>
/// </list>
///
/// The picker → reuse-or-fresh-connect → auto-piggyback flow itself is
/// deliberately <em>not</em> extracted here: both plug-ins drive a thicket
/// of plug-in-specific state (<c>m_client</c>, <c>m_secondaryConnected</c>,
/// <c>m_boundEndpoint</c>, <c>ConnectionStatus</c>, the post-connect
/// <c>RefreshAsync</c>/<c>PopulateServerInfoAsync</c> hooks, and Push's
/// <c>AdminCredentialsRequired</c>/<c>KeepAlive</c> wiring) from those
/// paths, and threading that through a generic helper requires more
/// callback parameters than the duplication itself carries.  The duplicated
/// outer skeletons therefore stay where they are.
/// </summary>
internal static class GdsSessionHelper
{
    /// <summary>
    /// Returns true when <paramref name="session"/> is the outer
    /// Connection-pane session and it is connected with
    /// <see cref="MessageSecurityMode.SignAndEncrypt"/> — the minimum
    /// security posture required before the GDS plug-ins will silently
    /// piggy-back on it instead of running the endpoint picker.
    /// </summary>
    public static bool IsOuterSuitable(ManagedSession? session)
    {
        return session is { Connected: true }
            && session.ConfiguredEndpoint?.Description is { } d
            && d.SecurityMode == MessageSecurityMode.SignAndEncrypt;
    }

    /// <summary>
    /// Returns true when <paramref name="session"/> is connected but
    /// uses a security mode weaker than
    /// <see cref="MessageSecurityMode.SignAndEncrypt"/>.  Used by the
    /// GDS plug-ins to flip the Connect button label to
    /// "Connect securely…" so the user understands why the outer
    /// session can't be reused as-is.
    /// </summary>
    public static bool IsOuterInsecure(ManagedSession? session)
    {
        return session is { Connected: true }
            && session.ConfiguredEndpoint?.Description is { } d
            && d.SecurityMode != MessageSecurityMode.SignAndEncrypt;
    }

    /// <summary>
    /// Defensive disconnect + dispose of a GDS secondary client.  Both
    /// stages are independently try/catched and any exceptions are
    /// downgraded to <see cref="LogLevel.Debug"/> against
    /// <paramref name="log"/> with <paramref name="contextDescription"/>
    /// as the structured-log scope tag, matching the historical behaviour
    /// of the per-plug-in <c>SafeDisposeClientAsync</c> methods.
    ///
    /// <paramref name="detachHandlers"/> runs inside the same try block
    /// as the disconnect call so that detaching event handlers on an
    /// already-faulted client surfaces the same suppressed-debug-log
    /// behaviour as the disconnect itself.
    /// </summary>
    /// <param name="client">The client to disconnect + dispose.  No-op if null.</param>
    /// <param name="detachHandlers">Optional pre-disconnect hook — typically
    /// detaches <c>AdminCredentialsRequired</c>/<c>KeepAlive</c> handlers
    /// that the plug-in wired onto the client.</param>
    /// <param name="disconnectAsync">Plug-in-supplied closure that calls
    /// <c>DisconnectAsync</c> on the client.  Required because
    /// <see cref="ServerPushConfigurationClient"/> and
    /// <see cref="Opc.Ua.Gds.Client.GlobalDiscoveryServerClient"/> do not
    /// share a common interface exposing that method.</param>
    /// <param name="log">Plug-in's logger.</param>
    /// <param name="contextDescription">Human-readable scope tag for log
    /// messages, e.g. <c>"GdsPush tab GDS Push 1"</c>.</param>
    public static async Task SafeDisconnectAndDisposeAsync(
        IAsyncDisposable? client,
        Action? detachHandlers,
        Func<CancellationToken, ValueTask> disconnectAsync,
        ILogger log,
        string contextDescription)
    {
        ArgumentNullException.ThrowIfNull(disconnectAsync);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(contextDescription);

        if (client is null)
        {
            return;
        }

        try
        {
            detachHandlers?.Invoke();
            await disconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "{Context}: disconnect threw (suppressed).", contextDescription);
        }

        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "{Context}: dispose threw (suppressed).", contextDescription);
        }
    }
}
