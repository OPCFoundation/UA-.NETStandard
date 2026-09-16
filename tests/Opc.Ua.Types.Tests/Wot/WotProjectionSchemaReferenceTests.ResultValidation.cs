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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task SuppliedRootFormsRequireValidOwnedUriVariables(
            [Values] bool thingModel,
            [Values("Valid", "Missing", "Malformed", "Escaped")] string scenario)
        {
            JsonObject plan = Plan();
            plan["uav:projectionKind"] = thingModel ? "ThingModel" : "ThingDescription";
            string href = scenario switch
            {
                "Malformed" => "https://host.test/all{device:0}",
                "Escaped" => "https://host.test/all%7Bdevice%7D",
                _ => "https://host.test/all{?device}"
            };
            plan["forms"] = new JsonArray(new JsonObject
            {
                ["href"] = href,
                ["op"] = "readallproperties"
            });
            if (scenario is "Valid" or "Malformed")
            {
                plan["uriVariables"] = new JsonObject { ["device"] = UriVariable("host-root") };
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, Source()).ConfigureAwait(false);
            using WotDocument view = result.Value;

            if (scenario is "Missing" or "Malformed")
            {
                Assert.That(result.Success, Is.False);
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                    diagnostic.Location?.Reference == "/forms/0/href"), Is.True);
            }
            else
            {
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
                Assert.That(view.Kind,
                    Is.EqualTo(thingModel ? WotDocumentKind.ThingModel : WotDocumentKind.ThingDescription));
                Assert.That(view.RootElement.GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo(href));
                Assert.That(view.Properties["reading"].GetProperty("forms")[0].GetProperty("href").GetString(),
                    Is.EqualTo("https://device.test/runtime/value"));
                if (scenario == "Valid")
                {
                    Assert.That(view.RootElement.GetProperty("uriVariables").GetProperty("device")
                        .GetProperty("default").GetString(), Is.EqualTo("host-root"));
                }
                else
                {
                    Assert.That(view.RootElement.TryGetProperty("uriVariables", out _), Is.False);
                }
            }
        }

        [Test]
        public async Task RootFormVariablesCannotBorrowSourceDeclarations(
            [Values] bool thingModel,
            [Values] bool localDeclaration)
        {
            JsonObject plan = Plan();
            plan["uav:projectionKind"] = thingModel ? "ThingModel" : "ThingDescription";
            plan["forms"] = new JsonArray(new JsonObject
            {
                ["href"] = "https://host.test/all{?device}",
                ["op"] = "readallproperties"
            });
            JsonObject source = Source();
            JsonNode owner = localDeclaration ? source["properties"]!["Value"]! : source;
            owner["uriVariables"] = new JsonObject { ["device"] = UriVariable("source-only") };

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Location?.Reference == "/forms/0/href"), Is.True);
        }
    }
}
