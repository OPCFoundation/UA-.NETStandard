// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        ArrayOf<string> StringTable)
    {
        /// <summary>
        /// Gets the originating Publish response's indication that further notifications
        /// are queued for this physical subscription. A Republish response does not carry
        /// this indication, so the value is <c>null</c> for republished messages.
        /// </summary>
        /// <remarks>
        /// This is response metadata, not a boundary of the acknowledgement handle.
        /// Each complete message must still be confirmed independently after delivery.
        /// A manually constructed message defaults to <c>null</c> (unknown).
        /// </remarks>
        public bool? MoreNotifications { get; init; }
    }

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
