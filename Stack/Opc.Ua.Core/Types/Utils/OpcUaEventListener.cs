/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.Diagnostics.Tracing;

namespace Opc.Ua
{
    /// <summary>
    /// The event listener.
    /// </summary>
    public class OpcUaEventListener : EventListener
    {
        /// <summary>
        /// 
        /// </summary>
        public OpcUaEventListener()
        {
#if !NETSTANDARD2_0
            this.EventSourceCreated += OpcUaCoreEventListener_EventSourceCreated;
            this.EventWritten += OpcUaCoreEventListener_EventWritten;
#endif
        }

#if NETSTANDARD2_0_OR_GREATER
        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventSource"></param>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            OnEventSourceCreated_Internal(eventSource);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="eventData"></param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            OpcUaCoreEventListener_EventWritten(null, eventData);
        }
#endif

        private void OpcUaCoreEventListener_EventWritten(object sender, EventWrittenEventArgs ev)
        {
            if (ev.EventId == 1)
            {
                Utils.Trace(null, Utils.TraceMasks.Information, ev.Payload[0] as string, false);
            }
        }

#if !NETSTANDARD2_0
        private void OpcUaCoreEventListener_EventSourceCreated(
            object sender,
            EventSourceCreatedEventArgs ev)
        {
            OnEventSourceCreated_Internal(ev.EventSource);
        }
#endif

        private void OnEventSourceCreated_Internal(EventSource eventSource)
        {
            if (eventSource != null)
            {
                Console.WriteLine("Source: {0} Guid: {1}", eventSource.Name, eventSource.Guid);

                if (eventSource.Name.StartsWith("OPC-UA"))
                {
                    this.EnableEvents(eventSource, EventLevel.Verbose);
                }
            }
        }
    }
}
