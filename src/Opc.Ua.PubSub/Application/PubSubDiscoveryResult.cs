// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
