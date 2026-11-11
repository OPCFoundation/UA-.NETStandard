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

using System;
using System.Threading;
using Opc.Ua.Schema;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Default DataSet JSON Schema provider backed by the OPC UA Core.Schema generator.
    /// </summary>
    public sealed class DataSetJsonSchemaProvider : IDataSetJsonSchemaProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataSetJsonSchemaProvider"/> class.
        /// </summary>
        /// <param name="schemaProvider">The Core.Schema provider used to generate the JSON Schema.</param>
        /// <param name="registry">The mutable registry used to resolve nested DataTypeDefinition entries.</param>
        public DataSetJsonSchemaProvider(ISchemaProvider schemaProvider, DataTypeDefinitionRegistry registry)
        {
            m_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public string CreateJsonSchema(DataSetMetaDataType metaData, bool verbose = false)
        {
            if (metaData is null)
            {
                throw new ArgumentNullException(nameof(metaData));
            }

            UaTypeDescription description = CreateTypeDescription(metaData);
            IUaSchema schema;
            lock (m_lock)
            {
                m_registry.Add(description);
                schema = m_schemaProvider.CreateSchema(
                    description,
                    verbose ? UaSchemaFormat.JsonVerbose : UaSchemaFormat.JsonCompact,
                    UaSchemaScope.Type);
            }
            return schema.ToSchemaString();
        }

        private static UaTypeDescription CreateTypeDescription(DataSetMetaDataType metaData)
        {
            // The current stack DataSetMetaDataType surface exposes Fields but not embedded
            // StructureDataTypes/EnumDataTypes/SimpleDataTypes collections. Custom complex field
            // DataType NodeIds are therefore emitted as permissive schemas unless the caller has
            // already registered those type definitions in the injected DataTypeDefinitionRegistry.
            string dataSetName = string.IsNullOrEmpty(metaData.Name) ? DefaultDataSetName : metaData.Name!;
            var fields = new StructureField[metaData.Fields.IsNull ? 0 : metaData.Fields.Count];
            for (int i = 0; i < fields.Length; i++)
            {
                FieldMetaData field = metaData.Fields[i];
                fields[i] = new StructureField
                {
                    Name = string.IsNullOrEmpty(field.Name) ? "Field" + i : field.Name,
                    DataType = ResolveDataType(field),
                    ValueRank = field.ValueRank,
                    ArrayDimensions = field.ArrayDimensions
                };
            }

            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = fields
            };
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(dataSetName, SyntheticNamespaceIndex), SyntheticNamespaceUri),
                new QualifiedName(dataSetName, SyntheticNamespaceIndex),
                definition,
                SyntheticNamespaceUri);
        }

        private static NodeId ResolveDataType(FieldMetaData field)
        {
            if (!field.DataType.IsNull)
            {
                return field.DataType;
            }

            BuiltInType builtInType = (BuiltInType)field.BuiltInType;
            if (builtInType != BuiltInType.Null)
            {
                return new NodeId((uint)builtInType);
            }

            return DataTypeIds.BaseDataType;
        }

        private const string DefaultDataSetName = "DataSet";
        private const string SyntheticNamespaceUri = "urn:opcua:pubsub:json-schema";
        private const ushort SyntheticNamespaceIndex = 1;

        private readonly Lock m_lock = new();
        private readonly ISchemaProvider m_schemaProvider;
        private readonly DataTypeDefinitionRegistry m_registry;
    }
}
