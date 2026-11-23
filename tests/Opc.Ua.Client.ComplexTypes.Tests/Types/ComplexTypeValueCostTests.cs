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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// The types built with Reflection.Emit come from the definitions of a
    /// server, and a value can take a few bytes on the wire. Creating a value
    /// must not reflect over its type or allocate per field: the property
    /// helpers are built once per type and shared by all its values.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ComplexTypeValueCostTests
    {
        [Test]
        public async Task ValuesOfOneTypeShareThePropertyHelpersAsync()
        {
            (MockResolver resolver, ExpandedNodeId typeId, _) = await LoadTypeAsync(
                "SharedUnion", StructureType.Union, IntFields(50)).ConfigureAwait(false);
            Assert.That(resolver.Factory.TryGetEncodeableType(typeId, out IEncodeableType? type), Is.True);

            var first = (BaseComplexType)type!.CreateInstance();
            var second = (BaseComplexType)type.CreateInstance();

            Assert.Multiple(() =>
            {
                Assert.That(second.GetPropertyEnumerator(), Is.SameAs(first.GetPropertyEnumerator()));
                Assert.That(second.GetPropertyCount(), Is.EqualTo(50));
                Assert.That(second.TypeId, Is.EqualTo(first.TypeId));
                Assert.That(second.TypeId.IsNull, Is.False);
                Assert.That(second.BinaryEncodingId, Is.EqualTo(first.BinaryEncodingId));
                Assert.That(second.BinaryEncodingId.IsNull, Is.False);
            });
        }

#if NET
        [Test]
        public async Task DecodingValuesOfAWideUnionDoesNotAllocatePerFieldAsync()
        {
            const int fieldCount = 1000;
            const int valueCount = 20000;
            (MockResolver resolver, _, NodeId encodingId) = await LoadTypeAsync(
                "WideUnion", StructureType.Union, IntFields(fieldCount)).ConfigureAwait(false);
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create(), resolver.Factory)
            {
                NamespaceUris = resolver.NamespaceUris
            };
            byte[] wire;
            using (var encoder = new BinaryEncoder(context))
            {
                // a Variant with an array of ExtensionObjects, each a Union
                // without a selected field: four bytes of body
                encoder.WriteByte(null, 0x96);
                encoder.WriteInt32(null, valueCount);
                for (int i = 0; i < valueCount; i++)
                {
                    encoder.WriteNodeId(null, encodingId);
                    encoder.WriteByte(null, 1);
                    encoder.WriteInt32(null, 4);
                    encoder.WriteUInt32(null, 0);
                }
                wire = encoder.CloseAndReturnBuffer()!;
            }
            // the first value of the type builds the shared helpers
            DecodeAll(wire, context);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int decoded = DecodeAll(wire, context);
            long perValue = (GC.GetAllocatedBytesForCurrentThread() - before) / valueCount;

            Assert.Multiple(() =>
            {
                Assert.That(decoded, Is.EqualTo(valueCount));
                // The value itself stores one Int32 per field and is copied
                // once into its ExtensionObject. Property helpers per value,
                // or a copy that reads every field, cost more than ten times
                // this.
                Assert.That(perValue, Is.LessThan(16 * fieldCount),
                    "Decoding a value must not build property helpers or read every field.");
            });
        }
#endif

        [Test]
        public async Task OptionalFieldMasksAreTheSameForEveryValueAsync()
        {
            StructureField[] fields = IntFields(4);
            fields[1].IsOptional = true;
            fields[3].IsOptional = true;
            (MockResolver resolver, ExpandedNodeId typeId, _) = await LoadTypeAsync(
                "Optional", StructureType.StructureWithOptionalFields, fields).ConfigureAwait(false);
            Assert.That(resolver.Factory.TryGetEncodeableType(typeId, out IEncodeableType? type), Is.True);
            var context = new ServiceMessageContext(NUnitTelemetryContext.Create(), resolver.Factory)
            {
                NamespaceUris = resolver.NamespaceUris
            };

            uint[] masks = new uint[3];
            for (int ii = 0; ii < masks.Length; ii++)
            {
                var value = (BaseComplexType)type!.CreateInstance();
                value["F3"] = new Variant(7);
                using var encoder = new BinaryEncoder(context);
                value.Encode(encoder);
                byte[] body = encoder.CloseAndReturnBuffer()!;
                masks[ii] = BitConverter.ToUInt32(body, 0);

                var decoded = (BaseComplexType)type.CreateInstance();
                using var decoder = new BinaryDecoder(body, context);
                decoded.Decode(decoder);
                Assert.That(decoded["F3"], Is.EqualTo(new Variant(7)));
                Assert.That(decoded.IsEqual(value), Is.True);
            }

            // F1 is the first optional field (bit 0), F3 the second (bit 1)
            Assert.That(masks, Is.All.EqualTo(2u));
        }

        private static int DecodeAll(byte[] wire, ServiceMessageContext context)
        {
            using var decoder = new BinaryDecoder(wire, context);
            Variant value = decoder.ReadVariant(null);
            int decoded = 0;
            if (value.TryGetValue(out ArrayOf<ExtensionObject> array))
            {
                foreach (ExtensionObject extension in array)
                {
                    if (extension.TryGetValue(out IEncodeable? body) && body is BaseComplexType)
                    {
                        decoded++;
                    }
                }
            }
            return decoded;
        }

        private static async Task<(MockResolver, ExpandedNodeId, NodeId)> LoadTypeAsync(
            string name,
            StructureType structureType,
            StructureField[] fields)
        {
            var resolver = new MockResolver();
            ushort ns = resolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            var node = new DataTypeNode
            {
                NodeId = new NodeId(9500, ns),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = LocalizedText.From(name),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = structureType,
                    Fields = fields
                })
            };
            var encodingId = new NodeId(9501, ns);
            var description = new ReferenceDescription
            {
                NodeId = encodingId,
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                BrowseName = QualifiedName.From(BrowseNames.DefaultBinary),
                DisplayName = LocalizedText.From(BrowseNames.DefaultBinary),
                IsForward = true,
                NodeClass = NodeClass.Object
            };
            resolver.DataTypeNodes[encodingId] = new Node(description);
            node.References += new ReferenceNode
            {
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IsInverse = false,
                TargetId = encodingId
            };
            resolver.DataTypeNodes[node.NodeId] = node;

            var typeSystem = new ComplexTypeSystem(
                resolver, new ComplexTypeBuilderFactory(), NUnitTelemetryContext.Create());
            bool loaded = await typeSystem.LoadAsync().ConfigureAwait(false);
            Assert.That(loaded, Is.True);
            return (resolver, NodeId.ToExpandedNodeId(node.NodeId, resolver.NamespaceUris), encodingId);
        }

        private static StructureField[] IntFields(int count)
        {
            return [.. Enumerable.Range(0, count).Select(i => new StructureField
            {
                Name = "F" + i,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            })];
        }
    }
}
