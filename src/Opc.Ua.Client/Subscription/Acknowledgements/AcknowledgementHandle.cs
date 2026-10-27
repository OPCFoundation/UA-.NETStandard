/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
            return ReferenceEquals(Scope, other.Scope) && SequenceNumber == other.SequenceNumber;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is AcknowledgementHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Scope == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Scope),
                SequenceNumber);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsNull ? "(null)" : $"{SubscriptionId}:{SequenceNumber}";
        }
    }
}
