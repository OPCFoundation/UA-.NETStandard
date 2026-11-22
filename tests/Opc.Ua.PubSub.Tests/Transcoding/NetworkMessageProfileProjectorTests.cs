// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

        [Test]
        public void Project_JsonToJson_SingleMessageDisabled_ClearsMaskBit()
        {
            JsonNetworkMessageV2 source = SampleJson() with
            {
                SingleMessageMode = true,
                ContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader |
                    JsonNetworkMessageContentMask.SingleDataSetMessage
            };
            var options = new TranscodeTargetOptions { JsonSingleMessageMode = false };

            var projected = (JsonNetworkMessageV2)NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Json, options, NewContext());

            Assert.That(projected.SingleMessageMode, Is.False);
            Assert.That(
                projected.ContentMask.HasFlag(JsonNetworkMessageContentMask.SingleDataSetMessage),
                Is.False);
        }

        [Test]
        public void Project_UadpToUadp_PreservesSourceContentMask()
        {
            var source = new UadpNetworkMessageV2
            {
                PublisherId = PublisherId.FromByte(1),
                DataSetMessages =
                [
                    new UadpDataSetMessageV2
                    {
                        DataSetWriterId = 1,
                        ContentMask = UadpDataSetMessageContentMask.SequenceNumber,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [Field("a", new Variant(1))]
                    }
                ]
            };
            var options = new TranscodeTargetOptions { FieldEncoding = PubSubFieldEncoding.RawData };

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                source, TranscodeEncoding.Uadp, options, NewContext());

            var dsm = (UadpDataSetMessageV2)projected.DataSetMessages[0];
            Assert.That(dsm.ContentMask, Is.EqualTo(UadpDataSetMessageContentMask.SequenceNumber));
            Assert.That(dsm.FieldEncoding, Is.EqualTo(PubSubFieldEncoding.RawData));
        }
    }
}
