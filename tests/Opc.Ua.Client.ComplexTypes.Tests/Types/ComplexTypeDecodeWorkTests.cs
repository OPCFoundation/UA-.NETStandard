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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Structures loaded from a server can be empty and nest, so their binary
    /// decoding is charged to the work limit of the decoder: the work of one
    /// decoder may exceed the length of its input by a fixed allowance only.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypeDecodeWorkTests
    {
        /// <summary>
        /// The complex type builder under test.
        /// </summary>
        public enum BuilderKind
        {
            /// <summary>
            /// The default complex type builder.
            /// </summary>
            Default,

            /// <summary>
            /// The Reflection.Emit complex type builder.
            /// </summary>
            Emit
        }

        [Test]
        public async Task NestedTypesWithinTheBudgetDecodeFromEmptyInputAsync(
            [Values] BuilderKind builder)
        {
            (ServiceMessageContext context, ExpandedNodeId[] typeIds) =
                await LoadNestedTypesAsync(builder, 17).ConfigureAwait(false);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, typeIds[8]);

            Assert.That(value, Is.Not.Null);
        }

        [Test]
        public async Task NestedTypesBeyondTheBudgetFailWithEncodingLimitsExceededAsync(
            [Values] BuilderKind builder)
        {
            (ServiceMessageContext context, ExpandedNodeId[] typeIds) =
                await LoadNestedTypesAsync(builder, 17).ConfigureAwait(false);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, typeIds[17]));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public async Task ArraysOfUnionsAndOptionalFieldStructuresChargeOnlyEncodedFieldsAsync(
            [Values] BuilderKind builder,
            [Values(StructureType.Union, StructureType.StructureWithOptionalFields)] StructureType structureType)
        {
            // Each element encodes one of 16 members in 8 bytes. Charging all
            // declared members would exceed the input length plus the allowance.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var resolver = new MockResolver();
            ushort ns = resolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 8400;
            var fields = new StructureField[16];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = Field("F" + (i + 1), DataTypeIds.Int32);
                fields[i].IsOptional = structureType == StructureType.StructureWithOptionalFields;
            }
            NodeId typeNodeId = AddStructure(resolver, "Sparse", ns, ref nodeId, new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = structureType,
                Fields = fields
            });
            IComplexTypeFactory factory = builder == BuilderKind.Emit
                ? new ComplexTypeBuilderFactory()
                : new DefaultComplexTypeFactory();
            var typeSystem = new ComplexTypeSystem(resolver, factory, telemetry);
            Assert.That(await typeSystem.LoadAsync().ConfigureAwait(false), Is.True);
            var context = new ServiceMessageContext(telemetry, resolver.Factory)
            {
                NamespaceUris = resolver.NamespaceUris
            };
            const int count = 20000;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteUInt32(null, 1);
                    encoder.WriteInt32(null, i);
                }
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeNodeId, resolver.NamespaceUris));

            Assert.That(values.Count, Is.EqualTo(count));
        }

        /// <summary>
        /// Loads T0 without fields and T1 to Tn, where each level has two
        /// fields of the type one level below.
        /// </summary>
        private static async Task<(ServiceMessageContext, ExpandedNodeId[])> LoadNestedTypesAsync(
            BuilderKind builder,
            int levels)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var resolver = new MockResolver();
            ushort ns = resolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            uint nodeId = 8300;
            var typeIds = new ExpandedNodeId[levels + 1];
            NodeId previous = NodeId.Null;
            for (int level = 0; level <= levels; level++)
            {
                StructureField[] fields = previous.IsNull
                    ? []
                    : [Field("A", previous), Field("B", previous)];
                previous = AddStructure(resolver, "T" + level, ns, ref nodeId, new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields = fields
                });
                typeIds[level] = NodeId.ToExpandedNodeId(previous, resolver.NamespaceUris);
            }

            IComplexTypeFactory factory = builder == BuilderKind.Emit
                ? new ComplexTypeBuilderFactory()
                : new DefaultComplexTypeFactory();
            var typeSystem = new ComplexTypeSystem(resolver, factory, telemetry);
            bool loaded = await typeSystem.LoadAsync().ConfigureAwait(false);
            Assert.That(loaded, Is.True);

            var context = new ServiceMessageContext(telemetry, resolver.Factory)
            {
                NamespaceUris = resolver.NamespaceUris
            };
            return (context, typeIds);
        }

        private static StructureField Field(string name, NodeId dataType)
        {
            return new StructureField
            {
                Name = name,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar
            };
        }

        private static NodeId AddStructure(
            MockResolver resolver,
            string name,
            ushort ns,
            ref uint nodeId,
            StructureDefinition definition)
        {
            var node = new DataTypeNode
            {
                NodeId = new NodeId(nodeId++, ns),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = LocalizedText.From(name),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(definition)
            };
            foreach (string browseName in new[] { BrowseNames.DefaultBinary, BrowseNames.DefaultXml })
            {
                var description = new ReferenceDescription
                {
                    NodeId = new NodeId(nodeId++, ns),
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    BrowseName = QualifiedName.From(browseName),
                    DisplayName = LocalizedText.From(browseName),
                    IsForward = true,
                    NodeClass = NodeClass.Object
                };
                var encoding = new Node(description);
                resolver.DataTypeNodes[encoding.NodeId] = encoding;
                node.References += new ReferenceNode
                {
                    ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                    IsInverse = false,
                    TargetId = description.NodeId
                };
            }
            resolver.DataTypeNodes[node.NodeId] = node;
            return node.NodeId;
        }
    }
}
