// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    [TestFixture]
    public sealed class WotLocalValuePlanningTests
    {
        [TestCase("1.0", 1)]
        [TestCase("future", 3)]
        public void NativeLocalVariablesNeedNoTransportAndUnknownGrammarsSupplyNoEvidence(
            string profile, int unsupported)
        {
            byte[] document = Encoding.UTF8.GetBytes($$"""
                {
                  "@type": "uav:object",
                  "uav:id": "nsu=urn:metadata;s=Root",
                  "properties": {
                    "Present": { "uav:id": "nsu=urn:metadata;i=1", "type": "number" },
                    "DeclaredWithoutValue": { "uav:id": "nsu=urn:metadata;i=2", "type": "number" },
                    "Unbacked": { "uav:id": "nsu=urn:metadata;i=3", "type": "number" }
                  },
                  "uav:nodes": {
                    "@type": "uav:NodeModel",
                    "profileVersion": "{{profile}}",
                    "namespaceUris": ["urn:metadata"],
                    "nodes": [
                      { "nodeId": "ns=1;i=1", "nodeClass": "Variable",
                        "valueXml": "<Double xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">12</Double>" },
                      { "nodeId": "ns=1;i=2", "nodeClass": "Variable" }
                    ]
                  }
                }
                """);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "metadata", WoTDocumentKindEnum.ThingDescription, document)
                .WithDeclarationContext(false)
                .WithProjectionRoot(new ExpandedNodeId("Root", "urn:metadata"));

            WotBindingPlan plan = new WotProtocolBinderRegistry([]).Prepare(request);

            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(unsupported));
            Assert.That(plan.UnsupportedForms.Select(form => form.AffordanceName), Does.Contain("Unbacked"));
            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.IsDeclarationContext, Is.False);
        }

        [TestCase("", true)]
        [TestCase(", \"uav:mapToNodeId\": \"nsu=urn:metadata;i=1\"", false)]
        [TestCase(", \"forms\": null", false)]
        public void LocalConstantsDoNotInventTransportBindings(string extra, bool supported)
        {
            byte[] document = Encoding.UTF8.GetBytes($$"""
                {
                  "@type": "uav:object",
                  "properties": {
                    "Value": { "uav:id": "nsu=urn:metadata;i=1", "type": "number", "const": 12{{extra}} }
                  }
                }
                """);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "metadata", WoTDocumentKindEnum.ThingDescription, document);

            WotBindingPlan plan = new WotProtocolBinderRegistry([]).Prepare(request);

            Assert.That(plan.FullySupported, Is.EqualTo(supported));
            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(supported ? 0 : 1));
        }
    }
}
