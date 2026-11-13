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
    /// The binary decoder limits the encodeables one message decodes to the
    /// larger of the maximum message size and the input length, plus a fixed
    /// allowance. Structures whose layout comes from a DataTypeDefinition can
    /// be empty, so a nesting of them can decode from no bytes.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureDecodeWorkTests
    {
        private const string kNs = "urn:test:structure-work";

        // With this maximum message size a message without input may decode
        // 4096 + 131072 = 135168 encodeables: T16 (131071) but not T17 (262143).
        private const int kMaxMessageSize = 4096;

        [Test]
        public void NestedStructuresWithinTheLimitDecodeFromEmptyInput()
        {
            ServiceMessageContext context = CreateContext(16, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, top);

            Assert.That(CountInstances((Structure)value), Is.EqualTo((1 << 17) - 1));
        }

        [Test]
        public void NestedStructuresBeyondTheLimitFailWithEncodingLimitsExceeded()
        {
            ServiceMessageContext context = CreateContext(17, out ExpandedNodeId top);
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadEncodeable<IEncodeable>(null, top));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void NestedStructuresBeyondTheLimitFailInsideAnExtensionObject([Values] bool forwardOnly)
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
            using BinaryDecoder decoder = CreateDecoder(buffer, context, forwardOnly);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObject(null));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void NestedStructuresDecodeUpToTheDefaultLimitWithoutAMaximumMessageSize()
        {
            // Without a maximum message size the default one applies: 2,228,224
            // encodeables, enough for T17.
            ServiceMessageContext context = CreateContext(17, out ExpandedNodeId top);
            context.MaxMessageSize = 0;
            using var decoder = new BinaryDecoder(Array.Empty<byte>(), context);

            IEncodeable value = decoder.ReadEncodeable<IEncodeable>(null, top);

            Assert.That(CountInstances((Structure)value), Is.EqualTo((1 << 18) - 1));
        }

        [Test]
        public void BinaryBodiesInOneJsonMessageShareTheLimit()
        {
            // One empty T16 body is within the limit, two bodies of one message
            // are not.
            ServiceMessageContext context = CreateContext(16, out _);
            string body = "{\"UaTypeId\":\"nsu=" + kNs + ";s=T16\",\"UaEncoding\":1,\"UaBody\":\"\"}";

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
        public void ExtensionObjectBodiesOfOneMessageShareTheLimit([Values] bool forwardOnly)
        {
            // Two empty T16 bodies exceed the limit of one message, whether the
            // decoder reads them from its buffer or replays them from a stream.
            ServiceMessageContext context = CreateContext(16, out _);
            byte[] buffer = EncodeBodies(context, 16, 0, 2);

            using (BinaryDecoder decoder = CreateDecoder(EncodeBodies(context, 16, 0, 1), context, forwardOnly))
            {
                Assert.That(decoder.ReadExtensionObjectArray(null).Count, Is.EqualTo(1));
            }
            using (BinaryDecoder decoder = CreateDecoder(buffer, context, forwardOnly))
            {
                ServiceResultException? error = Assert.Throws<ServiceResultException>(
                    () => decoder.ReadExtensionObjectArray(null));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [Test]
        public void MessagesLongerThanTheMaximumSizeDecodeOneEncodeablePerByte([Values] bool forwardOnly)
        {
            // Three opaque bodies of 60,000 bytes raise the limit to the input
            // length, so an empty T17 body after them decodes.
            ServiceMessageContext context = CreateContext(17, out _);
            using BinaryDecoder decoder = CreateDecoder(EncodeBodies(context, 17, 3, 1), context, forwardOnly);

            ArrayOf<ExtensionObject> values = decoder.ReadExtensionObjectArray(null);

            Assert.That(values.Count, Is.EqualTo(4));
            Assert.That(values[3].TryGetValue(out Structure? last), Is.True);
            Assert.That(CountInstances(last!), Is.EqualTo((1 << 18) - 1));
        }

        [Test]
        public void DeferredNotificationBodiesShareTheLimitOfTheResponse([Values] bool defer)
        {
            // Three opaque bodies of 60,000 bytes and an event notification with
            // an empty T17 body decode alike, eagerly or on first use.
            ServiceMessageContext context = CreateContext(17, out _);
            context.DeferNotificationBodies = defer;
            context.Factory.AddEncodeableType(typeof(EventNotificationList));
            context.Factory.AddEncodeableType(typeof(EventFieldList));
            byte[] events;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, 1); // events
                encoder.WriteUInt32(null, 1); // client handle
                encoder.WriteInt32(null, 1); // event fields
                encoder.WriteByte(null, (byte)BuiltInType.ExtensionObject);
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(EncodingIdOf(17), context.NamespaceUris));
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                events = encoder.CloseAndReturnBuffer()!;
            }
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, 4);
                WriteOpaqueBodies(encoder, context, 3);
                encoder.WriteNodeId(null, new NodeId(916u)); // EventNotificationList_Encoding_DefaultBinary
                encoder.WriteByte(null, 1);
                encoder.WriteByteString(null, events.ToByteString());
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            ArrayOf<ExtensionObject> values;
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                values = decoder.ReadExtensionObjectArray(null);
            }

            Assert.That(values.Count, Is.EqualTo(4));
            Assert.That(values[3].TryGetValue(out EventNotificationList? list), Is.True);
            Assert.That(list!.Events[0].EventFields[0].TryGetValue(out ExtensionObject field), Is.True);
            Assert.That(field.TryGetValue(out Structure? last), Is.True);
            Assert.That(CountInstances(last!), Is.EqualTo((1 << 18) - 1));
        }

        [Test]
        public void DeferredNotificationBodiesBeyondTheLimitAreNotDecoded()
        {
            // The event notification decodes on first use, after the decoder of
            // the response counted an empty T16 body.
            ServiceMessageContext context = CreateContext(16, out _);
            context.DeferNotificationBodies = true;
            context.Factory.AddEncodeableType(typeof(EventNotificationList));
            context.Factory.AddEncodeableType(typeof(EventFieldList));
            byte[] events;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, 1); // events
                encoder.WriteUInt32(null, 1); // client handle
                encoder.WriteInt32(null, 1); // event fields
                encoder.WriteByte(null, (byte)BuiltInType.ExtensionObject);
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(EncodingIdOf(16), context.NamespaceUris));
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                events = encoder.CloseAndReturnBuffer()!;
            }
            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteInt32(null, 2);
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(EncodingIdOf(16), context.NamespaceUris));
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
                encoder.WriteNodeId(null, new NodeId(916u)); // EventNotificationList_Encoding_DefaultBinary
                encoder.WriteByte(null, 1);
                encoder.WriteByteString(null, events.ToByteString());
                buffer = encoder.CloseAndReturnBuffer()!;
            }
            ArrayOf<ExtensionObject> values;
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                values = decoder.ReadExtensionObjectArray(null);
            }

            Assert.That(values.Count, Is.EqualTo(2));
            Assert.That(values[0].TryGetValue(out Structure? first), Is.True);
            Assert.That(CountInstances(first!), Is.EqualTo((1 << 17) - 1));
            Assert.That(values[1].TryGetValue(out EventNotificationList? _), Is.False);
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
        public void StructuresBackedByInputDecodeBeyondTheAllowance([Values] bool forwardOnly)
        {
            // An array of structures with an Int32 field: the limit grows with
            // the input, also as it is read from a stream.
            ServiceMessageContext context = CreateContext(0, out _);
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var typeId = new NodeId("WithValue", (ushort)ns);
            Structure prototype = CreateStructure(
                context,
                typeId,
                [new StructureField { Name = "Value", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }],
                new Dictionary<string, BuiltInType> { ["Value"] = BuiltInType.Int32 });
            context.Factory.Builder.AddEncodeableType(prototype).Commit();
            int count = (int)BinaryDecoder.kEncodeableAllowance + 10000;
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
            using BinaryDecoder decoder = CreateDecoder(buffer, context, forwardOnly);

            ArrayOf<IEncodeable> values = decoder.ReadEncodeableArray<IEncodeable>(
                null,
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(((Structure)values[count - 1])["Value"], Is.EqualTo(new Variant(count - 1)));
        }

        [Test]
        public void ManySmallExtensionObjectBodiesDecode([Values] bool forwardOnly)
        {
            // Each body holds a Boolean and 32 empty structures, 33 encodeables
            // in 27 bytes. A stream replays each body with its own decoder,
            // which reserves 64 units for them and returns the 31 it did not
            // use; keeping them would exceed the limit of the message.
            ServiceMessageContext context = CreateContext(0, out _);
            int ns = context.NamespaceUris.GetIndexOrAppend(kNs);
            var empty = new NodeId("T0", (ushort)ns);
            var typeId = new NodeId("Holder", (ushort)ns);
            var fields = new List<StructureField>
            {
                new() { Name = "Flag", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar }
            };
            for (int i = 0; i < 32; i++)
            {
                fields.Add(new StructureField { Name = "E" + i, DataType = empty, ValueRank = ValueRanks.Scalar });
            }
            context.Factory.Builder.AddEncodeableType(CreateStructure(
                context,
                typeId,
                fields.ToArrayOf(),
                new Dictionary<string, BuiltInType> { ["Flag"] = BuiltInType.Boolean })).Commit();
            const int count = 15000;
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
            long limit = buffer.Length + BinaryDecoder.kEncodeableAllowance;
            Assert.That(limit, Is.GreaterThan(33L * count));
            Assert.That(limit, Is.LessThan(64L * count));
            using BinaryDecoder decoder = CreateDecoder(buffer, context, forwardOnly);

            ArrayOf<ExtensionObject> values = decoder.ReadExtensionObjectArray(null);

            Assert.That(values.Count, Is.EqualTo(count));
            Assert.That(values[count - 1].TryGetValue(out Structure? last), Is.True);
            Assert.That(last!["Flag"], Is.EqualTo(new Variant(true)));
        }

        private static BinaryDecoder CreateDecoder(byte[] buffer, ServiceMessageContext context, bool forwardOnly)
        {
            return forwardOnly
                ? new BinaryDecoder(new ForwardOnlyStream(buffer), context)
                : new BinaryDecoder(buffer, context);
        }

        /// <summary>
        /// An array of opaque bodies of 60,000 bytes followed by empty bodies
        /// of type T<paramref name="level"/>.
        /// </summary>
        private static byte[] EncodeBodies(ServiceMessageContext context, int level, int opaque, int empty)
        {
            NodeId encodingId = ExpandedNodeId.ToNodeId(
                EncodingIdOf(level),
                context.NamespaceUris);
            using var encoder = new BinaryEncoder(context);
            encoder.WriteInt32(null, opaque + empty);
            WriteOpaqueBodies(encoder, context, opaque);
            for (int i = 0; i < empty; i++)
            {
                encoder.WriteNodeId(null, encodingId);
                encoder.WriteByte(null, 1);
                encoder.WriteInt32(null, 0);
            }
            return encoder.CloseAndReturnBuffer()!;
        }

        private static void WriteOpaqueBodies(BinaryEncoder encoder, ServiceMessageContext context, int count)
        {
            NodeId opaqueId = ExpandedNodeId.ToNodeId(
                new ExpandedNodeId("Opaque_Binary", kNs),
                context.NamespaceUris);
            for (int i = 0; i < count; i++)
            {
                encoder.WriteNodeId(null, opaqueId);
                encoder.WriteByte(null, 1);
                encoder.WriteByteString(null, new byte[60000].ToByteString());
            }
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
            context.MaxMessageSize = kMaxMessageSize;
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
