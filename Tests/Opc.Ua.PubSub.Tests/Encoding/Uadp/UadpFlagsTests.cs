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
 *
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

using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using UadpExtendedFlags2 = Opc.Ua.PubSub.Encoding.Uadp.ExtendedFlags2EncodingMask;
using UadpGroupFlags = Opc.Ua.PubSub.Encoding.Uadp.GroupFlagsEncodingMask;
using UadpHeaderFlags = Opc.Ua.PubSub.Encoding.Uadp.UadpFlagsEncodingMask;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Coverage for the per-byte UADP flag enums and their helper
    /// extensions: combine/split round-trips, publisher-id type
    /// mapping, field encoding and dataset message-type mapping.
    /// </summary>
    [TestFixture]
    [TestSpec("A.2.2.4")]
    [TestSpec("A.2.1.4")]
    public class UadpFlagsTests
    {
        [Test]
        [TestCase((byte)1, UadpHeaderFlags.PublisherIdEnabled)]
        [TestCase((byte)1, UadpHeaderFlags.PublisherIdEnabled |
            UadpHeaderFlags.GroupHeaderEnabled)]
        [TestCase((byte)1, UadpHeaderFlags.PublisherIdEnabled |
            UadpHeaderFlags.GroupHeaderEnabled |
            UadpHeaderFlags.PayloadHeaderEnabled |
            UadpHeaderFlags.ExtendedFlags1Enabled)]
        public void UadpFlags_CombineSplit_RoundTrips(
            byte version, UadpHeaderFlags flags)
        {
            byte combined = UadpFlagsEncodingMaskExtensions.Combine(version, flags);
            (byte v, UadpHeaderFlags f) =
                UadpFlagsEncodingMaskExtensions.Split(combined);
            Assert.That(v, Is.EqualTo(version));
            Assert.That(f, Is.EqualTo(flags));
        }

        [Test]
        public void UadpFlags_Combine_TruncatesInvalidVersion()
        {
            byte combined = UadpFlagsEncodingMaskExtensions.Combine(0x10, 0);
            (byte v, _) = UadpFlagsEncodingMaskExtensions.Split(combined);
            Assert.That(v, Is.Zero);
        }

        [Test]
        [TestCase(PublisherIdType.Byte)]
        [TestCase(PublisherIdType.UInt16)]
        [TestCase(PublisherIdType.UInt32)]
        [TestCase(PublisherIdType.UInt64)]
        [TestCase(PublisherIdType.String)]
        public void ExtendedFlags1_PublisherIdType_RoundTrips(PublisherIdType type)
        {
            byte raw = ExtendedFlags1EncodingMaskExtensions.EncodePublisherIdType(type);
            bool ok = ExtendedFlags1EncodingMaskExtensions
                .TryGetPublisherIdType(raw, out PublisherIdType decoded);
            Assert.That(ok, Is.True);
            Assert.That(decoded, Is.EqualTo(type));
        }

        [Test]
        public void ExtendedFlags1_PublisherIdType_RejectsUnsupportedValue()
        {
            bool ok = ExtendedFlags1EncodingMaskExtensions
                .TryGetPublisherIdType(0x05, out _);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void ExtendedFlags1PublisherIdTypeGuidThrows()
        {
            Assert.That(
                () => ExtendedFlags1EncodingMaskExtensions.EncodePublisherIdType(PublisherIdType.Guid),
                Throws.InvalidOperationException);
        }

        [Test]
        [TestCase(PubSubFieldEncoding.Variant)]
        [TestCase(PubSubFieldEncoding.RawData)]
        [TestCase(PubSubFieldEncoding.DataValue)]
        public void DataSetFlags1_FieldEncoding_RoundTrips(
            PubSubFieldEncoding encoding)
        {
            byte raw = DataSetFlags1EncodingMaskExtensions.EncodeFieldEncoding(encoding);
            bool ok = DataSetFlags1EncodingMaskExtensions
                .TryGetFieldEncoding(raw, out PubSubFieldEncoding decoded);
            Assert.That(ok, Is.True);
            Assert.That(decoded, Is.EqualTo(encoding));
        }

        [Test]
        public void DataSetFlags1_FieldEncoding_RejectsReservedValue()
        {
            const byte reservedBits = 0x06;
            bool ok = DataSetFlags1EncodingMaskExtensions
                .TryGetFieldEncoding(reservedBits, out _);
            Assert.That(ok, Is.False);
        }

        [Test]
        [TestCase(PubSubDataSetMessageType.KeyFrame)]
        [TestCase(PubSubDataSetMessageType.DeltaFrame)]
        [TestCase(PubSubDataSetMessageType.Event)]
        [TestCase(PubSubDataSetMessageType.KeepAlive)]
        public void DataSetFlags2_MessageType_RoundTrips(
            PubSubDataSetMessageType type)
        {
            byte raw = DataSetFlags2EncodingMaskExtensions.EncodeMessageType(type);
            bool ok = DataSetFlags2EncodingMaskExtensions
                .TryGetMessageType(raw, out PubSubDataSetMessageType decoded);
            Assert.That(ok, Is.True);
            Assert.That(decoded, Is.EqualTo(type));
        }

        [Test]
        public void DataSetFlags2_MessageType_RejectsReservedValue()
        {
            bool ok = DataSetFlags2EncodingMaskExtensions
                .TryGetMessageType(0x0F, out _);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void GroupFlags_AllBitsHonoured()
        {
            const UadpGroupFlags combined =
                UadpGroupFlags.WriterGroupIdEnabled |
                UadpGroupFlags.GroupVersionEnabled |
                UadpGroupFlags.NetworkMessageNumberEnabled |
                UadpGroupFlags.SequenceNumberEnabled;
            Assert.That((byte)combined, Is.EqualTo(0x0F));
        }

        [Test]
        public void ExtendedFlags2_DiscoveryBitsAreDistinct()
        {
            Assert.That((byte)UadpExtendedFlags2.ChunkMessage,
                Is.EqualTo(0x01));
            Assert.That((byte)UadpExtendedFlags2.PromotedFields,
                Is.EqualTo(0x02));
            Assert.That((byte)UadpExtendedFlags2.NetworkMessageWithDiscoveryRequest,
                Is.EqualTo(0x04));
            Assert.That((byte)UadpExtendedFlags2.NetworkMessageWithDiscoveryResponse,
                Is.EqualTo(0x08));
        }
    }
}
