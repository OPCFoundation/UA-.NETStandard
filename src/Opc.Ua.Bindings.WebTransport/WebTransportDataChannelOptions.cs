// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using Opc.Ua.Bindings.Native;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Selects an exact HTTP Origin policy; this is not browser authentication.
    /// </summary>
    public enum WebTransportOriginPolicy
    {
        /// <summary>
        /// Refuse any Origin header.
        /// </summary>
        AbsentOnly,

        /// <summary>
        /// Permit an absent header or the configured exact HTTPS origin.
        /// </summary>
        AbsentOrExact,

        /// <summary>
        /// Require the configured exact HTTPS origin.
        /// </summary>
        RequiredExact
    }

    /// <summary>
    /// Immutable, finite policy for one native WebTransport session per authenticated connection.
    /// Authority and path come from the explicit endpoint, never a wildcard/default resource.
    /// </summary>
    public sealed record WebTransportDataChannelOptions
    {
        /// <summary>
        /// Creates an explicit Origin policy. Required/exact policies require a canonical HTTPS origin.
        /// </summary>
        public WebTransportDataChannelOptions(WebTransportOriginPolicy originPolicy, string? origin = null)
        {
            OriginPolicy = originPolicy;
            Origin = origin;
        }

        /// <summary>
        /// Gets the explicitly selected Origin rule.
        /// </summary>
        public WebTransportOriginPolicy OriginPolicy { get; }

        /// <summary>
        /// Gets the exact HTTPS origin, or null for the absent-only policy.
        /// </summary>
        public string? Origin { get; }

        /// <summary>
        /// Gets finite native transport limits. HTTP/3 needs seven critical stream handles
        /// in addition to management and application flow handles.
        /// </summary>
        public NativeQuicConnectionSettings Native { get; init; } = new()
        {
            MaxStreams = 16,
            MaxPendingOperations = 48
        };

        /// <summary>
        /// Gets the deadline covering SETTINGS, CONNECT, management association and HELLO.
        /// </summary>
        public TimeSpan EstablishmentTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets the complete common-record capacity, sent with bounded ordered segmentation.
        /// </summary>
        public int MaxRecordSize { get; init; } = 65536;

        /// <summary>
        /// Gets the maximum simultaneous unbound physical WT streams.
        /// </summary>
        public int MaxPendingStreams { get; init; } = 8;

        /// <summary>
        /// Gets the native H3 stream/tombstone history bound, including critical streams.
        /// Exhaustion requires a new authenticated connection; IDs are never recycled.
        /// </summary>
        public int MaxStreamHistory { get; init; } = 256;

        /// <summary>
        /// Gets queued plus leased native H3 output-byte capacity.
        /// </summary>
        public int Http3OutputBytes { get; init; } = 262144;

        /// <summary>
        /// Gets the native H3 event bound, also constraining decoded header admission.
        /// </summary>
        public int Http3OutputEvents { get; init; } = 1024;

        /// <summary>
        /// Gets the native queued plus leased send-descriptor ceiling.
        /// </summary>
        public int Http3PendingSends { get; init; } = 64;

        /// <summary>
        /// Gets the initial WT receive credit. Credit is returned only after Core admission.
        /// </summary>
        public int InitialReceiveBytes { get; init; } = 262144;

        /// <summary>
        /// Gets initial WT bidirectional credit, independently of the QUIC stream limit.
        /// </summary>
        public int InitialBidirectionalStreams { get; init; } = 16;

        /// <summary>
        /// Gets the maximum additional host-owned copied payload bytes.
        /// This excludes Core's separately accounted application messages.
        /// </summary>
        public int MaxHostBufferedBytes { get; init; } = 1048576;

        /// <summary>
        /// Gets the bound on concurrently submitted application stream writes.
        /// Critical HTTP/3 writes have an independent slot to avoid starvation.
        /// </summary>
        public int ConcurrentStreamSends { get; init; } = 3;

        /// <summary>
        /// Gets an empty-queue safe input segment, including worst-case header event expansion.
        /// </summary>
        internal int InputSegmentSize => Math.Min(
            65536, Math.Min(Http3OutputBytes - 128, 2 * (Http3OutputEvents - HeaderBytes / 32 - 8) + 1));

        /// <summary>
        /// Gets a header bound that fits the configured output capacity, as required by the native ABI.
        /// </summary>
        internal int HeaderBytes => Math.Min(16384, Http3OutputBytes);

        /// <summary>
        /// Gets the maximum retained asynchronous host jobs.
        /// </summary>
        internal int MaxHostJobs => Http3PendingSends + 8 * Native.MaxStreams + 32;

        /// <summary>
        /// Gets conservative pre-admission for H3 storage, copied host payload and native operation copies.
        /// </summary>
        internal long ReservedBytes => (long)Http3OutputBytes + MaxHostBufferedBytes +
            Native.MaxPendingOperations * 131072L + 262144;

        /// <summary>
        /// Checks policy and memory/deadline bounds before opening native resources.
        /// </summary>
        internal void Validate()
        {
            if (!Enum.IsDefined(OriginPolicy) ||
                (OriginPolicy == WebTransportOriginPolicy.AbsentOnly && Origin != null) ||
                (OriginPolicy != WebTransportOriginPolicy.AbsentOnly &&
                    !WebTransportEndpoint.IsCanonicalOrigin(Origin)))
            {
                throw new ArgumentException("An explicit, canonical HTTP Origin policy is required.", nameof(Origin));
            }
            ArgumentNullException.ThrowIfNull(Native);
            if (Native.MaxStreams is < 12 or > 24 || Native.MaxPendingOperations is < 16 or > 64 ||
                Native.ReadSize is < 13 or > 65536 ||
                Native.OperationTimeout.TotalMilliseconds is < 1 or > 60000 ||
                Native.HandshakeTimeout.TotalMilliseconds is < 1 or > 60000 ||
                Native.IdleTimeout.TotalMilliseconds is < 1000 or > 600000 ||
                Native.CertificateRecheckInterval.TotalMilliseconds is < 1 or > 60000 ||
                3 * (Native.MaxStreams - 9) + ConcurrentStreamSends + 16 > Native.MaxPendingOperations ||
                EstablishmentTimeout.TotalMilliseconds is < 1 or > 60000 ||
                MaxRecordSize is < 1024 or > 16777216 ||
                MaxPendingStreams is < 1 or > 16 || MaxStreamHistory is < 9 or > 4096 ||
                Http3OutputBytes is < 4096 or > 1048576 ||
                Http3OutputEvents is < 528 or > 4096 ||
                Http3PendingSends is < 8 or > 512 ||
                InitialReceiveBytes is < 1 or > 16777216 ||
                InitialBidirectionalStreams is < 1 or > 128 ||
                ConcurrentStreamSends is < 1 or > 8 ||
                MaxHostBufferedBytes < Http3OutputBytes + Native.MaxStreams * Native.ReadSize + 65536 ||
                MaxHostBufferedBytes > 16777216)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRecordSize), "WebTransport limits must be bounded.");
            }
        }
    }
}
