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
 *
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
using System.Globalization;
using System.Threading;

namespace Opc.Ua.Client
{
    /// <summary>
    /// How the session came to own the subscription that produced a notification.
    /// </summary>
    public enum NotificationProvenance
    {
        /// <summary>
        /// The subscription was created by this session.
        /// </summary>
        Created,

        /// <summary>
        /// The subscription was transferred into this session.
        /// </summary>
        Transferred,

        /// <summary>
        /// The session received the notification for a subscription it neither created nor
        /// transferred itself, for example one restored from saved state.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Why a notification reached the end of its acknowledgement lifetime.
    /// </summary>
    public enum NotificationDisposition
    {
        /// <summary>
        /// The server confirmed the acknowledgement.
        /// </summary>
        Acknowledged,

        /// <summary>
        /// The server no longer holds the message, for example because its retransmission queue
        /// evicted it or the server has no retransmission queue.
        /// </summary>
        NotRetained,

        /// <summary>
        /// The subscription lifetime ended before the message was acknowledged: the subscription
        /// was deleted or recreated, the server reused its id, or the session ended without
        /// transferring it.
        /// </summary>
        OwnerRetired,

        /// <summary>
        /// The client stopped acknowledging the message while its subscription lived, because the
        /// acknowledgement policy dropped it or the server answered with a result count that does not
        /// match the request.
        /// </summary>
        Abandoned
    }

    /// <summary>
    /// Identifies one notification message of one subscription lifetime in this process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server can reuse a subscription id and restarts sequence numbers for every new
    /// subscription, so <c>(subscription id, sequence number)</c> alone does not identify a
    /// message over time. A lifetime also carries the session scope and the subscription
    /// lifetime the message was received in. It survives an in-place session recovery, including
    /// a recreate that transfers the subscription, and is invalidated when the subscription is
    /// deleted or recreated, or its id is reused. A session cloned for recreation (for example by
    /// <see cref="Session.RecreateAsync(CancellationToken)"/>) starts lifetimes of its own.
    /// </para>
    /// <para>
    /// Lifetimes are process-local values: they are not serializable, are not part of saved
    /// session or subscription state, and do not survive a fail-over to another process.
    /// </para>
    /// </remarks>
    public readonly struct NotificationLifetime : IEquatable<NotificationLifetime>
    {
        /// <summary>
        /// Creates a lifetime.
        /// </summary>
        internal NotificationLifetime(
            long sessionScope,
            long subscriptionLifetime,
            NotificationProvenance provenance,
            uint subscriptionId,
            uint sequenceNumber)
        {
            SessionScope = sessionScope;
            SubscriptionLifetime = subscriptionLifetime;
            Provenance = provenance;
            SubscriptionId = subscriptionId;
            SequenceNumber = sequenceNumber;
        }

        /// <summary>
        /// Gets the server-assigned subscription id.
        /// </summary>
        public uint SubscriptionId { get; }

        /// <summary>
        /// Gets the sequence number of the notification message.
        /// </summary>
        public uint SequenceNumber { get; }

        /// <summary>
        /// Gets how the session came to own the subscription.
        /// </summary>
        public NotificationProvenance Provenance { get; }

        /// <summary>
        /// Gets whether this is the default, empty lifetime.
        /// </summary>
        public bool IsNull => SessionScope == 0;

        /// <summary>
        /// Gets the process-local scope of the session that received the message.
        /// </summary>
        internal long SessionScope { get; }

        /// <summary>
        /// Gets the process-local lifetime of the subscription the message was received in.
        /// </summary>
        internal long SubscriptionLifetime { get; }

        /// <inheritdoc/>
        public bool Equals(NotificationLifetime other)
        {
            return SessionScope == other.SessionScope &&
                SubscriptionLifetime == other.SubscriptionLifetime &&
                SubscriptionId == other.SubscriptionId &&
                SequenceNumber == other.SequenceNumber &&
                Provenance == other.Provenance;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is NotificationLifetime other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(SessionScope, SubscriptionLifetime, SubscriptionId, SequenceNumber);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsNull
                ? "(null)"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}#{2}@{3}/{4}",
                    SubscriptionId,
                    SequenceNumber,
                    SubscriptionLifetime,
                    SessionScope,
                    Provenance);
        }

        /// <summary>
        /// Compares two lifetimes.
        /// </summary>
        public static bool operator ==(NotificationLifetime left, NotificationLifetime right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two lifetimes.
        /// </summary>
        public static bool operator !=(NotificationLifetime left, NotificationLifetime right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The end of the acknowledgement lifetime of one notification.
    /// </summary>
    /// <param name="Lifetime">The notification lifetime.</param>
    /// <param name="Disposition">Why the lifetime ended.</param>
    public readonly record struct NotificationTermination(
        NotificationLifetime Lifetime,
        NotificationDisposition Disposition);

    /// <summary>
    /// Reports notifications that reached the end of their acknowledgement lifetime.
    /// </summary>
    public sealed class NotificationsTerminatedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the event arguments.
        /// </summary>
        /// <param name="terminations">The ended lifetimes.</param>
        public NotificationsTerminatedEventArgs(ArrayOf<NotificationTermination> terminations)
        {
            Terminations = terminations;
        }

        /// <summary>
        /// Gets the ended lifetimes.
        /// </summary>
        public ArrayOf<NotificationTermination> Terminations { get; }
    }

    /// <summary>
    /// Optional subscription capability that identifies a received notification message.
    /// </summary>
    public interface INotificationLifetimeSource
    {
        /// <summary>
        /// Gets the lifetime of a notification message this subscription received. Call it
        /// synchronously from the notification callback, before handing the work to another
        /// task: the lifetime is stamped when the message is received, so a later recreation of
        /// the subscription does not change it.
        /// </summary>
        /// <param name="sequenceNumber">The sequence number of the message.</param>
        /// <param name="lifetime">The lifetime, when known.</param>
        /// <returns><c>false</c> when the message is unknown or no longer being delivered.</returns>
        bool TryGetNotificationLifetime(uint sequenceNumber, out NotificationLifetime lifetime);
    }

    /// <summary>
    /// Optional session capability that reports the end of notification acknowledgement lifetimes.
    /// </summary>
    public interface INotificationLifetimeEvents
    {
        /// <summary>
        /// Raised when notifications reached the end of their acknowledgement lifetime, whether or
        /// not the application approved them. Every lifetime is normally reported once. Raised
        /// outside the session and engine locks; handlers must be short and must not wait for
        /// session or subscription operations.
        /// </summary>
        event EventHandler<NotificationsTerminatedEventArgs>? NotificationsTerminated;
    }
}
