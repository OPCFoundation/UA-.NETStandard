/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Reflection;
using System.Collections.Generic;

namespace Opc.Ua.Server
{    
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// An interface to an object monitors for events and reports them when they occur.
    /// </summary>
    [Obsolete("The IEventSource interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IEventSource
    {
        /// <summary>
        /// Subscribes/unsubscribes to events for the specified notifier.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        void SubscribeToEvents(
            OperationContext    context,
            object              notifier, 
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe);
        
        /// <summary>
        /// Subscribes/unsubscribes to all events from the source.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        void SubscribeToAllEvents(            
            OperationContext    context,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe);

        /// <summary>
        /// Tells the source to refresh all conditions.
        /// </summary>
        void ConditionRefresh(            
            OperationContext           context,
            IList<IEventMonitoredItem> monitoredItems);
    }
#endif
}
