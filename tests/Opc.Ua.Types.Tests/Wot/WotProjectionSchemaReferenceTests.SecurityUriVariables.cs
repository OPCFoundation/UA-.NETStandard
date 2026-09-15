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
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task UriSecurityVariablesUseTheEffectiveFormOwner(
            [Values("Root", "Source", "Host")] string placement,
            [Values("Direct", "AllOf", "OneOf", "FormOverride")] string requirement)
        {
            (JsonObject plan, JsonObject source, JsonObject owner, JsonObject form) =
                CreateUriSecurityCase(placement);
            JsonNode definitions = owner["securityDefinitions"]!;
            if (requirement is "AllOf" or "OneOf")
            {
                string name = requirement == "AllOf" ? "clientId" : "adminKey";
                definitions["alternative"] = UriSecurityScheme(name);
                definitions["combined"] = new JsonObject
                {
                    ["scheme"] = "combo",
                    [requirement == "AllOf" ? "allOf" : "oneOf"] = new JsonArray("apikey_key", "alternative")
                };
                owner["security"] = "combined";
                if (requirement == "AllOf")
                {
                    form["href"] = "https://forms.test/{clientId}/{adminKey}{?device}";
                }
            }
            else if (requirement == "FormOverride")
            {
                owner["security"] = "none";
                form["security"] = "apikey_key";
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement target = placement == "Root" ? view.RootElement : view.Properties["reading"];
            Assert.That(target.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo(form["href"]!.GetValue<string>()));
            Assert.That(target.GetProperty("uriVariables").EnumerateObject().Select(variable => variable.Name),
                Is.EqualTo(["device"]));
            Assert.That(target.GetProperty("uriVariables").GetProperty("device").GetProperty("default").GetString(),
                Is.EqualTo("data-owner"));
            if (!target.GetProperty("forms")[0].TryGetProperty("security", out JsonElement security))
            {
                security = view.RootElement.GetProperty("security");
            }
            if (security.ValueKind == JsonValueKind.Array)
            {
                Assert.That(security.GetArrayLength(), Is.EqualTo(1));
                security = security[0];
            }
            Assert.That(security.ValueKind, Is.EqualTo(JsonValueKind.String));
            string scheme = security.GetString();
            Assert.That(scheme, Does.StartWith(placement == "Source" ? "q:s:" : "q:p:"));
            Assert.That(view.SecurityDefinitions.ContainsKey(scheme), Is.True);
        }

        [Test]
        public async Task UriSecurityDeclarationsCannotHideMissingOrConflictingDataVariables(
            [Values("Root", "Source", "Host")] string placement,
            [Values("Inactive", "OtherLocation", "Overridden", "DataCollision")] string failure)
        {
            (JsonObject plan, JsonObject source, JsonObject owner, JsonObject form) =
                CreateUriSecurityCase(placement);
            switch (failure)
            {
                case "Inactive":
                    owner["security"] = "none";
                    break;
                case "OtherLocation":
                    owner["securityDefinitions"]!["apikey_key"]!["in"] = "query";
                    break;
                case "Overridden":
                    form["security"] = "none";
                    break;
                case "DataCollision":
                    owner["uriVariables"]!["adminKey"] = new JsonObject { ["type"] = "string" };
                    break;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            string pointer = placement == "Root" ? "/forms/0/href" : "/properties/reading/forms/0/href";
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Location?.Reference == pointer), Is.True);
        }

        [TestCase("Root")]
        [TestCase("Source")]
        public async Task UriSecurityVariablesCannotBorrowAnotherOwnersScheme(string placement)
        {
            (JsonObject plan, JsonObject source, JsonObject owner, JsonObject form) =
                CreateUriSecurityCase(placement);
            owner["security"] = "none";
            JsonObject other = placement == "Root" ? source : plan;
            other["securityDefinitions"]!["apikey_key"] = UriSecurityScheme("adminKey");
            other["security"] = "apikey_key";
            if (placement == "Root")
            {
                source["properties"]!["Value"]!["forms"] = Forms("https://source.test/{adminKey}");
            }
            form["href"] = "https://forms.test/{adminKey}";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            string pointer = placement == "Root" ? "/forms/0/href" : "/properties/reading/forms/0/href";
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Location?.Reference == pointer), Is.True);
        }

        private static (JsonObject Plan, JsonObject Source, JsonObject Owner, JsonObject Form)
            CreateUriSecurityCase(string placement)
        {
            JsonObject plan = Plan();
            JsonObject source = Source();
            JsonObject owner = placement == "Source" ? source : plan;
            owner["securityDefinitions"]!["apikey_key"] = UriSecurityScheme("adminKey");
            owner["security"] = "apikey_key";
            owner["uriVariables"] = new JsonObject { ["device"] = UriVariable("data-owner") };
            var form = new JsonObject { ["href"] = "https://forms.test/{adminKey}{?device}" };
            if (placement == "Root")
            {
                form["op"] = "readallproperties";
                plan["forms"] = new JsonArray(form);
            }
            else if (placement == "Source")
            {
                source["properties"]!["Value"]!["forms"] = new JsonArray(form);
            }
            else
            {
                plan["uav:projects"]![0]!["uav:routing"] = "projection";
                plan["properties"]!["reading"]!["forms"] = new JsonArray(form);
            }
            return (plan, source, owner, form);
        }

        private static JsonObject UriSecurityScheme(string name)
        {
            return new JsonObject
            {
                ["scheme"] = "apikey",
                ["in"] = "uri",
                ["name"] = name
            };
        }
    }
}
