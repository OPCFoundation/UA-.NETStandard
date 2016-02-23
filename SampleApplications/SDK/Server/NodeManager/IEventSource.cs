/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
