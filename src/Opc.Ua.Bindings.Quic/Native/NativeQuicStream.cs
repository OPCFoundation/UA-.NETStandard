// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Owns a real native stream and serializes each I/O direction independently.
    /// Reset waits for actual committed writes, and release waits for every referencing operation.
    /// </summary>
    public sealed class NativeQuicStream : IAsyncDisposable
    {
        /// <summary>
        /// Adopts an actual stream returned by this connection's allocator or acceptor.
        /// </summary>
        internal NativeQuicStream(
            NativeQuicConnection connection,
            NativeQuicStreamHandle handle,
            ulong streamId)
        {
            Connection = connection;
            Handle = handle;
            StreamId = streamId;
            Closed = m_shutdown.Token;
        }

        /// <summary>
        /// Gets the owning authenticated connection; equal numeric IDs from another owner are unrelated.
        /// </summary>
        public NativeQuicConnection Connection { get; }

        /// <summary>
        /// Gets a borrowed typed native handle; consumers must not release it independently.
        /// </summary>
        public NativeQuicStreamHandle Handle { get; }

        /// <summary>
        /// Gets the actual QUIC stream identifier, not an application-assigned sequence.
        /// </summary>
        public ulong StreamId { get; }

        /// <summary>
        /// Gets whether the native stream is bidirectional.
        /// </summary>
        public bool IsBidirectional => (StreamId & 2) == 0;

        /// <summary>
        /// Gets whether the actual native transport role created this stream.
        /// </summary>
        public bool IsLocal => (StreamId & 1) == (Connection.IsClient ? 0UL : 1UL);

        /// <summary>
        /// Gets whether admission to this stream has closed.
        /// </summary>
        public bool IsClosed => Volatile.Read(ref m_closing);

        /// <summary>
        /// Gets directional-operation cancellation when this flow is released.
        /// </summary>
        public CancellationToken Closed { get; }

        /// <summary>
        /// Reads one bounded segment and preserves payload preceding FIN/reset in the same native result.
        /// </summary>
        public async ValueTask<NativeQuicCompletion> ReadAsync(CancellationToken cancellationToken = default)
        {
            using Operation streamOperation = EnterOperation();
            using NativeQuicConnection.Operation operation = Connection.EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Connection.Closed, Closed);
            await m_reader.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(IsClosed, this);
                return await Connection.Backend.StreamAsync(
                    Handle, NativeQuicStreamOperation.Read,
                    new NativeQuicRequest(Connection.Settings.OperationTimeout,
                        capacity: checked((uint)Connection.Settings.ReadSize)),
                    linked.Token).ConfigureAwait(false);
            }
            finally
            {
                m_reader.Release();
            }
        }

        /// <summary>
        /// Writes one bounded segment. A false callback returns null with no native admission.
        /// After commitment, the payload stays borrowed until the actual completion despite caller cancellation.
        /// Flags are native FIN (1) and reliable-boundary (2), not invented framing.
        /// </summary>
        public async ValueTask<NativeQuicCompletion?> SendAsync(
            ByteString payload,
            uint flags = 0,
            Func<bool>? tryCommit = null,
            CancellationToken cancellationToken = default)
        {
            if (payload.Length > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            using Operation streamOperation = EnterOperation();
            using NativeQuicConnection.Operation operation = Connection.EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Connection.Closed, Closed);
            await m_writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(IsClosed, this);
                linked.Token.ThrowIfCancellationRequested();
                var cancellation = new NativeQuicCancellation();
                lock (m_gate)
                {
                    if (m_resetting)
                    {
                        cancellation.Dispose();
                        throw new InvalidOperationException("The native stream's send direction was reset.");
                    }
                    m_activeSend = cancellation;
                }
                try
                {
                    var request = new NativeQuicRequest(Connection.Settings.OperationTimeout, payload, flags)
                    {
                        Cancellation = cancellation
                    };
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (tryCommit != null && !tryCommit())
                    {
                        return null;
                    }
                    NativeQuicCompletion sent = await Connection.Backend.StreamAsync(
                        Handle, NativeQuicStreamOperation.Send, request,
                        tryCommit == null ? linked.Token : CancellationToken.None).ConfigureAwait(false);
                    if ((flags & 1) != 0 && sent.NativeStatus == NativeQuicStatus.Success &&
                        sent.BytesTransferred == payload.Length)
                    {
                        lock (m_gate)
                        {
                            m_sendFinished = true;
                        }
                    }
                    return sent;
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_activeSend?.Dispose();
                        m_activeSend = null;
                    }
                }
            }
            finally
            {
                m_writer.Release();
            }
        }

        /// <summary>
        /// Interrupts an active native write with the exact late application error before waiting for its drainage.
        /// Only the send direction is reset; received data and the independent receive half remain valid.
        /// A racing completed send is still returned to its original caller before the ordinary native reset.
        /// </summary>
        public async ValueTask<NativeQuicCompletion> ResetSendAsync(ulong errorCode)
        {
            if (errorCode >= (1UL << 62))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }
            Task<NativeQuicCompletion> completion;
            Task signal = Task.CompletedTask;
            bool start = false;
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closing, this);
                if (m_reset == null)
                {
                    m_resetting = true;
                    m_sendFinished = false;
                    m_reset = new TaskCompletionSource<NativeQuicCompletion>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    signal = m_activeSend?.CancelAsync(errorCode) ?? Task.CompletedTask;
                    start = true;
                }
                completion = m_reset.Task;
            }
            if (start)
            {
                _ = ResetSendCoreAsync(errorCode, signal);
            }
            return await completion.ConfigureAwait(false);
        }

        /// <summary>
        /// Sends STOP_SENDING for the receive direction without closing the independent send direction.
        /// </summary>
        public ValueTask<NativeQuicCompletion> StopReceiveAsync(
            ulong errorCode,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(NativeQuicStreamOperation.Stop, errorCode, cancellationToken);
        }

        /// <summary>
        /// Observes actual LOCAL reset packetizer final/reliable sizes, frame code and application error.
        /// A received peer reset or a sum of accepted writes is not a substitute for this evidence.
        /// </summary>
        public ValueTask<NativeQuicCompletion> WaitResetSentAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(NativeQuicStreamOperation.WaitResetSent, 0, cancellationToken);
        }

        /// <summary>
        /// Observes an actual peer RESET independently of application reads and their backpressure.
        /// The operation preserves queued input and reports the native peer error and final/reliable sizes.
        /// </summary>
        public ValueTask<NativeQuicCompletion> WaitResetReceivedAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(NativeQuicStreamOperation.WaitResetReceived, 0, cancellationToken);
        }

        /// <summary>
        /// Observes an authenticated peer STOP_SENDING independently of pending writes or an earlier local reset.
        /// </summary>
        public ValueTask<NativeQuicCompletion> WaitStoppedAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(NativeQuicStreamOperation.WaitPeerStopped, 0, cancellationToken);
        }

        /// <summary>
        /// Reads the actual transmitted high-water for diagnostics, not as a replacement for reset final size.
        /// </summary>
        public ValueTask<NativeQuicCompletion> GetSentOffsetAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(NativeQuicStreamOperation.SentOffset, 0, cancellationToken);
        }

        /// <summary>
        /// Stops input, waits for writes to finish, resets output and releases only this stream.
        /// </summary>
        public async ValueTask CloseAsync(ulong errorCode = 0)
        {
            if (errorCode >= (1UL << 62))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode));
            }
            bool start;
            Task signal = Task.CompletedTask;
            lock (m_gate)
            {
                start = !m_closing;
                m_closing = true;
                if (start)
                {
                    signal = m_activeSend?.CancelAsync(errorCode) ?? Task.CompletedTask;
                }
            }
            if (start)
            {
                m_shutdown.Cancel();
                _ = CloseCoreAsync(errorCode, signal);
            }
            await m_completion.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        /// <summary>
        /// Drains I/O gates before resetting or releasing native ownership.
        /// </summary>
        private async Task CloseCoreAsync(ulong errorCode, Task signal)
        {
            try
            {
                await signal.ConfigureAwait(false);
                if (!Connection.IsClosed && (IsBidirectional || !IsLocal))
                {
                    NativeQuicCompletion stopped = await NativeQuicConnection.DrainCleanupAdmissionAsync(
                        () => Connection.Backend.StreamAsync(
                            Handle, NativeQuicStreamOperation.Stop,
                            new NativeQuicRequest(Connection.Settings.OperationTimeout, errorCode: errorCode)),
                        Connection.Settings.OperationTimeout).ConfigureAwait(false);
                    NativeQuicConnection.RequireCleanup(stopped);
                }
                Task idle;
                lock (m_gate)
                {
                    idle = m_activeOperations == 0 ? Task.CompletedTask : m_idle.Task;
                }
                await idle.ConfigureAwait(false);
                bool graceful;
                lock (m_gate)
                {
                    graceful = m_sendFinished && !m_resetting;
                }
                if (!Connection.IsClosed && !graceful && (IsBidirectional || IsLocal))
                {
                    NativeQuicCompletion reset = await NativeQuicConnection.DrainCleanupAdmissionAsync(
                        () => Connection.Backend.StreamAsync(
                            Handle, NativeQuicStreamOperation.Reset,
                            new NativeQuicRequest(Connection.Settings.OperationTimeout, errorCode: errorCode)),
                        Connection.Settings.OperationTimeout).ConfigureAwait(false);
                    NativeQuicConnection.RequireCleanup(reset);
                }
                NativeQuicCompletion released = await NativeQuicConnection.DrainCleanupAdmissionAsync(
                    () => Connection.Backend.StreamAsync(
                        Handle, graceful ? NativeQuicStreamOperation.ReleaseGracefully : NativeQuicStreamOperation.Release,
                        new NativeQuicRequest(Connection.Settings.OperationTimeout)),
                    Connection.Settings.OperationTimeout).ConfigureAwait(false);
                NativeQuicConnection.RequireSuccess(released);
                Connection.ForgetStream(this);
                m_activeSend?.Dispose();
                m_activeSend = null;
                m_reader.Dispose();
                m_writer.Dispose();
                m_shutdown.Dispose();
                m_completion.TrySetResult();
            }
            catch (Exception exception)
            {
                Connection.RequestClose(1, false);
                m_completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Completes an explicit reset after the already-signalled native write has returned its actual result.
        /// </summary>
        private async Task ResetSendCoreAsync(ulong errorCode, Task signal)
        {
            try
            {
                using Operation streamOperation = EnterOperation();
                using NativeQuicConnection.Operation operation = Connection.EnterOperation();
                await signal.ConfigureAwait(false);
                await m_writer.WaitAsync().ConfigureAwait(false);
                try
                {
                    NativeQuicCompletion result = await Connection.Backend.StreamAsync(
                        Handle, NativeQuicStreamOperation.Reset,
                        new NativeQuicRequest(Connection.Settings.OperationTimeout, errorCode: errorCode))
                        .ConfigureAwait(false);
                    m_reset!.TrySetResult(result);
                }
                finally
                {
                    m_writer.Release();
                }
            }
            catch (Exception exception)
            {
                m_reset!.TrySetException(exception);
            }
        }

        /// <summary>
        /// Counts and drains a directional operation without taking the independent stream reader/writer gate.
        /// </summary>
        private async ValueTask<NativeQuicCompletion> ExecuteAsync(
            NativeQuicStreamOperation kind,
            ulong errorCode,
            CancellationToken cancellationToken)
        {
            using Operation streamOperation = EnterOperation();
            using NativeQuicConnection.Operation operation = Connection.EnterOperation();
            ObjectDisposedException.ThrowIf(IsClosed, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Connection.Closed, Closed);
            return await Connection.Backend.StreamAsync(
                Handle, kind, new NativeQuicRequest(Connection.Settings.OperationTimeout, errorCode: errorCode),
                linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Counts all stream methods, including gate waiters, so disposal cannot race a late gate release.
        /// </summary>
        private Operation EnterOperation()
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_closing, this);
                if (m_activeOperations++ == 0)
                {
                    m_idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return new Operation(LeaveOperation);
            }
        }

        /// <summary>
        /// Releases method lifetime after native completion and all directional gate releases.
        /// </summary>
        private void LeaveOperation()
        {
            lock (m_gate)
            {
                if (--m_activeOperations == 0)
                {
                    m_idle.TrySetResult();
                }
            }
        }

        /// <summary>
        /// Owns one method-lifetime count, not the stream or an exposed lock.
        /// </summary>
        private sealed class Operation(Action release) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = release;
        }

        private readonly Lock m_gate = new();
        private readonly SemaphoreSlim m_reader = new(1, 1);
        private readonly SemaphoreSlim m_writer = new(1, 1);
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly TaskCompletionSource m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource m_idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<NativeQuicCompletion>? m_reset;
        private NativeQuicCancellation? m_activeSend;
        private bool m_closing;
        private bool m_resetting;

        /// <summary>Whether FIN completed without an explicit send reset.</summary>
        private bool m_sendFinished;
        private int m_activeOperations;
    }
}
