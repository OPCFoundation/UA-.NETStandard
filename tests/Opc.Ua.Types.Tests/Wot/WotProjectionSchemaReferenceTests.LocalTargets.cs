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

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [TestCase("/properties/Other", "Other", "")]
        [TestCase("/properties/Other/properties/part", "Other", "/properties/part")]
        [TestCase("/properties/Other/properties/const", "Other", "/properties/const")]
        [TestCase("/properties/Other/items", "Other", "/items")]
        [TestCase("/properties/Other/oneOf/0", "Other", "/oneOf/0")]
        [TestCase("/properties/Other/additionalProperties", "Other", "/additionalProperties")]
        [TestCase("/properties/Other/$defs/Local", "Other", "/$defs/Local")]
        [TestCase("/properties/Other/definitions/Local", "Other", "/definitions/Local")]
        [TestCase("/properties/a~1b~0c", "a/b~c", "")]
        [TestCase("/actions/hidden/input", "input", "")]
        [TestCase("/actions/hidden/output", "output", "")]
        [TestCase("/events/hidden/data", "data", "")]
        [TestCase("/events/hidden/subscription", "subscription", "")]
        [TestCase("/events/hidden/cancellation", "cancellation", "")]
        [TestCase("/events/hidden/dataResponse", "dataResponse", "")]
        [TestCase("/uriVariables/filter", "filter", "")]
        [TestCase("/$defs/Shared", "Shared", "")]
        [TestCase("/definitions/Shared", "Shared", "")]
        public async Task UnselectedLocalSchemaTargetsAreCarriedWithoutSelectingTheirOwners(
            string pointer, string name, string suffix)
        {
            JsonObject source = LocalSchemaSource();
            source["properties"]!["Value"]!["$ref"] = "#" + pointer;
            string original = source.ToJsonString();

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties.Keys, Is.EquivalentTo(s_selectedPropertyNames));
            Assert.That(view.Actions, Is.Empty);
            Assert.That(view.Events, Is.Empty);
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(1));
            string escapedName = name
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
            string destination = "#/schemaDefinitions/" + escapedName + suffix;
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(), Is.EqualTo(destination));
            Assert.That(view.TryEvaluatePointer(destination[1..], out JsonElement carried), Is.True);
            Assert.That(carried.GetProperty("type").GetString(), Is.EqualTo("number"));
            Assert.That(carried.GetProperty("minimum").GetInt32(), Is.EqualTo(3));
            Assert.That(carried.GetProperty("maximum").GetInt32(), Is.EqualTo(17));
            Assert.That(source.ToJsonString(), Is.EqualTo(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnselectedLocalSchemasReuseRecursiveTargetsWithoutReplacingHostDefinitions(bool collision)
        {
            JsonObject source = LocalSchemaSource();
            source["properties"]!["Value"]!["$ref"] = "#/properties/Other";
            source["properties"]!["Other"]!["properties"]!["part"]!["$ref"] = "#/properties/Other";
            JsonObject plan = Plan();
            const string stem = "q:d:cA:L3Byb3BlcnRpZXMvT3RoZXI";
            if (collision)
            {
                plan["schemaDefinitions"] = new JsonObject
                {
                    ["Other"] = new JsonObject { ["type"] = "string" },
                    [stem] = new JsonObject { ["type"] = "boolean" }
                };
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string name = collision ? stem + ":1" : "Other";
            JsonElement definitions = view.RootElement.GetProperty("schemaDefinitions");
            Assert.That(definitions.EnumerateObject().Count(), Is.EqualTo(collision ? 3 : 1));
            string destination = "#/schemaDefinitions/" + name;
            Assert.That(view.Properties["reading"].GetProperty("$ref").GetString(), Is.EqualTo(destination));
            Assert.That(definitions.GetProperty(name).GetProperty("properties").GetProperty("part")
                .GetProperty("$ref").GetString(), Is.EqualTo(destination));
            if (collision)
            {
                Assert.That(definitions.GetProperty("Other").GetProperty("type").GetString(), Is.EqualTo("string"));
                Assert.That(definitions.GetProperty(stem).GetProperty("type").GetString(), Is.EqualTo("boolean"));
            }
        }

        [Test]
        public async Task UnselectedLocalSchemaCarriageRetainsTheTargetContextAndReferenceOrigin()
        {
            JsonObject source = LocalSchemaSource();
            source["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["v"] = "urn:source:" });
            source["properties"]!["Value"]!["$ref"] = "#/actions/hidden/input";
            source["actions"]!["hidden"]!["@context"] = new JsonObject { ["v"] = "urn:action:" };
            source["actions"]!["hidden"]!["input"]!["@type"] = "v:Reading";
            source["actions"]!["hidden"]!["input"]!["$ref"] = "./schema.json#/Reading";
            JsonObject plan = Plan();
            plan["@context"] = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                new JsonObject { ["v"] = "urn:host:" });

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement schema = view.RootElement.GetProperty("schemaDefinitions").GetProperty("input");
            Assert.That(schema.GetProperty("@type").GetString(), Is.EqualTo("v:Reading"));
            Assert.That(view.TryGetContextPrefix("v", out string prefix, schema), Is.True);
            Assert.That(prefix, Is.EqualTo("urn:action:"));
            Assert.That(schema.GetProperty("$ref").GetString(),
                Is.EqualTo("https://origin.test/models/schema.json#/Reading"));
            Assert.That(view.TryGetContextPrefix("v", out string host), Is.True);
            Assert.That(host, Is.EqualTo("urn:host:"));
            Assert.That(view.Actions, Is.Empty);
        }

        [TestCase("/properties/Other/const")]
        [TestCase("/properties/Other/default")]
        [TestCase("/properties/Other/enum/0")]
        [TestCase("/properties/Other/examples/0")]
        [TestCase("/properties/Other/uav:metadata")]
        [TestCase("/properties/Other/uav:propertyConfiguration")]
        [TestCase("/properties/Other/unrecognized")]
        [TestCase("/properties/Other/forms/0")]
        [TestCase("/actions/hidden")]
        [TestCase("/events/hidden")]
        [TestCase("/properties/Other/oneOf/00")]
        public async Task UnselectedLocalSchemaTargetsCannotEnterLiteralOrNonSchemaLocations(string pointer)
        {
            JsonObject source = LocalSchemaSource();
            source["properties"]!["Value"]!["$ref"] = "#" + pointer;
            foreach (string name in new[]
            {
                "const", "default", "uav:metadata", "uav:propertyConfiguration", "unrecognized"
            })
            {
                source["properties"]!["Other"]![name] = LocalNumericSchema();
            }
            source["properties"]!["Other"]!["enum"] = new JsonArray(LocalNumericSchema());
            source["properties"]!["Other"]!["examples"] = new JsonArray(LocalNumericSchema());
            source["properties"]!["Other"]!["forms"] = new JsonArray(LocalNumericSchema());

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == SourceHref + "#" + pointer), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnselectedLocalSchemaReferencesCannotEnterAlreadyCarriedLiteralValues(bool literalFirst)
        {
            JsonObject source = LocalSchemaSource();
            source["properties"]!["Other"]!["const"] = LocalNumericSchema();
            var schema = new JsonObject { ["$ref"] = "#/properties/Other" };
            var literal = new JsonObject { ["$ref"] = "#/properties/Other/const" };
            source["properties"]!["Value"]!["oneOf"] = literalFirst
                ? new JsonArray(literal, schema)
                : new JsonArray(schema, literal);

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Location?.Reference == SourceHref + "#/properties/Other/const"), Is.True);
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public async Task UnselectedLocalSchemaRootsCountOnceAgainstTheOutputBudget(int budget, bool success)
        {
            JsonObject source = LocalSchemaSource();
            source["properties"]!["Value"]!["oneOf"] = new JsonArray(
                new JsonObject { ["$ref"] = "#/properties/Other/items" },
                new JsonObject { ["$ref"] = "#/properties/Other/properties/part" });
            var options = new WotNodeSetConverterOptions { MaxNodeCount = budget };

            WotConversionResult<WotDocument> result = await ResolveAsync(Plan(), source, options).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (success)
            {
                Assert.That(view.RootElement.GetProperty("schemaDefinitions").EnumerateObject().Count(), Is.EqualTo(1));
                JsonElement references = view.Properties["reading"].GetProperty("oneOf");
                Assert.That(references[0].GetProperty("$ref").GetString(),
                    Is.EqualTo("#/schemaDefinitions/Other/items"));
                Assert.That(references[1].GetProperty("$ref").GetString(),
                    Is.EqualTo("#/schemaDefinitions/Other/properties/part"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.TraversalBudgetExhausted), Is.True);
            }
        }

        private static JsonObject LocalSchemaSource()
        {
            JsonObject source = Source();
            JsonObject schema = LocalNumericSchema();
            schema["properties"] = new JsonObject
            {
                ["part"] = LocalNumericSchema(),
                ["const"] = LocalNumericSchema()
            };
            schema["items"] = LocalNumericSchema();
            schema["oneOf"] = new JsonArray(LocalNumericSchema());
            schema["additionalProperties"] = LocalNumericSchema();
            schema["$defs"] = new JsonObject { ["Local"] = LocalNumericSchema() };
            schema["definitions"] = new JsonObject { ["Local"] = LocalNumericSchema() };
            source["properties"]!["Other"] = schema;
            source["properties"]!["a/b~c"] = LocalNumericSchema();
            source["actions"] = new JsonObject
            {
                ["hidden"] = new JsonObject
                {
                    ["input"] = LocalNumericSchema(),
                    ["output"] = LocalNumericSchema()
                }
            };
            source["events"] = new JsonObject
            {
                ["hidden"] = new JsonObject
                {
                    ["data"] = LocalNumericSchema(),
                    ["subscription"] = LocalNumericSchema(),
                    ["cancellation"] = LocalNumericSchema(),
                    ["dataResponse"] = LocalNumericSchema()
                }
            };
            source["uriVariables"] = new JsonObject { ["filter"] = LocalNumericSchema() };
            source["$defs"] = new JsonObject { ["Shared"] = LocalNumericSchema() };
            source["definitions"] = new JsonObject { ["Shared"] = LocalNumericSchema() };
            return source;
        }

        private static JsonObject LocalNumericSchema()
        {
            return new JsonObject { ["type"] = "number", ["minimum"] = 3, ["maximum"] = 17 };
        }

        private static readonly string[] s_selectedPropertyNames = ["reading"];
    }
}
