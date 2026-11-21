// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Wraps a Bad <see cref="StatusCode"/> returned by an SKS
    /// endpoint or thrown while the SKS subsystem could not produce
    /// keys for the caller.
    /// </summary>
    /// <remarks>
    /// Implements the operational error contract for the SKS pull
    /// profile defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. The exception is
    /// surfaced by <see cref="ISecurityKeyService"/> implementations
    /// and by <see cref="IPubSubKeyServiceServer"/> when the server
    /// rejects a request. <see cref="Status"/> is set to the OPC UA
    /// StatusCode that caused the failure so that callers may map
    /// it onto the PubSub diagnostics counter set without parsing
    /// the message string.
    /// </remarks>
    public sealed class OpcUaSksException : Exception
    {
        /// <summary>
        /// Initializes a new <see cref="OpcUaSksException"/> with a
        /// default message and <see cref="StatusCodes.Bad"/>.
        /// </summary>
        public OpcUaSksException()
            : this(StatusCodes.Bad, "An SKS error occurred.")
        {
        }

        /// <summary>
        /// Initializes a new <see cref="OpcUaSksException"/> with a
        /// human-readable message and <see cref="StatusCodes.Bad"/>.
        /// </summary>
        /// <param name="message">Human-readable message.</param>
        public OpcUaSksException(string message)
            : this(StatusCodes.Bad, message)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="OpcUaSksException"/> with a
        /// human-readable message, an inner exception and
        /// <see cref="StatusCodes.Bad"/>.
        /// </summary>
        /// <param name="message">Human-readable message.</param>
        /// <param name="innerException">Inner exception.</param>
        public OpcUaSksException(string message, Exception? innerException)
            : this(StatusCodes.Bad, message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new <see cref="OpcUaSksException"/>.
        /// </summary>
        /// <param name="status">Causing StatusCode.</param>
        /// <param name="message">Human-readable message.</param>
        public OpcUaSksException(StatusCode status, string message)
            : base(message)
        {
            Status = status;
        }

        /// <summary>
        /// Initializes a new <see cref="OpcUaSksException"/>.
        /// </summary>
        /// <param name="status">Causing StatusCode.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="innerException">Inner exception.</param>
        public OpcUaSksException(
            StatusCode status,
            string message,
            Exception? innerException)
            : base(message, innerException)
        {
            Status = status;
        }

        /// <summary>
        /// StatusCode that caused the exception.
        /// </summary>
        public StatusCode Status { get; }
    }
}
