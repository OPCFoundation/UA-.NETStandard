// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Preserves typed HTTP/3 failures without disclosing peer headers or payload in diagnostics.
    /// </summary>
    public sealed class WebTransportException : Exception
    {
        /// <summary>
        /// Creates an unspecified transport failure.
        /// </summary>
        public WebTransportException()
            : base("The native WebTransport operation failed.")
        {
        }

        /// <summary>
        /// Creates a local diagnostic failure.
        /// </summary>
        public WebTransportException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Preserves a local diagnostic cause.
        /// </summary>
        public WebTransportException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Captures the exact native typed failure.
        /// </summary>
        internal WebTransportException(in Http3Error error)
            : this()
        {
            NativeCode = error.Code;
            Scope = error.Scope;
            ProtocolCode = error.ProtocolCode;
            StreamId = error.Stream;
            Operation = error.Operation;
        }

        /// <summary>
        /// Captures an actual peer CONNECT response status.
        /// </summary>
        internal WebTransportException(uint httpStatus)
            : base("The peer rejected the WebTransport CONNECT request.")
        {
            HttpStatus = httpStatus;
        }

        /// <summary>
        /// Gets the native adapter error code.
        /// </summary>
        public uint NativeCode { get; }

        /// <summary>
        /// Gets the native error scope (operation1, stream2, session3, connection4, configuration5).
        /// </summary>
        public uint Scope { get; }

        /// <summary>
        /// Gets the actual HTTP/3 wire error, or zero for a local failure.
        /// </summary>
        public ulong ProtocolCode { get; }

        /// <summary>
        /// Gets the affected stream, or UInt64.MaxValue when absent.
        /// </summary>
        public ulong StreamId { get; } = ulong.MaxValue;

        /// <summary>
        /// Gets the failed native operation.
        /// </summary>
        public uint Operation { get; }

        /// <summary>
        /// Gets the actual non-success CONNECT status, or zero when no HTTP rejection was observed.
        /// </summary>
        public uint HttpStatus { get; }
    }
}
