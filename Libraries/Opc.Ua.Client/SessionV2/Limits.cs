#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using System.Runtime.Serialization;

    /// <summary>
    /// Extended operation limits, either because the stack
    /// does not define them, or because we need them for
    /// operation of the client side.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public sealed class Limits : Opc.Ua.OperationLimits
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
#endif
