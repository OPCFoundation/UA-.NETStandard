// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Finite operation, stream and certificate-recheck limits shared by native QUIC and HTTP/3 hosts.
    /// </summary>
    public sealed record NativeQuicConnectionSettings
    {
        /// <summary>
        /// Gets the maximum owned streams plus pending allocation/accept reservations.
        /// Hosts must include their management, provisional and concurrent-accept capacity.
        /// </summary>
        public int MaxStreams { get; init; } = 8;

        /// <summary>
        /// Gets the maximum referencing native operations per connection, including pending reads.
        /// The backend additionally enforces its process-wide operation and retained-byte limits.
        /// </summary>
        public int MaxPendingOperations { get; init; } = 24;

        /// <summary>
        /// Gets the finite deadline for each native operation.
        /// Receive/accept timeouts may be retried; a committed write is never silently retried.
        /// </summary>
        public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets the TLS establishment deadline.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets the native idle timeout, independently of application flow deadlines.
        /// </summary>
        public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets the maximum interval between full stack certificate-policy rechecks.
        /// Change notifications and certificate expiry can terminate the connection sooner.
        /// </summary>
        public TimeSpan CertificateRecheckInterval { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets the bounded size of one native stream read.
        /// </summary>
        public int ReadSize { get; init; } = 16384;

        /// <summary>
        /// Checks all bounds before native resource admission.
        /// </summary>
        internal void Validate()
        {
            if (MaxStreams is < 1 or > 32 || MaxPendingOperations is < 4 or > 64 ||
                ReadSize is < 13 or > 65536 ||
                OperationTimeout.TotalMilliseconds is < 1 or > 60000 ||
                HandshakeTimeout.TotalMilliseconds is < 1 or > 60000 ||
                IdleTimeout.TotalMilliseconds is < 1000 or > 600000 ||
                CertificateRecheckInterval.TotalMilliseconds is < 1 or > 60000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxStreams), "Native limits must be finite and bounded.");
            }
        }
    }

    /// <summary>
    /// Preserves the actual terminal native result, including accepted bytes and racing cancellation evidence.
    /// </summary>
    public sealed class NativeQuicTransportException : Exception
    {
        /// <summary>
        /// Creates a generic native transport failure.
        /// </summary>
        public NativeQuicTransportException()
            : base("The native QUIC operation failed.")
        {
        }

        /// <summary>
        /// Creates a local native transport diagnostic.
        /// </summary>
        public NativeQuicTransportException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Preserves the cause of a local native transport failure.
        /// </summary>
        public NativeQuicTransportException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Preserves a terminal backend outcome without including endpoint or credential data in the message.
        /// </summary>
        public NativeQuicTransportException(NativeQuicCompletion completion)
            : this()
        {
            Completion = completion ?? throw new ArgumentNullException(nameof(completion));
        }

        /// <summary>
        /// Gets the actual native completion when a native operation was admitted.
        /// </summary>
        public NativeQuicCompletion? Completion { get; }
    }
}
