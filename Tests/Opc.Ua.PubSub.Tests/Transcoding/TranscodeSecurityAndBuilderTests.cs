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
            var builder = new PubSubTranscoderBuilder()
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
