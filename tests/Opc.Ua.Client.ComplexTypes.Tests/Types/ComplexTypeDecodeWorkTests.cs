// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Structures loaded from a server can be empty and nest, so the binary
    /// decoder limits how many encodeables one message decodes: the larger of
    /// the maximum message size and the input length, plus a fixed allowance.
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
        public async Task NestedTypesWithinTheLimitDecodeFromEmptyInputAsync(
            [Values] BuilderKind builder)
        {
            (ServiceMessageContext context, ExpandedNodeId[] typeIds) =
                await LoadNestedTypesAsync(builder, 17).ConfigureAwait(false);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, typeIds[16]);

            Assert.That(value, Is.Not.Null);
        }

        [Test]
        public async Task NestedTypesBeyondTheLimitFailWithEncodingLimitsExceededAsync(
            [Values] BuilderKind builder)
        {
            (ServiceMessageContext context, ExpandedNodeId[] typeIds) =
                await LoadNestedTypesAsync(builder, 17).ConfigureAwait(false);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, typeIds[17]));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
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
                NamespaceUris = resolver.NamespaceUris,

                // a message without input may decode 4096 + 131072 encodeables:
                // T16 (131071) but not T17 (262143).
                MaxMessageSize = 4096
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
