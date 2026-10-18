// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Local bounds for the optional native QUIC carrier, separate from common flow negotiation limits.
    /// </summary>
    public sealed record QuicDataChannelOptions
    {
        /// <summary>
        /// Gets the native connection's finite stream, operation and certificate-policy limits.
        /// </summary>
        public NativeQuicConnectionSettings Native { get; init; } = new();

        /// <summary>
        /// Gets optional shared application accounting for conservatively reserved native/host capacity.
        /// The factory uses its injected application budget when this is omitted.
        /// </summary>
        public DataChannelApplicationBudget? ApplicationBudget { get; init; }

        /// <summary>
        /// Gets the management/HELLO and unbound-stream timeout.
        /// The common runtime independently enforces its OPEN/READY deadline.
        /// </summary>
        public TimeSpan EstablishmentTimeout { get; init; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets the maximum simultaneously accepted but unbound native streams.
        /// </summary>
        public int MaxPendingStreams { get; init; } = 4;

        /// <summary>
        /// Gets the finite stream-association history; exhaustion requires a fresh connection.
        /// </summary>
        public int MaxStreamHistory { get; init; } = 4096;

        /// <summary>
        /// Gets the maximum complete common record submitted in one bounded native operation.
        /// </summary>
        public int MaxRecordSize { get; init; } = 65536;

        /// <summary>
        /// Rejects unsupported or unbounded local settings before native admission.
        /// </summary>
        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Native);
            Native.Validate();
            if (Native.MaxStreams < 4 || Native.MaxPendingOperations < 11 ||
                EstablishmentTimeout.TotalMilliseconds is < 1 or > 60000 ||
                MaxPendingStreams < 1 || MaxPendingStreams > Native.MaxStreams ||
                MaxStreamHistory < Native.MaxStreams || MaxStreamHistory > 65536 ||
                MaxRecordSize is < 1024 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxPendingStreams), "QUIC flow bounds are invalid.");
            }
        }
    }
}
