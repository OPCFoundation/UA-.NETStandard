/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
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

namespace Opc.Ua
{
    /// <summary>
    /// A list of event field values returned in a NotificationMessage.
    /// </summary>
    public partial class EventFieldList
    {
        /// <summary>
        /// The handle cast to a notification message.
        /// </summary>
        public NotificationMessage Message
        {
            get => Handle as NotificationMessage; set => Handle = value;
        }

        /// <summary>
        /// A handle associated withe the event instance.
        /// </summary>
        public object Handle { get; set; }
    }
}
