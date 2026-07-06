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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Tests.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.Tests;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Coverage-focused unit tests for the transcoding builder, encoding
    /// helpers, context, security, projector branches, and transform
    /// argument guards.
    /// </summary>
    [TestFixture]
    public class TranscodeUnitCoverageTests
    {
        [Test]
        public void Builder_AllFluentMethods_ComposeSpecAndDescriptor()
        {
            var builder = new PubSubTranscoderBuilder()
                .From("in")
                .To("out", TranscodeEncoding.Json)
                .AddTransform(new IdRemapTransform(PublisherId.FromByte(2)))
                .RemapIds(writerGroupId: 3)
                .RenameField("a", "alpha")
                .SelectFields("alpha")
                .ExcludeFields("noise")
                .TransformValue((name, value) => value)
                .TransformMetaData(meta => meta)
                .FilterMessageTypes(type => type != PubSubDataSetMessageType.Event)
                .DropKeepAlive()
                .WithFieldEncoding(PubSubFieldEncoding.RawData)
                .AsJsonSingleMessage()
                .PreserveMetaDataVersion(false)
                .AllowInsecureCrossEncoding()
                .ToTopic("t");

            TranscodeSpec spec = builder.BuildSpec();
            TranscodingBridgeDescriptor descriptor = builder.Build();

            Assert.That(spec.TargetEncoding, Is.EqualTo(TranscodeEncoding.Json));
            Assert.That(spec.Transforms.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(spec.TargetOptions.FieldEncoding, Is.EqualTo(PubSubFieldEncoding.RawData));
            Assert.That(spec.TargetOptions.JsonSingleMessageMode, Is.True);
            Assert.That(spec.TargetOptions.PreserveMetaDataVersion, Is.False);
            Assert.That(descriptor.AllowInsecureCrossEncoding, Is.True);
            Assert.That(descriptor.TopicSelector, Is.Not.Null);
        }

        [Test]
        public void Builder_WithTopicSelector_IsUsed()
        {
            TranscodingBridgeDescriptor descriptor = new PubSubTranscoderBuilder()
                .From("in")
                .To("out", TranscodeEncoding.Uadp)
                .WithTopicSelector(received => received.SourceConnectionName)
                .Build();

            Assert.That(descriptor.TopicSelector, Is.Not.Null);
        }

        [Test]
        public void Builder_NullArguments_Throw()
        {
            var builder = new PubSubTranscoderBuilder();
            Assert.Multiple(() =>
            {
                Assert.That(() => builder.From(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.To(null!, TranscodeEncoding.Uadp), Throws.ArgumentNullException);
                Assert.That(() => builder.AddTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.RenameField(null!, "x"), Throws.ArgumentNullException);
                Assert.That(() => builder.RenameField("x", null!), Throws.ArgumentNullException);
                Assert.That(() => builder.TransformValue(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.TransformMetaData(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.WithTopicSelector(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.ToTopic(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Encoding_MappingHelpers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TranscodeEncoding.Uadp.ToTransportProfileUri(),
                    Is.EqualTo(Profiles.PubSubUdpUadpTransport));
                Assert.That(TranscodeEncoding.Json.ToTransportProfileUri(),
                    Is.EqualTo(Profiles.PubSubMqttJsonTransport));
                Assert.That(TranscodeEncodingExtensions.FromTransportProfileUri(
                    Profiles.PubSubMqttJsonTransport), Is.EqualTo(TranscodeEncoding.Json));
                Assert.That(TranscodeEncodingExtensions.FromTransportProfileUri(
                    Profiles.PubSubUdpUadpTransport), Is.EqualTo(TranscodeEncoding.Uadp));
                Assert.That(TranscodeEncodingExtensions.FromTransportProfileUri(null!),
                    Is.EqualTo(TranscodeEncoding.Uadp));
                Assert.That(() => ((TranscodeEncoding)99).ToTransportProfileUri(),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => TranscodeEncodingExtensions.EncodingOf(null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Context_NullArguments_Throw()
        {
            TranscodeContext context = NewContext();
            Assert.Multiple(() =>
            {
                Assert.That(() => new TranscodeContext(null!, NUnitTelemetryContext.Create()),
                    Throws.ArgumentNullException);
                Assert.That(() => new TranscodeContext(context.EncodingContext, null!),
                    Throws.ArgumentNullException);
                Assert.That(context.MetaDataRegistry, Is.Not.Null);
            });
        }

        [Test]
        public async Task Security_WrapUadp_WithWrapper_ProducesLargerSecuredFrame()
        {
            TranscodeContext context = NewContext();
            UadpSecurityWrapper wrapper = BuildWrapper();
            var security = new TranscodeSecurity
            {
                TargetWrapper = wrapper,
                TargetWrapOptions = UadpSecurityWrapOptions.SignAndEncrypt
            };
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("v", new Variant(42)));
            ReadOnlyMemory<byte> encoded = UadpEncoderV2.EncodeWithSecurityBoundary(
                message, context.EncodingContext, out int payloadOffset);

            ReadOnlyMemory<byte> wrapped = await security
                .WrapUadpAsync(encoded, payloadOffset)
                .ConfigureAwait(false);

            Assert.That(security.IsTargetSecured, Is.True);
            Assert.That(wrapped.Length, Is.GreaterThan(encoded.Length));
        }

        [Test]
        public void Projector_NullArguments_Throw()
        {
            TranscodeContext context = NewContext();
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("v", new Variant(1)));
            Assert.Multiple(() =>
            {
                Assert.That(() => NetworkMessageProfileProjector.Instance.Project(
                    null!, TranscodeEncoding.Uadp, TranscodeTargetOptions.Default, context),
                    Throws.ArgumentNullException);
                Assert.That(() => NetworkMessageProfileProjector.Instance.Project(
                    message, TranscodeEncoding.Uadp, null!, context),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Projector_SameEncoding_NoPreserveMetaDataVersion_Rebuilds()
        {
            TranscodeContext context = NewContext();
            var options = new TranscodeTargetOptions { PreserveMetaDataVersion = false };
            UadpNetworkMessageV2 uadp = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("v", new Variant(1)));

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                uadp, TranscodeEncoding.Uadp, options, context);

            Assert.That(projected, Is.Not.Null);
            Assert.That(projected.DataSetMessages[0].MetaDataVersion.MajorVersion, Is.Zero);
        }

        [Test]
        public void Projector_UadpToJson_PropagatesDataSetClassId()
        {
            TranscodeContext context = NewContext();
            var classId = new Uuid(Guid.NewGuid());
            UadpNetworkMessageV2 uadp = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("v", new Variant(1)))
                with { DataSetClassId = classId };

            PubSubNetworkMessage projected = NetworkMessageProfileProjector.Instance.Project(
                uadp, TranscodeEncoding.Json, TranscodeTargetOptions.Default, context);

            var json = (Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage)projected;
            Assert.That(json.DataSetClassId, Is.EqualTo(classId));
        }

        [Test]
        public async Task Transforms_ArgumentGuards_Throw()
        {
            TranscodeContext context = NewContext();
            Assert.Multiple(() =>
            {
                Assert.That(() => new FieldRenameTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => new ValueTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => new MetaDataTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => new FieldProjectionTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => new DelegateMessageTransform(null!), Throws.ArgumentNullException);
                Assert.That(() => DelegateMessageTransform.FromSync(null!), Throws.ArgumentNullException);
            });

            Assert.That(async () => await new IdRemapTransform()
                .TransformAsync(null!, context).ConfigureAwait(false),
                Throws.ArgumentNullException);
            Assert.That(async () => await new FieldEncodingTransform(PubSubFieldEncoding.Variant)
                .TransformAsync(null!, context).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task Transforms_EmptyDataSets_ReturnSameInstance()
        {
            TranscodeContext context = NewContext();
            var empty = new UadpNetworkMessageV2 { PublisherId = PublisherId.FromByte(1) };

            PubSubNetworkMessage? encoding = await new FieldEncodingTransform(
                PubSubFieldEncoding.RawData).TransformAsync(empty, context).ConfigureAwait(false);
            PubSubNetworkMessage? projection = await new FieldProjectionTransform(["x"])
                .TransformAsync(empty, context).ConfigureAwait(false);
            PubSubNetworkMessage? rename = await new FieldRenameTransform(
                new Dictionary<string, string> { ["a"] = "b" })
                .TransformAsync(empty, context).ConfigureAwait(false);
            PubSubNetworkMessage? value = await new ValueTransform((_, v) => v)
                .TransformAsync(empty, context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(encoding, Is.SameAs(empty));
                Assert.That(projection, Is.SameAs(empty));
                Assert.That(rename, Is.SameAs(empty));
                Assert.That(value, Is.SameAs(empty));
            });
        }

        private static UadpSecurityWrapper BuildWrapper()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                tokenId: 1U,
                policy.SigningKeyLength,
                policy.EncryptingKeyLength,
                policy.NonceLength);
            var ring = new PubSubSecurityKeyRing("group");
            ring.SetCurrent(key);
            return new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group", ring),
                new RandomNonceProvider(PublisherId.FromUInt32(0xABCDEFU)),
                new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
        }
    }
}
