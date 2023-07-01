/* Copyright (c) 1996-2023 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

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
        public DateTime PublishTime { get; set; }
    }
}
