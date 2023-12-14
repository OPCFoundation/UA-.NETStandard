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
    /// UADP Discovery messages interface
    /// </summary>
    public interface IUadpDiscoveryMessages
    {
        /// <summary>
        /// Set GetPublisherEndpoints callback used by the subscriber to receive PublisherEndpoints data from publisher
        /// </summary>
        /// <param name="eventHandler"></param>
        void GetPublisherEndpointsCallback(GetPublisherEndpointsEventHandler eventHandler);

        /// <summary>
        /// Set GetDataSetWriterIds callback used by the subscriber to receive DataSetWriter ids from publisher
        /// </summary>
        /// <param name="eventHandler"></param>
        void GetDataSetWriterConfigurationCallback(GetDataSetWriterIdsEventHandler eventHandler);

        /// <summary>
        /// Create and return the list of EndpointDescription to be used only by UADP Discovery response messages
        /// </summary>
        /// <param name="endpoints"></param>
        /// <param name="publisherProvideEndpointsStatusCode"></param>
        /// <param name="publisherId"></param>
        /// <returns></returns>
        UaNetworkMessage CreatePublisherEndpointsNetworkMessage(EndpointDescription[] endpoints,
            StatusCode publisherProvideEndpointsStatusCode, object publisherId);

        /// <summary>
        /// Create and return the list of DataSetMetaData response messages 
        /// </summary>
        /// <param name="dataSetWriterIds"></param>
        /// <returns></returns>
        IList<UaNetworkMessage> CreateDataSetMetaDataNetworkMessages(UInt16[] dataSetWriterIds);

        /// <summary>
        /// Create and return the list of DataSetWriterConfiguration response message
        /// </summary>
        /// <param name="dataSetWriterIds">DatasetWriter ids</param>
        /// <returns></returns>
        IList<UaNetworkMessage> CreateDataSetWriterCofigurationMessage(UInt16[] dataSetWriterIds);

        /// <summary>
        /// Request UADP Discovery DataSetWriterConfiguration messages
        /// </summary>
        void RequestDataSetWriterConfiguration();

        /// <summary>
        /// Request UADP Discovery DataSetMetaData messages
        /// </summary>
        void RequestDataSetMetaData();

        /// <summary>
        /// Request UADP Discovery Publisher endpoints only
        /// </summary>
        void RequestPublisherEndpoints();
    }

    /// <summary>
    /// Get PublisherEndpoints event handler
    /// </summary>
    /// <returns></returns>
    public delegate IList<EndpointDescription> GetPublisherEndpointsEventHandler();

    /// <summary>
    /// Get DataSetWriterConfiguration ids event handler
    /// </summary>
    /// <returns></returns>
    public delegate IList<UInt16> GetDataSetWriterIdsEventHandler(UaPubSubApplication uaPubSubApplication);
}
