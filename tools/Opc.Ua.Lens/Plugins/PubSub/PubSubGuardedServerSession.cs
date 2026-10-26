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
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;
using UaLens.Workspace;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Cancels and drains only the selected UA binding operations. It borrows the
/// provider session and does not own the independent PubSub network runtime.
/// </summary>
internal sealed class PubSubGuardedServerSession : IServerSession
{
    /// <summary>
    /// Borrows a server session and gives its document-owned calls a separate cancellation lifetime.
    /// </summary>
    public PubSubGuardedServerSession(IServerSession session)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc/>
    public bool IsConnected => !m_operations.IsStopped && m_session.IsConnected;

    /// <inheritdoc/>
    public event EventHandler? ModelChanged
    {
        add => m_session.ModelChanged += value;
        remove => m_session.ModelChanged -= value;
    }

    /// <inheritdoc/>
    public ValueTask ConnectAsync(CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(m_session.ConnectAsync, ct);
    }

    /// <inheritdoc/>
    public ValueTask<ArrayOf<DataValue>> ReadAsync(ArrayOf<ReadValueId> nodesToRead, CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(token => m_session.ReadAsync(nodesToRead, token), ct);
    }

    /// <inheritdoc/>
    public ValueTask<ArrayOf<StatusCode>> WriteAsync(ArrayOf<WriteValue> nodesToWrite, CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(token => m_session.WriteAsync(nodesToWrite, token), ct);
    }

    /// <inheritdoc/>
    public ValueTask<RemoteCallResult> CallAsync(
        NodeId objectId,
        NodeId methodId,
        ArrayOf<Variant> inputArguments,
        CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(token => m_session.CallAsync(objectId, methodId, inputArguments, token), ct);
    }

    /// <inheritdoc/>
    public ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
        double publishingIntervalMs,
        CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(
            token => m_session.CreateDataChangeSubscriptionAsync(publishingIntervalMs, token), ct);
    }

    /// <inheritdoc/>
    public ValueTask StartModelChangeMonitoringAsync(CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(m_session.StartModelChangeMonitoringAsync, ct);
    }

    /// <inheritdoc/>
    public ValueTask<NodeId> ResolveNodeIdAsync(NodeId nodeId, CancellationToken ct = default)
    {
        return m_operations.ExecuteAsync(token => m_session.ResolveNodeIdAsync(nodeId, token), ct);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return m_operations.DisposeAsync();
    }

    private readonly IServerSession m_session;
    private readonly OwnedOperationScope m_operations = new("The selected UA binding has stopped.");
}
