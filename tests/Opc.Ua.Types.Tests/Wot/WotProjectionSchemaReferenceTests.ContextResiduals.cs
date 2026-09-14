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

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task HostPrefixChainsResolveCompletelyAndCyclesFail(
            [Values] bool objectForm, [Values] bool cycle)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["a"] = objectForm
                        ? new JsonObject { ["@id"] = "b:", ["@prefix"] = true }
                        : JsonValue.Create("b:"),
                    ["b"] = objectForm
                        ? new JsonObject { ["@id"] = cycle ? "a:" : "urn:host:", ["@prefix"] = true }
                        : JsonValue.Create(cycle ? "a:" : "urn:host:")
                });
            plan["properties"]!["reading"]!["@type"] = "a:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!cycle), string.Join("; ", result.Diagnostics));
            if (cycle)
            {
                AssertContextRejected(result);
            }
            else
            {
                Assert.That(view.Properties["reading"].GetProperty("@type").EnumerateArray()
                    .Select(item => item.GetString()), Is.EquivalentTo(["uav:variable", "urn:host:Signal"]));
            }
        }

        [Test]
        public async Task SelfAndIndirectPrefixCyclesReturnDiagnostics(
            [Values] bool objectForm, [Values("self", "self-suffix", "indirect-suffix")] string cycle)
        {
            JsonObject plan = Plan();
            string first = cycle switch
            {
                "self" => "a:",
                "self-suffix" => "a:base:",
                _ => "b:first:"
            };
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["a"] = objectForm
                        ? new JsonObject { ["@id"] = first, ["@prefix"] = true }
                        : JsonValue.Create(first),
                    ["b"] = objectForm
                        ? new JsonObject { ["@id"] = "a:second:", ["@prefix"] = true }
                        : JsonValue.Create("a:second:")
                });
            plan["properties"]!["reading"]!["@type"] = "a:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            AssertContextRejected(result);
        }

        [Test]
        public async Task TextPredicatesRequireTheirOriginalKnownDefinition(
            [Values("title", "description")] string term,
            [Values("unknown-last", "known-last", "disabled")] string ordering)
        {
            JsonObject plan = Plan();
            JsonObject known = new()
            {
                [term] = ordering == "disabled" ? null :
                    new JsonObject { ["@id"] = "https://host.test/label", ["@language"] = "fr" }
            };
            JsonNode scope = ordering switch
            {
                "unknown-last" => new JsonArray(known, "https://unacquired.test/context.jsonld"),
                "known-last" => new JsonArray("https://unacquired.test/context.jsonld", known),
                _ => known
            };
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                PropertiesScope(scope));
            plan["properties"]!["reading"]![term] = "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (ordering != "known-last")
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definition = view.Properties["reading"].GetProperty("@context").EnumerateArray()
                .Last(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(term, out _))
                .GetProperty(term);
            Assert.That(definition.GetProperty("@id").GetString(), Is.EqualTo("https://host.test/label"));
            Assert.That(definition.GetProperty("@language").GetString(), Is.EqualTo("fr"));
        }

        [Test]
        public async Task RestoredTextPredicateDoesNotInventUnknownLanguage(
            [Values("title", "description")] string term,
            [Values("absent", "fr", "null")] string language)
        {
            JsonObject plan = Plan();
            var definition = new JsonObject { ["@id"] = "https://host.test/label" };
            if (language != "absent")
            {
                definition["@language"] = language == "null" ? null : JsonValue.Create(language);
            }
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                PropertiesScope(new JsonArray("https://unacquired.test/context.jsonld",
                    new JsonObject { [term] = definition })));
            plan["properties"]!["reading"]![term] = "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (language == "absent")
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement carried = view.Properties["reading"].GetProperty("@context").EnumerateArray()
                .Last(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(term, out _))
                .GetProperty(term);
            Assert.That(carried.GetProperty("@id").GetString(), Is.EqualTo("https://host.test/label"));
            Assert.That(carried.GetProperty("@language").GetString(), Is.EqualTo(language == "null" ? null : "fr"));
        }

        [Test]
        public async Task NestedInvalidContextDeclarationsFailBeforeAcquisition(
            [Values] bool host, [Values("42", "true")] string invalid)
        {
            JsonObject plan = Plan();
            JsonObject source = ContextReviewSource();
            JsonObject owner = host ? plan : source;
            owner["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                PropertiesScope(JsonNode.Parse(invalid)));

            WotConversionResult<WotDocument> result;
            if (host)
            {
                var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
                using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
                result = await new WotProjectionResolver(documents.Object).ResolveAsync(document).ConfigureAwait(false);
                documents.VerifyNoOtherCalls();
            }
            else
            {
                result = await ResolveAsync(plan, source).ConfigureAwait(false);
            }
            using WotDocument view = result.Value;
            AssertContextRejected(result);
        }

        [Test]
        public async Task ImportedContextsInvalidateEarlierPrefixAuthority(
            [Values] bool restored, [Values] bool sameObject,
            [Values("https", "urn-unknown", "urn-disabled")] string namespaceKind)
        {
            var import = new JsonObject { ["@import"] = "https://unacquired.test/context.jsonld" };
            var contexts = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["g"] = "urn:earlier:" }, import);
            if (namespaceKind == "urn-disabled")
            {
                if (sameObject)
                {
                    import["urn"] = null;
                }
                else
                {
                    contexts.Add(new JsonObject { ["urn"] = null });
                }
            }
            string namespaceUri = namespaceKind == "https" ? "https://host.test/types/" : "urn:host:";
            if (restored)
            {
                if (sameObject)
                {
                    import["g"] = namespaceUri;
                }
                else
                {
                    contexts.Add(new JsonObject { ["g"] = namespaceUri });
                }
            }
            contexts.Add(PropertiesScope(new JsonObject
            {
                ["@vocab"] = "https://www.w3.org/2019/wot/json-schema#"
            }));
            JsonObject plan = Plan();
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["uav:semanticId"] = "g:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (!restored || namespaceKind == "urn-unknown")
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["reading"].GetProperty("uav:semanticId").GetString(),
                Is.EqualTo(namespaceKind == "https" ? "https://host.test/types/Signal" : "urn:host:Signal"));
        }

        [Test]
        public async Task PropertyScopeMustBeEstablishedAfterUnknownRootContext(
            [Values] bool restored, [Values("@type", "title")] string annotation)
        {
            JsonObject plan = Plan();
            plan["id"] = "urn:host:opaque";
            var contexts = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1", "https://unacquired.test/context.jsonld");
            if (restored)
            {
                contexts.Add(PropertiesScope(new JsonObject
                {
                    ["@vocab"] = "https://www.w3.org/2019/wot/json-schema#",
                    ["title"] = new JsonObject
                    {
                        ["@id"] = "https://www.w3.org/2019/wot/td#title",
                        ["@language"] = "en"
                    }
                }));
            }
            plan["@context"] = contexts;
            plan["properties"]!["reading"]![annotation] = annotation == "@type" ? "Signal" : "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (!restored)
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            if (annotation == "@type")
            {
                Assert.That(view.Properties["reading"].GetProperty("@type").EnumerateArray()
                    .Any(item => item.GetString() is "Signal" or
                        "https://www.w3.org/2019/wot/json-schema#Signal"), Is.True);
            }
            else
            {
                Assert.That(view.Properties["reading"].GetProperty("title").GetString(), Is.EqualTo("Host label"));
            }
        }

        private static JsonObject PropertiesScope(JsonNode context)
        {
            return new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["@id"] = "https://www.w3.org/2019/wot/td#hasPropertyAffordance",
                    ["@type"] = "@id",
                    ["@container"] = "@index",
                    ["@index"] = "name",
                    ["@context"] = context
                }
            };
        }

        private static void AssertContextRejected(WotConversionResult<WotDocument> result)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True, string.Join("; ", result.Diagnostics));
        }
    }
}
