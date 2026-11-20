// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Interface for a data store component responsible to store/get data to and from Ua publisher
    /// </summary>
#if NET5_0_OR_GREATER
    [Obsolete(
        "Use IPublishedDataSetSource. See docs/migrate/2.0.x/pubsub.md",
        DiagnosticId = "UA0023",
        UrlFormat = "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/pubsub.md#UA0023")]
#else
    [Obsolete("Use IPublishedDataSetSource. See docs/migrate/2.0.x/pubsub.md (UA0023)")]
#endif
    public interface IUaPubSubDataStore
    {
        /// <summary>
        /// Write a DataValue to the DataStore.
        /// The DataValue is identified by node NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier for DataValue that will be stored</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/>.</param>
        /// <param name="dataValue">Default value is the default DataValue.</param>
        void WritePublishedDataItem(
            NodeId nodeId,
            uint attributeId = Attributes.Value,
            DataValue dataValue = default);

        /// <summary>
        /// Try to read the DataValue stored for a specific NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier of node</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/></param>
        /// <param name="dataValue">The stored DataValue when this method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a DataValue is stored for the given NodeId and Attribute; otherwise <c>false</c>.</returns>
        bool TryReadPublishedDataItem(
            NodeId nodeId,
            uint attributeId,
            out DataValue dataValue);

        /// <summary>
        /// Updates the metadata if it has changed from when the DataStore was created.
        /// </summary>
        void UpdateMetaData(PublishedDataSetDataType publishedDataSet);
    }
}
