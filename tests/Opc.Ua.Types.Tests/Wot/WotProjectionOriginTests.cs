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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionOriginTests
    {
        [TestCase("../device.json", null,
            "https://origin.test/device.json", "https://origin.test/value")]
        [TestCase("../device.json", "./runtime/",
            "https://origin.test/device.json", "https://origin.test/runtime/value")]
        [TestCase("https://device.test/device.json", "./runtime/",
            "https://device.test/device.json", "https://device.test/runtime/value")]
        [TestCase("https://device.test/device.json", "https://service.test/api/",
            "https://device.test/device.json", "https://service.test/api/value")]
        public async Task SourceRetrievalAndCarriedFormsRetainTheirOriginalBase(
            string href, string sourceBase, string retrievalUri, string formUri)
        {
            JsonObject projection = Projection(href);
            projection["base"] = "https://origin.test/views/view.json";
            JsonObject sourceDocument = Source(sourceBase);
            byte[] sourceBytes = Encoding.UTF8.GetBytes(sourceDocument.ToJsonString());
            var sources = new Mock<IWotThingResolver>();
            sources.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.NotFound);
            sources.Setup(resolver => resolver.ResolveThingAsync(
                    retrievalUri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(sourceBytes));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var diagnostics = new List<WotDiagnostic>();
            var parsed = WotProjection.Parse(document, diagnostics);
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(formUri));
            Assert.That(view.Properties["value"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(retrievalUri + "#/properties/value"));
            Assert.That(parsed.Sources[0].Href, Is.EqualTo(href));
            Assert.That(sourceDocument.ToJsonString(), Is.EqualTo(Encoding.UTF8.GetString(sourceBytes)));
            sources.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                retrievalUri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(null, "../device.json", "https://origin.test/views/device.json")]
        [TestCase("../../assets/", "device.json", "https://origin.test/assets/device.json")]
        public async Task NestedSourcesResolveAgainstTheirOwningDocumentLocation(
            string nestedBase, string leafHref, string leafLocation)
        {
            JsonObject root = Projection("./groups/view.json");
            root["base"] = "https://origin.test/views/root.json";
            JsonObject nested = Projection(leafHref);
            if (nestedBase is not null)
            {
                nested["base"] = nestedBase;
            }
            var documents = new Dictionary<string, JsonObject>
            {
                ["https://origin.test/views/groups/view.json"] = nested,
                [leafLocation] = Source("https://device.test/api/")
            };
            Mock<IWotThingResolver> sources = Resolver(documents);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/api/value"));
            sources.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                leafLocation, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(nested["uav:projects"]![0]!["href"]!.GetValue<string>(), Is.EqualTo(leafHref));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OrganizingTraversalRetainsTheOriginAtEveryHop(bool cycle)
        {
            JsonObject root = Projection("urn:source");
            root["base"] = "https://origin.test/views/root.json";
            root["links"] = new JsonArray(Organizes("./groups/a.json"));
            var first = new JsonObject
            {
                ["title"] = "First",
                ["links"] = new JsonArray(Organizes("../b.json"))
            };
            var second = new JsonObject { ["title"] = "Second" };
            if (cycle)
            {
                second["links"] = new JsonArray(Organizes("./groups/a.json"));
            }
            var documents = new Dictionary<string, JsonObject>
            {
                ["urn:source"] = Source("https://device.test/api/"),
                ["https://origin.test/views/groups/a.json"] = first,
                ["https://origin.test/views/b.json"] = second
            };
            Mock<IWotThingResolver> sources = Resolver(documents);
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!cycle), string.Join("; ", result.Diagnostics));
            sources.Verify(sourceResolver => sourceResolver.ResolveThingAsync(
                "https://origin.test/views/b.json",
                It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            if (cycle)
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionCycle), Is.True);
                Assert.That(view, Is.Null);
            }
        }

        private static JsonObject Organizes(string href)
        {
            return new JsonObject
            {
                ["rel"] = "ua:Organizes",
                ["href"] = href,
                ["type"] = "application/td+json",
                ["uav:refName"] = "Group"
            };
        }

        private static Mock<IWotThingResolver> Resolver(Dictionary<string, JsonObject> documents)
        {
            var resolver = new Mock<IWotThingResolver>();
            resolver.Setup(source => source.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns((string href, WotResolutionContext _, CancellationToken _) =>
                    new ValueTask<WotResolverResult>(documents.TryGetValue(href, out JsonObject document)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(document.ToJsonString()))
                        : WotResolverResult.NotFound));
            return resolver;
        }

        private static JsonObject Projection(string source)
        {
            return new JsonObject
            {
                ["@type"] = new JsonArray("Thing", "uav:projection"),
                ["title"] = "Origin view",
                ["uav:scenario"] = "urn:scenario:origin",
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "source",
                    ["href"] = source,
                    ["type"] = "application/td+json",
                    ["uav:selectAll"] = true
                })
            };
        }

        private static JsonObject Source(string sourceBase)
        {
            var source = new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = new JsonArray("Thing", "uav:object"),
                ["title"] = "Source",
                ["id"] = "urn:source:origin",
                ["securityDefinitions"] = new JsonObject
                {
                    ["none"] = new JsonObject { ["scheme"] = "nosec" }
                },
                ["security"] = "none",
                ["properties"] = new JsonObject
                {
                    ["value"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = "value",
                            ["op"] = "readproperty"
                        })
                    }
                }
            };
            if (sourceBase is not null)
            {
                source["base"] = sourceBase;
            }
            return source;
        }
    }
}
