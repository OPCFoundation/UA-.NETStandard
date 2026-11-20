// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for the frame-level <see cref="PubSubTranscoder"/>.
    /// </summary>
    [TestFixture]
    public class PubSubTranscoderTests
    {
        private static PubSubTranscoder NewTranscoder(
            TranscodeSpec spec,
            TranscodeContext context,
            TranscodeSecurity? security = null)
        {
            return new PubSubTranscoder(
                spec, TranscodingTestUtilities.Encoders(), context, security);
        }

        [Test]
        public async Task FastPath_IdentitySameEncoding_ForwardsRawFrame()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Uadp };
            PubSubTranscoder transcoder = NewTranscoder(spec, context);
            byte[] frame = [1, 2, 3, 4];
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message, frame))
                .ConfigureAwait(false);

            Assert.That(result.FastPath, Is.True);
            Assert.That(result.Frames.Count, Is.EqualTo(1));
            Assert.That(result.Frames[0].ToArray(), Is.EqualTo(frame));
        }

        [Test]
        public async Task UadpToJson_RoundTripsFields()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            PubSubTranscoder transcoder = NewTranscoder(spec, context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(3), 7, 55, Field("x", new Variant(9)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.False);
            JsonNetworkMessageV2 decoded = await DecodeJsonAsync(result.Frames[0], context)
                .ConfigureAwait(false);
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(9)));
        }

        [Test]
        public async Task JsonToUadp_RoundTripsFields()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Uadp };
            PubSubTranscoder transcoder = NewTranscoder(spec, context);
            var message = new JsonNetworkMessageV2
            {
                PublisherId = PublisherId.FromByte(4),
                WriterGroupId = 8,
                DataSetMessages =
                [
                    new JsonDataSetMessageV2
                    {
                        DataSetWriterId = 12,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = [Field("y", new Variant(21))]
                    }
                ]
            };

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            UadpNetworkMessageV2 decoded = await DecodeUadpAsync(result.Frames[0], context)
                .ConfigureAwait(false);
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(21)));
        }

        [Test]
        public async Task TransformDroppingMessage_ReturnsEmpty()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Uadp,
                Transforms = [DelegateMessageTransform.FromSync(_ => null)]
            };
            PubSubTranscoder transcoder = NewTranscoder(spec, context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.True);
        }

        [Test]
        public async Task SecuredSourceToJson_WithoutAllow_IsRefused()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var security = new TranscodeSecurity { AllowInsecureCrossEncoding = false };
            PubSubTranscoder transcoder = NewTranscoder(spec, context, security);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message, default, SourceFrameSecured: true))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.True);
        }

        [Test]
        public async Task SecuredSourceToJson_WithAllow_ProducesFrame()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var security = new TranscodeSecurity { AllowInsecureCrossEncoding = true };
            PubSubTranscoder transcoder = NewTranscoder(spec, context, security);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message, default, SourceFrameSecured: true))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.False);
            Assert.That(result.Frames.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task NoEncoderRegistered_ReturnsEmpty()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var transcoder = new PubSubTranscoder(
                spec,
                new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal),
                context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Dropped, Is.True);
        }
    }
}
