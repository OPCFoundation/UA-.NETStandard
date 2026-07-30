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

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Derives an Avro schema and its SchemaId from a <see cref="DataSetMetaDataType"/> alone,
    /// without an AddressSpace (§6.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is primarily a Publisher path. A translation bridge that receives UADP or JSON verbose
    /// NetworkMessages and re-publishes them as Avro holds the DataSetMetaData for each writer it
    /// forwards, but usually has no session with the originating Server and therefore no
    /// AddressSpace to read DataTypeDefinitions from. Likewise, a deployment already publishing
    /// JSON verbose messages already produces DataSetMetaData, so it can add Avro without gaining
    /// any new access to the type model.
    /// </para>
    /// <para>
    /// This is a different <em>source of inputs</em>, not a different algorithm: it produces the
    /// schema the encode-time path produces for the same DataSet, and therefore the same SchemaId.
    /// That identity is enforced structurally - both paths run the same field projection and the
    /// same <see cref="AvroSchemaBuilder"/> - rather than by two implementations that are merely
    /// intended to agree. If they disagreed, a SchemaId would no longer identify one canonical
    /// schema and §6.3 would not hold.
    /// </para>
    /// </remarks>
    public static class AvroDataSetSchema
    {
        /// <summary>
        /// Generates the Avro schema document for a DataSet from its metadata.
        /// </summary>
        /// <param name="metaData">The DataSetMetaData describing the DataSet.</param>
        /// <param name="fieldContentMask">
        /// The DataSetFieldContentMask of the DataSetWriter, which selects the field framing
        /// (§8.2). The framing is not part of the metadata and is applied after the type mapping.
        /// </param>
        /// <returns>The Avro schema document as JSON.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="metaData"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a field declares a DataType that is neither a built-in nor declared by the
        /// metadata, so no schema can be generated for it.
        /// </exception>
        public static string Create(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask = DataSetFieldContentMask.None)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            return AvroSchemaBuilder.Build(
                string.IsNullOrEmpty(metaData.Name) ? "DataSet" : metaData.Name,
                CollectFields(metaData, fieldContentMask),
                null,
                new AvroMetaDataTypeResolver(metaData));
        }

        /// <summary>
        /// Generates the Avro schema document and its SchemaId for a DataSet from its metadata.
        /// </summary>
        /// <param name="metaData">The DataSetMetaData describing the DataSet.</param>
        /// <param name="fieldContentMask">The DataSetFieldContentMask of the DataSetWriter.</param>
        /// <param name="schemaId">
        /// The CRC-64-AVRO fingerprint of the Parsing Canonical Form of the generated schema (§6.3).
        /// </param>
        /// <returns>The Avro schema document as JSON.</returns>
        public static string Create(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask,
            out ByteString schemaId)
        {
            string schema = Create(metaData, fieldContentMask);
            schemaId = SchemaCache.ComputeSchemaId(
                ByteString.From(System.Text.Encoding.UTF8.GetBytes(schema)),
                SchemaCache.AvroFormat);
            return schema;
        }

        /// <summary>
        /// Projects the DataSetMetaData fields onto the descriptors used for schema generation.
        /// </summary>
        /// <param name="metaData">The DataSetMetaData describing the DataSet.</param>
        /// <param name="fieldContentMask">The DataSetFieldContentMask of the DataSetWriter.</param>
        /// <returns>The ordered field descriptors.</returns>
        internal static List<AvroSchemaField> CollectFields(
            DataSetMetaDataType metaData,
            DataSetFieldContentMask fieldContentMask)
        {
            PubSubFieldEncoding encoding = FramingFor(fieldContentMask);
            var fields = new List<AvroSchemaField>();
            if (metaData.Fields.IsNull)
            {
                return fields;
            }

            // §6.2 step 3 / §6.7: one Avro record field per declared entry, in `Fields` order,
            // never sorted and never omitted. MaxStringLength, DataSetFieldId, Properties and
            // Description are deliberately not read: they constrain or annotate values but must not
            // change the Parsing Canonical Form.
            foreach (FieldMetaData field in metaData.Fields)
            {
                fields.Add(new AvroSchemaField(
                    field.Name,
                    (BuiltInType)field.BuiltInType,
                    field.ValueRank,
                    encoding,
                    field.DataType));
            }
            return fields;
        }

        /// <summary>
        /// Selects the field framing implied by a DataSetFieldContentMask (§8.2).
        /// </summary>
        /// <param name="mask">The DataSetFieldContentMask of the DataSetWriter.</param>
        /// <returns>The field framing.</returns>
        internal static PubSubFieldEncoding FramingFor(DataSetFieldContentMask mask)
        {
            const DataSetFieldContentMask dataValueBits = DataSetFieldContentMask.StatusCode
                | DataSetFieldContentMask.SourceTimestamp
                | DataSetFieldContentMask.SourcePicoSeconds
                | DataSetFieldContentMask.ServerTimestamp
                | DataSetFieldContentMask.ServerPicoSeconds;
            if ((mask & dataValueBits) != 0)
            {
                return PubSubFieldEncoding.DataValue;
            }
            return (mask & DataSetFieldContentMask.RawData) != 0
                ? PubSubFieldEncoding.RawData
                : PubSubFieldEncoding.Variant;
        }
    }
}
