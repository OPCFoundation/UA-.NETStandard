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

using System.Runtime.Serialization;

namespace Opc.Ua.Client
{
    /// <summary>
    /// In addition to operation limits this provides additional
    /// information about server capabilities to a session user.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public sealed class ServerCapabilities
    {
        /// <summary>
        /// Max browse continuation points
        /// </summary>
        [DataMember(Order = 200)]
        public ushort MaxBrowseContinuationPoints { get; set; }

        /// <summary>
        /// Max query continuation points
        /// </summary>
        [DataMember(Order = 210)]
        public ushort MaxQueryContinuationPoints { get; set; }

        /// <summary>
        /// Max history continuation points
        /// </summary>
        [DataMember(Order = 220)]
        public ushort MaxHistoryContinuationPoints { get; set; }

        /// <summary>
        /// Min supported sampling rate
        /// </summary>
        [DataMember(Order = 240)]
        public double MinSupportedSampleRate { get; set; }

        /// <summary>
        /// Max array length supported
        /// </summary>
        [DataMember(Order = 250)]
        public uint MaxArrayLength { get; set; }

        /// <summary>
        /// Max string length supported
        /// </summary>
        [DataMember(Order = 260)]
        public uint MaxStringLength { get; set; }

        /// <summary>
        /// Max byte buffer length supported
        /// </summary>
        [DataMember(Order = 270)]
        public uint MaxByteStringLength { get; set; }

        /// <summary>
        /// Max sessions the server can handle
        /// </summary>
        [DataMember(Order = 300)]
        public uint MaxSessions { get; set; }

        /// <summary>
        /// Max subscriptions the server can handle
        /// </summary>
        [DataMember(Order = 310)]
        public uint MaxSubscriptions { get; set; }

        /// <summary>
        /// Max monitored items the server can handle
        /// </summary>
        [DataMember(Order = 320)]
        public uint MaxMonitoredItems { get; set; }

        /// <summary>
        /// Max subscriptions per session
        /// </summary>
        [DataMember(Order = 330)]
        public uint MaxSubscriptionsPerSession { get; set; }

        /// <summary>
        /// Max monitored items per subscription
        /// </summary>
        [DataMember(Order = 340)]
        public uint MaxMonitoredItemsPerSubscription { get; set; }

        /// <summary>
        /// Max select clause parameters
        /// </summary>
        [DataMember(Order = 350)]
        public uint MaxSelectClauseParameters { get; set; }

        /// <summary>
        /// Max where clause parameters
        /// </summary>
        [DataMember(Order = 360)]
        public uint MaxWhereClauseParameters { get; set; }

        /// <summary>
        /// Max monitored items queue size
        /// </summary>
        [DataMember(Order = 370)]
        public uint MaxMonitoredItemsQueueSize { get; set; }
    }
}
