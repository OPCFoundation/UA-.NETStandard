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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Verifies the amended minimal UADP application header against independent wire bytes.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UadpApplicationPayloadHeaderCodecTests
    {
        private static IEnumerable<TestCaseData> PublisherVectors
        {
            get
            {
                yield return new TestCaseData(
                    new Variant((byte)0x2A),
                    BuiltInType.Byte,
                    "91800c2adead")
                    .SetName("BytePublisherId_HasIndependentApplicationHeaderVector");
                yield return new TestCaseData(
                    new Variant((ushort)0x1234),
                    BuiltInType.UInt16,
                    "91810c3412dead")
                    .SetName("UInt16PublisherId_HasIndependentApplicationHeaderVector");
                yield return new TestCaseData(
                    new Variant(0x12345678u),
                    BuiltInType.UInt32,
                    "91820c78563412dead")
                    .SetName("UInt32PublisherId_HasIndependentApplicationHeaderVector");
                yield return new TestCaseData(
                    new Variant(0x0102030405060708ul),
                    BuiltInType.UInt64,
                    "91830c0807060504030201dead")
                    .SetName("UInt64PublisherId_HasIndependentApplicationHeaderVector");
                yield return new TestCaseData(
                    new Variant("plant-Ω"),
                    BuiltInType.String,
                    "91840c08000000706c616e742dcea9dead")
                    .SetName("StringPublisherId_HasIndependentApplicationHeaderVector");
            }
        }

        /// <summary>
        /// Gets fixed inputs and literal wire vectors authored independently of the codec.
        /// </summary>
        public static IEnumerable<TestCaseData> Vectors => PublisherVectors;

        /// <summary>
        /// Keeps each PublisherId's scalar wire discriminator and NetworkMessageType bits fixed.
        /// </summary>
        [TestCaseSource(nameof(Vectors))]
        public void Encode_ValidPublisherId_MatchesIndependentLiteralVector(
            Variant publisherId,
            BuiltInType expectedType,
            string expectedHex)
        {
            ByteString encoded = UadpApplicationPayloadHeaderCodec.Encode(
                publisherId,
                ByteString.FromHexString("dead"),
                64);

            Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo(expectedHex));
            Assert.That(encoded.Span[0], Is.EqualTo(0x91));
            Assert.That(encoded.Span[2], Is.EqualTo(0x0C));
            Assert.That(encoded.Span[1] & 0x07, Is.EqualTo(expectedType switch
            {
                BuiltInType.Byte => 0,
                BuiltInType.UInt16 => 1,
                BuiltInType.UInt32 => 2,
                BuiltInType.UInt64 => 3,
                BuiltInType.String => 4,
                _ => -1
            }));
        }

        /// <summary>
        /// Matches the Part 14 amendment's assigned NetworkMessageType 3 example exactly.
        /// </summary>
        [Test]
        public void Encode_AmendmentUInt32PublisherExample_EmitsAssignedTypeThreeHeader()
        {
            ByteString encoded = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(1u),
                ByteString.Empty,
                7);

            Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo("91820c01000000"));
        }

        /// <summary>
        /// Confirms decoded values retain their exact scalar type and untouched payload bytes.
        /// </summary>
        [TestCaseSource(nameof(Vectors))]
        public void Decode_ValidLiteralVector_PreservesPublisherTypeAndPayload(
            Variant publisherId,
            BuiltInType expectedType,
            string expectedHex)
        {
            UadpApplicationPayload decoded = UadpApplicationPayloadHeaderCodec.Decode(
                ByteString.FromHexString(expectedHex),
                64);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.PublisherId.TypeInfo.BuiltInType, Is.EqualTo(expectedType));
                Assert.That(decoded.PublisherId, Is.EqualTo(publisherId));
                Assert.That(decoded.Payload.ToArray(), Is.EqualTo(new byte[] { 0xDE, 0xAD }));
                Assert.That(decoded.Payload.Length, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Extracts the ordinary UADP PublisherId without modifying the complete NetworkMessage.
        /// </summary>
        [TestCase("112adead", 0, BuiltInType.Byte, 0x2Aul, TestName = "DecodePublisherHeader_ByteDataMessage")]
        [TestCase("910201000000dead", 0, BuiltInType.UInt32, 1ul,
            TestName = "DecodePublisherHeader_UInt32DataMessage")]
        [TestCase("9182040100000002010000000100", 1, BuiltInType.UInt32, 1ul,
            TestName = "DecodePublisherHeader_UInt32DiscoveryRequest")]
        [TestCase("918208010000000201000100dead", 2, BuiltInType.UInt32, 1ul,
            TestName = "DecodePublisherHeader_UInt32DiscoveryResponse")]
        public void DecodePublisherHeader_OrdinaryUadp_ExtractsTypedPublisherId(
            string hex,
            byte expectedNetworkMessageType,
            BuiltInType publisherIdType,
            ulong publisherIdValue)
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString(hex));
            Variant expectedPublisherId = publisherIdType switch
            {
                BuiltInType.Byte => new Variant((byte)publisherIdValue),
                BuiltInType.UInt32 => new Variant((uint)publisherIdValue),
                _ => throw new ArgumentOutOfRangeException(nameof(publisherIdType))
            };

            Assert.Multiple(() =>
            {
                Assert.That(decoded.NetworkMessageType, Is.EqualTo(expectedNetworkMessageType));
                Assert.That(decoded.PublisherId, Is.EqualTo(expectedPublisherId));
            });
        }

        /// <summary>
        /// Extracts every requested writer ID from a UADP discovery request.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_DiscoveryRequest_ExtractsWriterIds()
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString("91820401000000020200000001000200"));

            Assert.That(decoded.DataSetWriterIds, Is.EqualTo(new ushort[] { 1, 2 }));
        }

        /// <summary>
        /// Extracts writer identities from metadata and writer-configuration discovery responses.
        /// </summary>
        [TestCase("918208010000000201000100dead", new ushort[] { 1 },
            TestName = "DecodePublisherHeader_MetadataResponse_ExtractsWriterId")]
        [TestCase("918208010000000301000200000001000200dead", new ushort[] { 1, 2 },
            TestName = "DecodePublisherHeader_WriterConfigurationResponse_ExtractsWriterIds")]
        public void DecodePublisherHeader_DiscoveryResponse_ExtractsWriterIds(string hex, ushort[] expectedIds)
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString(hex));

            Assert.That(decoded.DataSetWriterIds, Is.EqualTo(expectedIds));
        }

        /// <summary>
        /// Represents an omitted PublisherId so only an unambiguous wildcard association can match.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_HeaderWithoutPublisherId_ReturnsNullIdentity()
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString("01dead"));

            Assert.Multiple(() =>
            {
                Assert.That(decoded.NetworkMessageType, Is.Zero);
                Assert.That(decoded.PublisherId.IsNull, Is.True);
            });
        }

        /// <summary>
        /// Dispatches the amended application header through the same visible-header parser.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_ApplicationPayload_ExtractsTypedPublisherId()
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString("91820c01000000dead"));

            Assert.Multiple(() =>
            {
                Assert.That(decoded.NetworkMessageType, Is.EqualTo(3));
                Assert.That(decoded.PublisherId, Is.EqualTo(new Variant(1u)));
            });
        }

        /// <summary>
        /// Keeps all unassigned NetworkMessageType values unavailable to application dispatch.
        /// </summary>
        [TestCase("9180102a", TestName = "DecodePublisherHeader_ReservedTypeFour_RejectsHeader")]
        [TestCase("9180142a", TestName = "DecodePublisherHeader_ReservedTypeFive_RejectsHeader")]
        [TestCase("9180182a", TestName = "DecodePublisherHeader_ReservedTypeSix_RejectsHeader")]
        [TestCase("91801c2a", TestName = "DecodePublisherHeader_ReservedTypeSeven_RejectsHeader")]
        public void DecodePublisherHeader_ReservedNetworkMessageType_ReportsApplicationHeaderError(string hex)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(() =>
                UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(ByteString.FromHexString(hex)))!;

            Assert.That(exception.Error, Is.EqualTo(FlowCodecError.InvalidApplicationHeader));
        }

        /// <summary>
        /// Extracts source and writer identity after ordinary UADP optional and chunk flags.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_ChunkedUadpString_ExtractsPublisherId()
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString(
                    "f1a4010f0000007075626c69736865722d373330303101341245230000000000000000dead"));

            Assert.Multiple(() =>
            {
                Assert.That(decoded.NetworkMessageType, Is.Zero);
                Assert.That(decoded.PublisherId, Is.EqualTo(new Variant("publisher-73001")));
                Assert.That(decoded.WriterGroupId, Is.EqualTo(0x1234));
                Assert.That(decoded.DataSetWriterIds, Is.EqualTo(new ushort[] { 0x2345 }));
            });
        }

        /// <summary>
        /// Parses all payload-header writer IDs instead of selecting only the first writer.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_MultiWriterPayloadHeader_PreservesAllWriterIds()
        {
            UadpNetworkMessageHeader decoded = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                ByteString.FromHexString("f182002a000000010c000222002300dead"));

            Assert.Multiple(() =>
            {
                Assert.That(decoded.PublisherId, Is.EqualTo(new Variant(42u)));
                Assert.That(decoded.WriterGroupId, Is.EqualTo((ushort)12));
                Assert.That(decoded.DataSetWriterIds, Is.EqualTo(new ushort[] { 34, 35 }));
            });
        }

        /// <summary>
        /// Rejects a truncated group or payload header rather than using a partial route identity.
        /// </summary>
        [TestCase("f182012a00000001", TestName = "DecodePublisherHeader_TruncatedWriterGroupId")]
        [TestCase("f182012a000000010c0001", TestName = "DecodePublisherHeader_TruncatedDataSetWriterId")]
        [TestCase(
            "f1a4010f0000007075626c69736865722d3733303031013412452300000000",
            TestName = "DecodePublisherHeader_TruncatedTimestamp")]
        [TestCase(
            "9182042a00000002020000002200",
            TestName = "DecodePublisherHeader_TruncatedDiscoveryRequestWriterIds")]
        [TestCase(
            "9182082a000000030100020000002200",
            TestName = "DecodePublisherHeader_TruncatedDiscoveryResponseWriterIds")]
        public void DecodePublisherHeader_TruncatedRoutingFields_RejectsHeader(string hex)
        {
            AssertCodecFailure(
                FlowCodecError.Truncated,
                () => UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(ByteString.FromHexString(hex)));
        }

        /// <summary>
        /// Rejects reserved GroupHeader flags and an empty DataSet payload header.
        /// </summary>
        [TestCase(
            "f182002a00000010dead",
            TestName = "DecodePublisherHeader_ReservedGroupFlags")]
        [TestCase(
            "f182002a000000010c0000dead",
            TestName = "DecodePublisherHeader_EmptyDataSetPayloadHeader")]
        public void DecodePublisherHeader_InvalidRoutingFields_RejectsHeader(string hex)
        {
            AssertCodecFailure(
                FlowCodecError.InvalidApplicationHeader,
                () => UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(ByteString.FromHexString(hex)));
        }

        /// <summary>
        /// Refuses unsupported NetworkMessageTypes instead of guessing an association.
        /// </summary>
        [Test]
        public void DecodePublisherHeader_UnsupportedNetworkMessageType_RejectsHeader()
        {
            AssertCodecFailure(
                FlowCodecError.InvalidApplicationHeader,
                () => UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(
                    ByteString.FromHexString("91821001000000dead")));
        }

        /// <summary>
        /// Checks that encode followed by decode does not coerce any supported PublisherId scalar type.
        /// </summary>
        [TestCaseSource(nameof(Vectors))]
        public void EncodeThenDecode_ValidPublisherId_PreservesExactScalarType(
            Variant publisherId,
            BuiltInType expectedType,
            string expectedHex)
        {
            ByteString encoded = UadpApplicationPayloadHeaderCodec.Encode(
                publisherId,
                ByteString.FromHexString("dead"),
                64);
            UadpApplicationPayload decoded = UadpApplicationPayloadHeaderCodec.Decode(encoded, 64);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.PublisherId.TypeInfo.BuiltInType, Is.EqualTo(expectedType));
                Assert.That(decoded.PublisherId, Is.EqualTo(publisherId));
                Assert.That(encoded.ToHexString().ToLowerInvariant(), Is.EqualTo(expectedHex));
                Assert.That(decoded.Payload.ToArray(), Is.EqualTo(new byte[] { 0xDE, 0xAD }));
            });
        }

        /// <summary>
        /// Demonstrates that decoding returns a view into the original NetworkMessage memory.
        /// </summary>
        [Test]
        public void Decode_Payload_IsBorrowedViewOfNetworkMessage()
        {
            byte[] wire = [0x91, 0x80, 0x0C, 0x2A, 0xDE, 0xAD];
            UadpApplicationPayload decoded = UadpApplicationPayloadHeaderCodec.Decode(
                new ByteString(wire),
                wire.Length);

            Assert.That(decoded.Payload.ToArray(), Is.EqualTo(new byte[] { 0xDE, 0xAD }));
            wire[^1] = 0x5A;
            Assert.That(decoded.Payload.ToArray(), Is.EqualTo(new byte[] { 0xDE, 0x5A }));
        }

        /// <summary>
        /// Treats an uninitialized Variant and explicit UA null as missing PublisherIds.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Encode_MissingOrNullPublisherId_RejectsValue(bool explicitNull)
        {
            Variant publisherId = explicitNull ? Variant.Null : default;

            AssertCodecFailure(
                FlowCodecError.InvalidPublisherId,
                () => UadpApplicationPayloadHeaderCodec.Encode(publisherId, ByteString.Empty, 16));
        }

        /// <summary>
        /// Rejects zero for each permitted numeric scalar instead of treating it as a valid identifier.
        /// </summary>
        [TestCaseSource(nameof(ZeroPublisherIds))]
        public void Encode_ZeroNumericPublisherId_RejectsValue(Variant publisherId)
        {
            AssertCodecFailure(
                FlowCodecError.InvalidPublisherId,
                () => UadpApplicationPayloadHeaderCodec.Encode(publisherId, ByteString.Empty, 16));
        }

        /// <summary>
        /// Supplies each supported-width numeric zero as an independent invalid PublisherId case.
        /// </summary>
        public static IEnumerable<TestCaseData> ZeroPublisherIds
        {
            get
            {
                yield return new TestCaseData(new Variant((byte)0)).SetName("ByteZero");
                yield return new TestCaseData(new Variant((ushort)0)).SetName("UInt16Zero");
                yield return new TestCaseData(new Variant(0u)).SetName("UInt32Zero");
                yield return new TestCaseData(new Variant(0ul)).SetName("UInt64Zero");
            }
        }

        /// <summary>
        /// Rejects wire PublisherIds whose valid numeric discriminator carries the reserved zero value.
        /// </summary>
        [TestCase("91800c00", TestName = "Decode_ZeroBytePublisherId_ReportsInvalidPublisherId")]
        [TestCase("91810c0000", TestName = "Decode_ZeroUInt16PublisherId_ReportsInvalidPublisherId")]
        [TestCase("91820c00000000", TestName = "Decode_ZeroUInt32PublisherId_ReportsInvalidPublisherId")]
        [TestCase("91830c0000000000000000", TestName = "Decode_ZeroUInt64PublisherId_ReportsInvalidPublisherId")]
        public void Decode_ZeroNumericPublisherId_ReportsInvalidPublisherId(string hex)
        {
            AssertCodecFailure(
                FlowCodecError.InvalidPublisherId,
                () => UadpApplicationPayloadHeaderCodec.Decode(ByteString.FromHexString(hex), 64));
        }

        /// <summary>
        /// Rejects empty strings and values outside the permitted PublisherId scalar types.
        /// </summary>
        [TestCaseSource(nameof(InvalidPublisherIds))]
        public void Encode_EmptyOrUnsupportedPublisherId_RejectsValue(Variant publisherId)
        {
            AssertCodecFailure(
                FlowCodecError.InvalidPublisherId,
                () => UadpApplicationPayloadHeaderCodec.Encode(publisherId, ByteString.Empty, 16));
        }

        /// <summary>
        /// Gets invalid PublisherIds represented by a supported-but-empty string and unsupported scalar types.
        /// </summary>
        public static IEnumerable<TestCaseData> InvalidPublisherIds
        {
            get
            {
                yield return new TestCaseData(new Variant((string)null!)).SetName("NullString");
                yield return new TestCaseData(new Variant(string.Empty)).SetName("EmptyString");
                yield return new TestCaseData(new Variant(42)).SetName("Int32");
                yield return new TestCaseData(new Variant(true)).SetName("Boolean");
            }
        }

        /// <summary>
        /// Rejects bad fixed UADP flags independently from bad application NetworkMessageType bits.
        /// </summary>
        [TestCase("90000c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_InvalidUadpFlags_RejectsHeader")]
        [TestCase("91800b2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_InvalidNetworkMessageTypeBits_RejectsHeader")]
        [TestCase("91800d2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_ChunkMessageFlag_RejectsHeader")]
        [TestCase("91800e2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_PromotedFieldsFlag_RejectsHeader")]
        [TestCase("91802c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_ActionHeaderFlag_RejectsHeader")]
        [TestCase("91880c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_DataSetClassIdFlag_RejectsHeader")]
        [TestCase("91900c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_SecurityHeaderFlag_RejectsHeader")]
        [TestCase("91a00c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_TimestampFlag_RejectsHeader")]
        [TestCase("91c00c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_PicoSecondsFlag_RejectsHeader")]
        [TestCase("11000c2a", (int)FlowCodecError.InvalidApplicationHeader,
            TestName = "Decode_MissingExtendedFlagsEnable_RejectsHeader")]
        public void Decode_InvalidHeaderFlags_ReportsApplicationHeaderError(
            string hex,
            int expectedError)
        {
            AssertCodecFailure(
                (FlowCodecError)expectedError,
                () => UadpApplicationPayloadHeaderCodec.Decode(ByteString.FromHexString(hex), 64));
        }

        /// <summary>
        /// Reports unsupported PublisherId discriminator values after validating the fixed header.
        /// </summary>
        [Test]
        public void Decode_UnsupportedPublisherIdDiscriminator_ReportsTypeError()
        {
            AssertCodecFailure(
                FlowCodecError.InvalidPublisherIdType,
                () => UadpApplicationPayloadHeaderCodec.Decode(
                    ByteString.FromHexString("91850c2a"),
                    64));
        }

        /// <summary>
        /// Rejects every truncated fixed-width integer PublisherId representation.
        /// </summary>
        [TestCase("91800c", TestName = "Decode_TruncatedBytePublisherId_ReportsTruncated")]
        [TestCase("91810c34", TestName = "Decode_TruncatedUInt16PublisherId_ReportsTruncated")]
        [TestCase("91820c785634", TestName = "Decode_TruncatedUInt32PublisherId_ReportsTruncated")]
        [TestCase("91830c08070605040302", TestName = "Decode_TruncatedUInt64PublisherId_ReportsTruncated")]
        public void Decode_TruncatedIntegerPublisherId_ReportsTruncated(string hex)
        {
            AssertCodecFailure(
                FlowCodecError.Truncated,
                () => UadpApplicationPayloadHeaderCodec.Decode(ByteString.FromHexString(hex), 64));
        }

        /// <summary>
        /// Separates a short fixed header, truncated String length prefix, invalid lengths, and truncated text.
        /// </summary>
        [TestCase("91", (int)FlowCodecError.Truncated, TestName = "Decode_TruncatedFixedHeader_ReportsTruncated")]
        [TestCase("91840c0102", (int)FlowCodecError.Truncated,
            TestName = "Decode_TruncatedStringLengthPrefix_ReportsTruncated")]
        [TestCase("91840c00000000", (int)FlowCodecError.InvalidPublisherId,
            TestName = "Decode_ZeroStringLength_ReportsInvalidPublisherId")]
        [TestCase("91840cffffffff", (int)FlowCodecError.InvalidPublisherId,
            TestName = "Decode_NegativeStringLength_ReportsInvalidPublisherId")]
        [TestCase("91840c0200000041", (int)FlowCodecError.Truncated,
            TestName = "Decode_StringLengthExceedsAvailableBytes_ReportsTruncated")]
        [TestCase("91840c01000000ff", (int)FlowCodecError.InvalidUtf8,
            TestName = "Decode_InvalidUtf8PublisherId_ReportsInvalidUtf8")]
        public void Decode_MalformedStringOrHeader_ReportsSpecificFailure(
            string hex,
            int expectedError)
        {
            AssertCodecFailure(
                (FlowCodecError)expectedError,
                () => UadpApplicationPayloadHeaderCodec.Decode(ByteString.FromHexString(hex), 64));
        }

        /// <summary>
        /// Allows a complete message exactly at the configured bound and rejects the next byte.
        /// </summary>
        [Test]
        public void Encode_AtMaximumMessageSize_SucceedsAndOneByteOverFails()
        {
            var payload = ByteString.FromHexString("a1b2");
            ByteString exact = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant((byte)0x2A),
                payload,
                6);

            Assert.That(exact.ToHexString().ToLowerInvariant(), Is.EqualTo("91800c2aa1b2"));
            AssertCodecFailure(
                FlowCodecError.TooLarge,
                () => UadpApplicationPayloadHeaderCodec.Encode(new Variant((byte)0x2A), payload, 5));
        }

        /// <summary>
        /// Allows decoding at the exact configured message bound and rejects a one-byte excess.
        /// </summary>
        [Test]
        public void Decode_AtMaximumMessageSize_SucceedsAndOneByteOverFails()
        {
            ByteString exactMessage = ByteString.FromHexString("91800c2aa1b2");
            UadpApplicationPayload decoded = UadpApplicationPayloadHeaderCodec.Decode(exactMessage, 6);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.PublisherId, Is.EqualTo(new Variant((byte)0x2A)));
                Assert.That(decoded.Payload.ToArray(), Is.EqualTo(new byte[] { 0xA1, 0xB2 }));
            });
            AssertCodecFailure(
                FlowCodecError.TooLarge,
                () => UadpApplicationPayloadHeaderCodec.Decode(exactMessage, 5));
        }

        private static void AssertCodecFailure(FlowCodecError expectedError, Action action)
        {
            FlowCodecException exception = Assert.Throws<FlowCodecException>(action)!;
            Assert.That(exception.Error, Is.EqualTo(expectedError));
        }
    }
}
