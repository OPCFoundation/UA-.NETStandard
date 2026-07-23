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

using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Covers the OPC UA WoT Binding review revisions: the <c>uav:eventType</c>
    /// annotation, portable ExpandedNodeId identity, and HasComponent-subtype
    /// typed reference links.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotBindingReviewTests
    {
        private const string Context =
            "\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
            "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}],";

        private static readonly string[] s_orderedStageIds =
        [
            "nsu=urn:demo:pump;i=2001",
            "nsu=urn:demo:pump;i=2002"
        ];

        // ---- uav:eventType (Section 5.2) -----------------------------------

        [Test]
        public void EventAffordanceEmitsEventTypeAnnotationAndIsEvent()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            JsonElement overTemp = document.Events["OverTemperatureEventType"];
            Assert.That(overTemp.GetProperty("@type").GetString(), Is.EqualTo("uav:eventType"));
            Assert.That(overTemp.GetProperty("uav:isEvent").GetBoolean(), Is.True);
        }

        [Test]
        public void EventTypeRootProjectsEventTypeAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:demo:events"],
                Models = [new ModelTableEntry { ModelUri = "urn:demo:events" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1002",
                        BrowseName = "1:OverTemperatureEventType",
                        DisplayName = [new Export.LocalizedText { Value = "OverTemperatureEventType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=2041" }
                        ]
                    }
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            string[] types = document.TypeTokens.ToArray();
            Assert.That(types, Does.Contain("uav:eventType"));
            Assert.That(types, Does.Not.Contain("uav:objectType"));
            Assert.That(
                document.RootElement.GetProperty("uav:isEvent").GetBoolean(), Is.True);
        }

        [Test]
        public void EventTypeAnnotatedThingModelSynthesizesBaseEventTypeSubtype()
        {
            string json =
                "{" + Context +
                "\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                "\"title\":\"OverTemperatureEventType\"," +
                "\"uav:browseName\":\"1:OverTemperatureEventType\"," +
                "\"uav:isEvent\":true}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Assert.That(
                root.References!.Any(r =>
                    r.ReferenceType == "HasSubtype" && !r.IsForward && r.Value == "i=2041"),
                Is.True,
                "An event-type Thing Model must derive from BaseEventType (i=2041).");
        }

        [Test]
        public void ContradictoryEventTypeAndIsEventFalseIsRejected()
        {
            string json =
                "{" + Context +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"events\":{\"overTemp\":{\"@type\":\"uav:eventType\"," +
                "\"uav:isEvent\":false,\"uav:browseName\":\"1:OverTemp\"}}}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventAnnotationConflict),
                Is.True);
            Assert.That(result.HasErrors, Is.True);
        }

        // ---- Portable identity (Section 5.1.1) -----------------------------

        [Test]
        public void ForwardIdentityTermsArePortableExpandedNodeIds()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(WotTestData.CreateRichNodeSet());

            Assert.That(
                document.RootElement.GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:test:model;i=1001"));
            Assert.That(
                document.Properties["Speed"].GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:test:model;i=6001"));
            Assert.That(
                document.Events["OverTemperatureEventType"].GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:test:model;i=1002"));

            // No emitted WoT-native identity uses the session-local ns=<index> form.
            string text = Encoding.UTF8.GetString(document.Utf8Json.ToArray());
            int afterEnvelope = text.IndexOf("\"uav:nodeSet\"", System.StringComparison.Ordinal);
            string readable = afterEnvelope < 0 ? text : text.Substring(0, afterEnvelope);
            Assert.That(readable, Does.Not.Contain("\"ns=1;"));
        }

        [Test]
        public void NamespaceZeroIdentityKeepsCanonicalForm()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:demo:x"],
                Models = [new ModelTableEntry { ModelUri = "urn:demo:x" }],
                Items =
                [
                    new UAObjectType
                    {
                        // A namespace-0 NodeId keeps its canonical i= form.
                        NodeId = "i=1500",
                        BrowseName = "1:CanonicalType",
                        DisplayName = [new Export.LocalizedText { Value = "CanonicalType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" }
                        ]
                    }
                ]
            };

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(
                document.RootElement.GetProperty("uav:id").GetString(), Is.EqualTo("i=1500"));
        }

        [Test]
        public void PortableIdentityIsStableAcrossNamespaceTableReordering()
        {
            using WotDocument first = WotNodeSetConverter.FromNodeSet(
                CreateReorderableNodeSet(["urn:demo:a", "urn:demo:b"], aIndex: 1));
            using WotDocument second = WotNodeSetConverter.FromNodeSet(
                CreateReorderableNodeSet(["urn:demo:b", "urn:demo:a"], aIndex: 2));

            // The same URI-anchored identity survives the namespace-table swap,
            // even though the raw NodeSet NodeIds used different indices.
            Assert.That(
                first.RootElement.GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:demo:a;i=1001"));
            Assert.That(
                second.RootElement.GetProperty("uav:id").GetString(),
                Is.EqualTo(first.RootElement.GetProperty("uav:id").GetString()));
            Assert.That(
                second.Properties["Speed"].GetProperty("uav:id").GetString(),
                Is.EqualTo(first.Properties["Speed"].GetProperty("uav:id").GetString()));
        }

        [Test]
        public void SynthesisDiagnosesSessionLocalNsIndexInPortableField()
        {
            string json =
                "{" + Context +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"uav:id\":\"ns=1;i=1001\"}";

            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonPortableIdentity),
                Is.True);
            // A non-portable identity is diagnosed, not fatal: conversion still succeeds.
            Assert.That(result.Value, Is.Not.Null);
        }

        // ---- HasComponent subtypes (Section 5.3) ---------------------------

        [Test]
        public void HasComponentSubtypeEmitsDiscoveryAndTypedReference()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateOrderedComponentNodeSet());

            string[] children = document.RootElement.GetProperty("uav:hasComponent")
                .EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.That(children, Is.EquivalentTo(s_orderedStageIds));

            JsonElement links = document.RootElement.GetProperty("links");
            Assert.That(links.GetArrayLength(), Is.EqualTo(2));
            foreach (JsonElement link in links.EnumerateArray())
            {
                Assert.That(link.GetProperty("rel").GetString(), Is.EqualTo("uav:typedReference"));
                Assert.That(link.GetProperty("uav:refType").GetString(), Is.EqualTo("i=49"));
                Assert.That(link.GetProperty("href").GetString(), Does.StartWith("nsu=urn:demo:pump;"));
                Assert.That(link.GetProperty("uav:refName").GetString(), Does.StartWith("Stage_"));
            }
        }

        [Test]
        public void TypedReferencePinnedComponentRecreatesExactSubtype()
        {
            string json =
                "{" + Context +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"uav:hasComponent\":[\"nsu=urn:demo:pump;i=2001\"]," +
                "\"links\":[{\"rel\":\"uav:typedReference\"," +
                "\"href\":\"nsu=urn:demo:pump;i=2001\",\"uav:refType\":\"i=49\"," +
                "\"uav:refName\":\"Stage_1\"}]}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            var toTarget = root.References!
                .Where(r => r.Value == "nsu=urn:demo:pump;i=2001").ToArray();
            Assert.That(toTarget, Has.Length.EqualTo(1),
                "The pinned component must not be emitted twice.");
            Assert.That(toTarget[0].ReferenceType, Is.EqualTo("i=49"));
            Assert.That(toTarget[0].IsForward, Is.True);
        }

        [Test]
        public void UnpinnedComponentDefaultsToPlainHasComponent()
        {
            string json =
                "{" + Context +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"1:PumpType\"," +
                "\"uav:hasComponent\":[\"nsu=urn:demo:pump;i=2001\"]}";

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json));

            UAObjectType root = nodeSet.Items!.OfType<UAObjectType>().Single();
            Reference reference = root.References!
                .Single(r => r.Value == "nsu=urn:demo:pump;i=2001");
            Assert.That(reference.ReferenceType, Is.EqualTo("HasComponent"));
            Assert.That(reference.IsForward, Is.True);
        }

        [Test]
        public void HasComponentSubtypeRoundTripsExactlyThroughReadableMapping()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(CreateOrderedComponentNodeSet());

            // Rebuild a native (envelope-free) Thing Model from the emitted
            // readable surface and confirm the ordered components survive.
            string readable = BuildReadableOnly(document);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(readable));

            UAObjectType root = restored.Items!.OfType<UAObjectType>().Single();
            int ordered = root.References!.Count(r => r.ReferenceType == "i=49" && r.IsForward);
            Assert.That(ordered, Is.EqualTo(2));
            Assert.That(
                root.References!.Any(r => r.ReferenceType == "HasComponent" && r.IsForward),
                Is.False,
                "A pinned ordered component must not degrade to plain HasComponent.");
        }

        private static string BuildReadableOnly(WotDocument document)
        {
            // Strip the preservation envelope and native projection so ToNodeSet
            // exercises the synthesis (reverse conversion) path rather than the
            // exact envelope restore.
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodeSet" or "uav:nodes")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static UANodeSet CreateReorderableNodeSet(string[] namespaceUris, int aIndex)
        {
            string prefix = "ns=" + aIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";";
            return new UANodeSet
            {
                NamespaceUris = namespaceUris,
                Models = [new ModelTableEntry { ModelUri = "urn:demo:a" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = prefix + "i=1001",
                        BrowseName = aIndex + ":PumpType",
                        DisplayName = [new Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", IsForward = true, Value = prefix + "i=6001" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = prefix + "i=6001",
                        BrowseName = aIndex + ":Speed",
                        DisplayName = [new Export.LocalizedText { Value = "Speed" }],
                        DataType = "Double",
                        AccessLevel = 1,
                        ParentNodeId = prefix + "i=1001",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=63" },
                            new Reference { ReferenceType = "HasComponent", IsForward = false, Value = prefix + "i=1001" }
                        ]
                    }
                ]
            };
        }

        private static UANodeSet CreateOrderedComponentNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:demo:pump"],
                Models = [new ModelTableEntry { ModelUri = "urn:demo:pump" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:PumpType",
                        DisplayName = [new Export.LocalizedText { Value = "PumpType" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "HasOrderedComponent", IsForward = true, Value = "ns=1;i=2001" },
                            new Reference { ReferenceType = "HasOrderedComponent", IsForward = true, Value = "ns=1;i=2002" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=2001",
                        BrowseName = "1:Stage_1",
                        DisplayName = [new Export.LocalizedText { Value = "Stage_1" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=58" },
                            new Reference { ReferenceType = "HasOrderedComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=2002",
                        BrowseName = "1:Stage_2",
                        DisplayName = [new Export.LocalizedText { Value = "Stage_2" }],
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=58" },
                            new Reference { ReferenceType = "HasOrderedComponent", IsForward = false, Value = "ns=1;i=1001" }
                        ]
                    }
                ]
            };
        }
    }
}
