/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Processes one complete OPC UA notification message rather than separate payload callbacks.
    /// Returning does not confirm delivery. In confirmation mode, confirm
    /// <see cref="SubscriptionNotificationMessage.Acknowledgement"/> only after every payload
    /// was successfully processed downstream. Keep-alives and subscription state changes
    /// continue through <see cref="ISubscriptionNotificationHandler"/> when implemented.
    /// </summary>
    public interface ISubscriptionNotificationMessageHandler
    {
        /// <summary>
        /// Processes all payloads of one message, in their original wire order.
        /// </summary>
        /// <param name="message">The complete message, safe to retain after the call.</param>
        /// <param name="ct">Cancelled when notification processing or the subscription shuts down.</param>
        ValueTask OnNotificationMessageAsync(
            SubscriptionNotificationMessage message,
            CancellationToken ct = default);
    }

    /// <summary>
    /// A complete notification message of one physical subscription partition.
    /// Its payload arrays do not refer to pooled notification containers.
    /// This is not a group of Publish responses indicated by MoreNotifications.
    /// </summary>
    /// <param name="Acknowledgement">The single acknowledgement of the whole message.</param>
    /// <param name="PublishTime">The server's publishing timestamp.</param>
    /// <param name="Notifications">The payloads in their original wire order.</param>
    /// <param name="StringTable">The response's diagnostic string table.</param>
    public readonly record struct SubscriptionNotificationMessage(
        AcknowledgementHandle Acknowledgement,
        DateTime PublishTime,
        ArrayOf<SubscriptionNotificationPayload> Notifications,
        ArrayOf<string> StringTable);

    /// <summary>
    /// The kind of a notification payload.
    /// </summary>
    public enum SubscriptionNotificationKind
    {
        /// <summary>
        /// Data changes.
        /// </summary>
        DataChange,

        /// <summary>
        /// Events.
        /// </summary>
        Event,

        /// <summary>
        /// A subscription status change.
        /// </summary>
        StatusChange
    }

    /// <summary>
    /// One payload in a complete message. Only the fields corresponding to
    /// <see cref="Kind"/> are populated.
    /// </summary>
    /// <param name="Kind">The payload kind.</param>
    /// <param name="DataChanges">Data changes for a data-change payload.</param>
    /// <param name="Events">Events for an event payload.</param>
    /// <param name="Status">The status of a status-change payload.</param>
    /// <param name="DiagnosticInfo">Diagnostics for a status-change payload.</param>
    public readonly record struct SubscriptionNotificationPayload(
        SubscriptionNotificationKind Kind,
        ArrayOf<DataValueChange> DataChanges,
        ArrayOf<EventNotification> Events,
        StatusCode Status,
        DiagnosticInfo? DiagnosticInfo);
}
