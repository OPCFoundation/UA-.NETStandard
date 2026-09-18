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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Represents subscription state persisted by an <see cref="ISubscriptionStore"/>.
    /// </summary>
    public interface IStoredSubscription
    {
        /// <summary>
        /// Gets or sets the server identifier of the subscription.
        /// </summary>
        /// <value>The identifier retained across restoration.</value>
        uint Id { get; set; }

        /// <summary>
        /// Gets or sets whether the subscription is durable.
        /// </summary>
        /// <value><c>true</c> for a durable subscription; otherwise, <c>false</c>.</value>
        bool IsDurable { get; set; }

        /// <summary>
        /// Gets or sets the elapsed lifetime counter.
        /// </summary>
        /// <value>The number of publishing cycles consumed from the subscription's lifetime.</value>
        uint LifetimeCounter { get; set; }

        /// <summary>
        /// Gets or sets the maximum lifetime count.
        /// </summary>
        /// <value>The lifetime limit in publishing cycles.</value>
        uint MaxLifetimeCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum keepalive count.
        /// </summary>
        /// <value>The number of idle publishing cycles before a keepalive is due.</value>
        uint MaxKeepaliveCount { get; set; }

        /// <summary>
        /// Gets or sets the retransmission message capacity.
        /// </summary>
        /// <value>The stored capacity; a zero value uses the live queue's minimum capacity of one message.</value>
        uint MaxMessageCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum notifications in a single Publish response.
        /// </summary>
        /// <value>The notification limit, or zero for no subscription-specific limit.</value>
        uint MaxNotificationsPerPublish { get; set; }

        /// <summary>
        /// Gets or sets the monitored items owned by the subscription.
        /// </summary>
        /// <value>The persisted monitored-item states to restore with the subscription.</value>
        IEnumerable<IStoredMonitoredItem> MonitoredItems { get; set; }

        /// <summary>
        /// Gets or sets the subscription priority.
        /// </summary>
        /// <value>The priority used when selecting subscriptions for publication.</value>
        byte Priority { get; set; }

        /// <summary>
        /// Gets or sets the publishing interval.
        /// </summary>
        /// <value>The publishing interval in milliseconds.</value>
        double PublishingInterval { get; set; }

        /// <summary>
        /// Gets or sets retained messages that have been sent or are queued for publication.
        /// </summary>
        /// <value>The retained messages in publication order.</value>
        List<NotificationMessage> SentMessages { get; set; }

        /// <summary>
        /// Gets or sets the position of the next queued message to publish.
        /// </summary>
        /// <value>The zero-based index in <see cref="SentMessages"/> immediately after the last sent message.</value>
        int LastSentMessage { get; set; }

        /// <summary>
        /// Gets or sets the next notification sequence number.
        /// </summary>
        /// <value>The sequence number to assign to the next newly constructed notification message.</value>
        uint SequenceNumber { get; set; }

        /// <summary>
        /// Gets or sets the subscription owner's persisted user identity token.
        /// </summary>
        /// <value>The owner's token, or <c>null</c> when no token was persisted.</value>
        UserIdentityToken? UserIdentityToken { get; set; }
    }

    /// <summary>
    /// Extends persisted subscription state with publishing mode and owner application identity.
    /// </summary>
    public interface IStoredSubscriptionState : IStoredSubscription
    {
        /// <summary>
        /// Gets or sets whether publishing was enabled when the subscription was stored.
        /// </summary>
        /// <value><c>true</c> when publishing was enabled; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// Stores that did not persist this field should restore it as <c>true</c> for backward compatibility.
        /// </remarks>
        bool PublishingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the application URI of the client that owns the subscription.
        /// </summary>
        /// <value>The owner application's URI, or <c>null</c> when it was not persisted.</value>
        string? OwnerClientApplicationUri { get; set; }
    }
}
