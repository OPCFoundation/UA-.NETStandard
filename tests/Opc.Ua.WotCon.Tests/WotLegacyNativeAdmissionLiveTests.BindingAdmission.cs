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

        [TestCase("valid")]
        [TestCase("aliased")]
        [TestCase("missing")]
        [TestCase("wrong-class")]
        public async Task PropertyTypeLinksRequireNativeAdmission(string binding)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            string typeId = binding switch
            {
                "missing" => "nsu=urn:test:r42-admission;i=9999",
                "wrong-class" => "nsu=urn:test:r42-admission;i=4001",
                _ => "i=68"
            };
            string relation = binding == "aliased" ? "edge:HasTypeDefinition" : "ua:HasTypeDefinition";
            ByteString content = Document(binding: "\"@type\":\"Thing\",", properties: $$$"""
                {"Speed":{"@context":{"edge":"http://opcfoundation.org/UA/"},"type":"number",
                    "links":[{"rel":"{{{relation}}}","href":"{{{typeId}}}"}],
                    "forms":[{"href":"sim://opcua.test/wot/speed"}]}}
                """);
            if (binding is "missing" or "wrong-class")
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await asset.UploadThingDescriptionAsync(content.Span.ToArray()))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(fixture.Provider.Connects, Is.Zero);
                Assert.That((await PropertiesAsync(asset)).Count, Is.Zero);
            }
            else
            {
                await asset.UploadThingDescriptionAsync(content.Span.ToArray());
                ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
                Assert.That(properties.Count, Is.EqualTo(1));
                Assert.That(await fixture.TypeAsync(properties[0].NodeId), Is.EqualTo(Ua.VariableTypeIds.PropertyType));
                Assert.That(fixture.Recorder.Calls, Is.EqualTo(1));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            }
        }

        [TestCase("valid")]
        [TestCase("missing")]
        [TestCase("unloaded")]
        public async Task PropertyTypeNamesUseTheirAffordanceContext(string binding)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            string namespaceUri = binding == "unloaded" ? "urn:test:unloaded" : "http://opcfoundation.org/UA/";
            string type = binding == "missing" ? "AbsentVariableType" : "PropertyType";
            ByteString content = Document(binding: "\"@type\":\"Thing\",", properties: $$$"""
                {"Speed":{"@context":{"model":"{{{namespaceUri}}}"},"@type":["model:{{{type}}}"],
                    "type":"number","forms":[{"href":"sim://opcua.test/wot/speed"}]}}
                """);
            if (binding == "missing")
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await asset.UploadThingDescriptionAsync(content.Span.ToArray()))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(fixture.Provider.Connects, Is.Zero);
                Assert.That((await PropertiesAsync(asset)).Count, Is.Zero);
            }
            else
            {
                await asset.UploadThingDescriptionAsync(content.Span.ToArray());
                ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
                Assert.That(properties.Count, Is.EqualTo(1));
                Assert.That(await fixture.TypeAsync(properties[0].NodeId), Is.EqualTo(binding == "valid"
                    ? Ua.VariableTypeIds.PropertyType : Ua.VariableTypeIds.BaseDataVariableType));
                Assert.That(fixture.Recorder.Calls, Is.EqualTo(binding == "valid" ? 1 : 0));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            }
        }
    }
}
