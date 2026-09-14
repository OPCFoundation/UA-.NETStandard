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
        public async Task UnresolvedHostAnnotationsCannotAcquireSourceMeaning(
            [Values("none", "urn", "tag", "null-base", "null-prefix", "unknown-context")] string scope,
            [Values("@type", "uav:semanticId")] string annotation)
        {
            JsonObject plan = Plan();
            JsonObject source = ContextReviewSource();
            var context = new JsonArray("https://www.w3.org/2022/wot/td/v1.1");
            plan["@context"] = context;
            string value = "Role";
            if (scope == "urn")
            {
                plan["id"] = "urn:host:opaque";
            }
            else if (scope == "tag")
            {
                plan["id"] = "tag:host,2026:opaque";
            }
            else if (scope == "null-base")
            {
                plan["id"] = "https://host.test/views/view.json";
                context.Add(new JsonObject { ["@base"] = null });
            }
            else if (scope == "null-prefix")
            {
                context.Add(new JsonObject { ["g"] = null });
                value = "g:HostRole";
            }
            else if (scope == "unknown-context")
            {
                context.Add("https://host.test/unacquired.context.jsonld");
                value = "g:HostRole";
            }
            if (annotation == "@type")
            {
                context.Add(PropertyVocabulary(null));
            }
            plan["properties"]!["reading"]![annotation] = value;

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True);
        }

        [Test]
        public async Task LocatedHostAnnotationsKeepTheirOwnBase(
            [Values] bool explicitBase, [Values("@type", "uav:semanticId")] string annotation)
        {
            JsonObject plan = Plan();
            plan["id"] = explicitBase ? "urn:host:opaque" : "https://host.test/views/view.json";
            if (explicitBase)
            {
                plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                    new JsonObject { ["@base"] = "https://host.test/views/" });
            }
            if (annotation == "@type")
            {
                JsonArray context = plan["@context"] is JsonArray entries
                    ? entries
                    : new JsonArray(plan["@context"]!.DeepClone());
                if (plan["@context"] is not JsonArray)
                {
                    plan["@context"] = context;
                }
                context.Add(PropertyVocabulary(null));
            }
            plan["properties"]!["reading"]![annotation] = "Role";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement actual = view.Properties["reading"].GetProperty(annotation);
            Assert.That(annotation == "@type"
                ? actual.EnumerateArray().Any(item => item.GetString() == "https://host.test/views/Role")
                : actual.GetString() == "https://host.test/views/Role", Is.True);
        }

        [TestCase("string-chain", true)]
        [TestCase("object-chain", true)]
        [TestCase("compact-vocab", true)]
        [TestCase("object-prefix-vocab", true)]
        [TestCase("alias-cycle", false)]
        [TestCase("null-alias", false)]
        public async Task HostTypeAliasesAndVocabularyResolveCompletely(string kind, bool success)
        {
            JsonObject plan = Plan();
            plan["id"] = "https://host.test/views/view.json";
            var terms = new JsonObject();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1", terms);
            plan["properties"]!["reading"]!["@type"] = "Signal";
            if (kind is "compact-vocab" or "object-prefix-vocab")
            {
                terms["h"] = kind == "compact-vocab" ? JsonValue.Create("urn:host:") :
                    new JsonObject { ["@id"] = "urn:host:", ["@prefix"] = true };
                terms["@vocab"] = "h:";
                plan["@context"]!.AsArray().Add(PropertyVocabulary("h:"));
            }
            else
            {
                terms["Signal"] = kind == "null-alias" ? null :
                    kind == "object-chain" ? new JsonObject { ["@id"] = "Kind" } : JsonValue.Create("Kind");
                terms["Kind"] = kind == "alias-cycle" ? "Signal" : "urn:host:Signal";
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.Properties["reading"].GetProperty("@type").EnumerateArray()
                    .Select(item => item.GetString()), Is.EquivalentTo(["uav:variable", "urn:host:Signal"]));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                    Is.True);
            }
        }

        [Test]
        public async Task SecurityDefinitionNamesAreContextMapKeys(
            [Values("@context", "@base", "context")] string name, [Values] bool host)
        {
            JsonObject plan = Plan();
            JsonObject source = ContextReviewSource();
            JsonObject owner = host ? plan : source;
            owner["security"] = name;
            owner["securityDefinitions"] = new JsonObject { [name] = new JsonObject { ["scheme"] = "nosec" } };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string encoded = name switch
            {
                "@context" => "QGNvbnRleHQ",
                "@base" => "QGJhc2U",
                _ => "Y29udGV4dA"
            };
            string qualified = (host ? "q:p:" : "q:s:cA:") + encoded;
            JsonElement definition = view.RootElement.GetProperty("securityDefinitions").GetProperty(qualified);
            Assert.That(definition.GetProperty("scheme").GetString(), Is.EqualTo("nosec"));
            Assert.That(definition.TryGetProperty("@context", out _), Is.True);
        }

        [Test]
        public async Task DuplicateSemanticContextsReturnDiagnostics(
            [Values("host-root", "source-root", "source-local", "source-term")] string location,
            [Values("ex", "@base")] string member)
        {
            JsonObject plan = Plan();
            JsonObject source = ContextReviewSource();
            JsonObject malformed = new() { [member] = member == "ex" ? "urn:first:" : "./first/" };
            JsonObject owner = location == "host-root" ? plan : source;
            if (location == "source-local")
            {
                owner["properties"]!["Value"]!["@context"] = malformed;
            }
            else if (location == "source-term")
            {
                owner["@context"]!.AsArray().Add(new JsonObject
                {
                    ["labels"] = new JsonObject { ["@id"] = "urn:labels", ["@context"] = malformed }
                });
            }
            else
            {
                if (owner["@context"] is not JsonArray)
                {
                    owner["@context"] = new JsonArray(owner["@context"]!.DeepClone());
                }
                owner["@context"]!.AsArray().Add(malformed);
            }
            string original = malformed.ToJsonString();
            string duplicate = original[..^1] + ",\"" + member + "\":\"urn:second:\"}";
            string hostJson = plan.ToJsonString();
            string sourceJson = source.ToJsonString();
            if (location == "host-root")
            {
                hostJson = hostJson.Replace(original, duplicate, StringComparison.Ordinal);
            }
            else
            {
                sourceJson = sourceJson.Replace(original, duplicate, StringComparison.Ordinal);
            }

            WotConversionResult<WotDocument> result;
            if (location == "host-root")
            {
                var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
                using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(hostJson));
                result = await new WotProjectionResolver(documents.Object).ResolveAsync(document).ConfigureAwait(false);
                documents.VerifyNoOtherCalls();
            }
            else
            {
                result = await ResolveAsync(hostJson, sourceJson).ConfigureAwait(false);
            }
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True);
        }

        [TestCase("title")]
        [TestCase("description")]
        public async Task HostTextPredicateCannotAcquireSourcePrefix(string term)
        {
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject
                {
                    ["g"] = null,
                    ["properties"] = new JsonObject
                    {
                        ["@container"] = "@index",
                        ["@context"] = new JsonObject
                        {
                            [term] = new JsonObject { ["@id"] = "g:HostLabel", ["@language"] = "fr" }
                        }
                    }
                });
            plan["properties"]!["reading"]![term] = "Texte";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True);
        }

        [TestCase("true")]
        [TestCase("42")]
        public async Task InvalidContextDeclarationKindsReturnDiagnostics(string json)
        {
            JsonObject source = ContextReviewSource();
            source["@context"] = JsonNode.Parse(json);

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True);
        }

        [TestCase("@context")]
        [TestCase("@base")]
        public async Task HostSecurityMapNamesRemainNamesWithoutExplicitContext(string name)
        {
            JsonObject plan = Plan();
            plan.Remove("@context");
            plan["security"] = name;
            plan["securityDefinitions"] = new JsonObject { [name] = new JsonObject { ["scheme"] = "nosec" } };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string key = name == "@context" ? "q:p:QGNvbnRleHQ" : "q:p:QGJhc2U";
            Assert.That(view.RootElement.GetProperty("securityDefinitions").GetProperty(key)
                .GetProperty("scheme").GetString(), Is.EqualTo("nosec"));
        }

        [TestCase("root", "https://www.w3.org/2019/wot/td#")]
        [TestCase("property", "https://www.w3.org/2019/wot/json-schema#")]
        [TestCase("form", "https://www.w3.org/2019/wot/hypermedia#")]
        [TestCase("security", "https://www.w3.org/2019/wot/security#")]
        public void StandardVocabularyMatchesItsCarryingScope(string scope, string expected)
        {
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(ContextReviewSource().ToJsonString()));
            JsonElement owner = scope switch
            {
                "property" => document.Properties["Value"],
                "form" => document.Properties["Value"].GetProperty("forms")[0],
                "security" => document.SecurityDefinitions["none"],
                _ => document.RootElement
            };

            Assert.That(document.TryGetContextTerm("@vocab", out JsonElement vocabulary, owner), Is.True);
            Assert.That(vocabulary.GetString(), Is.EqualTo(expected));
        }

        [Test]
        public async Task StandardPropertyVocabularyPreservesBareTypeAnnotations(
            [Values("Signal", "dataPoint")] string typeName, [Values] bool rootOverride)
        {
            JsonObject plan = Plan();
            plan["id"] = "urn:host:opaque";
            if (rootOverride)
            {
                plan["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                    new JsonObject { ["@vocab"] = "urn:host:" });
            }
            plan["properties"]!["reading"]!["@type"] = typeName;

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement property = view.Properties["reading"];
            Assert.That(view.TryGetContextTerm("@vocab", out JsonElement vocabulary, property), Is.True);
            Assert.That(vocabulary.GetString(), Is.EqualTo("https://www.w3.org/2019/wot/json-schema#"));
            Assert.That(property.GetProperty("@type").EnumerateArray()
                .Any(item => item.GetString() == typeName ||
                    item.GetString() == "https://www.w3.org/2019/wot/json-schema#" + typeName), Is.True);
        }

        private static JsonObject ContextReviewSource()
        {
            JsonObject source = Source();
            source["@context"] = new JsonArray("https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["s"] = "urn:source", ["g"] = "urn:source:", ["h"] = "urn:source:" });
            source["uav:id"] = "nsu=urn:source;i=1";
            source["properties"]!["Value"]!["@type"] = "uav:variable";
            source["properties"]!["Value"]!["type"] = "number";
            source["properties"]!["Value"]!["uav:dataTypeId"] = "i=11";
            source["properties"]!["Value"]!["uav:id"] = "nsu=urn:source;i=2";
            source["properties"]!["Value"]!["uav:browseName"] = "s:Reading";
            return source;
        }

        private static JsonObject PropertyVocabulary(string vocabulary)
        {
            return new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["@id"] = "https://www.w3.org/2019/wot/td#hasPropertyAffordance",
                    ["@type"] = "@id",
                    ["@container"] = "@index",
                    ["@index"] = "name",
                    ["@context"] = new JsonObject { ["@vocab"] = vocabulary }
                }
            };
        }
    }
}
