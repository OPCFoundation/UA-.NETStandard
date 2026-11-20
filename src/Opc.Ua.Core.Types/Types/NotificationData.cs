// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    public partial class NotificationData
    {
        /// <summary>
        /// Helper variable for a client to pass the sequence number
        /// of the publish response for the data and the event change notification
        /// to a client application which subscribes to subscription notifications.
        /// </summary>
        /// <remarks>
        /// A value of 0 indicates that the sequence number is not known.
        /// A KeepAlive notification contains the sequence number of the next
        /// notification.
        /// </remarks>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// Helper variable for a client to pass the publish time
        /// of the publish response for the data and the event change notification
        /// to a client application which subscribes to subscription notifications.
        /// </summary>
        /// <remarks>
        /// A value of MinTime indicates that the time is not known.
        /// </remarks>
        public DateTimeUtc PublishTime { get; set; }

        /// <summary>
        /// Helper variable for a client to pass the information that more
        /// notifications are expected for this publish interval.
        /// </summary>
        public bool MoreNotifications { get; set; }
    }
}
