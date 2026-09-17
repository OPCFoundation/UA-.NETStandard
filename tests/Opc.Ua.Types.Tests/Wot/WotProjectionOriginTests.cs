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
        [TestCase("g:h", "g:h")]
        [TestCase("g", "http://a/b/c/g")]
        [TestCase("./g", "http://a/b/c/g")]
        [TestCase("g/", "http://a/b/c/g/")]
        [TestCase("/g", "http://a/g")]
        [TestCase("//g", "http://g")]
        [TestCase("?y", "http://a/b/c/d;p?y")]
        [TestCase("g?y", "http://a/b/c/g?y")]
        [TestCase("#s", "http://a/b/c/d;p?q#s")]
        [TestCase("g#s", "http://a/b/c/g#s")]
        [TestCase("g?y#s", "http://a/b/c/g?y#s")]
        [TestCase(";x", "http://a/b/c/;x")]
        [TestCase("g;x", "http://a/b/c/g;x")]
        [TestCase("g;x?y#s", "http://a/b/c/g;x?y#s")]
        [TestCase("", "http://a/b/c/d;p?q")]
        [TestCase(".", "http://a/b/c/")]
        [TestCase("./", "http://a/b/c/")]
        [TestCase("..", "http://a/b/")]
        [TestCase("../", "http://a/b/")]
        [TestCase("../g", "http://a/b/g")]
        [TestCase("../..", "http://a/")]
        [TestCase("../../", "http://a/")]
        [TestCase("../../g", "http://a/g")]
        [TestCase("../../../g", "http://a/g")]
        [TestCase("../../../../g", "http://a/g")]
        [TestCase("/./g", "http://a/g")]
        [TestCase("/../g", "http://a/g")]
        [TestCase("g.", "http://a/b/c/g.")]
        [TestCase(".g", "http://a/b/c/.g")]
        [TestCase("g..", "http://a/b/c/g..")]
        [TestCase("..g", "http://a/b/c/..g")]
        [TestCase("./../g", "http://a/b/g")]
        [TestCase("./g/.", "http://a/b/c/g/")]
        [TestCase("g/./h", "http://a/b/c/g/h")]
        [TestCase("g/../h", "http://a/b/c/h")]
        [TestCase("g;x=1/./y", "http://a/b/c/g;x=1/y")]
        [TestCase("g;x=1/../y", "http://a/b/c/y")]
        [TestCase("g?y/./x", "http://a/b/c/g?y/./x")]
        [TestCase("g?y/../x", "http://a/b/c/g?y/../x")]
        [TestCase("g#s/./x", "http://a/b/c/g#s/./x")]
        [TestCase("g#s/../x", "http://a/b/c/g#s/../x")]
        public async Task ReviewedCarriedFormsFollowRfc3986ReferenceExamples(string reference, string expected)
        {
            JsonObject source = Source("http://a/b/c/d;p?q");
            source["properties"]!["value"]!["forms"]![0]!["href"] = reference;
            Mock<IWotThingResolver> sources = Resolver(new Dictionary<string, JsonObject> { ["urn:source"] = source });
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(Projection("urn:source").ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(expected));
        }

        [TestCase("https://review.test/views/root.json?old=1", "?edition=2",
            "https://review.test/views/root.json?edition=2")]
        [TestCase("https://review.test/views/root.json?path=/wrong/route", "../device.json",
            "https://review.test/device.json")]
        [TestCase("https://review.test/views/root.json#old", "?edition=2",
            "https://review.test/views/root.json?edition=2")]
        [TestCase("https://review.test/views/root.json", "/assets/../device.json",
            "https://review.test/device.json")]
        public async Task ReviewedSourceUrisResolvePathQueryAndFragmentAsSeparateComponents(
            string baseUri, string reference, string expected)
        {
            JsonObject projection = Projection(reference);
            projection["base"] = baseUri;
            Mock<IWotThingResolver> sources = Resolver(new Dictionary<string, JsonObject>
            {
                [expected] = Source("https://device.test/api/")
            });
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["value"].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/api/value"));
            sources.Verify(value => value.ResolveThingAsync(
                expected, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReviewedQueryBearingOrganizesGraphCannotHideItsCycle(bool oldQuery)
        {
            JsonObject projection = Projection("https://review.test/source.json");
            projection["base"] = "https://review.test/views/root.json";
            projection["links"] = new JsonArray(Organizes("./group/a.json"));
            var first = new JsonObject
            {
                ["@type"] = "Thing",
                ["title"] = "First",
                ["base"] = "../collections/node.json" + (oldQuery ? "?old=1" : string.Empty),
                ["links"] = new JsonArray(Organizes("?next=2"))
            };
            var second = new JsonObject
            {
                ["@type"] = "Thing",
                ["title"] = "Second",
                ["links"] = new JsonArray(Organizes("../group/a.json"))
            };
            Mock<IWotThingResolver> sources = Resolver(new Dictionary<string, JsonObject>
            {
                ["https://review.test/source.json"] = Source("https://device.test/api/"),
                ["https://review.test/views/group/a.json"] = first,
                ["https://review.test/views/collections/node.json?next=2"] = second
            });
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionCycle), Is.True, string.Join("; ", result.Diagnostics));
            sources.Verify(value => value.ResolveThingAsync(
                "https://review.test/views/collections/node.json?next=2",
                It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once());
        }

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
            root["uav:projects"]![0]!["type"] = WotProjection.ContentType;
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
                ["@type"] = new JsonArray("uav:projection"),
                ["uav:projectionKind"] = "ThingDescription",
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
