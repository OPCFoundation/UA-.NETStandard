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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DefinitiveTypeLinkDoesNotRequireAnObjectAnnotation(bool missing)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            string id = missing ? "9999" : "4001";
            ByteString content = Document(binding: $$"""
                "@type":["Thing"],
                "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:r42-admission;i={{id}}"}],
                """);
            if (missing)
            {
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await asset.UploadThingDescriptionAsync(content.Span.ToArray()));
                Assert.That(fixture.Provider.Connects, Is.Zero);
            }
            else
            {
                await asset.UploadThingDescriptionAsync(content.Span.ToArray());
                Assert.That(await fixture.TypeAsync(asset.AssetId),
                    Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            }
        }

        [Test]
        public async Task UnloadedAnnotationNamespaceRetainsUnboundLegacyBehavior()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId originalType = await fixture.TypeAsync(asset.AssetId);
            ByteString content = Document(binding:
                "\"@type\":[\"Thing\",\"https://saref.etsi.org/core/TemperatureSensor\"],");
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(originalType));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadableTypeBindingDoesNotRequireAnObjectAnnotation(bool missing)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId originalType = await fixture.TypeAsync(asset.AssetId);
            string type = missing ? "MissingType" : "PumpType";
            ByteString content = Document(binding: $"\"@type\":[\"Thing\",\"model:{type}\"],");
            if (missing)
            {
                ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await asset.UploadThingDescriptionAsync(content.Span.ToArray()));
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(fixture.Provider.Connects, Is.Zero);
                Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(originalType));
            }
            else
            {
                await asset.UploadThingDescriptionAsync(content.Span.ToArray());
                Assert.That(await fixture.TypeAsync(asset.AssetId),
                    Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
                Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            }
        }
    }
}
