// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Schema;

namespace Opc.Ua.PubSub.Schema
{
    /// <summary>
    /// Generates schema documents for OPC UA PubSub runtime metadata.
    /// </summary>
    public interface IPubSubSchemaProvider
    {
        /// <summary>
        /// Creates a JSON Schema document for the fields of a PubSub DataSet payload.
        /// </summary>
        /// <param name="metaData">The DataSet metadata that describes the fields.</param>
        /// <param name="fieldContentMask">The writer field content mask that controls field value shape.</param>
        /// <param name="verbose">Whether verbose OPC UA JSON encoding schema fragments are requested.</param>
        /// <returns>The generated JSON Schema document.</returns>
        IUaSchema CreateDataSetSchema(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false);

        /// <summary>
        /// Creates a JSON Schema document for a single PubSub JSON DataSetMessage object.
        /// </summary>
        /// <param name="metaData">The DataSet metadata that describes the payload fields.</param>
        /// <param name="messageContentMask">The JSON DataSetMessage content mask that controls header fields.</param>
        /// <param name="fieldContentMask">The writer field content mask that controls field value shape.</param>
        /// <param name="verbose">Whether verbose OPC UA JSON encoding schema fragments are requested.</param>
        /// <returns>The generated JSON Schema document.</returns>
        IUaSchema CreateDataSetMessageSchema(
            DataSetMetaDataType metaData,
            JsonDataSetMessageContentMask messageContentMask,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false);

        /// <summary>
        /// Creates a JSON Schema document for a PubSub JSON NetworkMessage envelope.
        /// </summary>
        /// <param name="metaData">The DataSet metadata that describes the payload fields.</param>
        /// <param name="networkContentMask">The JSON NetworkMessage content mask that controls envelope fields.</param>
        /// <param name="messageContentMask">The JSON DataSetMessage content mask that controls message header fields.</param>
        /// <param name="fieldContentMask">The writer field content mask that controls field value shape.</param>
        /// <param name="verbose">Whether verbose OPC UA JSON encoding schema fragments are requested.</param>
        /// <returns>The generated JSON Schema document.</returns>
        IUaSchema CreateNetworkMessageSchema(
            DataSetMetaDataType metaData,
            JsonNetworkMessageContentMask networkContentMask,
            JsonDataSetMessageContentMask messageContentMask,
            DataSetFieldContentMask fieldContentMask,
            bool verbose = false);

        /// <summary>
        /// Creates a JSON Schema document for a PubSub JSON metadata message envelope.
        /// </summary>
        /// <param name="metaData">The DataSet metadata announced by the metadata message.</param>
        /// <param name="verbose">Whether verbose OPC UA JSON encoding schema fragments are requested.</param>
        /// <returns>The generated JSON Schema document.</returns>
        IUaSchema CreateMetaDataMessageSchema(
            DataSetMetaDataType metaData,
            bool verbose = false);
    }
}
