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

using Opc.Ua.Schema.Json;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Helpers to build data type descriptions and providers for the schema
    /// generation tests.
    /// </summary>
    internal static class SchemaTestData
    {
        /// <summary>
        /// The namespace uri used by the test data types.
        /// </summary>
        public const string TestNamespace = "http://test.org/UA/schema";

        /// <summary>
        /// The namespace index used by the test data types.
        /// </summary>
        public const ushort TestNamespaceIndex = 1;

        /// <summary>
        /// Creates a schema provider populated with the supplied data types.
        /// </summary>
        public static ISchemaProvider CreateProvider(params UaTypeDescription[] types)
        {
            var registry = new DataTypeDefinitionRegistry();
            foreach (UaTypeDescription type in types)
            {
                registry.Add(type);
            }
            return new DefaultSchemaProvider(registry, [new JsonSchemaGenerator()]);
        }

        /// <summary>
        /// Returns the node id of a standard built-in data type.
        /// </summary>
        public static NodeId BuiltIn(BuiltInType builtInType)
        {
            return new NodeId((uint)builtInType);
        }

        /// <summary>
        /// Creates a structure field.
        /// </summary>
        public static StructureField Field(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar,
            bool optional = false)
        {
            return new StructureField
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank,
                IsOptional = optional
            };
        }

        /// <summary>
        /// Creates a structure type description.
        /// </summary>
        public static UaTypeDescription Structure(
            uint id,
            string name,
            params StructureField[] fields)
        {
            return BuildStructure(id, name, StructureType.Structure, fields);
        }

        /// <summary>
        /// Creates a union type description.
        /// </summary>
        public static UaTypeDescription Union(
            uint id,
            string name,
            params StructureField[] fields)
        {
            return BuildStructure(id, name, StructureType.Union, fields);
        }

        /// <summary>
        /// Creates an enumeration type description.
        /// </summary>
        public static UaTypeDescription Enumeration(
            uint id,
            string name,
            params (string Name, long Value)[] values)
        {
            var fields = new EnumField[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                fields[i] = new EnumField
                {
                    Name = values[i].Name,
                    Value = values[i].Value
                };
            }
            var definition = new EnumDefinition { Fields = fields };
            return Describe(id, name, definition);
        }

        private static UaTypeDescription BuildStructure(
            uint id,
            string name,
            StructureType structureType,
            StructureField[] fields)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = structureType,
                Fields = fields
            };
            return Describe(id, name, definition);
        }

        private static UaTypeDescription Describe(uint id, string name, DataTypeDefinition definition)
        {
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(id, TestNamespaceIndex)),
                new QualifiedName(name, TestNamespaceIndex),
                definition,
                TestNamespace);
        }
    }
}
