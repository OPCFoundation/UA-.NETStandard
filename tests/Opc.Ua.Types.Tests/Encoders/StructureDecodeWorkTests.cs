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
using System.IO;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Encoders;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// The binary decoder bounds the work of decoding structures whose layout
    /// comes from a DataTypeDefinition by the length of its input. Such
    /// structures can be empty, so a nesting of them can decode from no bytes.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureDecodeWorkTests
    {
        private const string kNs = "urn:test:structure-work";

        [Test]
        public void NestedStructuresWithinTheBudgetDecodeFromEmptyInput()
        {
            ServiceMessageContext context = CreateContext(10, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, top);

            Assert.That(CountInstances((Structure)value), Is.EqualTo((1 << 11) - 1));
        }

        [Test]
        public void NestedStructuresBeyondTheBudgetFailWithEncodingLimitsExceeded()
        {
            // 2^18 - 1 instances, several times the allowance, from zero bytes.
            ServiceMessageContext context = CreateContext(17, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, top));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void NestedStructuresBeyondTheBudgetFailInsideAnExtensionObject()
        {
            ServiceMessageContext context = CreateContext(17, out _);
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(EncodingIdOf(17), context.NamespaceUris));
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObject(null));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void BinaryBodiesInOneJsonMessageShareTheAllowance()
        {
            // T15 decodes 131069 units of work from an empty body: one body is
            // within the allowance, two bodies of one message are not.
            ServiceMessageContext context = CreateContext(15, out _);
            string body = "{\"UaTypeId\":\"nsu=" + kNs + ";s=T15\",\"UaEncoding\":1,\"UaBody\":\"\"}";

            using (var decoder = new JsonDecoder("{\"F\":[" + body + "]}", context))
            {
                ArrayOf<IEncodeable> one = decoder.ReadEncodeableArrayAsExtensionObjects<IEncodeable>("F");
                Assert.That(one.Count, Is.EqualTo(1));
            }

            using (var decoder = new JsonDecoder("{\"F\":[" + body + "," + body + "]}", context))
            {
                ServiceResultException? error = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadEncodeableArrayAsExtensionObjects<IEncodeable>("F"));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [Test]
        public void LongArrayOfEmptyStructuresDecodes()
        {
            ServiceMessageContext context = CreateContext(0, out ExpandedNodeId empty);
            const int count = 65535;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(null, empty);

            Assert.That(values.Count, Is.EqualTo(count));
        }

        [Test]
        public void StructuresBackedByInputDecodeBeyondTheAllowance()
        {
            // An array of structures with an Int32 field: the work grows with
            // the input, so the allowance does not limit it.
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var typeId = new NodeId("WithValue", (ushort)ns);
            Structure prototype = CreateStructure(
                context,
                typeId,
                [new StructureField { Name = "Value", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            context.Factory.Builder.AddEncodeableType(prototype).Commit();
            int count = (int)BinaryDecoder.kStructureWorkAllowance + 1000;
            context.MaxArrayLength = count;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteInt32(null, i);
                }
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(buffer, context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(((Structure)values[count - 1])["Value"], Is.EqualTo(new Variant(count - 1)));
        }

        [Test]
        public void ArraysOfUnionsAndOptionalFieldStructuresChargeOnlyEncodedFields(
            [Values(StructureType.Union, StructureType.StructureWithOptionalFields)] StructureType structureType)
        {
            // Each element encodes one of 16 members in 8 bytes. Charging all
            // declared members would exceed the input length plus the allowance.
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var typeId = new NodeId("Sparse", (ushort)ns);
            var fields = new List<StructureField>();
            var fieldTypes = new Dictionary<string, BuiltInType>();
            for (int i = 1; i <= 16; i++)
            {
                fields.Add(new StructureField
                {
                    Name = "F" + i,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    IsOptional = structureType == StructureType.StructureWithOptionalFields
                });
                fieldTypes["F" + i] = BuiltInType.Int32;
            }
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = structureType,
                Fields = fields.ToArrayOf()
            };
            var xmlName = new XmlQualifiedName("Sparse", kNs);
            ExpandedNodeId expandedTypeId = NodeId.ToExpandedNodeId(typeId, context.NamespaceUris);
            var binaryId = new ExpandedNodeId("Sparse_Binary", kNs);
            var xmlId = new ExpandedNodeId("Sparse_Xml", kNs);
            Structure prototype = structureType == StructureType.Union
                ? new Opc.Ua.Encoders.Union(xmlName, expandedTypeId, binaryId, xmlId, definition, fieldTypes)
                : new StructureWithOptionalFields(xmlName, expandedTypeId, binaryId, xmlId, definition, fieldTypes);
            context.Factory.Builder.AddEncodeableType(prototype).Commit();
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

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(null, expandedTypeId);

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(((Structure)values[count - 1])["F1"], Is.EqualTo(new Variant(count - 1)));
        }

        [Test]
        public void ExtensionObjectArrayDecodesWithinItsInputLength([Values] bool forwardOnly)
        {
            // Each body holds a Boolean and three empty structures: 8 units of
            // work from 1 body byte, within the length of the whole element.
            ServiceMessageContext context = CreateContext(0, out _);
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var empty = new NodeId("T0", (ushort)ns);
            var typeId = new NodeId("Holder", (ushort)ns);
            context.Factory.Builder.AddEncodeableType(CreateStructure(
                context,
                typeId,
                [
                    new StructureField { Name = "Flag", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "A", DataType = empty, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "B", DataType = empty, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "C", DataType = empty, ValueRank = ValueRanks.Scalar }
                ],
                new Dictionary<string, BuiltInType> { ["Flag"] = BuiltInType.Boolean })).Commit();
            const int count = 20000;
            context.MaxArrayLength = count;
            NodeId encodingId = ExpandedNodeId.ToNodeId(new ExpandedNodeId("Holder_Binary", kNs), context.NamespaceUris);
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteNodeId(null, encodingId);
                    encoder.WriteByte(null, 1);
                    encoder.WriteInt32(null, 1);
                    encoder.WriteBoolean(null, true);
                }
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using BinaryDecoder decoder = forwardOnly
                ? new BinaryDecoder(new ForwardOnlyStream(buffer), context)
                : new BinaryDecoder(buffer, context);

            ArrayOf<ExtensionObject> values = decoder.ReadExtensionObjectArray(null);

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(values[count - 1].TryGetValue(out Structure? last), Is.True);
            Assert.That(last!["Flag"], Is.EqualTo(new Variant(true)));
        }

        [Test]
        public void OpaqueBodiesCountTowardTheInputLength([Values] bool forwardOnly)
        {
            // Three opaque bodies of 60,000 bytes and an empty T16 body, whose
            // 262,141 units of work fit the input length plus the allowance.
            ServiceMessageContext context = CreateContext(16, out _);
            const int opaqueLength = 60000;
            NodeId opaqueId = ExpandedNodeId.ToNodeId(
                new ExpandedNodeId("Opaque_Binary", kNs),
                context.NamespaceUris);
            NodeId encodingId = ExpandedNodeId.ToNodeId(EncodingIdOf(16), context.NamespaceUris);
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, 4);
                for (int i = 0; i < 3; i++)
                {
                    encoder.WriteNodeId(null, opaqueId);
                    encoder.WriteByte(null, 1);
                    encoder.WriteByteString(null, new byte[opaqueLength].ToByteString());
                }
                encoder.WriteNodeId(null, encodingId);
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using BinaryDecoder decoder = forwardOnly
                ? new BinaryDecoder(new ForwardOnlyStream(buffer), context)
                : new BinaryDecoder(buffer, context);

            ArrayOf<ExtensionObject> values = decoder.ReadExtensionObjectArray(null);

            Assert.That(values.Count, Is.EqualTo(4));
            Assert.That(values[3].TryGetValue(out Structure? last), Is.True);
            Assert.That(CountInstances(last!), Is.EqualTo((1 << 17) - 1));
        }

        [Test]
        public void NestedStructuresBeyondTheBudgetFailOnAnEmptyForwardOnlyStream(
            [Values(0, 2 * 1024 * 1024)] int maxMessageSize)
        {
            ServiceMessageContext context = CreateContext(17, out ExpandedNodeId top);
            context.MaxMessageSize = maxMessageSize;
            using var decoder = new BinaryDecoder(new ForwardOnlyStream(Array.Empty<byte>()), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, top));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void StructuresBackedByAForwardOnlyStreamDecodeBeyondTheAllowance()
        {
            // The credit of a forward-only stream grows with the bytes read.
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var typeId = new NodeId("WithValue", (ushort)ns);
            Structure prototype = CreateStructure(
                context,
                typeId,
                [new StructureField { Name = "Value", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            context.Factory.Builder.AddEncodeableType(prototype).Commit();
            int count = (int)BinaryDecoder.kStructureWorkAllowance + 1000;
            context.MaxArrayLength = count;
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, count);
                for (int i = 0; i < count; i++)
                {
                    encoder.WriteInt32(null, i);
                }
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(new ForwardOnlyStream(buffer), context);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(((Structure)values[count - 1])["Value"], Is.EqualTo(new Variant(count - 1)));
        }

        private static int CountInstances(Structure structure)
        {
            int count = 1;
            foreach (Field field in structure.PropertyList)
            {
                object? value = field.Value.AsBoxedObject();
                if (value is ExtensionObject extension &&
                    extension.TryGetValue(out Structure? body))
                {
                    value = body;
                }
                if (value is Structure nested)
                {
                    count += CountInstances(nested);
                }
            }
            return count;
        }

        /// <summary>
        /// Registers T0 without fields and T1 to Tn, where each level has two
        /// fields of the type one level below.
        /// </summary>
        private static ServiceMessageContext CreateContext(int levels, out ExpandedNodeId top)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            for (int level = 0; level <= levels; level++)
            {
                var fields = new List<StructureField>();
                if (level > 0)
                {
                    var child = new NodeId("T" + (level - 1), (ushort)ns);
                    fields.Add(new StructureField { Name = "A", DataType = child, ValueRank = ValueRanks.Scalar });
                    fields.Add(new StructureField { Name = "B", DataType = child, ValueRank = ValueRanks.Scalar });
                }
                builder.AddEncodeableType(CreateStructure(
                    context,
                    new NodeId("T" + level, (ushort)ns),
                    fields.ToArrayOf(),
                    []));
            }
            builder.Commit();
            top = new ExpandedNodeId("T" + levels, kNs);
            return context;
        }

        /// <summary>
        /// A stream that can only be read forward and has no known length.
        /// </summary>
        private sealed class ForwardOnlyStream(byte[] data) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return m_inner.Read(buffer, offset, count);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_inner.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly MemoryStream m_inner = new(data, false);
        }
        private static ExpandedNodeId EncodingIdOf(int level)
        {
            return new ExpandedNodeId("T" + level + "_Binary", kNs);
        }

        private static Structure CreateStructure(
            ServiceMessageContext context,
            NodeId typeId,
            ArrayOf<StructureField> fields,
            Dictionary<string, BuiltInType> fieldTypes)
        {
            string name = typeId.TryGetValue(out string s) ? s : "T";
            return new Structure(
                new XmlQualifiedName(name, kNs),
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris),
                new ExpandedNodeId(name + "_Binary", kNs),
                new ExpandedNodeId(name + "_Xml", kNs),
                new StructureDefinition
                {
                    BaseDataType = DataTypeIds.Structure,
                    StructureType = StructureType.Structure,
                    Fields = fields
                },
                fieldTypes);
        }
    }
}
