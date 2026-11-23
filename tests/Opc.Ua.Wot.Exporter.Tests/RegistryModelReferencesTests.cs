// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter.Tests
{
    /// <summary>
    /// Tests registry addressing without rewriting literal values or native preservation payloads.
    /// </summary>
    [TestFixture]
    public sealed class RegistryModelReferencesTests
    {
        /// <summary>
        /// Uses final authored IDs, preserves fragments and limits rewriting to document-reference positions.
        /// </summary>
        [TestCase("dataResponse")]
        [TestCase("if")]
        [TestCase("then")]
        [TestCase("else")]
        [TestCase("contains")]
        [TestCase("propertyNames")]
        public void ReferencesPreserveAuthoredIdsFragmentsAndLiteralValues(string schemaChild)
        {
            using var documents = new WotDocumentSet("source.TM.json", [
                new WotDocumentSetEntry("source.TM.json", Parse(
                    /*lang=json,strict*/
                    $$$$"""
                    {
                      "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                      "@type": ["tm:ThingModel", "uav:objectType"],
                      "id": "urn:authored:source",
                      "uav:id": "nsu=urn:export:source;i=1",
                      "links": [
                        {"rel": "tm:extends", "href": "target.TM.json", "type": "incorrect"},
                        {"rel": "item", "href": "instance.json", "type": "application/td+json"},
                        {"rel": "alternate", "href": "https://external.test/unrelated"}
                      ],
                      "properties": {
                        "reading": {
                          "tm:ref": "target.TM.json#/properties/reading",
                          "{{{{schemaChild}}}}": {"$ref": "target.TM.json#/schemaDefinitions/reading"},
                          "patternProperties": {"^sensor": {"$ref": "target.TM.json#/data"}},
                          "forms": [{"href": "target.TM.json"}],
                          "const": {
                            "tm:ref": "target.TM.json",
                            "links": [{"href": "target.TM.json"}],
                            "patternProperties": {"^sensor": {"$ref": "target.TM.json#/data"}}
                          }
                        }
                      },
                      "actions": {"read": {"input": {"$ref": "nsu=urn:export:target;i=2#/data"}}},
                      "events": {
                        "changed": {
                          "uav:eventSelectClauses": [{"tm:ref": "target.TM.json#/data", "uav:browsePath": "Value"}]
                        }
                      },
                      "uav:nodes": [{"links": [{"href": "target.TM.json"}], "tm:ref": "target.TM.json"}]
                    }
                    """)),
                new WotDocumentSetEntry("target.TM.json", Parse(
                    /*lang=json,strict*/
                    """
                    {
                      "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                      "@type": ["tm:ThingModel", "uav:objectType"],
                      "id": "urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType",
                      "uav:id": "nsu=urn:export:target;i=2",
                      "version": {"model": "old", "instance": "unchanged"}
                    }
                    """)),
                new WotDocumentSetEntry("instance.json", Parse(
                    /*lang=json,strict*/
                    """
                    {
                      "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                      "id": "urn:instance",
                      "links": [{"rel": "type", "href": "target.TM.json"}]
                    }
                    """))
            ]);
            var nodeSet = new UANodeSet
            {
                Models = [
                    new ModelTableEntry { ModelUri = "urn:export:source", Version = "1" },
                    new ModelTableEntry { ModelUri = "urn:export:target", Version = "42" }
                ]
            };
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            references.Add(nodeSet, documents, new Dictionary<ExpandedNodeId, string>
            {
                [ExpandedNodeId.Parse("nsu=urn:export:source;i=1")] = "urn:generated:source",
                [ExpandedNodeId.Parse("nsu=urn:export:target;i=2")] = "urn:generated:target"
            });

            ArrayOf<ReadOnlyMemory<byte>> output = references.Write(documents, CancellationToken.None);

            const string href = "https://registry.test/thingmodelgroups/opcua/thingmodels/" +
                "aacc8e0db1e57bb41a53019c162369a4f63680be6ee5c292d67f70f613274d26/versions/42";
            using var source = JsonDocument.Parse(output[0]);
            JsonElement root = source.RootElement;
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("urn:authored:source"));
            Assert.That(root.GetProperty("uav:id").GetString(), Is.EqualTo("nsu=urn:export:source;i=1"));
            JsonElement links = root.GetProperty("links");
            Assert.That(links[0].GetProperty("href").GetString(), Is.EqualTo(href));
            Assert.That(links[0].GetProperty("type").GetString(), Is.EqualTo("application/tm+json"));
            Assert.That(links[1].GetProperty("href").GetString(), Is.EqualTo("instance.json"));
            Assert.That(links[1].GetProperty("type").GetString(), Is.EqualTo("application/td+json"));
            Assert.That(links[2].GetProperty("href").GetString(), Is.EqualTo("https://external.test/unrelated"));
            JsonElement property = root.GetProperty("properties").GetProperty("reading");
            Assert.That(property.GetProperty("tm:ref").GetString(), Is.EqualTo(href + "#/properties/reading"));
            Assert.That(property.GetProperty(schemaChild).GetProperty("$ref").GetString(),
                Is.EqualTo(href + "#/schemaDefinitions/reading"));
            Assert.That(property.GetProperty("patternProperties").GetProperty("^sensor").GetProperty("$ref").GetString(),
                Is.EqualTo(href + "#/data"));
            Assert.That(property.GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo("target.TM.json"));
            Assert.That(property.GetProperty("const").GetProperty("tm:ref").GetString(), Is.EqualTo("target.TM.json"));
            Assert.That(property.GetProperty("const").GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("target.TM.json"));
            Assert.That(property.GetProperty("const").GetProperty("patternProperties").GetProperty("^sensor")
                .GetProperty("$ref").GetString(), Is.EqualTo("target.TM.json#/data"));
            Assert.That(root.GetProperty("actions").GetProperty("read").GetProperty("input")
                .GetProperty("$ref").GetString(), Is.EqualTo(href + "#/data"));
            Assert.That(root.GetProperty("events").GetProperty("changed").GetProperty("uav:eventSelectClauses")[0]
                .GetProperty("tm:ref").GetString(), Is.EqualTo(href + "#/data"));
            Assert.That(root.GetProperty("uav:nodes")[0].GetProperty("tm:ref").GetString(),
                Is.EqualTo("target.TM.json"));
            Assert.That(root.GetProperty("uav:nodes")[0].GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("target.TM.json"));
            using var target = JsonDocument.Parse(output[1]);
            Assert.That(target.RootElement.GetProperty("id").GetString(),
                Is.EqualTo("urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType"));
            Assert.That(target.RootElement.GetProperty("version").GetProperty("model").GetString(), Is.EqualTo("42"));
            Assert.That(target.RootElement.GetProperty("version").GetProperty("instance").GetString(),
                Is.EqualTo("unchanged"));
            using var instance = JsonDocument.Parse(output[2]);
            Assert.That(instance.RootElement.GetProperty("id").GetString(), Is.EqualTo("urn:instance"));
            Assert.That(instance.RootElement.GetProperty("links")[0].GetProperty("href").GetString(), Is.EqualTo(href));
        }

        /// <summary>
        /// Preserves hashes inside native string identities and any appended reference fragment.
        /// </summary>
        [TestCase("")]
        [TestCase("#/schemaDefinitions/reading")]
        [TestCase("#/schemaDefinitions/reading#part")]
        public void NativeStringIdsPreserveEmbeddedHashesAndFragments(string fragment)
        {
            using var documents = new WotDocumentSet("source.json", [
                new WotDocumentSetEntry("source.json", Parse(
                    /*lang=json,strict*/
                    $$$$"""
                    {
                      "links": [{"rel": "type", "href": "nsu=urn:export:target;s=Type#2{{{{fragment}}}}"}]
                    }
                    """)),
                new WotDocumentSetEntry("target.TM.json", Parse(
                    /*lang=json,strict*/
                    """
                    {
                      "@type": ["tm:ThingModel"],
                      "id": "urn:org.opcfoundation.UA.ADI.AccessorySlotStateMachineType",
                      "uav:id": "nsu=urn:export:target;s=Type#2"
                    }
                    """))
            ]);
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            references.Add(new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = "urn:export:target", Version = "42" }]
            }, documents, new Dictionary<ExpandedNodeId, string>
            {
                [ExpandedNodeId.Parse("nsu=urn:export:target;s=Type#2")] = "urn:fallback"
            });

            ArrayOf<ReadOnlyMemory<byte>> output = references.Write(documents, CancellationToken.None);

            using var source = JsonDocument.Parse(output[0]);
            Assert.That(source.RootElement.GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://registry.test/thingmodelgroups/opcua/thingmodels/" +
                    "aacc8e0db1e57bb41a53019c162369a4f63680be6ee5c292d67f70f613274d26/versions/42" +
                    fragment));
        }

        /// <summary>
        /// Rejects a shared authored ID whose targets have different namespace versions.
        /// </summary>
        [Test]
        public void AmbiguousTargetIdsAreRejected()
        {
            using var documents = new WotDocumentSet("first", [
                new WotDocumentSetEntry("first", Parse(
                    /*lang=json,strict*/
                    """
                    {
                      "@type": ["tm:ThingModel"],
                      "id": "urn:shared",
                      "uav:id": "nsu=urn:export:first;i=1",
                      "links": [{"rel": "tm:extends", "href": "urn:shared"}]
                    }
                    """)),
                new WotDocumentSetEntry("second", Parse(
                    /*lang=json,strict*/
                    """
                    {
                      "@type": ["tm:ThingModel"],
                      "id": "urn:shared",
                      "uav:id": "nsu=urn:export:second;i=1"
                    }
                    """))
            ]);
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            references.Add(new UANodeSet
            {
                Models = [
                    new ModelTableEntry { ModelUri = "urn:export:first", Version = "1" },
                    new ModelTableEntry { ModelUri = "urn:export:second", Version = "2" }
                ]
            }, documents, new Dictionary<ExpandedNodeId, string>
            {
                [ExpandedNodeId.Parse("nsu=urn:export:first;i=1")] = "urn:first",
                [ExpandedNodeId.Parse("nsu=urn:export:second;i=1")] = "urn:second"
            });

            Assert.That(() => references.Write(documents, CancellationToken.None),
                Throws.TypeOf<FormatException>().With.Message.Contains("ambiguous"));
        }

        /// <summary>
        /// Metadata registration does not keep disposed conversion payloads alive.
        /// </summary>
        [Test]
        public void RegisteredDocumentsAreNotRetained()
        {
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            WeakReference document = RegisterTemporaryModel(references, 1);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(document.IsAlive, Is.False);
            GC.KeepAlive(references);
        }

        /// <summary>
        /// Lightweight metadata can be applied to a fresh conversion after the first payload was disposed.
        /// </summary>
        [Test]
        public void ReconvertedDocumentsUseRegisteredMetadata()
        {
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            _ = RegisterTemporaryModel(references, 1);
            using WotDocumentSet documents = CreateModelDocuments(1);

            ArrayOf<ReadOnlyMemory<byte>> output = references.Write(documents, CancellationToken.None);

            using var result = JsonDocument.Parse(output[0]);
            Assert.That(result.RootElement.GetProperty("id").GetString(), Is.EqualTo("urn:model:1"));
            Assert.That(result.RootElement.GetProperty("version").GetProperty("model").GetString(), Is.EqualTo("42"));
        }

        /// <summary>
        /// Different native types cannot publish to one resource and version, even without outgoing references.
        /// </summary>
        [Test]
        public void DistinctNativeTypesCannotClaimTheSameResource()
        {
            var references = new RegistryModelReferences("https://registry.test", "opcua");
            using WotDocumentSet first = CreateModelDocuments(1, "urn:shared");
            using WotDocumentSet second = CreateModelDocuments(2, "urn:shared");
            Register(references, first);
            Register(references, second);

            Assert.That(() => references.Write(first, CancellationToken.None),
                Throws.TypeOf<FormatException>().With.Message.Contains("ambiguous"));
            Assert.That(() => references.Write(second, CancellationToken.None),
                Throws.TypeOf<FormatException>().With.Message.Contains("ambiguous"));
        }

        /// <summary>
        /// Writing a source without model references does not copy the full input catalogue.
        /// </summary>
        [Test]
        public void SourceWriteAllocationsDoNotGrowWithUnrelatedModels()
        {
            var small = new RegistryModelReferences("https://registry.test", "opcua");
            var large = new RegistryModelReferences("https://registry.test", "opcua");
            _ = RegisterTemporaryModel(small, 1);
            for (uint i = 1; i <= 256; i++)
            {
                _ = RegisterTemporaryModel(large, i);
            }
            using var source = new WotDocumentSet("support.json", [
                new WotDocumentSetEntry("support.json", Parse(/*lang=json,strict*/ """{"title":"No model references"}"""))
            ]);
            _ = small.Write(source, CancellationToken.None);
            _ = large.Write(source, CancellationToken.None);

            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = small.Write(source, CancellationToken.None);
            long smallAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            _ = large.Write(source, CancellationToken.None);
            long largeAllocation = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(largeAllocation, Is.LessThanOrEqualTo(smallAllocation + 4096));
        }

        /// <summary>
        /// Registers one disposable payload outside the lifetime of the collection assertion.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterTemporaryModel(RegistryModelReferences references, uint nodeId)
        {
            using WotDocumentSet documents = CreateModelDocuments(nodeId);
            Register(references, documents);
            return new WeakReference(documents.Entries[0].Document);
        }

        /// <summary>
        /// Registers the source namespace and fallback identifier for one model.
        /// </summary>
        private static void Register(RegistryModelReferences references, WotDocumentSet documents)
        {
            var identity = ExpandedNodeId.Parse(
                documents.Entries[0].Document.RootElement.GetProperty("uav:id").GetString()!);
            references.Add(new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = "urn:export:registry", Version = "42" }]
            }, documents, new Dictionary<ExpandedNodeId, string> { [identity] = "urn:fallback" });
        }

        /// <summary>
        /// Creates one independently owned model conversion.
        /// </summary>
        private static WotDocumentSet CreateModelDocuments(uint nodeId, string? authoredId = null)
        {
            string ordinal = nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new WotDocumentSet("model.TM.json", [
                new WotDocumentSetEntry("model.TM.json", Parse(
                    /*lang=json,strict*/
                    $$"""
                    {
                      "@type": ["tm:ThingModel"],
                      "id": "{{authoredId ?? ("urn:model:" + ordinal)}}",
                      "uav:id": "nsu=urn:export:registry;i={{ordinal}}"
                    }
                    """))
            ]);
        }

        /// <summary>
        /// Parses an owned test document.
        /// </summary>
        private static WotDocument Parse(string json)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(json));
        }
    }
}
