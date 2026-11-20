// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Identifies the acknowledgement of one notification message of one subscription
    /// lifetime. Unlike the subscription id and sequence number on their own, a handle stays
    /// unambiguous when the server reuses a subscription id or a recreated subscription
    /// restarts its sequence numbers.
    /// </summary>
    /// <remarks>
    /// The SDK creates handles: data change and event notifications carry the handle of the
    /// message they arrived in (<c>DataValueChange.Acknowledgement</c>,
    /// <c>EventNotification.Acknowledgement</c>; classic subscriptions through
    /// <c>Subscription.TryGetAcknowledgementHandle</c>). Handles compare by value, are only
    /// valid within the process and are not serializable.
    /// </remarks>
    public readonly struct AcknowledgementHandle : IEquatable<AcknowledgementHandle>, INullable
    {
        /// <summary>
        /// Creates a handle for a message of a subscription lifetime.
        /// </summary>
        /// <param name="scope">The subscription lifetime.</param>
        /// <param name="sequenceNumber">The message's sequence number.</param>
        internal AcknowledgementHandle(AcknowledgementScope scope, uint sequenceNumber)
        {
            Scope = scope;
            SequenceNumber = sequenceNumber;
        }

        /// <summary>
        /// The null handle.
        /// </summary>
        public static AcknowledgementHandle Null => default;

        /// <summary>
        /// Gets the server-assigned id of the subscription the message belongs to.
        /// </summary>
        public uint SubscriptionId => Scope?.SubscriptionId ?? 0;

        /// <summary>
        /// Gets the sequence number of the acknowledged message.
        /// </summary>
        public uint SequenceNumber { get; }

        /// <inheritdoc/>
        public bool IsNull => Scope == null;

        /// <summary>
        /// Gets the subscription lifetime the message belongs to.
        /// </summary>
        internal AcknowledgementScope? Scope { get; }

        /// <summary>
        /// Compares two handles.
        /// </summary>
        /// <param name="left">The first handle.</param>
        /// <param name="right">The second handle.</param>
        public static bool operator ==(AcknowledgementHandle left, AcknowledgementHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two handles.
        /// </summary>
        /// <param name="left">The first handle.</param>
        /// <param name="right">The second handle.</param>
        public static bool operator !=(AcknowledgementHandle left, AcknowledgementHandle right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public bool Equals(AcknowledgementHandle other)
        {
            return SequenceNumber == other.SequenceNumber && AcknowledgementScope.Equals(Scope, other.Scope);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AcknowledgementHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Scope?.GetHashCode() ?? 0, SequenceNumber);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsNull ? "(null)" : $"{SubscriptionId}:{SequenceNumber}";
        }
    }
}
