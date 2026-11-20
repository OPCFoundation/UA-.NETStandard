// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transcoding;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Unit tests for <see cref="TranscodeSecurity"/> and the fluent
    /// <see cref="PubSubTranscoderBuilder"/>.
    /// </summary>
    [TestFixture]
    public class TranscodeSecurityAndBuilderTests
    {
        [Test]
        public void None_HasNoTargetSecurity()
        {
            Assert.That(TranscodeSecurity.None.IsTargetSecured, Is.False);
            Assert.That(TranscodeSecurity.None.AllowInsecureCrossEncoding, Is.False);
        }

        [Test]
        public void WouldRefuseDowngrade_SecuredSourceToJson_RefusedByDefault()
        {
            var security = new TranscodeSecurity { AllowInsecureCrossEncoding = false };

            Assert.That(security.WouldRefuseDowngrade(true, TranscodeEncoding.Json), Is.True);
            Assert.That(security.WouldRefuseDowngrade(true, TranscodeEncoding.Uadp), Is.True);
        }

        [Test]
        public void WouldRefuseDowngrade_UnsecuredSource_NotRefused()
        {
            var security = new TranscodeSecurity { AllowInsecureCrossEncoding = false };

            Assert.That(security.WouldRefuseDowngrade(false, TranscodeEncoding.Json), Is.False);
        }

        [Test]
        public void WouldRefuseDowngrade_AllowInsecure_NotRefused()
        {
            var security = new TranscodeSecurity { AllowInsecureCrossEncoding = true };

            Assert.That(security.WouldRefuseDowngrade(true, TranscodeEncoding.Json), Is.False);
        }

        [Test]
        public async Task WrapUadpAsync_NoTargetWrapper_ReturnsInputUnchanged()
        {
            var security = new TranscodeSecurity();
            byte[] encoded = [1, 2, 3, 4, 5, 6];

            ReadOnlyMemory<byte> result = await security
                .WrapUadpAsync(encoded, payloadOffset: 2)
                .ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(encoded));
        }

        [Test]
        public void Builder_BuildSpec_CarriesEncodingAndTransforms()
        {
            PubSubTranscoderBuilder builder = new PubSubTranscoderBuilder()
                .From("in")
                .To("out", TranscodeEncoding.Json)
                .RenameField("a", "alpha")
                .RenameField("b", "beta")
                .SelectFields("alpha", "beta");

            TranscodeSpec spec = builder.BuildSpec();

            Assert.That(spec.TargetEncoding, Is.EqualTo(TranscodeEncoding.Json));
            Assert.That(spec.Transforms.Count, Is.EqualTo(2));
        }

        [Test]
        public void Builder_Build_RequiresSourceAndTarget()
        {
            Assert.That(
                () => new PubSubTranscoderBuilder().To("out", TranscodeEncoding.Uadp).Build(),
                Throws.InvalidOperationException);
            Assert.That(
                () => new PubSubTranscoderBuilder().From("in").Build(),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Builder_Build_ProducesDescriptor()
        {
            TranscodingBridgeDescriptor descriptor = new PubSubTranscoderBuilder()
                .From("in")
                .To("out", TranscodeEncoding.Json)
                .AllowInsecureCrossEncoding()
                .Build();

            Assert.That(descriptor.SourceConnectionName, Is.EqualTo("in"));
            Assert.That(descriptor.TargetConnectionName, Is.EqualTo("out"));
            Assert.That(descriptor.AllowInsecureCrossEncoding, Is.True);
            Assert.That(descriptor.Spec.TargetEncoding, Is.EqualTo(TranscodeEncoding.Json));
        }
    }
}
