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

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Round-trip coverage for every <see cref="PublisherIdType"/> value
    /// through the UADP encoder + decoder.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5.2")]
    [TestSpec("A.2.2.4")]
    public class UadpPublisherIdTests
    {
        [Test]
        public async Task PublisherId_Byte_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromByte(0xA5)).ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherId_UInt16_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromUInt16(0xABCD)).ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherId_UInt32_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromUInt32(0x12345678u)).ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherId_UInt64_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromUInt64(0x0123456789ABCDEFul)).ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherId_String_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromString("publisher-ä-42")).ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherId_Guid_RoundTrips()
        {
            await RoundTripAsync(PublisherId.FromGuid(
                new Guid("12345678-1234-1234-1234-1234567890AB")))
                .ConfigureAwait(false);
        }

        private static async Task RoundTripAsync(PublisherId publisherId)
        {
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId,
                PublisherId = publisherId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [ new DataSetField { Value = new Variant((uint)42) } ]
                    }
                ]
            };

            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var encoder = new UadpEncoder();
            ReadOnlyMemory<byte> encoded =
                await encoder.EncodeAsync(message, context).ConfigureAwait(false);

            var decoder = new UadpDecoder();
            PubSubNetworkMessage? decoded =
                await decoder.TryDecodeAsync(encoded, context).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded, Is.InstanceOf<UadpNetworkMessage>());
            var decodedUadp = (UadpNetworkMessage)decoded!;
            Assert.That(decodedUadp.PublisherId.Type, Is.EqualTo(publisherId.Type));

            switch (publisherId.Type)
            {
                case PublisherIdType.Byte:
                    publisherId.TryGetByte(out byte b1);
                    decodedUadp.PublisherId.TryGetByte(out byte b2);
                    Assert.That(b2, Is.EqualTo(b1));
                    break;
                case PublisherIdType.UInt16:
                    publisherId.TryGetUInt16(out ushort u16a);
                    decodedUadp.PublisherId.TryGetUInt16(out ushort u16b);
                    Assert.That(u16b, Is.EqualTo(u16a));
                    break;
                case PublisherIdType.UInt32:
                    publisherId.TryGetUInt32(out uint u32a);
                    decodedUadp.PublisherId.TryGetUInt32(out uint u32b);
                    Assert.That(u32b, Is.EqualTo(u32a));
                    break;
                case PublisherIdType.UInt64:
                    publisherId.TryGetUInt64(out ulong u64a);
                    decodedUadp.PublisherId.TryGetUInt64(out ulong u64b);
                    Assert.That(u64b, Is.EqualTo(u64a));
                    break;
                case PublisherIdType.String:
                    publisherId.TryGetString(out string? sa);
                    decodedUadp.PublisherId.TryGetString(out string? sb);
                    Assert.That(sb, Is.EqualTo(sa));
                    break;
                case PublisherIdType.Guid:
                    publisherId.TryGetGuid(out Guid ga);
                    decodedUadp.PublisherId.TryGetGuid(out Guid gb);
                    Assert.That(gb, Is.EqualTo(ga));
                    break;
            }
        }
    }
}
