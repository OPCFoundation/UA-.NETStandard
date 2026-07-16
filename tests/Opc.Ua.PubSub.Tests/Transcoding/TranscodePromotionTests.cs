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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.PubSub.Transports;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transcoding
{
    /// <summary>
    /// Tests for promoting DataSet fields into transport message properties
    /// (Part 14 PromotedFields to MQTT User Properties) through the
    /// transcoder and the fluent builder.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class TranscodePromotionTests
    {
        [Test]
        public async Task Transcode_PromotesConfiguredFields()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Uadp,
                Promotion = new TranscodePromotion
                {
                    FieldNames = new[] { "Temperature", "Unit" }
                }
            };
            var transcoder = new PubSubTranscoder(spec, TranscodingTestUtilities.Encoders(), context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100,
                Field("Temperature", new Variant(21.5)),
                Field("Unit", new Variant("C")));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Properties, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.Properties[0].Name, Is.EqualTo("Temperature"));
                Assert.That(result.Properties[0].Value, Is.EqualTo("21.5"));
                Assert.That(result.Properties[1].Name, Is.EqualTo("Unit"));
                Assert.That(result.Properties[1].Value, Is.EqualTo("C"));
            });
        }

        [Test]
        public async Task Transcode_AppliesPropertyKeyPrefix()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Uadp,
                Promotion = new TranscodePromotion
                {
                    FieldNames = new[] { "Value" },
                    PropertyKeyPrefix = "opc_"
                }
            };
            var transcoder = new PubSubTranscoder(spec, TranscodingTestUtilities.Encoders(), context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("Value", new Variant(42)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Properties, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(result.Properties[0].Name, Is.EqualTo("opc_Value"));
                Assert.That(result.Properties[0].Value, Is.EqualTo("42"));
            });
        }

        [Test]
        public async Task Transcode_MissingPromotedField_IsSkipped()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec
            {
                TargetEncoding = TranscodeEncoding.Uadp,
                Promotion = new TranscodePromotion
                {
                    FieldNames = new[] { "Present", "Absent" }
                }
            };
            var transcoder = new PubSubTranscoder(spec, TranscodingTestUtilities.Encoders(), context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("Present", new Variant(7)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Properties, Has.Count.EqualTo(1));
            Assert.That(result.Properties[0].Name, Is.EqualTo("Present"));
        }

        [Test]
        public async Task Transcode_NoPromotion_ProducesNoProperties()
        {
            TranscodeContext context = NewContext();
            var spec = new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json };
            var transcoder = new PubSubTranscoder(spec, TranscodingTestUtilities.Encoders(), context);
            UadpNetworkMessageV2 message = NewUadpMessage(
                PublisherId.FromByte(1), 10, 100, Field("a", new Variant(1)));

            TranscodeResult result = await transcoder
                .TranscodeAsync(new TranscodeInput(message))
                .ConfigureAwait(false);

            Assert.That(result.Properties, Is.Empty);
        }

        [Test]
        public void Builder_PromoteFields_BuildsPromotionSpec()
        {
            TranscodeSpec spec = new PubSubTranscoderBuilder()
                .From("s")
                .To("t", TranscodeEncoding.Uadp)
                .PromoteFields("a", "b")
                .WithPromotedFieldPrefix("p_")
                .BuildSpec();

            Assert.That(spec.Promotion, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(spec.Promotion!.FieldNames.Count, Is.EqualTo(2));
                Assert.That(spec.Promotion!.FieldNames[0], Is.EqualTo("a"));
                Assert.That(spec.Promotion!.FieldNames[1], Is.EqualTo("b"));
                Assert.That(spec.Promotion!.PropertyKeyPrefix, Is.EqualTo("p_"));
                Assert.That(spec.IsIdentity, Is.False);
            });
        }

        [Test]
        public void Builder_NoPromoteFields_LeavesPromotionNull()
        {
            TranscodeSpec spec = new PubSubTranscoderBuilder()
                .From("s")
                .To("t", TranscodeEncoding.Uadp)
                .BuildSpec();

            Assert.Multiple(() =>
            {
                Assert.That(spec.Promotion, Is.Null);
                Assert.That(spec.IsIdentity, Is.True);
            });
        }

        [Test]
        public void Builder_PromoteFields_NullArguments_Throw()
        {
            var builder = new PubSubTranscoderBuilder();
            Assert.Multiple(() =>
            {
                Assert.That(() => builder.PromoteFields(null!), Throws.ArgumentNullException);
                Assert.That(() => builder.PromoteFields("ok", null!),
                    Throws.ArgumentException);
                Assert.That(() => builder.WithPromotedFieldPrefix(null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void MessageProperty_CarriesNameAndValue()
        {
            var property = new PubSubMessageProperty("k", "v");
            Assert.Multiple(() =>
            {
                Assert.That(property.Name, Is.EqualTo("k"));
                Assert.That(property.Value, Is.EqualTo("v"));
            });
        }
    }
}
