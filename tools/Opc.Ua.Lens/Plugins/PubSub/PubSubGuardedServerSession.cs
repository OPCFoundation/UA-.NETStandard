// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
