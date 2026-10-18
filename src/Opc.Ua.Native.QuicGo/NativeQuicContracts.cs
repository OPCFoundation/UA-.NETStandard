// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// An opaque process-local listener handle. Zero is invalid; handles are never recycled.
    /// </summary>
    /// <param name="Value">The native identity, not a pointer.</param>
    public readonly record struct NativeQuicListenerHandle(ulong Value);

    /// <summary>
    /// An opaque process-local connection handle, independent of any application flow.
    /// </summary>
    /// <param name="Value">The native identity, not a pointer.</param>
    public readonly record struct NativeQuicConnectionHandle(ulong Value);

    /// <summary>
    /// An opaque process-local stream handle, distinct from the QUIC stream identifier.
    /// </summary>
    /// <param name="Value">The native identity, not a pointer.</param>
    public readonly record struct NativeQuicStreamHandle(ulong Value);

    /// <summary>
    /// Native operation status. Cancellation never erases accepted bytes or newly created handles.
    /// </summary>
    public enum NativeQuicStatus
    {
        /// <summary>Authenticated peer transport capabilities do not satisfy the contract.</summary>
        Capability = -12,

        /// <summary>An overlapping operation or outstanding ownership prevents this operation.</summary>
        Busy = -11,

        /// <summary>Cancellation interrupted the operation.</summary>
        Cancelled = -10,

        /// <summary>An internal native invariant failed.</summary>
        Internal = -9,

        /// <summary>The finite native deadline elapsed.</summary>
        Timeout = -8,

        /// <summary>The result buffer is too small; no result was consumed.</summary>
        BufferSmall = -7,

        /// <summary>Bounded resource admission refused the operation.</summary>
        Limit = -6,

        /// <summary>The native transport or stream closed.</summary>
        Closed = -5,

        /// <summary>The native datagram payload limit was exceeded.</summary>
        TooLarge = -4,

        /// <summary>Identity, signing capability or peer authentication was refused.</summary>
        Authentication = -3,

        /// <summary>Invalid arguments, stale handle, wrong handle type or direction.</summary>
        Invalid = -1,

        /// <summary>Completed successfully.</summary>
        Success = 0,

        /// <summary>The operation is still owned by its native worker.</summary>
        Pending = 1
    }

    /// <summary>
    /// Operations on an independently created listener.
    /// </summary>
    public enum NativeQuicListenerOperation
    {
        /// <summary>Accept a mutually authenticated connection.</summary>
        Accept = 3,

        /// <summary>Read the bound numeric endpoint as UTF8 bytes.</summary>
        Address = 4,

        /// <summary>Close and release the listener; accepted connections remain alive.</summary>
        Close = 5
    }

    /// <summary>
    /// Operations on a connection. Closing and releasing ownership are separate operations.
    /// </summary>
    public enum NativeQuicConnectionOperation
    {
        /// <summary>Get authenticated TLS and transport facts.</summary>
        Information = 10,

        /// <summary>Get a DER certificate from the actual verified chain.</summary>
        Certificate = 11,

        /// <summary>Allocate a native stream; flag one selects unidirectional.</summary>
        OpenStream = 12,

        /// <summary>Accept a native stream; flag one selects unidirectional.</summary>
        AcceptStream = 13,

        /// <summary>Queue an RFC9221 datagram; cancellation of a blocked native queue closes this connection.</summary>
        SendDatagram = 14,

        /// <summary>Receive an RFC9221 datagram without stream emulation.</summary>
        ReceiveDatagram = 15,

        /// <summary>Close this connection with the application error code.</summary>
        Close = 16,

        /// <summary>Observe the connection closure and its remote application error.</summary>
        WaitClosed = 17,

        /// <summary>Release a connection after releasing its streams and referencing operations.</summary>
        Release = 18
    }

    /// <summary>
    /// Operations on a native stream. Each direction permits one active I/O operation.
    /// </summary>
    public enum NativeQuicStreamOperation
    {
        /// <summary>
        /// Write bytes, optionally marking a reliable boundary and/or FIN.
        /// </summary>
        Send = 20,

        /// <summary>
        /// Read bytes and observe FIN or the actual received reset.
        /// </summary>
        Read = 21,

        /// <summary>
        /// Reset the send direction after any active write has completed.
        /// </summary>
        Reset = 22,

        /// <summary>
        /// Wait for actual local packetizer reset final/reliable-size evidence.
        /// </summary>
        WaitResetSent = 23,

        /// <summary>
        /// Stop the receive direction.
        /// </summary>
        Stop = 24,

        /// <summary>
        /// Observe the first send-context cancellation cause, which can be local.
        /// Use <see cref="WaitPeerStopped"/> to observe a later peer STOP after local cancellation.
        /// </summary>
        WaitStopped = 25,

        /// <summary>
        /// Get the actual native sent offset high-water, not accepted Write bytes.
        /// </summary>
        SentOffset = 26,

        /// <summary>
        /// Release both directions after releasing referencing operations.
        /// </summary>
        Release = 27,

        /// <summary>
        /// Observe actual peer reset metadata without reading or cancelling either stream direction.
        /// </summary>
        WaitResetReceived = 28,

        /// <summary>
        /// Observe the first authenticated peer STOP_SENDING independently of local send cancellation.
        /// Success reports the actual peer error and stream identity without reset metadata or payload.
        /// </summary>
        WaitPeerStopped = 29
    }

    /// <summary>
    /// A bounded immutable native request. The operation determines the meaning of flags.
    /// </summary>
    public readonly record struct NativeQuicRequest
    {
        /// <summary>
        /// Creates a request. Flag 1 is FIN for sends or unidirectional for stream allocation;
        /// flag 2 marks a complete reliable prefix before any subsequent reset.
        /// </summary>
        public NativeQuicRequest(
            TimeSpan timeout,
            ByteString payload = default,
            uint flags = 0,
            uint capacity = 0,
            ulong errorCode = 0,
            uint certificateIndex = 0,
            ulong cancellationErrorCode = 0)
        {
            if (timeout.TotalMilliseconds is < 1 or > 60000)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            if (payload.Length > 65536 ||
                capacity > 65536 ||
                errorCode >= (1UL << 62) ||
                cancellationErrorCode >= (1UL << 62))
            {
                throw new ArgumentOutOfRangeException(nameof(payload), "Native request bounds exceeded.");
            }
            Timeout = timeout;
            Payload = payload;
            Flags = flags;
            Capacity = capacity;
            ErrorCode = errorCode;
            CertificateIndex = certificateIndex;
            CancellationErrorCode = cancellationErrorCode;
        }

        /// <summary>
        /// A finite thirty-second request without payload or flags.
        /// </summary>
        public static NativeQuicRequest Default => new(TimeSpan.FromSeconds(30));

        /// <summary>
        /// Gets the native deadline, including time waiting for transport backpressure.
        /// </summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Gets caller bytes, copied synchronously by begin before its return.
        /// </summary>
        public ByteString Payload { get; }

        /// <summary>
        /// Gets the operation-specific flags.
        /// </summary>
        public uint Flags { get; }

        /// <summary>
        /// Gets the maximum read size.
        /// </summary>
        public uint Capacity { get; }

        /// <summary>
        /// Gets the QUIC application error code.
        /// </summary>
        public ulong ErrorCode { get; }

        /// <summary>
        /// Gets the index in the verified certificate chain.
        /// </summary>
        public uint CertificateIndex { get; }

        /// <summary>
        /// Gets the application code used if cancellation must interrupt the admitted native operation.
        /// </summary>
        public ulong CancellationErrorCode { get; }

        /// <summary>
        /// Gets an optional one-shot control for an application error learned after submission.
        /// Its first requested code supersedes CancellationErrorCode when signalling cancellation.
        /// Keep the control alive until the operation has drained; already delivered native cancellation
        /// cannot be overwritten, and racing successful I/O is not undone.
        /// </summary>
        public NativeQuicCancellation? Cancellation { get; init; }
    }

    /// <summary>
    /// An observed terminal result. The caller owns every nonzero returned handle,
    /// including when cancellation raced a successful operation.
    /// </summary>
    public sealed record NativeQuicCompletion
    {
        /// <summary>
        /// Gets the final status, including admission failures that did not create a job.
        /// </summary>
        public NativeQuicStatus Status { get; internal init; }

        /// <summary>
        /// Gets the underlying native status before cancellation/deadline normalization.
        /// </summary>
        public NativeQuicStatus NativeStatus { get; internal init; }

        /// <summary>
        /// Gets whether cancellation was requested before native completion.
        /// </summary>
        public bool CancellationRequested { get; internal init; }

        /// <summary>
        /// Gets actual bytes accepted by this native write/read, even on failure.
        /// </summary>
        public uint BytesTransferred { get; internal init; }

        /// <summary>
        /// Gets an independent copy of the result payload.
        /// </summary>
        public ByteString Payload { get; internal init; }

        /// <summary>
        /// Gets a newly created listener.
        /// </summary>
        public NativeQuicListenerHandle Listener { get; internal init; }

        /// <summary>
        /// Gets a newly created connection.
        /// </summary>
        public NativeQuicConnectionHandle Connection { get; internal init; }

        /// <summary>
        /// Gets a newly created stream.
        /// </summary>
        public NativeQuicStreamHandle Stream { get; internal init; }

        /// <summary>
        /// Gets the native stream identifier, including its role and direction bits.
        /// </summary>
        public ulong StreamId { get; internal init; }

        /// <summary>
        /// Gets the application error code.
        /// </summary>
        public ulong ErrorCode { get; internal init; }

        /// <summary>
        /// Gets native reset final size or, for SentOffset, the actual transmitted high-water.
        /// </summary>
        public ulong FinalSize { get; internal init; }

        /// <summary>
        /// Gets the native reset reliable size. Never derive this from leased/write bytes.
        /// </summary>
        public ulong ReliableSize { get; internal init; }

        /// <summary>
        /// Gets the native reset encoder codepoint: 0x24 for draft09, 0x04 for ordinary reset.
        /// </summary>
        public ulong ResetFrameType { get; internal init; }

        /// <summary>
        /// Gets the native payload limit reported by an oversized datagram.
        /// </summary>
        public uint MaximumDatagramPayload { get; internal init; }

        /// <summary>
        /// Gets whether the closure/reset originated at the peer.
        /// </summary>
        public bool Remote { get; internal init; }

        /// <summary>
        /// Gets the read outcome: 0=data, 1=FIN, 2=reset. Process payload bytes first.
        /// </summary>
        public uint ReadOutcome { get; internal init; }
    }

    /// <summary>
    /// Injectable generic native backend; no application flow, Session or HTTP/3 interpretation.
    /// Dispose credentials after creation completes. Release stream/connection/listener handles explicitly.
    /// </summary>
    public interface INativeQuicBackend
    {
        /// <summary>
        /// Creates a listener using only supplied identity, trust and peer policy.
        /// </summary>
        ValueTask<NativeQuicCompletion> ListenAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Connects to an independent listener; no paired endpoint or identity is generated.
        /// </summary>
        ValueTask<NativeQuicCompletion> ConnectAsync(
            NativeQuicOptions options, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an operation on a listener, draining native cancellation before returning.
        /// </summary>
        ValueTask<NativeQuicCompletion> ListenerAsync(
            NativeQuicListenerHandle listener, NativeQuicListenerOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an operation on a connection, preserving all native completion evidence.
        /// </summary>
        ValueTask<NativeQuicCompletion> ConnectionAsync(
            NativeQuicConnectionHandle connection, NativeQuicConnectionOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes stream I/O without blocking a managed thread on the native operation.
        /// </summary>
        ValueTask<NativeQuicCompletion> StreamAsync(
            NativeQuicStreamHandle stream, NativeQuicStreamOperation operation,
            NativeQuicRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads current process-wide native ownership counters.
        /// </summary>
        ValueTask<NativeQuicCounts> GetCountsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets typed verified peer/transport facts; never authorizes application-level assertions.
        /// </summary>
        ValueTask<NativeQuicPeerEvidence> GetPeerEvidenceAsync(
            NativeQuicConnectionHandle connection, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Native process-wide resource counters. Background socket cleanup can briefly lag connection closure.
    /// </summary>
    /// <param name="Operations">Outstanding operations, including unreleased terminal results.</param>
    /// <param name="Terminal">Terminal operations.</param>
    /// <param name="Listeners">Listener handles.</param>
    /// <param name="Connections">Connection handles.</param>
    /// <param name="Streams">Stream handles.</param>
    /// <param name="NativeConnections">All native peers, including bounded in-flight handshakes.</param>
    /// <param name="Transports">Owned UDP transports.</param>
    /// <param name="RetainedBytes">Conservatively reserved operation bytes.</param>
    public readonly record struct NativeQuicCounts(
        uint Operations, uint Terminal, uint Listeners, uint Connections, uint Streams,
        uint NativeConnections, uint Transports, ulong RetainedBytes);
}
