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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection schema reference.
    /// </summary>
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        /// <summary>
        /// Verifies that host prefix namespace is bound when defined.
        /// </summary>
        /// <param name="objectForm">The object-form flag for this test case.</param>
        /// <param name="before">The before flag for this test case.</param>
        [Test]
        public async Task HostPrefixNamespaceIsBoundWhenDefined(
            [Values] bool before, [Values] bool objectForm,
            [Values("@type", "uav:semanticId")] string annotation)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts(
                DefinitionTimePrefix("a", "https://first.test/ns/", objectForm));
            if (before)
            {
                contexts.Add(DefinitionTimePrefix("g", "a:", objectForm));
            }
            contexts.Add(DefinitionTimePrefix("a", "https://second.test/ns/", objectForm));
            if (!before)
            {
                contexts.Add(DefinitionTimePrefix("g", "a:", objectForm));
            }
            plan["@context"] = contexts;
            plan["properties"]!["reading"]![annotation] = "g:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, annotation,
                before ? "https://first.test/ns/Signal" : "https://second.test/ns/Signal");
        }

        /// <summary>
        /// Verifies that host term alias is bound when defined.
        /// </summary>
        /// <param name="objectForm">The object-form flag for this test case.</param>
        /// <param name="before">The before flag for this test case.</param>
        [Test]
        public async Task HostTermAliasIsBoundWhenDefined([Values] bool before, [Values] bool objectForm)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts(
                new JsonObject { ["Kind"] = "https://first.test/ns/Signal" });
            var alias = new JsonObject
            {
                ["Signal"] = objectForm ? new JsonObject { ["@id"] = "Kind" } : JsonValue.Create("Kind")
            };
            if (before)
            {
                contexts.Add(alias);
            }
            contexts.Add(new JsonObject { ["Kind"] = "https://second.test/ns/Signal" });
            if (!before)
            {
                contexts.Add(alias);
            }
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["@type"] = "Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, "@type",
                before ? "https://first.test/ns/Signal" : "https://second.test/ns/Signal");
        }

        /// <summary>
        /// Verifies that host urn namespace is bound when defined.
        /// </summary>
        /// <param name="objectForm">The object-form flag for this test case.</param>
        [Test]
        public async Task HostUrnNamespaceIsBoundWhenDefined(
            [Values("before-rebind", "after-rebind", "before-disable")] string ordering,
            [Values] bool objectForm)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts();
            if (ordering == "after-rebind")
            {
                contexts.Add(DefinitionTimePrefix("urn", "https://second.test/ns/", objectForm));
            }
            else if (ordering == "before-disable")
            {
                contexts.Add(DefinitionTimePrefix("urn", "https://first.test/ns/", objectForm));
            }
            contexts.Add(DefinitionTimePrefix("g", "urn:host:", objectForm));
            if (ordering == "before-rebind")
            {
                contexts.Add(DefinitionTimePrefix("urn", "https://second.test/ns/", objectForm));
            }
            else if (ordering == "before-disable")
            {
                contexts.Add(new JsonObject { ["urn"] = null });
            }
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["@type"] = "g:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string expected = ordering switch
            {
                "before-rebind" => "urn:host:Signal",
                "after-rebind" => "https://second.test/ns/host:Signal",
                _ => "https://first.test/ns/host:Signal"
            };
            AssertDefinitionTimeAnnotation(view, "@type", expected);
        }

        /// <summary>
        /// Verifies that ordered redefinitions are not prefix cycles.
        /// </summary>
        [TestCase("a:Signal")]
        [TestCase("g:Signal")]
        public async Task OrderedRedefinitionsAreNotPrefixCycles(string value)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(
                new JsonObject { ["a"] = "https://first.test/ns/" },
                new JsonObject { ["g"] = "a:" },
                new JsonObject { ["a"] = "g:" });
            plan["properties"]!["reading"]!["@type"] = value;

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, "@type", "https://first.test/ns/Signal");
        }

        /// <summary>
        /// Verifies that property scoped binding uses application context.
        /// </summary>
        [Test]
        public async Task PropertyScopedBindingUsesApplicationContext()
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(
                new JsonObject { ["a"] = "https://first.test/ns/" },
                PropertiesScope(new JsonObject { ["g"] = "a:" }),
                new JsonObject { ["a"] = "https://second.test/ns/" });
            plan["properties"]!["reading"]!["@type"] = "g:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, "@type", "https://second.test/ns/Signal");
        }

        /// <summary>
        /// Verifies that text predicate uses definition time namespace.
        /// </summary>
        [TestCase("title")]
        [TestCase("description")]
        public async Task TextPredicateUsesDefinitionTimeNamespace(string term)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(PropertiesScope(new JsonArray(
                new JsonObject { ["a"] = "https://first.test/ns/" },
                new JsonObject
                {
                    [term] = new JsonObject { ["@id"] = "a:Label", ["@language"] = "fr" }
                },
                new JsonObject { ["a"] = "https://second.test/ns/" })));
            plan["properties"]!["reading"]![term] = "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definition = view.Properties["reading"].GetProperty("@context").EnumerateArray()
                .Last(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(term, out _))
                .GetProperty(term);
            Assert.That(definition.GetProperty("@id").GetString(), Is.EqualTo("https://first.test/ns/Label"));
            Assert.That(definition.GetProperty("@language").GetString(), Is.EqualTo("fr"));
        }

        /// <summary>
        /// Verifies that vocabulary uses definition time namespace.
        /// </summary>
        /// <param name="urn">The urn flag for this test case.</param>
        [Test]
        public async Task VocabularyUsesDefinitionTimeNamespace([Values] bool urn)
        {
            JsonObject plan = Plan();
            var context = new JsonArray();
            if (!urn)
            {
                context.Add(new JsonObject { ["h"] = "https://first.test/ns/" });
            }
            context.Add(new JsonObject { ["@vocab"] = urn ? "urn:host:" : "h:" });
            context.Add(new JsonObject { [urn ? "urn" : "h"] = "https://second.test/ns/" });
            plan["@context"] = DefinitionTimeContexts(PropertiesScope(context));
            plan["properties"]!["reading"]!["@type"] = "Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, "@type",
                urn ? "urn:host:Signal" : "https://first.test/ns/Signal");
        }

        /// <summary>
        /// Verifies that self restated term retains definition time prefix.
        /// </summary>
        /// <param name="rebind">The rebind flag for this test case.</param>
        [Test]
        public async Task SelfRestatedTermRetainsDefinitionTimePrefix([Values] bool rebind)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts(
                new JsonObject { ["h"] = "https://first.test/ns/" },
                new JsonObject { ["h:Signal"] = new JsonObject { ["@id"] = "h:Signal" } });
            if (rebind)
            {
                contexts.Add(new JsonObject { ["h"] = "https://second.test/ns/" });
            }
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["@type"] = "h:Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view, "@type", "https://first.test/ns/Signal");
        }

        [TestCase("title")]
        [TestCase("description")]
        public async Task TextPredicateWithNullIdentityCannotAcquireDefault(string term)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(PropertiesScope(new JsonObject
            {
                [term] = new JsonObject { ["@id"] = null, ["@language"] = "fr" }
            }));
            plan["properties"]!["reading"]![term] = "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument? view = result.Value;

            AssertContextRejected(result);
        }

        /// <summary>
        /// Verifies that text predicate uses its implicit definition vocabulary.
        /// </summary>
        [TestCase("title", "https://first.test/ns/title")]
        [TestCase("description", "https://first.test/ns/description")]
        public async Task TextPredicateUsesItsImplicitDefinitionVocabulary(string term, string expected)
        {
            JsonObject plan = Plan();
            plan["@context"] = DefinitionTimeContexts(PropertiesScope(new JsonArray(
                new JsonObject
                {
                    ["@vocab"] = "https://first.test/ns/",
                    [term] = new JsonObject { ["@language"] = "fr" }
                },
                new JsonObject { ["@vocab"] = "https://second.test/ns/" })));
            plan["properties"]!["reading"]![term] = "Host label";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement definition = view.Properties["reading"].GetProperty("@context").EnumerateArray()
                .Last(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(term, out _))
                .GetProperty(term);
            Assert.That(definition.GetProperty("@id").GetString(), Is.EqualTo(expected));
            Assert.That(definition.GetProperty("@language").GetString(), Is.EqualTo("fr"));
        }

        /// <summary>
        /// Verifies that unknown bare type cannot fall through known vocabulary.
        /// </summary>
        [Test]
        public async Task UnknownBareTypeCannotFallThroughKnownVocabulary(
            [Values("context", "import")] string barrier,
            [Values("none", "absolute", "null")] string term)
        {
            JsonObject plan = Plan();
            var contexts = DefinitionTimeContexts(new JsonObject { ["Signal"] = "https://earlier.test/Signal" });
            contexts.Add(barrier == "context"
                ? JsonValue.Create("https://unacquired.test/context.jsonld")
                : new JsonObject { ["@import"] = "https://unacquired.test/context.jsonld" });
            var scope = new JsonObject { ["@vocab"] = "https://www.w3.org/2019/wot/json-schema#" };
            if (term != "none")
            {
                scope["Signal"] = term == "null" ? null : JsonValue.Create("https://host.test/Signal");
            }
            contexts.Add(PropertiesScope(scope));
            plan["@context"] = contexts;
            plan["properties"]!["reading"]!["@type"] = "Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument? view = result.Value;

            if (term != "absolute")
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            AssertDefinitionTimeAnnotation(view.Required(), "@type", "https://host.test/Signal");
        }

        /// <summary>
        /// Verifies that known inherited term outranks property vocabulary.
        /// </summary>
        /// <param name="alias">The alias flag for this test case.</param>
        [Test]
        public async Task KnownInheritedTermOutranksPropertyVocabulary([Values] bool alias)
        {
            JsonObject plan = Plan();
            var inherited = new JsonObject();
            if (alias)
            {
                inherited["Signal"] = "https://other.example/Signal";
            }
            plan["@context"] = DefinitionTimeContexts(inherited, PropertiesScope(new JsonObject
            {
                ["@vocab"] = "https://www.w3.org/2019/wot/json-schema#"
            }));
            plan["properties"]!["reading"]!["@type"] = "Signal";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            if (alias)
            {
                AssertDefinitionTimeAnnotation(view.Required(), "@type", "https://other.example/Signal");
            }
            else
            {
                Assert.That(view.Required().Properties["reading"].GetProperty("@type").EnumerateArray()
                    .Any(item => item.GetString() is "Signal" or
                        "https://www.w3.org/2019/wot/json-schema#Signal"), Is.True);
            }
        }

        private static JsonArray DefinitionTimeContexts(params JsonNode[] contexts)
        {
            var result = new JsonArray("https://www.w3.org/2022/wot/td/v1.1");
            foreach (JsonNode context in contexts)
            {
                result.Add(context);
            }
            return result;
        }

        private static JsonObject DefinitionTimePrefix(string name, string value, bool objectForm)
        {
            return new JsonObject
            {
                [name] = objectForm
                    ? new JsonObject { ["@id"] = value, ["@prefix"] = true }
                    : JsonValue.Create(value)
            };
        }

        private static void AssertDefinitionTimeAnnotation(WotDocument view, string annotation, string expected)
        {
            JsonElement value = view.Properties["reading"].GetProperty(annotation);
            if (annotation == "@type")
            {
                Assert.That(value.EnumerateArray().Select(item => item.GetString()),
                    Is.EquivalentTo(["uav:variable", expected]));
            }
            else
            {
                Assert.That(value.GetString(), Is.EqualTo(expected));
            }
        }
    }
}
