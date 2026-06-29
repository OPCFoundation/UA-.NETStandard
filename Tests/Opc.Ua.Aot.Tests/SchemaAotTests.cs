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

using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Schema;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests for runtime OPC UA schema generation.
    /// </summary>
    public class SchemaAotTests
    {
        [Test]
        public async Task CreateSchemaForAllFormatsIsAotSafeAsync()
        {
            using ServiceProvider services = CreateServices(out UaTypeDescription outer);
            ISchemaProvider provider = services.GetRequiredService<ISchemaProvider>();

            UaSchemaFormat[] formats =
            [
                UaSchemaFormat.JsonCompact,
                UaSchemaFormat.JsonVerbose,
                UaSchemaFormat.Xsd,
                UaSchemaFormat.Bsd
            ];

            foreach (UaSchemaFormat format in formats)
            {
                IUaSchema schema = provider.CreateSchema(outer, format);
                string text = schema.ToSchemaString();

                await Assert.That(text).IsNotNull();
                await Assert.That(text.Length).IsGreaterThan(0);

                if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
                {
                    JsonNode? parsed = JsonNode.Parse(text);
                    await Assert.That(parsed).IsNotNull();
                }
                else
                {
                    XDocument parsed = XDocument.Parse(text);
                    await Assert.That(parsed.Root).IsNotNull();
                }
            }
        }

        private static ServiceProvider CreateServices(out UaTypeDescription outer)
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();
            ServiceProvider provider = services.BuildServiceProvider();

            DataTypeDefinitionRegistry registry = provider.GetRequiredService<DataTypeDefinitionRegistry>();
            UaTypeDescription inner = Structure(
                7102,
                "AotInner",
                Field("Code", BuiltInType.Int32),
                Field("DisplayName", BuiltInType.String));
            UaTypeDescription color = Enumeration(7103, "AotColor", ("Red", 0), ("Green", 1));
            outer = Structure(
                7101,
                "AotOuter",
                Field("Enabled", BuiltInType.Boolean),
                Field("Values", BuiltInType.Double, ValueRanks.OneDimension),
                Field("Child", new NodeId(7102, TestNamespaceIndex)),
                Field("Shade", new NodeId(7103, TestNamespaceIndex)));

            registry.Add(inner);
            registry.Add(color);
            registry.Add(outer);
            return provider;
        }

        private static StructureField Field(
            string name,
            BuiltInType builtInType,
            int valueRank = ValueRanks.Scalar)
        {
            return Field(name, new NodeId((uint)builtInType), valueRank);
        }

        private static StructureField Field(
            string name,
            NodeId dataType,
            int valueRank = ValueRanks.Scalar)
        {
            return new StructureField
            {
                Name = name,
                DataType = dataType,
                ValueRank = valueRank
            };
        }

        private static UaTypeDescription Structure(
            uint id,
            string name,
            params StructureField[] fields)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = fields
            };
            return Describe(id, name, definition);
        }

        private static UaTypeDescription Enumeration(
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

            return Describe(id, name, new EnumDefinition { Fields = fields });
        }

        private static UaTypeDescription Describe(uint id, string name, DataTypeDefinition definition)
        {
            return new UaTypeDescription(
                new ExpandedNodeId(new NodeId(id, TestNamespaceIndex)),
                new QualifiedName(name, TestNamespaceIndex),
                definition,
                TestNamespace);
        }

        private const string TestNamespace = "http://test.org/UA/schema/aot";
        private const ushort TestNamespaceIndex = 7;
    }
}
