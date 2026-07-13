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
using Opc.Ua.PubSub.Transcoding;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for the experimental Avro transcoding support: projection to the Avro mapping,
    /// encoding classification, and the progressive schema generation / reset lifecycle.
    /// </summary>
    [TestFixture]
    public class AvroTranscodingTests
    {
        private static AvroNetworkMessage NewAvroMessage(
            uint majorVersion = 1,
            params DataSetField[] fields)
        {
            return new AvroNetworkMessage
            {
                PublisherId = PublisherId.FromByte(3),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new AvroDataSetMessage
                    {
                        DataSetWriterId = 55,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = majorVersion,
                            MinorVersion = 0
                        },
                        Fields = fields
                    }
                ]
            };
        }

        [Test]
        public void AvroTransportProfileUri_RoundTripsThroughClassification()
        {
            string uri = TranscodeEncoding.Avro.ToTransportProfileUri();

            Assert.That(uri, Is.EqualTo(AvroNetworkMessage.PubSubMqttAvroTransport));
            Assert.That(uri.FromTransportProfileUri(), Is.EqualTo(TranscodeEncoding.Avro));
        }

        [Test]
        public void EncodingOf_AvroNetworkMessage_ReturnsAvro()
        {
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            Assert.That(message.EncodingOf(), Is.EqualTo(TranscodeEncoding.Avro));
        }

        [Test]
        public void Project_UadpToAvro_PreservesIdentityAndFields()
        {
            var source = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Avro, TranscodeTargetOptions.Default, NewContext());

            var avro = (AvroNetworkMessage)projected;
            Assert.That(avro.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(avro.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(avro.DataSetMessages[0], Is.InstanceOf<AvroDataSetMessage>());
            Assert.That(avro.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)55));
            Assert.That(avro.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_AvroToAvro_IdentityReturnsSameInstance()
        {
            AvroNetworkMessage source = NewAvroMessage(1, Field("a", new Variant(1)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Avro, TranscodeTargetOptions.Default, NewContext());

            Assert.That(projected, Is.SameAs(source));
        }

        [Test]
        public async Task AvroEncoder_AnnouncesSchemaOncePerShape()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "The first encode should announce the schema.");

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Null,
                "The second encode of the same shape should not re-announce.");
        }

        [Test]
        public async Task AvroEncoder_MetaDataVersionChange_ReAnnouncesSchema()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();

            await encoder.EncodeAsync(
                NewAvroMessage(1, Field("x", new Variant(9))), context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null);

            await encoder.EncodeAsync(
                NewAvroMessage(2, Field("x", new Variant(9))), context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "A DataSet MetaData version change should re-announce the schema.");
        }

        [Test]
        public async Task AvroEncoder_SchemaCacheReset_ReAnnouncesSchema()
        {
            var encoder = new AvroNetworkMessageEncoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(9)));

            await encoder.EncodeAsync(message, context.EncodingContext);
            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Null);

            encoder.SchemaCache.Reset();

            await encoder.EncodeAsync(message, context.EncodingContext);
            Assert.That(encoder.LastSchemaAnnouncement, Is.Not.Null,
                "After a schema-cache reset the schema should be announced again.");
        }

        [Test]
        public async Task AvroEncode_Decode_RoundTripsFields()
        {
            var encoder = new AvroNetworkMessageEncoder();
            var decoder = new AvroNetworkMessageDecoder();
            TranscodeContext context = NewContext();
            AvroNetworkMessage message = NewAvroMessage(1, Field("x", new Variant(42)));

            ReadOnlyMemory<byte> frame = await encoder.EncodeAsync(
                message, context.EncodingContext);
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(
                frame, context.EncodingContext);

            Assert.That(decoded, Is.InstanceOf<AvroNetworkMessage>());
            Assert.That(decoded!.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(decoded.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)55));
            Assert.That(
                decoded.DataSetMessages[0].Fields[0].Value,
                Is.EqualTo(new Variant(42)));
        }
    }
}
