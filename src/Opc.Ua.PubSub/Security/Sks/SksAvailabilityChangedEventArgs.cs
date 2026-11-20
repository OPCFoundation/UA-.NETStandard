// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Event payload raised by an <see cref="ISecurityKeyService"/>
    /// when its connectivity to the underlying SKS endpoint changes.
    /// Subscribers use it to drive WriterGroup / ReaderGroup state
    /// (PreOperational while the SKS is unreachable).
    /// </summary>
    /// <remarks>
    /// Implements the operational notification described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>: when the SKS is
    /// unavailable, components must enter <c>PreOperational</c>
    /// rather than publish unsecured messages.
    /// </remarks>
    public sealed class SksAvailabilityChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="SksAvailabilityChangedEventArgs"/>.
        /// </summary>
        /// <param name="isAvailable">
        /// <see langword="true"/> when the SKS is reachable and
        /// returning Good results.
        /// </param>
        /// <param name="status">
        /// StatusCode describing the most recent transition.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable reason. Sensitive values must
        /// never be passed here.
        /// </param>
        public SksAvailabilityChangedEventArgs(
            bool isAvailable,
            StatusCode status,
            string? reason)
        {
            IsAvailable = isAvailable;
            Status = status;
            Reason = reason;
        }

        /// <summary>
        /// <see langword="true"/> when the SKS is reachable and
        /// returning <c>Good</c> results.
        /// </summary>
        public bool IsAvailable { get; }

        /// <summary>
        /// StatusCode describing the most recent transition.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Optional human-readable reason for this transition.
        /// </summary>
        public string? Reason { get; }
    }
}
