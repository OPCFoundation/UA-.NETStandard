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

using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for <see cref="NetworkMessageProfileProjector"/> covering
    /// the four source/target encoding combinations and format options.
    /// </summary>
    [TestFixture]
    public class NetworkMessageProfileProjectorTests
    {
        private static JsonNetworkMessageV2 SampleJson()
        {
            return new JsonNetworkMessageV2
            {
                PublisherId = PublisherId.FromByte(3),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new JsonDataSetMessageV2
                    {
                        DataSetWriterId = 55,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [Field("x", new Variant(9))]
                    }
                ]
            };
        }

        [Test]
        public void Project_UadpToJson_PreservesIdentityAndFields()
        {
            UadpNetworkMessageV2 source = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Json, TranscodeTargetOptions.Default, NewContext());

            var json = (JsonNetworkMessageV2)projected;
            Assert.That(json.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(json.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(json.MessageId, Is.Not.Empty);
            Assert.That(json.DataSetMessages[0].DataSetWriterId, Is.EqualTo((ushort)55));
            Assert.That(json.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_JsonToUadp_SetsIdentityContentMask()
        {
            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                SampleJson(), TranscodeEncoding.Uadp, TranscodeTargetOptions.Default, NewContext());

            var uadp = (UadpNetworkMessageV2)projected;
            Assert.That(uadp.PublisherId, Is.EqualTo(PublisherId.FromByte(3)));
            Assert.That(uadp.WriterGroupId, Is.EqualTo((ushort)7));
            Assert.That(uadp.ContentMask & UadpNetworkMessageContentMask.PublisherId,
                Is.EqualTo(UadpNetworkMessageContentMask.PublisherId));
            Assert.That(uadp.ContentMask & UadpNetworkMessageContentMask.PayloadHeader,
                Is.EqualTo(UadpNetworkMessageContentMask.PayloadHeader));
            Assert.That(uadp.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public void Project_UadpToUadp_IdentityReturnsSameInstance()
        {
            UadpNetworkMessageV2 source = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Uadp, TranscodeTargetOptions.Default, NewContext());

            Assert.That(projected, Is.SameAs(source));
        }

        [Test]
        public void Project_UadpToUadp_FieldEncodingOptionRebuilds()
        {
            UadpNetworkMessageV2 source = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));
            var options = new TranscodeTargetOptions { FieldEncoding = PubSubFieldEncoding.RawData };

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Uadp, options, NewContext());

            var dsm = (UadpDataSetMessageV2)projected.DataSetMessages[0];
            Assert.That(dsm.FieldEncoding, Is.EqualTo(PubSubFieldEncoding.RawData));
        }

        [Test]
        public void Project_JsonToJson_SingleMessageOption()
        {
            var options = new TranscodeTargetOptions { JsonSingleMessageMode = true };

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                SampleJson(), TranscodeEncoding.Json, options, NewContext());

            var json = (JsonNetworkMessageV2)projected;
            Assert.That(json.SingleMessageMode, Is.True);
        }
    }
}
