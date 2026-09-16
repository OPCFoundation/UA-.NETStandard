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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    public sealed class WotProjectionDependencyTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectedValueCarriesItsUnitOrReusesItsSelectedRename(bool selectUnit)
        {
            JsonObject source = Source("rpm");
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };
            if (selectUnit)
            {
                plan["properties"]!["speedUnit"] = Select("/properties/Unit");
            }
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string name = selectUnit ? "speedUnit" : "Unit";
            Assert.That(view.Properties.Keys, Is.EquivalentTo(["speed", name]));
            Assert.That(view.Properties["speed"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/" + name));
            Assert.That(view.Properties[name].GetProperty("const").GetString(), Is.EqualTo("rpm"));
            Assert.That(view.Properties[name].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/Unit"));
            Assert.That(view.Properties[name].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Unit"));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
        }

        [Test]
        public async Task UnitSupportCannotOverwriteAuthoredSelectionsOrTheirGeneratedNameLookalikes()
        {
            const string stem = "q:d:cA:L3Byb3BlcnRpZXMvVW5pdA";
            JsonObject source = Source("rpm");
            source["properties"]!["Other"] = Property("string", "%", "Other");
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject
            {
                ["speed"] = Select("/properties/Value"),
                ["Unit"] = Select("/properties/Other"),
                [stem] = Select("/properties/Other")
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties, Has.Count.EqualTo(4));
            Assert.That(view.Properties["speed"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/" + stem + ":1"));
            Assert.That(view.Properties[stem + ":1"].GetProperty("const").GetString(), Is.EqualTo("rpm"));
            Assert.That(view.Properties["Unit"].GetProperty("const").GetString(), Is.EqualTo("%"));
            Assert.That(view.Properties[stem].GetProperty("const").GetString(), Is.EqualTo("%"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SameNamedDependenciesFromDifferentSourcesRetainTheirSourceIdentity(bool reverseMembers)
        {
            const string secondHref = "https://origin.test/other.td.json";
            JsonObject first = Source("C");
            JsonObject second = Source("F");
            second["id"] = secondHref;
            JsonObject plan = Plan();
            ((JsonArray)plan["uav:projects"]!).Add(new JsonObject
            {
                ["uav:sourceName"] = "q",
                ["href"] = secondHref,
                ["type"] = "application/td+json"
            });
            var selected = new JsonObject();
            foreach (string name in (string[])(reverseMembers ? ["right", "left"] : ["left", "right"]))
            {
                selected[name] = new JsonObject
                {
                    ["tm:ref"] = (name == "left" ? SourceHref : secondHref) + "#/properties/Value"
                };
            }
            plan["properties"] = selected;

            WotConversionResult<WotDocument> result = await ResolveAsync(plan,
                new Dictionary<string, JsonObject> { [SourceHref] = first, [secondHref] = second }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["left"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Unit"));
            Assert.That(view.Properties["right"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/q:d:cQ:L3Byb3BlcnRpZXMvVW5pdA"));
            Assert.That(view.Properties["Unit"].GetProperty("const").GetString(), Is.EqualTo("C"));
            Assert.That(view.Properties["q:d:cQ:L3Byb3BlcnRpZXMvVW5pdA"].GetProperty("const").GetString(), Is.EqualTo("F"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OneSourceLocationCannotSupplyConflictingDependencyDefinitions(bool changed)
        {
            JsonObject first = Source("C");
            JsonObject second = Source(changed ? "F" : "C");
            JsonObject plan = Plan();
            plan["uav:projects"]![0]!["uav:selectAll"] = true;
            plan["uav:projects"]![0]!["uav:namePrefix"] = "left";
            ((JsonArray)plan["uav:projects"]!).Add(new JsonObject
            {
                ["uav:sourceName"] = "q",
                ["href"] = SourceHref,
                ["type"] = "application/td+json",
                ["uav:selectAll"] = true,
                ["uav:namePrefix"] = "right"
            });
            var sources = new Mock<IWotThingResolver>(MockBehavior.Strict);
            sources.SetupSequence(resolver => resolver.ResolveThingAsync(
                    SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(first.ToJsonString())))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(second.ToJsonString(
                    new JsonSerializerOptions { WriteIndented = true }))));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
            var resolver = new WotProjectionResolver(sources.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(!changed), string.Join("; ", result.Diagnostics));
            if (changed)
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved), Is.True);
            }
            else
            {
                Assert.That(view.Properties, Has.Count.EqualTo(4));
                Assert.That(view.Properties["rightValue"].GetProperty("uav:unitProperty").GetString(),
                    Is.EqualTo("/properties/leftUnit"));
                Assert.That(view.Properties["leftUnit"].GetProperty("const").GetString(), Is.EqualTo("C"));
            }
            sources.Verify(source => source.ResolveThingAsync(
                SourceHref, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CarriedDependenciesRetainOriginalProvenanceAtItsDocumentOrigin(bool selected)
        {
            JsonObject source = Source("rpm");
            source["properties"]!["Unit"]!["uav:resolvedFrom"] = "./original.td.json#/properties/unit";
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };
            if (selected)
            {
                plan["properties"]!["selectedUnit"] = Select("/properties/Unit");
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string name = selected ? "selectedUnit" : "Unit";
            Assert.That(view.Properties[name].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("https://origin.test/original.td.json#/properties/unit"));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("\"\"")]
        [TestCase("[]")]
        public async Task MalformedOriginalProvenanceCannotBeReplacedByAFalseOrigin(string provenance)
        {
            JsonObject source = Source("rpm");
            source["properties"]!["Unit"]!["uav:resolvedFrom"] = JsonNode.Parse(provenance);
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public async Task FiniteRecursiveUnitDependenciesAreCarriedOnceWithoutReverseExpansion()
        {
            JsonObject source = Source("rpm");
            source["properties"]!["Unit"]!["uav:unitProperty"] = "/properties/Other";
            source["properties"]!["Other"] = Property("string", "label", "Other");
            source["properties"]!["Other"]!["uav:unitProperty"] = "/properties/Unit";
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, source, new WotNodeSetConverterOptions { MaxNodeCount = 3 }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.Keys, Is.EquivalentTo(s_recursiveUnitMembers));
            Assert.That(view.Properties["Unit"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Other"));
            Assert.That(view.Properties["Other"].GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Unit"));
        }

        [Test]
        public async Task OpaqueReferenceLookalikesAreNotDependenciesOrRewriteTargets()
        {
            JsonObject source = Source("rpm");
            source["properties"]!["Value"]!["uav:metadata"] = new JsonObject
            {
                ["uav:unitProperty"] = "/properties/Missing",
                ["uav:actsOn"] = "Missing"
            };
            source["properties"]!["Unit"]!["const"] = "/properties/Missing";
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["speed"].GetProperty("uav:metadata").GetProperty("uav:unitProperty").GetString(),
                Is.EqualTo("/properties/Missing"));
            Assert.That(view.Properties["Unit"].GetProperty("const").GetString(), Is.EqualTo("/properties/Missing"));
            Assert.That(view.Properties, Has.Count.EqualTo(2));
            Assert.That(view.Events, Is.Empty);
        }

        [TestCase("unit/name", "/properties/unit~1name")]
        [TestCase("unit~name", "/properties/unit~0name")]
        public async Task UnitDependencyPointersRetainCanonicalEscaping(string name, string pointer)
        {
            JsonObject source = Source("rpm");
            source["properties"]![name] = source["properties"]!["Unit"]!.DeepClone();
            ((JsonObject)source["properties"]!).Remove("Unit");
            source["properties"]!["Value"]!["uav:unitProperty"] = pointer;
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["speed"].GetProperty("uav:unitProperty").GetString(), Is.EqualTo(pointer));
            Assert.That(view.Properties[name].GetProperty("const").GetString(), Is.EqualTo("rpm"));
        }

        [TestCase("null")]
        [TestCase("42")]
        [TestCase("\"/properties/Missing\"")]
        [TestCase("\"/properties/Value\"")]
        [TestCase("\"/properties/Unit/type\"")]
        [TestCase("\"/properties/unit~2\"")]
        public async Task InvalidUnitDependenciesFailWithoutAPartialResolvedResult(string pointer)
        {
            JsonObject source = Source("rpm");
            source["properties"]!["unit~2"] = Property("string", "bad canonical escape", "bad");
            source["properties"]!["Value"]!["uav:unitProperty"] = JsonNode.Parse(pointer);
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.InvalidUnitPointer &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True, string.Join("; ", result.Diagnostics));
        }

        [Test]
        public async Task UnitDependencyMustStillBeStringValued()
        {
            JsonObject source = Source("rpm");
            source["properties"]!["Unit"]!["type"] = "number";
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectedConditionActionCarriesItsOwnEventWithoutReverseExpandingActions(bool selectEvent)
        {
            JsonObject source = Source("rpm");
            source["events"] = new JsonObject
            {
                ["alarm"] = new JsonObject
                {
                    ["uav:conditionType"] = "ua:AlarmConditionType",
                    ["forms"] = Forms("alarm"),
                    ["data"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["EventId"] = new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" }
                        }
                    }
                }
            };
            source["actions"] = new JsonObject
            {
                ["enable"] = new JsonObject
                {
                    ["uav:conditionAction"] = "Enable",
                    ["uav:actsOn"] = "alarm",
                    ["forms"] = Forms("enable")
                },
                ["disable"] = new JsonObject
                {
                    ["uav:conditionAction"] = "Disable",
                    ["uav:actsOn"] = "alarm",
                    ["forms"] = Forms("disable")
                }
            };
            JsonObject plan = Plan();
            plan["actions"] = new JsonObject { ["enablePump"] = Select("/actions/enable") };
            if (selectEvent)
            {
                plan["events"] = new JsonObject { ["pumpAlarm"] = Select("/events/alarm") };
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string eventName = selectEvent ? "pumpAlarm" : "alarm";
            Assert.That(view.Actions.Keys, Is.EquivalentTo(s_selectedActions));
            Assert.That(view.Events.Keys, Is.EquivalentTo([eventName]));
            Assert.That(view.Actions["enablePump"].GetProperty("uav:actsOn").GetString(), Is.EqualTo(eventName));
            Assert.That(view.Events[eventName].GetProperty("uav:conditionType").GetString(),
                Is.EqualTo("ua:AlarmConditionType"));
            Assert.That(view.Events[eventName].GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://device.test/alarm"));
        }

        [TestCase("null", true)]
        [TestCase("42", true)]
        [TestCase("\"\"", true)]
        [TestCase("\"missing\"", true)]
        [TestCase("\"alarm\"", false)]
        public async Task InvalidConditionDependenciesCannotProduceAPartialView(string target, bool condition)
        {
            JsonObject source = Source("rpm");
            source["events"] = new JsonObject
            {
                ["alarm"] = new JsonObject { ["forms"] = Forms("alarm") }
            };
            if (condition)
            {
                source["events"]!["alarm"]!["uav:conditionType"] = "ua:AlarmConditionType";
            }
            source["actions"] = new JsonObject
            {
                ["enable"] = new JsonObject
                {
                    ["uav:conditionAction"] = "Enable",
                    ["uav:actsOn"] = JsonNode.Parse(target),
                    ["forms"] = Forms("enable")
                }
            };
            JsonObject plan = Plan();
            plan["actions"] = new JsonObject { ["enablePump"] = Select("/actions/enable") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.InvalidConditionTarget),
                Is.True);
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        public async Task SupportingAffordancesCountAgainstTheConfiguredNodeBudget(int limit, bool success)
        {
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject { ["speed"] = Select("/properties/Value") };

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, Source("rpm"), new WotNodeSetConverterOptions { MaxNodeCount = limit }).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.Properties, Has.Count.EqualTo(2));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.TraversalBudgetExhausted), Is.True);
            }
        }

        private static Task<WotConversionResult<WotDocument>> ResolveAsync(
            JsonObject plan, JsonObject source, WotNodeSetConverterOptions options = null)
        {
            return ResolveAsync(plan, new Dictionary<string, JsonObject> { [SourceHref] = source }, options);
        }

        private static async Task<WotConversionResult<WotDocument>> ResolveAsync(
            JsonObject plan, Dictionary<string, JsonObject> sources, WotNodeSetConverterOptions options = null)
        {
            var documents = new Mock<IWotThingResolver>(MockBehavior.Strict);
            foreach (KeyValuePair<string, JsonObject> source in sources)
            {
                documents.Setup(resolver => resolver.ResolveThingAsync(
                        source.Key, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(source.Value.ToJsonString())));
            }
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(plan.ToJsonString()));
            var resolver = new WotProjectionResolver(documents.Object, options);
            return await resolver.ResolveAsync(document).ConfigureAwait(false);
        }

        private static JsonObject Plan()
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = "uav:projection",
                ["uav:projectionKind"] = "ThingDescription",
                ["id"] = "urn:projection:dependencies",
                ["title"] = "Projection",
                ["uav:scenario"] = "urn:scenario:dependencies",
                ["securityDefinitions"] = SecurityDefinitions(),
                ["security"] = "none",
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "p",
                    ["href"] = SourceHref,
                    ["type"] = "application/td+json"
                })
            };
        }

        private static JsonObject Source(string unit)
        {
            return new JsonObject
            {
                ["@context"] = "https://www.w3.org/2022/wot/td/v1.1",
                ["@type"] = "Thing",
                ["title"] = "Source",
                ["id"] = SourceHref,
                ["base"] = "https://device.test/",
                ["securityDefinitions"] = SecurityDefinitions(),
                ["security"] = "none",
                ["properties"] = new JsonObject
                {
                    ["Value"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["uav:unitProperty"] = "/properties/Unit",
                        ["forms"] = Forms("Value")
                    },
                    ["Unit"] = Property("string", unit, "Unit")
                }
            };
        }

        private static JsonObject Select(string pointer)
        {
            return new JsonObject { ["tm:ref"] = SourceHref + "#" + pointer };
        }

        private static JsonObject Property(string type, string value, string href)
        {
            return new JsonObject { ["type"] = type, ["const"] = value, ["forms"] = Forms(href) };
        }

        private static JsonArray Forms(string href)
        {
            return new JsonArray(new JsonObject { ["href"] = href });
        }

        private static JsonObject SecurityDefinitions()
        {
            return new JsonObject { ["none"] = new JsonObject { ["scheme"] = "nosec" } };
        }

        private const string SourceHref = "https://origin.test/source.td.json";
        private static readonly string[] s_selectedActions = ["enablePump"];
        private static readonly string[] s_recursiveUnitMembers = ["speed", "Unit", "Other"];
    }
}
