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

using System.Diagnostics.Tracing;

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
