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
using Opc.Ua.Client;

namespace UaLens.ViewModels;

internal enum WriteValueState
{
    Loading,
    Editing,
    Writing,
    Closing,
    Succeeded,
    Failed,
    Uncertain
}

internal delegate WriteValueOperation WriteValueOperationFactory(NodeId nodeId, ISession session);

/// <summary>
/// Owns one metadata read and at most one dispatched write. A dispatched request
/// cannot be rolled back by cancelling its local wait.
/// </summary>
internal sealed class WriteValueOperation : IAsyncDisposable
{
    public WriteValueOperation(NodeId nodeId, ISession session)
    {
        m_nodeId = nodeId;
        m_session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public WriteValueState State { get; private set; } = WriteValueState.Loading;
    public string Outcome { get; private set; } = string.Empty;
    public NodeId DataType { get; private set; }
    public int ValueRank { get; private set; } = ValueRanks.Scalar;
    public DataValue CurrentValue { get; private set; }
    public bool WasDispatched { get; private set; }
    public CancellationToken CancellationToken => m_lifetime.Token;

    public Task LoadAsync()
    {
        lock (m_gate)
        {
            if (!m_started && !m_stopping)
            {
                m_started = true;
                m_work = LoadCoreAsync();
            }
            return m_work;
        }
    }

    public Task WriteAsync(in DataValue value)
    {
        lock (m_gate)
        {
            if (!m_stopping && State == WriteValueState.Editing && m_work.IsCompleted)
            {
                State = WriteValueState.Writing;
                WasDispatched = true;
                m_work = WriteCoreAsync(value);
            }
            return m_work;
        }
    }

    public Task CancelAndDrainAsync()
    {
        lock (m_gate)
        {
            if (m_shutdown is null)
            {
                m_stopping = true;
                if (!m_work.IsCompleted)
                {
                    State = WriteValueState.Closing;
                }
                m_shutdown = CancelCoreAsync(m_work);
            }
            return m_shutdown;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAndDrainAsync().ConfigureAwait(false);
        lock (m_gate)
        {
            if (!m_disposed)
            {
                m_disposed = true;
                m_lifetime.Dispose();
            }
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            ReadResponse response = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = m_nodeId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = m_nodeId, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = m_nodeId, AttributeId = Attributes.ValueRank }
                ], m_lifetime.Token).ConfigureAwait(false);
            m_lifetime.Token.ThrowIfCancellationRequested();
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult) || response.Results.Count != 3)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The current-value read did not return all requested attributes.");
            }
            foreach (DataValue attribute in response.Results)
            {
                if (!StatusCode.IsGood(attribute.StatusCode))
                {
                    throw new ServiceResultException(attribute.StatusCode);
                }
            }
            if (!response.Results[1].WrappedValue.TryGetValue(out NodeId dataType) ||
                dataType.IsNull ||
                !response.Results[2].WrappedValue.TryGetValue(out int valueRank))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid DataType or ValueRank.");
            }
            DataType = dataType;
            ValueRank = valueRank;
            CurrentValue = response.Results[0];
            State = WriteValueState.Editing;
        }
        catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
        {
            State = WriteValueState.Failed;
            Outcome = "Read cancelled. No write was sent.";
        }
        catch (Exception error)
        {
            State = WriteValueState.Failed;
            Outcome = $"Cannot write: metadata read failed. {error.Message}";
        }
    }

    private async Task WriteCoreAsync(DataValue value)
    {
        try
        {
            WriteResponse response = await m_session.WriteAsync(
                null,
                [new WriteValue { NodeId = m_nodeId, AttributeId = Attributes.Value, Value = value }],
                m_lifetime.Token).ConfigureAwait(false);
            StatusCode serviceStatus = response.ResponseHeader.ServiceResult;
            if (!StatusCode.IsGood(serviceStatus))
            {
                State = WriteValueState.Failed;
                Outcome = $"Write failed: {serviceStatus}";
            }
            else if (response.Results.Count != 1)
            {
                State = WriteValueState.Uncertain;
                Outcome = "Write outcome unknown: the server returned no unambiguous result. Read the value to verify.";
            }
            else
            {
                StatusCode status = response.Results[0];
                State = StatusCode.IsGood(status) ? WriteValueState.Succeeded : WriteValueState.Failed;
                Outcome = StatusCode.IsGood(status) ? $"Write succeeded: {status}" : $"Write failed: {status}";
            }
        }
        catch (Exception error)
        {
            State = WriteValueState.Uncertain;
            Outcome = "Write outcome unknown. The server may have applied the value; cancellation is not rollback. " +
                $"Read the value to verify. {error.Message}";
        }
    }

    private async Task CancelCoreAsync(Task work)
    {
        await m_lifetime.CancelAsync().ConfigureAwait(false);
        await work.ConfigureAwait(false);
    }

    private readonly NodeId m_nodeId;
    private readonly ISession m_session;
    private readonly Lock m_gate = new();
    private readonly CancellationTokenSource m_lifetime = new();
    private Task m_work = Task.CompletedTask;
    private Task? m_shutdown;
    private bool m_started;
    private bool m_stopping;
    private bool m_disposed;
}
