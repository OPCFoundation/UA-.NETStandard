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

using System.IO;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Tests;

using OptionSetEncoder = Opc.Ua.Encoders.OptionSet;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    /// <summary>
    /// Tests for the OptionSet type created by DefaultComplexTypeBuilder.
    /// </summary>
    [TestFixture]
    [Category("DefaultComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DefaultOptionSetTests
    {
        private const ushort kNamespaceIndex = 3;
        private const string kTypeNamespace = Namespaces.OpcUaEncoderTests;
        private static readonly ExpandedNodeId s_typeId =
            new(1001u, kTypeNamespace);
        private static readonly ExpandedNodeId s_binaryId =
            new(1002u, kTypeNamespace);
        private static readonly ExpandedNodeId s_xmlId =
            new(1003u, kTypeNamespace);

        private DefaultComplexTypeFactory m_factory;
        private IComplexTypeBuilder m_builder;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_factory = new DefaultComplexTypeFactory();
            m_builder = m_factory.Create(
                kTypeNamespace,
                kNamespaceIndex,
                "OptionSetTests");
        }

        private static EnumDefinition CreateAccessRightsDefinition()
        {
            return new EnumDefinition
            {
                IsOptionSet = true,
                Fields =
                [
                    new EnumField { Name = "CurrentRead", Value = 0 },
                    new EnumField { Name = "CurrentWrite", Value = 1 },
                    new EnumField { Name = "HistoryRead", Value = 2 },
                    new EnumField { Name = "HistoryWrite", Value = 3 }
                ]
            };
        }

        [Test]
        public void AddOptionSetTypeReturnsRegisterableType()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();

            IEncodeableType encodeableType = m_builder.AddOptionSetType(
                QualifiedName.From("AccessRights"),
                s_typeId,
                s_binaryId,
                s_xmlId,
                ExpandedNodeId.Null,
                definition);

            Assert.That(encodeableType, Is.Not.Null);
            Assert.That(encodeableType.Type, Is.EqualTo(typeof(OptionSetEncoder)));
            Assert.That(
                encodeableType.XmlName,
                Is.EqualTo(new XmlQualifiedName("AccessRights", kTypeNamespace)));

            IEncodeable instance = encodeableType.CreateInstance();
            Assert.That(instance, Is.InstanceOf<OptionSetEncoder>());
            var optionSet = (OptionSetEncoder)instance;
            Assert.That(optionSet.TypeId, Is.EqualTo(s_typeId));
            Assert.That(optionSet.BinaryEncodingId, Is.EqualTo(s_binaryId));
            Assert.That(optionSet.XmlEncodingId, Is.EqualTo(s_xmlId));
            Assert.That(optionSet.Definition, Is.SameAs(definition));
            Assert.That(optionSet.Value.IsEmpty, Is.True);
            Assert.That(optionSet.ValidBits.IsEmpty, Is.True);
        }

        [Test]
        public void BitAccessorsByNameSetValueAndValidBits()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            var optionSet = new OptionSetEncoder(
                new XmlQualifiedName("AccessRights", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);

            optionSet["CurrentRead"] = true;
            optionSet["HistoryWrite"] = true;

            Assert.That(optionSet["CurrentRead"], Is.True);
            Assert.That(optionSet["CurrentWrite"], Is.False);
            Assert.That(optionSet["HistoryRead"], Is.False);
            Assert.That(optionSet["HistoryWrite"], Is.True);

            // Value bits 0 and 3 set => 0b1001 = 0x09
            Assert.That(optionSet.Value.Span.ToArray(), Is.EqualTo(new byte[] { 0x09 }));
            // ValidBits mirror any Set bit
            Assert.That(optionSet.ValidBits.Span.ToArray(), Is.EqualTo(new byte[] { 0x09 }));

            Assert.That(
                optionSet.GetSetFieldNames(),
                Is.EqualTo(new[] { "CurrentRead", "HistoryWrite" }));
        }

        [Test]
        public void BitAccessorByIndexOutsideValueIsFalse()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            var optionSet = new OptionSetEncoder(
                new XmlQualifiedName("AccessRights", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);

            Assert.That(optionSet[0], Is.False);
            Assert.That(optionSet[63], Is.False);
        }

        [Test]
        public void BinaryRoundTripPreservesValueAndValidBits()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(kTypeNamespace);

            // Register the type for decode via the factory.
            IEncodeableType type = m_builder.AddOptionSetType(
                QualifiedName.From("AccessRights"),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);
            context.Factory.Builder
                .AddEncodeableType(s_binaryId, type)
                .AddEncodeableType(s_typeId, type)
                .AddEncodeableType(type)
                .Commit();

            var source = (OptionSetEncoder)type.CreateInstance();
            source["CurrentWrite"] = true;
            source["HistoryRead"] = true;

            byte[] buffer;
            using (var stream = new MemoryStream())
            using (var encoder = new BinaryEncoder(stream, context, false))
            {
                encoder.WriteExtensionObject("os", new ExtensionObject(source, true));
                buffer = stream.ToArray();
            }

            OptionSetEncoder decoded;
            using (var stream = new MemoryStream(buffer))
            using (var decoder = new BinaryDecoder(stream, context))
            {
                ExtensionObject eo = decoder.ReadExtensionObject("os");
                Assert.That(eo.IsNull, Is.False);
                Assert.That(eo.TryGetEncodeable(out IEncodeable body), Is.True);
                decoded = body as OptionSetEncoder;
                Assert.That(decoded, Is.Not.Null, "Decoded body is not an OptionSet.");
            }

            Assert.That(decoded.Value.Span.ToArray(),
                Is.EqualTo(source.Value.Span.ToArray()));
            Assert.That(decoded.ValidBits.Span.ToArray(),
                Is.EqualTo(source.ValidBits.Span.ToArray()));
            Assert.That(decoded["CurrentWrite"], Is.True);
            Assert.That(decoded["HistoryRead"], Is.True);
            Assert.That(decoded["CurrentRead"], Is.False);
            Assert.That(decoded["HistoryWrite"], Is.False);
        }

        [Test]
        public void WireFormatDecodesHandcraftedBytes()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(kTypeNamespace);

            IEncodeableType type = m_builder.AddOptionSetType(
                QualifiedName.From("AccessRights"),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);
            context.Factory.Builder
                .AddEncodeableType(s_binaryId, type)
                .AddEncodeableType(s_typeId, type)
                .AddEncodeableType(type)
                .Commit();

            // Build a body matching Part 6 encoding:
            //   Value     : ByteString  [0x03]
            //   ValidBits : ByteString  [0x03]
            byte[] body;
            using (var bodyStream = new MemoryStream())
            using (var bodyEncoder = new BinaryEncoder(bodyStream, context, false))
            {
                bodyEncoder.WriteByteString(null, new byte[] { 0x03 });
                bodyEncoder.WriteByteString(null, new byte[] { 0x03 });
                body = bodyStream.ToArray();
            }

            var eo = new ExtensionObject(s_binaryId, ByteString.From(body));
            byte[] buffer;
            using (var stream = new MemoryStream())
            using (var encoder = new BinaryEncoder(stream, context, false))
            {
                encoder.WriteExtensionObject("os", eo);
                buffer = stream.ToArray();
            }

            using var decodeStream = new MemoryStream(buffer);
            using var decoder = new BinaryDecoder(decodeStream, context);
            ExtensionObject decodedEo = decoder.ReadExtensionObject("os");
            Assert.That(decodedEo.TryGetEncodeable(out IEncodeable decodedBody), Is.True);
            var decoded = decodedBody as OptionSetEncoder;
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded[0], Is.True);
            Assert.That(decoded[1], Is.True);
            Assert.That(decoded[2], Is.False);
            Assert.That(
                decoded.GetSetFieldNames(),
                Is.EqualTo(new[] { "CurrentRead", "CurrentWrite" }));
        }

        [Test]
        public void CreateInstanceProducesFreshInstance()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            IEncodeableType type = m_builder.AddOptionSetType(
                QualifiedName.From("AccessRights"),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);

            var a = (OptionSetEncoder)type.CreateInstance();
            a["CurrentRead"] = true;
            var b = (OptionSetEncoder)type.CreateInstance();

            Assert.That(b.Value.IsEmpty, Is.True);
            Assert.That(b.ValidBits.IsEmpty, Is.True);
            Assert.That(a.Definition, Is.SameAs(b.Definition));
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            EnumDefinition definition = CreateAccessRightsDefinition();
            var optionSet = new OptionSetEncoder(
                new XmlQualifiedName("AccessRights", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                definition);
            optionSet["HistoryRead"] = true;

            var clone = (OptionSetEncoder)optionSet.Clone();
            Assert.That(clone, Is.Not.SameAs(optionSet));
            Assert.That(clone["HistoryRead"], Is.True);

            clone["CurrentRead"] = true;
            Assert.That(optionSet["CurrentRead"], Is.False,
                "Mutating the clone must not affect the source.");
        }

        [Test]
        public void ByteLengthIsDerivedFromHighestBit()
        {
            // Bits 0..3 => ceil(4 / 8) = 1 byte.
            var oneByte = new OptionSetEncoder(
                new XmlQualifiedName("AccessRights", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                CreateAccessRightsDefinition());
            Assert.That(oneByte.ByteLength, Is.EqualTo(1));

            // Bit 10 => ceil(11 / 8) = 2 bytes.
            var twoBytes = new OptionSetEncoder(
                new XmlQualifiedName("TwoByteOptionSet", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                new EnumDefinition
                {
                    IsOptionSet = true,
                    Fields =
                    [
                        new EnumField { Name = "Bit0", Value = 0 },
                        new EnumField { Name = "Bit10", Value = 10 }
                    ]
                });
            Assert.That(twoBytes.ByteLength, Is.EqualTo(2));
        }

        [Test]
        public void SetBitOutsideFixedLengthThrows()
        {
            // AccessRights-style OptionSet has max bit 3, so ByteLength == 1.
            var optionSet = new OptionSetEncoder(
                new XmlQualifiedName("AccessRights", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                CreateAccessRightsDefinition());

            // Bit 8 is outside the 1-byte fixed length — must throw per Part 3 §8.40.
            Assert.That(
                () => optionSet[8] = true,
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void SetBitPadsValueAndValidBitsToFixedLength()
        {
            // Max bit 10 => ByteLength 2.
            var optionSet = new OptionSetEncoder(
                new XmlQualifiedName("TwoByteOptionSet", kTypeNamespace),
                s_typeId, s_binaryId, s_xmlId, ExpandedNodeId.Null,
                new EnumDefinition
                {
                    IsOptionSet = true,
                    Fields =
                    [
                        new EnumField { Name = "Bit0", Value = 0 },
                        new EnumField { Name = "Bit10", Value = 10 }
                    ]
                });

            optionSet[0] = true;

            // Even though we only touched byte 0, the fixed-length invariant
            // means Value and ValidBits are materialized to 2 bytes.
            Assert.That(optionSet.Value.Length, Is.EqualTo(2));
            Assert.That(optionSet.ValidBits.Length, Is.EqualTo(2));
        }
    }
}
