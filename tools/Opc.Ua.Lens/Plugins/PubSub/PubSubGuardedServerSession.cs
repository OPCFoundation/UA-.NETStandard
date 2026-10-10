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

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Cancels and drains only the selected UA binding operations. It borrows the
/// provider session and does not own the independent PubSub network runtime.
/// </summary>
internal sealed class PubSubGuardedServerSession : IServerSession
{
    public PubSubGuardedServerSession(IServerSession session)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_token = m_lifetime.Token;
    }

    public bool IsConnected => !m_token.IsCancellationRequested && m_session.IsConnected;

    public event EventHandler? ModelChanged
    {
        add => m_session.ModelChanged += value;
        remove => m_session.ModelChanged -= value;
    }

    public ValueTask ConnectAsync(CancellationToken ct = default)
    {
        return ExecuteAsync(m_session.ConnectAsync, ct);
    }

    public ValueTask<ArrayOf<DataValue>> ReadAsync(ArrayOf<ReadValueId> nodesToRead, CancellationToken ct = default)
    {
        return ExecuteAsync(token => m_session.ReadAsync(nodesToRead, token), ct);
    }

    public ValueTask<ArrayOf<StatusCode>> WriteAsync(ArrayOf<WriteValue> nodesToWrite, CancellationToken ct = default)
    {
        return ExecuteAsync(token => m_session.WriteAsync(nodesToWrite, token), ct);
    }

    public ValueTask<RemoteCallResult> CallAsync(
        NodeId objectId,
        NodeId methodId,
        ArrayOf<Variant> inputArguments,
        CancellationToken ct = default)
    {
        return ExecuteAsync(token => m_session.CallAsync(objectId, methodId, inputArguments, token), ct);
    }

    public ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
        double publishingIntervalMs,
        CancellationToken ct = default)
    {
        return ExecuteAsync(token => m_session.CreateDataChangeSubscriptionAsync(publishingIntervalMs, token), ct);
    }

    public ValueTask StartModelChangeMonitoringAsync(CancellationToken ct = default)
    {
        return ExecuteAsync(m_session.StartModelChangeMonitoringAsync, ct);
    }

    public ValueTask<NodeId> ResolveNodeIdAsync(NodeId nodeId, CancellationToken ct = default)
    {
        return ExecuteAsync(token => m_session.ResolveNodeIdAsync(nodeId, token), ct);
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_stopped = true;
            m_disposal ??= DrainAsync(m_drained.Task);
            return new ValueTask(m_disposal);
        }
    }

    private async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        BeginOperation();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_token);
            linked.Token.ThrowIfCancellationRequested();
            return await operation(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    private async ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        BeginOperation();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_token);
            linked.Token.ThrowIfCancellationRequested();
            await operation(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    private void BeginOperation()
    {
        lock (m_gate)
        {
            if (m_stopped)
            {
                throw new OperationCanceledException("The selected UA binding has stopped.", m_token);
            }
            if (m_operations++ == 0)
            {
                m_drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void EndOperation()
    {
        lock (m_gate)
        {
            if (--m_operations == 0)
            {
                m_drained.TrySetResult();
            }
        }
    }

    private async Task DrainAsync(Task drained)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            await m_lifetime.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await drained.ConfigureAwait(false);
            }
            finally
            {
                m_lifetime.Dispose();
            }
        }
    }

    private static TaskCompletionSource CreateDrained()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }

    private readonly Lock m_gate = new();
    private readonly IServerSession m_session;
    private readonly CancellationTokenSource m_lifetime = new();
    private readonly CancellationToken m_token;
    private TaskCompletionSource m_drained = CreateDrained();
    private Task? m_disposal;
    private int m_operations;
    private bool m_stopped;
}
