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

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Core", Guid = "753029BC-A4AA-4440-8668-290D0692A72B")]
    internal sealed class OpcUaCoreEventSource : EventSource
    {
        /// <summary>
        /// The core event ids.
        /// </summary>
        internal const int ServiceCallStartId = 10;
        internal const int ServiceCallStopId = ServiceCallStartId + 1;
        internal const int ServiceCallBadStopId = ServiceCallStopId + 1;
        internal const int SubscriptionStateId = ServiceCallBadStopId + 1;
        internal const int SendResponseId = SubscriptionStateId + 1;

        /// <summary>
        /// The task definitions.
        /// </summary>
        public static class Tasks
        {
            /// <summary>
            /// Service Call Activity.
            /// </summary>
            public const EventTask ServiceCallTask = (EventTask)1;
        }

        /// <summary>
        /// The keywords for message filters.
        /// </summary>
        public static class Keywords
        {
            /// <summary>
            /// Services events.
            /// </summary>
            public const EventKeywords Services = (EventKeywords)2;
        }

        /// <summary>
        /// A server service call message.
        /// </summary>
        [Event(
            ServiceCallStartId,
            Keywords = Keywords.Services,
            Message = "{0} Called. RequestHandle={1}, PendingRequestCount={2}",
            Level = EventLevel.Verbose,
            Task = Tasks.ServiceCallTask
        )]
        public void ServiceCallStart(string serviceName, int requestHandle, int pendingRequestCount)
        {
            WriteEvent(ServiceCallStartId, serviceName, requestHandle, pendingRequestCount);
        }

        /// <summary>
        /// The server service completed message.
        /// </summary>
        [Event(
            ServiceCallStopId,
            Keywords = Keywords.Services,
            Message = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}",
            Level = EventLevel.Verbose,
            Task = Tasks.ServiceCallTask
        )]
        public void ServiceCallStop(string serviceName, int requestHandle, int pendingRequestCount)
        {
            WriteEvent(ServiceCallStopId, serviceName, requestHandle, pendingRequestCount);
        }

        /// <summary>
        /// A service message completed with a bad status code.
        /// </summary>
        [Event(
            ServiceCallBadStopId,
            Keywords = Keywords.Services,
            Message = "{0} Completed. RequestHandle={1}, PendingRequestCount={2}, StatusCode={3}",
            Level = EventLevel.Warning,
            Task = Tasks.ServiceCallTask
        )]
        public void ServiceCallBadStop(
            string serviceName,
            int requestHandle,
            int statusCode,
            int pendingRequestCount)
        {
            WriteEvent(
                ServiceCallBadStopId,
                serviceName,
                requestHandle,
                pendingRequestCount,
                statusCode);
        }

        /// <summary>
        /// The send response of a server channel.
        /// </summary>
        [Event(
            SendResponseId,
            Message = "ChannelId {0}: SendResponse {1}",
            Level = EventLevel.Verbose)]
        public void SendResponse(int channelId, int requestId)
        {
            WriteEvent(SendResponseId, channelId, requestId);
        }
    }
}
