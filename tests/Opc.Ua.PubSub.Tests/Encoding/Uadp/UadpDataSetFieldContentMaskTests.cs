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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Validates that the per-bit DataSetFieldContentMask (StatusCode,
    /// SourceTimestamp, SourcePicoSeconds, ServerTimestamp,
    /// ServerPicoSeconds) round-trips through the UADP encoder /
    /// decoder when the field encoding is
    /// <see cref="PubSubFieldEncoding.DataValue"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("6.3.1.3", Summary = "UADP DataSetFieldContentMask round-trip")]
    [TestSpec("5.3.2")]
    public class UadpDataSetFieldContentMaskTests
    {
        [Test]
        [TestSpec("6.3.1.3")]
        public async Task RoundTripDataValue_StatusCodeBitAsync()
        {
            PubSub.Encoding.Uadp.UadpNetworkMessage decoded = await RoundTripAsync(
                DataSetFieldContentMask.StatusCode,
                new DataSetField
                {
                    Value = new Variant(42),
                    StatusCode = StatusCodes.UncertainInitialValue
                }).ConfigureAwait(false);

            DataSetField field = decoded.DataSetMessages[0].Fields[0];
            Assert.That(field.Value, Is.EqualTo(new Variant(42)));
            Assert.That((uint)field.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        [Test]
        [TestSpec("6.3.1.3")]
        public async Task RoundTripDataValue_SourceTimestampBitAsync()
        {
            var ts = DateTimeUtc.From(
                new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
            PubSub.Encoding.Uadp.UadpNetworkMessage decoded = await RoundTripAsync(
                DataSetFieldContentMask.SourceTimestamp,
                new DataSetField
                {
                    Value = new Variant(1.0),
                    SourceTimestamp = ts
                }).ConfigureAwait(false);

            DataSetField field = decoded.DataSetMessages[0].Fields[0];
            Assert.That(field.SourceTimestamp, Is.EqualTo(ts));
        }

        [Test]
        [TestSpec("6.3.1.3")]
        public async Task RoundTripDataValue_AllBitsAsync()
        {
            var src = DateTimeUtc.From(
                new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
            var srv = DateTimeUtc.From(
                new DateTimeOffset(2026, 6, 16, 12, 0, 1, TimeSpan.Zero));
            PubSub.Encoding.Uadp.UadpNetworkMessage decoded = await RoundTripAsync(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.ServerPicoSeconds,
                new DataSetField
                {
                    Value = new Variant(7.0),
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = src,
                    SourcePicoSeconds = 12,
                    ServerTimestamp = srv,
                    ServerPicoSeconds = 34
                }).ConfigureAwait(false);

            DataSetField field = decoded.DataSetMessages[0].Fields[0];
            Assert.That(field.SourceTimestamp, Is.EqualTo(src));
            Assert.That(field.ServerTimestamp, Is.EqualTo(srv));
            Assert.That(field.SourcePicoSeconds, Is.EqualTo(12));
            Assert.That(field.ServerPicoSeconds, Is.EqualTo(34));
        }

        private static async Task<PubSub.Encoding.Uadp.UadpNetworkMessage> RoundTripAsync(
            DataSetFieldContentMask mask,
            DataSetField field)
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var msg = new PubSub.Encoding.Uadp.UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.From(1u),
                DataSetMessages =
                [
                    new PubSub.Encoding.Uadp.UadpDataSetMessage
                    {
                        DataSetWriterId = 7,
                        FieldEncoding = PubSubFieldEncoding.DataValue,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        FieldContentMask = mask,
                        Fields = [field]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await new PubSub.Encoding.Uadp.UadpEncoder().EncodeAsync(msg, context).ConfigureAwait(false);
            PubSubNetworkMessage? decoded = await new PubSub.Encoding.Uadp.UadpDecoder()
                .TryDecodeAsync(bytes, context).ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null);
            return (PubSub.Encoding.Uadp.UadpNetworkMessage)decoded!;
        }
    }
}
