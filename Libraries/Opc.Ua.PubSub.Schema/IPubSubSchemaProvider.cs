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
