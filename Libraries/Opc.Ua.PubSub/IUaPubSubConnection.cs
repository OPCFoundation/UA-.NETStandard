/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Interface for an UaPubSubConnection
    /// </summary>
    public interface IUaPubSubConnection : IDisposable
    {
        /// <summary>
        /// Get assigned transport protocol for this connection instance
        /// </summary>
        TransportProtocol TransportProtocol { get; }

        /// <summary>
        /// Get the configuration object for this PubSub connection
        /// </summary>
        PubSubConnectionDataType PubSubConnectionConfiguration { get; }

        /// <summary>
        /// Get reference to <see cref="UaPubSubApplication"/>
        /// </summary>
        UaPubSubApplication Application { get; }

        /// <summary>
        /// Get flag that indicates if the Connection is in running state
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        void Start();

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        void Stop();

        /// <summary>
        /// Determine if the connection has anything to publish -> at least one WriterDataSet is configured as enabled for current writer group
        /// </summary>
        bool CanPublish(WriterGroupDataType writerGroupConfiguration);

        /// <summary>
        /// Create the network messages built from the provided writerGroupConfiguration
        /// </summary>
        IList<UaNetworkMessage> CreateNetworkMessages(WriterGroupDataType writerGroupConfiguration, WriterGroupPublishState state);

        /// <summary>
        /// Publish the network message
        /// </summary>
        bool PublishNetworkMessage(UaNetworkMessage networkMessage);

        /// <summary>
        /// Get flag that indicates if all the network clients are connected
        /// </summary>
        bool AreClientsConnected();

        /// <summary>
        /// Get current list of dataset readers available in this UaSubscriber component
        /// </summary>
        List<DataSetReaderDataType> GetOperationalDataSetReaders();
    }    
}
