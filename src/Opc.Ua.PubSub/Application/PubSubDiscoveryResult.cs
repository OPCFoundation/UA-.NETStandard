/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// DataSetMetaData discovery response entry.
    /// </summary>
    public sealed record PubSubDataSetMetaDataDiscoveryResult
    {
        /// <summary>
        /// PublisherId that sent the response.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// WriterGroupId that sent the response.
        /// </summary>
        public ushort WriterGroupId { get; init; }

        /// <summary>
        /// DataSetWriterId associated with the metadata.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// Status reported by the publisher.
        /// </summary>
        public StatusCode StatusCode { get; init; }

        /// <summary>
        /// Discovered metadata payload.
        /// </summary>
        public DataSetMetaDataType? DataSetMetaData { get; init; }
    }

    /// <summary>
    /// DataSetWriterConfiguration discovery response entry.
    /// </summary>
    public sealed record PubSubDataSetWriterConfigurationDiscoveryResult
    {
        /// <summary>
        /// PublisherId that sent the response.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// WriterGroupId that sent the response.
        /// </summary>
        public ushort WriterGroupId { get; init; }

        /// <summary>
        /// DataSetWriterIds included in the writer configuration.
        /// </summary>
        public ArrayOf<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// Status reported by the publisher.
        /// </summary>
        public StatusCode StatusCode { get; init; }

        /// <summary>
        /// Discovered writer-group configuration payload.
        /// </summary>
        public WriterGroupDataType? WriterConfiguration { get; init; }
    }

    /// <summary>
    /// Immutable aggregate of PubSub discovery responses collected within a timeout.
    /// </summary>
    public sealed record PubSubDiscoveryResult
    {
        /// <summary>
        /// DataSetMetaData response entries.
        /// </summary>
        public ArrayOf<PubSubDataSetMetaDataDiscoveryResult> DataSetMetaDataEntries { get; init; } = [];

        /// <summary>
        /// DataSetWriterConfiguration response entries.
        /// </summary>
        public ArrayOf<PubSubDataSetWriterConfigurationDiscoveryResult> WriterConfigurations { get; init; } = [];

        /// <summary>
        /// Publisher endpoint descriptions returned by publishers.
        /// </summary>
        public ArrayOf<EndpointDescription> PublisherEndpoints { get; init; } = [];
    }
}
