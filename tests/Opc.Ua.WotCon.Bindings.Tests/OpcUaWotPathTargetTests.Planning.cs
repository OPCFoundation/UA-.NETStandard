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

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotPathTargetTests
    {
        [Test]
        public void ResolvingRelativeHrefRetainsOriginalPathContextAndRootAnchor()
        {
            const string json = /*lang=json,strict*/ """
                {
                  "@context":{"t":"urn:root"},
                  "base":"opc.tcp://localhost:4840/UA/",
                  "uav:browsePathAnchor":"nsu=urn:path:source;s=RootAnchor",
                  "properties":{"Value":{
                    "uav:id":"nsu=urn:path:source;s=LocalDeclaration",
                    "@context":{"t":"urn:affordance"},
                    "forms":[{
                      "@context":[{"t":"urn:old"},{"t":{"@id":"urn:form","@prefix":true}}],
                      "href":"Factory",
                      "op":"readproperty",
                      "uav:browsePath":"t:Value"
                    }]
                  }}
                }
                """;
            WotProtocolBinderRegistry registry = Registry(new Mock<ISession>());

            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "path-context", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(json)));

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            WotCompiledForm compiled = plan.CompiledForms[0];
            Assert.That(compiled.Endpoint.BaseUri, Is.EqualTo("opc.tcp://localhost:4840/UA/Factory"));
            Assert.That(compiled.Addressing.Target, Is.Empty);
            Assert.That(compiled.Addressing.BrowsePathTarget, Is.Not.Null);
            Assert.That(compiled.Addressing.BrowsePathTarget!.AnchorId,
                Is.EqualTo("nsu=urn:path:source;s=RootAnchor"));
            Assert.That(compiled.Addressing.BrowsePathTarget.Elements[0].TargetName,
                Is.EqualTo(new WotBrowsePathElement("urn:form", "Value")));
        }

        [Test]
        public void DirectFormConstructionUsesTheSameScopedCaptureAsDocumentExtraction()
        {
            using var affordance = JsonDocument.Parse(
                """{"@context":{"t":"urn:affordance"},"uav:browsePath":"t:Value","uav:id":"i=85"}""");
            using var form = JsonDocument.Parse("""{"href":"Factory","@context":{"t":"urn:form"}}""");
            var direct = new WotAffordanceForm(
                WotAffordanceKind.Property, "Value", ["readproperty"], "Factory", null, null, [],
                "/properties/Value/forms/0", form.RootElement, affordance.RootElement);
            WotProtocolBinderRegistry registry = Registry(new Mock<ISession>());
            var request = new WotBindingPlanRequest(
                "direct-path", WoTDocumentKindEnum.ThingDescription, [direct],
                baseUri: "opc.tcp://localhost:4840/UA/",
                namespacePrefixes: ImmutableDictionary<string, string>.Empty.Add("t", "urn:outer"));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            WotBrowsePathTarget? path = plan.CompiledForms[0].Addressing.BrowsePathTarget;
            Assert.That(path, Is.Not.Null);
            Assert.That(path!.AnchorId, Is.EqualTo("i=85"));
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(new WotBrowsePathElement("urn:affordance", "Value")));
        }

        [TestCase(2, true)]
        [TestCase(1, false)]
        public void PlannerEnforcesTheExactPathElementBound(int limit, bool accepted)
        {
            WotProtocolBinderRegistry registry = Registry(
                new Mock<ISession>(), new WotBindingBounds { MaxBrowsePathElements = limit });

            WotBindingPlan plan = Plan(registry, "properties", "readproperty", "/Objects/t:Value");

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(accepted ? 1 : 0));
            if (!accepted)
            {
                Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError &&
                    diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
            }
        }

        [TestCase("42")]
        [TestCase("null")]
        [TestCase("\"\"")]
        public void AnExplicitInvalidNodeIdCannotDisappearBehindAValidPath(string id)
        {
            WotProtocolBinderRegistry registry = Registry(new Mock<ISession>());

            WotBindingPlan plan = Plan(
                registry, "properties", "readproperty", "/Objects/t:Value", "\"uav:id\":" + id + ",");

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError &&
                diagnostic.JsonPointer?.EndsWith("/uav:id", System.StringComparison.Ordinal) == true), Is.True);
        }

        [TestCase("0:Objects")]
        [TestCase("/1:Value")]
        [TestCase("/unknown:Value")]
        [TestCase("/")]
        public void InvalidPathsStayUnsupportedWithoutConnecting(string path)
        {
            var session = new Mock<ISession>();
            WotProtocolBinderRegistry registry = Registry(session);

            WotBindingPlan plan = Plan(registry, "properties", "readproperty", path);

            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError), Is.True);
            session.VerifyNoOtherCalls();
        }
    }
}
