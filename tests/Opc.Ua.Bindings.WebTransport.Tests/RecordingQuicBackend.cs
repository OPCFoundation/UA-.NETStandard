// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Observes real backend operations; optional test hooks delay reads or alter submitted negative-case bytes.
    /// It never manufactures transport completions, datagrams, TLS facts, stream IDs or reset evidence.
    /// </summary>
    internal sealed class RecordingQuicBackend(INativeQuicBackend backend) : INativeQuicBackend
    {
        /// <summary>
        /// Gets actual native stream allocations/accepts.
        /// </summary>
        internal ConcurrentDictionary<ulong, ulong> Streams { get; } = new();

        /// <summary>
        /// Gets actual outbound stream payloads/flags.
        /// </summary>
        internal ConcurrentQueue<RecordedStreamWrite> Writes { get; } = new();

        /// <summary>
        /// Gets actual outbound native datagrams.
        /// </summary>
        internal ConcurrentQueue<ByteString> Datagrams { get; } = new();

        /// <summary>
        /// Gets actual locally transmitted reset observations.
        /// </summary>
        internal ConcurrentQueue<NativeQuicCompletion> Resets { get; } = new();

        /// <summary>
        /// Gets actual completed body writes, including accepted byte counts on cancellation.
        /// </summary>
        internal ConcurrentQueue<NativeQuicCompletion> SendResults { get; } = new();

        /// <summary>
        /// Gets actual connection closure observations.
        /// </summary>
        internal ConcurrentQueue<NativeQuicCompletion> Closures { get; } = new();

        /// <summary>
        /// Gets actual independent peer-reset observations.
        /// </summary>
        internal ConcurrentQueue<NativeQuicCompletion> ReceivedResets { get; } = new();

        /// <summary>
        /// Gets actual peer STOP observations, not inferred from reset or read results.
        /// </summary>
        internal ConcurrentQueue<NativeQuicCompletion> PeerStops { get; } = new();

        /// <summary>
        /// Gets or sets a bounded test scheduling hook before a real native operation.
        /// </summary>
        internal Func<ulong, NativeQuicStreamOperation, CancellationToken, ValueTask>? BeforeOperation { get; set; }

        /// <summary>
        /// Gets or sets an explicit real-byte mutation for a negative native protocol test.
        /// </summary>
        internal Func<ulong, NativeQuicRequest, NativeQuicRequest>? MutateSend { get; set; }

        /// <summary>
        /// Gets or sets a bounded pre-read wait, leaving actual native flow control untouched.
        /// </summary>
        internal Func<ulong, CancellationToken, ValueTask>? BeforeRead { get; set; }

        /// <summary>
        /// Gets or sets a notification immediately before real native send submission.
        /// </summary>
        internal Action<ulong, NativeQuicRequest>? Sending { get; set; }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ListenAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default)
        {
            return backend.ListenAsync(options, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ConnectAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default)
        {
            return backend.ConnectAsync(options, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCompletion> ListenerAsync(
            NativeQuicListenerHandle listener, NativeQuicListenerOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            return backend.ListenerAsync(listener, operation, request, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<NativeQuicCompletion> ConnectionAsync(
            NativeQuicConnectionHandle connection, NativeQuicConnectionOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            if (operation == NativeQuicConnectionOperation.SendDatagram)
            {
                Retain(request.Payload.Length);
                Datagrams.Enqueue(request.Payload.Copy());
            }
            NativeQuicCompletion actual = await backend.ConnectionAsync(
                connection, operation, request, cancellationToken)
                .ConfigureAwait(false);
            if (actual.Stream.Value != 0)
            {
                Streams.TryAdd(actual.Stream.Value, actual.StreamId);
            }
            if (operation == NativeQuicConnectionOperation.WaitClosed)
            {
                Closures.Enqueue(actual);
            }
            return actual;
        }

        /// <inheritdoc/>
        public async ValueTask<NativeQuicCompletion> StreamAsync(
            NativeQuicStreamHandle stream, NativeQuicStreamOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default)
        {
            ulong id = Streams[stream.Value];
            if (BeforeOperation != null)
            {
                await BeforeOperation(id, operation, cancellationToken).ConfigureAwait(false);
            }
            if (operation == NativeQuicStreamOperation.Read && BeforeRead != null)
            {
                await BeforeRead(id, cancellationToken).ConfigureAwait(false);
            }
            if (operation == NativeQuicStreamOperation.Send)
            {
                if (MutateSend != null)
                {
                    request = MutateSend(id, request);
                }
                Retain(request.Payload.Length);
                Writes.Enqueue(new RecordedStreamWrite(id, request.Flags, request.Payload.Copy()));
                Sending?.Invoke(id, request);
            }
            NativeQuicCompletion actual = await backend.StreamAsync(stream, operation, request, cancellationToken)
                .ConfigureAwait(false);
            if (operation == NativeQuicStreamOperation.WaitResetSent)
            {
                Resets.Enqueue(actual);
            }
            else if (operation == NativeQuicStreamOperation.Send)
            {
                SendResults.Enqueue(actual);
            }
            else if (operation == NativeQuicStreamOperation.WaitResetReceived)
            {
                ReceivedResets.Enqueue(actual);
            }
            else if (operation is NativeQuicStreamOperation.WaitStopped or
                NativeQuicStreamOperation.WaitPeerStopped && actual.Remote)
            {
                PeerStops.Enqueue(actual);
            }
            return actual;
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicCounts> GetCountsAsync(CancellationToken cancellationToken = default)
        {
            return backend.GetCountsAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NativeQuicPeerEvidence> GetPeerEvidenceAsync(
            NativeQuicConnectionHandle connection, CancellationToken cancellationToken = default)
        {
            return backend.GetPeerEvidenceAsync(connection, cancellationToken);
        }

        /// <summary>
        /// Prevents test observations from becoming an unbounded packet/payload archive.
        /// </summary>
        private void Retain(int length)
        {
            if (Interlocked.Add(ref m_retained, length) > 4194304 || Interlocked.Increment(ref m_records) > 4096)
            {
                throw new InvalidOperationException("The bounded native observation budget was exceeded.");
            }
        }

        private int m_retained;
        private int m_records;
    }

    /// <summary>
    /// Exact actual native stream submission, before any transport result is inferred.
    /// </summary>
    internal readonly record struct RecordedStreamWrite(ulong StreamId, uint Flags, ByteString Payload);
}
