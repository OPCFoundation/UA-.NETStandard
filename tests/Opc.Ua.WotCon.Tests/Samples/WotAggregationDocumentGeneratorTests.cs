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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using ManifestEntry = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.ManifestEntry;
using SampleDocument = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.SampleDocument;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Exercises manifest validation and source-identity enrichment without writing artifacts.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotAggregationDocumentGeneratorTests
    {
        [TestCase("tm:ref")]
        [TestCase("uav:componentOf")]
        [TestCase("href")]
        public void ManifestRejectsMissingLogicalDependencies(string referenceTerm)
        {
            SampleDocument document = CreateDocument("dependent", new JsonObject
            {
                ["nested"] = new JsonObject { [referenceTerm] = "./missing#/properties/value" }
            });

            Assert.That(
                () => WotAggregationDocumentGenerator.GenerateManifest([document]),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "'dependent' references missing sample document 'missing'."));
        }

        [Test]
        public void ManifestRejectsDependencyCycles()
        {
            ArrayOf<SampleDocument> documents =
            [
                CreateDocument("a", new JsonObject { ["tm:ref"] = "b" }),
                CreateDocument("b", new JsonObject { ["uav:componentOf"] = "c" }),
                CreateDocument("c", new JsonObject
                {
                    ["links"] = new JsonArray(new JsonObject { ["href"] = "a#/schemaDefinitions/Type" })
                })
            ];

            Assert.That(
                () => WotAggregationDocumentGenerator.GetManifestEntries(documents),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "Cyclic sample document dependency at 'a': a -> b -> c -> a."));
        }

        [Test]
        public void ManifestRejectsDuplicateResourcesEvenWhenTheirPathsAndKindsDiffer()
        {
            ArrayOf<SampleDocument> documents =
            [
                CreateDocument("duplicate", path: "first/duplicate.json"),
                CreateDocument("duplicate", kind: WoTDocumentKindEnum.ThingModel, path: "second/duplicate.json")
            ];

            Assert.That(
                () => WotAggregationDocumentGenerator.GetManifestEntries(documents),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "Duplicate sample resource 'duplicate'."));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ManifestOrderIsStableWithoutChainingIndependentDocuments(int inputOrder)
        {
            SampleDocument[] input =
            [
                CreateDocument("c-independent"),
                CreateDocument("a-view", new JsonObject { ["tm:ref"] = "z-model#/properties/Reading" }),
                CreateDocument("b-independent"),
                CreateDocument("z-model", kind: WoTDocumentKindEnum.ThingModel)
            ];
            ArrayOf<SampleDocument> documents = inputOrder switch
            {
                1 => Enumerable.Reverse(input).ToArrayOf(),
                2 => input.OrderBy(document => document.ResourceId, StringComparer.Ordinal).ToArrayOf(),
                _ => input.ToArrayOf()
            };

            ArrayOf<ManifestEntry> entries = WotAggregationDocumentGenerator.GetManifestEntries(documents);

            Assert.That(entries.ToList().Select(entry => entry.ResourceId),
                Is.EqualTo(s_independentOrder));
            Assert.That(entries.ToList().Single(entry => entry.ResourceId == "a-view").DependsOn.ToList(),
                Is.EqualTo(s_modelDependency));
            foreach (string resource in new[] { "z-model", "b-independent", "c-independent" })
            {
                Assert.That(entries.ToList().Single(entry => entry.ResourceId == resource).DependsOn.IsEmpty,
                    Is.True, resource);
            }
        }

        [Test]
        public void ManifestUsesNestedLogicalReferencesButNotTransportAddressesOrSelfReferences()
        {
            SampleDocument view = CreateDocument("view", new JsonObject
            {
                ["@context"] = new JsonObject { ["href"] = "not-a-context-document" },
                ["uav:componentOf"] = "owner",
                ["properties"] = new JsonObject
                {
                    ["Reading"] = new JsonObject
                    {
                        ["tm:ref"] = "./model#/properties/Reading",
                        ["const"] = new JsonObject { ["href"] = "not-a-value-document" },
                        ["default"] = new JsonObject { ["uav:mapToType"] = "nsu=urn:value;i=999" },
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = "not-a-transport-document",
                            ["uav:componentOf"] = "not-a-transport-owner"
                        })
                    }
                },
                ["links"] = new JsonArray(
                    new JsonObject { ["href"] = "model#/schemaDefinitions/Reading" },
                    new JsonObject { ["href"] = "view#/properties/Reading" },
                    new JsonObject { ["href"] = "./view#/properties/Reading" },
                    new JsonObject { ["href"] = "#/properties/Reading" },
                    new JsonObject { ["href"] = "i=2915" },
                    new JsonObject { ["href"] = "nsu=urn:source;s=Pump1" },
                    new JsonObject { ["href"] = "https://example.invalid/model" },
                    new JsonObject { ["href"] = "urn:external:model" })
            });

            ArrayOf<ManifestEntry> entries = WotAggregationDocumentGenerator.GetManifestEntries(
                [view, CreateDocument("owner"), CreateDocument("model")]);

            Assert.That(entries.ToList().Select(entry => entry.ResourceId),
                Is.EqualTo(s_logicalOrder));
            Assert.That(entries[2].DependsOn.ToList(), Is.EqualTo(s_logicalDependencies));
            Assert.That(entries[0].DependsOn.IsEmpty, Is.True);
            Assert.That(entries[1].DependsOn.IsEmpty, Is.True);
        }

        [TestCase(WoTDocumentKindEnum.ThingModel, "thingmodels")]
        [TestCase(WoTDocumentKindEnum.ThingDescription, "thingdescriptions")]
        public void ManifestPreservesLogicalIdsUriPathsKindsAndGroups(WoTDocumentKindEnum kind, string group)
        {
            SampleDocument document = CreateDocument("resource", kind: kind, path: "sample-pump/resource.json");

            ByteString bytes = WotAggregationDocumentGenerator.GenerateManifest([document]);

            using var manifest = JsonDocument.Parse(bytes.ToArray());
            Assert.That(manifest.RootElement.GetArrayLength(), Is.EqualTo(1));
            JsonElement entry = manifest.RootElement[0];
            Assert.That(entry.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(s_manifestFields));
            Assert.That(entry.GetProperty("resourceId").GetString(), Is.EqualTo("resource"));
            Assert.That(entry.GetProperty("path").GetString(), Is.EqualTo("sample-pump/resource.json"));
            Assert.That(entry.GetProperty("documentKind").GetString(), Is.EqualTo(kind.ToString()));
            Assert.That(entry.GetProperty("groupId").GetString(), Is.EqualTo(group));
            Assert.That(entry.GetProperty("dependsOn").GetArrayLength(), Is.Zero);
        }

        [Test]
        public void ManifestMapsPortableTypeReferencesToTheirActualDocuments()
        {
            const string pumpType = "nsu=urn:model;i=1001";
            const string dataType = "nsu=urn:model;i=2001";
            const string referenceType = "nsu=urn:model;i=3001";
            SampleDocument pump = CreateNodeDocument("pump", "nsu=urn:local;s=Pump1", "uav:object", "Pump",
                new JsonObject
                {
                    ["links"] = new JsonArray(
                        new JsonObject { ["rel"] = "ua:HasTypeDefinition", ["href"] = pumpType },
                        new JsonObject
                        {
                            ["rel"] = "model:Feeds",
                            ["uav:refId"] = referenceType,
                            ["href"] = "nsu=urn:source;s=Other"
                        }),
                    ["properties"] = new JsonObject
                    {
                        ["Reading"] = new JsonObject
                        {
                            ["uav:id"] = "nsu=urn:local;s=Pump1.Reading",
                            ["uav:mapToType"] = dataType,
                            ["forms"] = new JsonArray(new JsonObject
                            {
                                ["href"] = "opc.tcp://source:4840",
                                ["uav:typeDefinitionId"] = "nsu=urn:source;i=9001"
                            })
                        }
                    }
                });
            ArrayOf<SampleDocument> documents =
            [
                pump,
                CreateNodeDocument("model-header", "nsu=urn:model;s=Metadata", "uav:object", "Metadata"),
                CreateNodeDocument("pump-type", pumpType, "uav:objectType", "PumpType"),
                CreateNodeDocument("reading-type", dataType, "uav:dataType", "Reading"),
                CreateNodeDocument("feeds-type", referenceType, "uav:referenceType", "Feeds"),
                CreateNodeDocument("transport-only", "nsu=urn:source;i=9001", "uav:objectType", "RemoteType")
            ];

            ManifestEntry entry = WotAggregationDocumentGenerator.GetManifestEntries(documents)
                .ToList().Single(candidate => candidate.ResourceId == "pump");

            Assert.That(entry.DependsOn.ToList(), Is.EqualTo(s_portableDependencies));
            Assert.That(entry.DependsOn.ToList(), Does.Not.Contain("model-header").And.Not.Contain("transport-only"));
        }

        [Test]
        public void ManifestResolvesQualifiedModelTypeAndReferenceNames()
        {
            SampleDocument pump = CreateNodeDocument("pump", "nsu=urn:local;s=Pump1", "uav:object", "Pump",
                new JsonObject
                {
                    ["@type"] = new JsonArray("uav:object", "model:PumpType"),
                    ["links"] = new JsonArray(new JsonObject
                    {
                        ["rel"] = "model:Feeds",
                        ["href"] = "nsu=urn:source;s=Other"
                    })
                });
            ArrayOf<SampleDocument> documents =
            [
                pump,
                CreateNodeDocument("pump-type", "nsu=urn:model;i=1001", "uav:objectType", "PumpType"),
                CreateNodeDocument("feeds-type", "nsu=urn:model;i=3001", "uav:referenceType", "Feeds")
            ];

            Assert.That(
                WotAggregationDocumentGenerator.GetManifestEntries(documents)
                    .ToList().Single(entry => entry.ResourceId == "pump").DependsOn.ToList(),
                Is.EqualTo(s_namedDependencies));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManifestUsesDefinitiveReferencePinsOrRejectsAmbiguousNames(bool pinned)
        {
            var link = new JsonObject { ["rel"] = "model:SharedName", ["href"] = "nsu=urn:source;s=Other" };
            if (pinned)
            {
                link["uav:refId"] = "nsu=urn:model;i=3002";
            }
            ArrayOf<SampleDocument> documents =
            [
                CreateNodeDocument("pump", "nsu=urn:local;s=Pump1", "uav:object", "Pump",
                    new JsonObject { ["links"] = new JsonArray(link) }),
                CreateNodeDocument("first-reference", "nsu=urn:model;i=3001", "uav:referenceType", "SharedName"),
                CreateNodeDocument("second-reference", "nsu=urn:model;i=3002", "uav:referenceType", "SharedName")
            ];

            if (pinned)
            {
                Assert.That(
                    WotAggregationDocumentGenerator.GetManifestEntries(documents)
                        .ToList().Single(entry => entry.ResourceId == "pump").DependsOn.ToList(),
                    Is.EqualTo(s_pinnedReferenceDependency));
            }
            else
            {
                Assert.That(
                    () => WotAggregationDocumentGenerator.GetManifestEntries(documents),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("'pump' has an ambiguous model reference 'model:SharedName'."));
            }
        }

        [Test]
        public void ManifestDoesNotTurnForwardObjectReferencesIntoParentDependencyCycles()
        {
            const string parentId = "nsu=urn:local;s=Parent";
            const string childId = "nsu=urn:local;s=Parent.Child";
            ArrayOf<SampleDocument> documents =
            [
                CreateNodeDocument("a-child", childId, "uav:object", "Child",
                    new JsonObject { ["uav:componentOf"] = new JsonArray(parentId) }),
                CreateNodeDocument("z-parent", parentId, "uav:object", "Parent",
                    new JsonObject
                    {
                        ["uav:hasComponent"] = new JsonArray(childId),
                        ["links"] = new JsonArray(
                            new JsonObject { ["rel"] = "ua:HasComponent", ["href"] = childId })
                    })
            ];

            ArrayOf<ManifestEntry> entries = WotAggregationDocumentGenerator.GetManifestEntries(documents);

            Assert.That(entries.ToList().Select(entry => entry.ResourceId),
                Is.EqualTo(s_parentChildOrder));
            Assert.That(entries[0].DependsOn.IsEmpty, Is.True);
            Assert.That(entries[1].DependsOn.ToList(), Is.EqualTo(s_parentDependency));
        }

        [Test]
        public void ManifestDoesNotTreatAnInverseInterfaceEdgeAsATypePrerequisite()
        {
            const string interfaceId = "nsu=urn:model;i=1001";
            const string implementingTypeId = "nsu=urn:model;i=1002";
            ArrayOf<SampleDocument> documents =
            [
                CreateNodeDocument("interface", interfaceId, "uav:objectType", "Interface",
                    new JsonObject
                    {
                        ["links"] = new JsonArray(new JsonObject
                        {
                            ["rel"] = "ua:IsInterfaceOf", ["href"] = implementingTypeId
                        })
                    }),
                CreateNodeDocument("implementation", implementingTypeId, "uav:objectType", "Implementation",
                    new JsonObject
                    {
                        ["links"] = new JsonArray(new JsonObject
                        {
                            ["rel"] = "ua:HasInterface", ["href"] = interfaceId
                        })
                    })
            ];

            ArrayOf<ManifestEntry> entries = WotAggregationDocumentGenerator.GetManifestEntries(documents);

            Assert.That(entries[0].ResourceId, Is.EqualTo("interface"));
            Assert.That(entries[0].DependsOn.IsEmpty, Is.True);
            Assert.That(entries[1].ResourceId, Is.EqualTo("implementation"));
            Assert.That(entries[1].DependsOn.Count, Is.EqualTo(1));
            Assert.That(entries[1].DependsOn[0], Is.EqualTo("interface"));
        }

        [Test]
        public void ManifestDoesNotDependOnDataTypesAlreadyDeclaredInTheDocument()
        {
            const string firstId = "nsu=urn:model;i=2001";
            const string secondId = "nsu=urn:model;i=2002";
            JsonArray declarations = new(
                new JsonObject
                {
                    ["uav:dataTypeId"] = firstId,
                    ["uav:binaryEncodingId"] = "nsu=urn:model;i=5001",
                    ["uav:fields"] = new JsonArray(new JsonObject { ["uav:fieldDataTypeId"] = secondId })
                },
                new JsonObject
                {
                    ["uav:dataTypeId"] = secondId,
                    ["uav:binaryEncodingId"] = "nsu=urn:model;i=5002",
                    ["uav:fields"] = new JsonArray(new JsonObject { ["uav:fieldDataTypeId"] = firstId })
                });
            ArrayOf<SampleDocument> documents =
            [
                CreateNodeDocument("first-type", firstId, "uav:dataType", "First",
                    new JsonObject { ["uav:dataTypeDefinitions"] = declarations.DeepClone() }),
                CreateNodeDocument("second-type", secondId, "uav:dataType", "Second",
                    new JsonObject { ["uav:dataTypeDefinitions"] = declarations.DeepClone() }),
                CreateNodeDocument("instance", "nsu=urn:local;s=Instance", "uav:object", "Instance",
                    new JsonObject
                    {
                        ["uav:dataTypeDefinitions"] = declarations.DeepClone(),
                        ["properties"] = new JsonObject
                        {
                            ["Reading"] = new JsonObject { ["uav:mapToType"] = secondId }
                        }
                    }),
                CreateNodeDocument("first-encoding", "nsu=urn:model;i=5001", "uav:object", "Default Binary"),
                CreateNodeDocument("second-encoding", "nsu=urn:model;i=5002", "uav:object", "Default Binary")
            ];

            foreach (ManifestEntry entry in WotAggregationDocumentGenerator.GetManifestEntries(documents))
            {
                Assert.That(entry.DependsOn.IsEmpty, Is.True, entry.ResourceId);
            }
        }

        [Test]
        public void ManifestPreservesHashCharactersInNodeIdsAndLogicalDocumentIdentities()
        {
            const string nodeId = "nsu=urn:model;s=Type#1";
            ArrayOf<SampleDocument> documents =
            [
                CreateDocument("consumer", new JsonObject
                {
                    ["uav:mapToType"] = nodeId,
                    ["tm:ref"] = "urn:example:model#logical"
                }),
                CreateNodeDocument("type", nodeId, "uav:objectType", "Type"),
                CreateDocument("logical-model", new JsonObject { ["id"] = "urn:example:model#logical" })
            ];

            Assert.That(
                WotAggregationDocumentGenerator.GetManifestEntries(documents)
                    .ToList().Single(entry => entry.ResourceId == "consumer").DependsOn.ToList(),
                Is.EqualTo(s_hashIdentityDependencies));
        }

        [Test]
        public void ManifestRejectsAReferencedTypeWithoutAnOwner()
        {
            SampleDocument document = CreateDocument("consumer", new JsonObject
            {
                ["uav:mapToType"] = "nsu=urn:missing;i=42"
            });

            Assert.That(
                () => WotAggregationDocumentGenerator.GetManifestEntries([document]),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "'consumer' references node 'nsu=urn:missing;i=42' without a document owner."));
        }

        [Test]
        public void ManifestRejectsMultipleRootsClaimingOneNode()
        {
            ArrayOf<SampleDocument> documents =
            [
                CreateNodeDocument("first", "nsu=urn:model;i=1001", "uav:objectType", "First"),
                CreateNodeDocument("second", "nsu=urn:model;i=1001", "uav:objectType", "Second")
            ];

            Assert.That(
                () => WotAggregationDocumentGenerator.GetManifestEntries(documents),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    "Multiple root documents own the local node 'nsu=urn:model;i=1001'."));
        }

        [Test]
        public void AffordanceReferenceUsesLocalIdentityAndEscapesNestedJsonPointerTokens()
        {
            const string nodeId = "nsu=urn:opcfoundation.org:UA:WotAggregation:PumpInstance;s=Pump1.Reading";
            ArrayOf<SampleDocument> documents =
            [
                CreateDocument("decoy", new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["Pressure/~Raw"] = new JsonObject
                        {
                            ["uav:id"] = nodeId + "Other",
                            ["forms"] = new JsonArray(new JsonObject { ["uav:id"] = nodeId })
                        }
                    }
                }),
                CreateDocument("measurements", new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["Group/~"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["Pressure/~Raw"] = new JsonObject
                                {
                                    ["uav:id"] = nodeId,
                                    ["type"] = "number"
                                }
                            }
                        }
                    }
                })
            ];

            string reference = WotAggregationDocumentGenerator.AffordanceReference(documents, "properties", nodeId);

            Assert.That(reference,
                Is.EqualTo("measurements#/properties/Group~1~0/properties/Pressure~1~0Raw"));
            Assert.That(ReadDeclaration(documents, reference)["type"]!.GetValue<string>(), Is.EqualTo("number"));
            Assert.That(ReadDeclaration(documents, reference)["uav:id"]!.GetValue<string>(), Is.EqualTo(nodeId));
        }

        [TestCase("properties", 0)]
        [TestCase("properties", 2)]
        [TestCase("actions", 0)]
        [TestCase("actions", 2)]
        [TestCase("events", 0)]
        [TestCase("events", 2)]
        public void AffordanceReferenceRejectsMissingOrAmbiguousLocalDeclarations(string mapName, int declarations)
        {
            const string nodeId = "nsu=urn:local;s=Pump1.Member";
            ArrayOf<SampleDocument> documents = Enumerable.Range(0, declarations).Select(index =>
                CreateDocument(index == 0 ? "first" : "second", new JsonObject
                {
                    [mapName] = new JsonObject { ["Member"] = new JsonObject { ["uav:id"] = nodeId } }
                })).ToArrayOf();

            Assert.That(
                () => WotAggregationDocumentGenerator.AffordanceReference(documents, mapName, nodeId),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    $"Expected exactly one '{mapName}' declaration for '{nodeId}'; found {declarations}."));
        }

        [TestCase("Pump1", "sample-pump", "Pump #1")]
        [TestCase("Pump2", "sample-pump-pump-2", "Pump #2")]
        public async Task GeneratedPumpRootTitlesPreserveSourceDisplayNames(
            string pumpName,
            string resourceId,
            string displayName)
        {
            ArrayOf<SampleDocument> documents = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            SampleDocument document = documents.ToList().Single(candidate => candidate.ResourceId == resourceId);
            using var json = JsonDocument.Parse(document.Json.ToArray());

            Assert.That(json.RootElement.GetProperty("title").GetString(), Is.EqualTo(displayName));
            Assert.That(json.RootElement.GetProperty("uav:id").GetString(),
                Is.EqualTo(WotAggregationDocumentGenerator.LocalNodeId(pumpName, string.Empty)));
        }

        [Test]
        public void PumpSourceXmlRangeBodiesUseTheStandardXmlEncoding()
        {
            var source = XDocument.Load(PumpSourcePath);
            XNamespace types = "http://opcfoundation.org/UA/2008/02/Types.xsd";
            XElement[] ranges = [.. source.Descendants(types + "ExtensionObject")
                .Where(extension => extension.Element(types + "Body")?.Element(types + "Range") is not null)];

            Assert.That(ranges, Is.Not.Empty, "The source must exercise XML-bodied Range values.");
            NodeId expected = global::Opc.Ua.ObjectIds.Range_Encoding_DefaultXml;
            foreach (XElement range in ranges)
            {
                string origin = range.Ancestors().Select(element => (string?)element.Attribute("NodeId"))
                    .FirstOrDefault(nodeId => nodeId is not null) ?? PumpSourcePath;
                XElement? identifier = range.Element(types + "TypeId")?.Element(types + "Identifier");
                Assert.That(identifier, Is.Not.Null, origin);
                Assert.That(NodeId.TryParse(identifier!.Value, out NodeId encodingId), Is.True, origin);
                Assert.That(encodingId, Is.EqualTo(expected), origin);
            }
        }

        [Test]
        public void GeneratedPumpSourceUsesLfLineEndings()
        {
            ByteString generated = WotAggregationDocumentGenerator.GeneratePumpNodeSetXml(PumpSourcePath);

            Assert.That(generated.Span.IndexOf((byte)'\r'), Is.EqualTo(-1));
            Assert.That(generated.Span[generated.Length - 1], Is.EqualTo((byte)'\n'));
        }

        [Test]
        public void GeneratedPumpSourceRebuildsMissingDeclarationsAndTheSecondPump()
        {
            var seed = XDocument.Load(PumpSourcePath);
            string[] ownedNames =
            [
                "SourceARunning", "SourceBRunning",
                "SourceAStart", "SourceAStop", "SourceAReset",
                "SourceBStart", "SourceBStop", "SourceBReset",
                "CavitationAlarm", "MotorOverheatAlarm",
                "CavitationAcknowledge", "CavitationConfirm",
                "MotorOverheatAcknowledge", "MotorOverheatConfirm"
            ];
            seed.Root!.Elements().Where(element => IsGeneratedNodeId((string?)element.Attribute("NodeId"))).Remove();
            XElement pump = seed.Root.Elements().Single(element =>
                (string?)element.Attribute("NodeId") == "ns=1;s=Pump1");
            pump.SetAttributeValue("EventNotifier", null);
            pump.Descendants().Where(element =>
                element.Name.LocalName == "Reference" && IsGeneratedNodeId(element.Value)).Remove();
            seed.Root.Descendants().Where(element => element.Name.LocalName == "Alias" &&
                (string?)element.Attribute("Alias") is "Argument" or "HasSubtype" or "GeneratesEvent").Remove();

            string temporarySource = Path.GetTempFileName();
            try
            {
                seed.Save(temporarySource);

                ByteString generated = WotAggregationDocumentGenerator.GeneratePumpNodeSetXml(temporarySource);

                Assert.That(generated.ToArray(), Is.EqualTo(File.ReadAllBytes(PumpSourcePath)));
                using var stream = new MemoryStream(generated.ToArray(), writable: false);
                UANodeSet restored = UANodeSet.Read(stream)!;
                foreach (string pumpName in new[] { "Pump1", "Pump2" })
                {
                    UANode root = restored.Items!.Single(node => node.NodeId == $"ns=1;s={pumpName}");
                    Assert.That(root.References!.Any(reference => reference.ReferenceType == "i=35" &&
                        !reference.IsForward && reference.Value == "i=85"), Is.True, pumpName);
                    Assert.That(restored.Items!.Select(node => node.NodeId),
                        Is.SupersetOf(ownedNames.Select(name => $"ns=1;s={pumpName}.{name}")), pumpName);
                }
            }
            finally
            {
                File.Delete(temporarySource);
            }

            bool IsGeneratedNodeId(string? nodeId)
            {
                return nodeId == "ns=1;s=Pump2" ||
                    nodeId?.StartsWith("ns=1;s=Pump2.", StringComparison.Ordinal) == true ||
                    ownedNames.Any(name => nodeId == "ns=1;s=Pump1." + name ||
                        nodeId?.StartsWith("ns=1;s=Pump1." + name + ".", StringComparison.Ordinal) == true);
            }
        }

        [Test]
        public async Task BindingEnrichmentPreservesSourceDeclarationsAndPropertySchemas()
        {
            ArrayOf<SampleDocument> source = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            Dictionary<string, byte[]> originalBytes = source.ToList().ToDictionary(
                document => document.ResourceId, document => document.Json.ToArray(), StringComparer.Ordinal);

            ArrayOf<SampleDocument> bound = await WotAggregationDocumentGenerator
                .BindPumpDocumentsAsync(source).ConfigureAwait(false);

            Assert.That(bound.ToList().Select(document => (document.ResourceId, document.Path, document.DocumentKind)),
                Is.EquivalentTo(source.ToList().Select(document =>
                    (document.ResourceId, document.Path, document.DocumentKind))));
            string[] originalDeclarations = DeclarationInventory(source)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Assert.That(originalDeclarations, Is.Not.Empty);
            Assert.That(DeclarationInventory(bound).OrderBy(value => value, StringComparer.Ordinal),
                Is.EqualTo(originalDeclarations));
            foreach (SampleDocument document in source)
            {
                Assert.That(document.Json.ToArray(), Is.EqualTo(originalBytes[document.ResourceId]), document.Path);
                JsonObject root = JsonNode.Parse(bound.ToList().Single(candidate =>
                    candidate.ResourceId == document.ResourceId).Json.Span)!.AsObject();
                Assert.That(root.ContainsKey("uav:nodes"), Is.False, document.Path);
                Assert.That(root.ContainsKey("uav:nodeSet"), Is.False, document.Path);
            }

            foreach (string pumpName in new[] { "Pump1", "Pump2" })
            {
                foreach (WotAggregationDocumentGenerator.PropertyBinding binding in
                    WotAggregationDocumentGenerator.PropertyBindings)
                {
                    string nodeId = WotAggregationDocumentGenerator.LocalNodeId(pumpName, binding.LocalPath);
                    string reference = WotAggregationDocumentGenerator.AffordanceReference(
                        source, "properties", nodeId);
                    Assert.That(
                        WotAggregationDocumentGenerator.AffordanceReference(bound, "properties", nodeId),
                        Is.EqualTo(reference));
                    JsonObject original = ReadDeclaration(source, reference);
                    JsonObject enriched = ReadDeclaration(bound, reference);
                    Assert.That(original.ContainsKey("uav:mapToNodeId"), Is.False, nodeId);
                    Assert.That(enriched["uav:mapToNodeId"]!.GetValue<string>(), Is.EqualTo(nodeId));
                    Assert.That(enriched["forms"]!.AsArray(), Has.Count.EqualTo(1), nodeId);
                    Assert.That(enriched["forms"]![0]!["uav:id"]!.GetValue<string>(),
                        Is.EqualTo($"nsu=urn:opcfoundation.org:UA:WotAggregation:{binding.Source};" +
                            $"s={pumpName}.{binding.SourcePath}"));
                    original.Remove("forms");
                    enriched.Remove("forms");
                    enriched.Remove("uav:mapToNodeId");
                    Assert.That(JsonNode.DeepEquals(enriched, original), Is.True,
                        $"Enrichment changed the source schema of {nodeId}.");
                }
            }
        }

        [TestCase("Pump1", "properties", "Identification.Manufacturer")]
        [TestCase("Pump2", "properties", "SourceBRunning")]
        [TestCase("Pump1", "actions", "SourceAStart")]
        [TestCase("Pump2", "actions", "SourceBReset")]
        [TestCase("Pump1", "actions", "CavitationAcknowledge")]
        [TestCase("Pump2", "actions", "MotorOverheatConfirm")]
        [TestCase("Pump1", "events", "CavitationAlarm")]
        [TestCase("Pump2", "events", "MotorOverheatAlarm")]
        public async Task BindingEnrichmentRejectsMissingSourceDeclarations(
            string pumpName,
            string mapName,
            string localPath)
        {
            ArrayOf<SampleDocument> source = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            string nodeId = WotAggregationDocumentGenerator.LocalNodeId(pumpName, localPath);
            ArrayOf<SampleDocument> incomplete = RemoveDeclaration(source, mapName, nodeId);

            await Assert.ThatAsync(
                async () =>
                {
                    await WotAggregationDocumentGenerator.BindPumpDocumentsAsync(incomplete).ConfigureAwait(false);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    $"Expected exactly one '{mapName}' declaration for '{nodeId}'; found 0.")).ConfigureAwait(false);
        }

        [TestCase("Pump1", "uav:nodes")]
        [TestCase("Pump1", "uav:nodeSet")]
        [TestCase("Pump2", "uav:nodes")]
        [TestCase("Pump2", "uav:nodeSet")]
        public async Task BindingEnrichmentRejectsMalformedNativeOwners(string pumpName, string nativeTerm)
        {
            ArrayOf<SampleDocument> source = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            string nodeId = WotAggregationDocumentGenerator.LocalNodeId(
                pumpName, "Operational.Measurements.DifferentialPressure");
            string reference = WotAggregationDocumentGenerator.AffordanceReference(source, "properties", nodeId);
            string owner = reference[..reference.IndexOf('#', StringComparison.Ordinal)];
            ArrayOf<SampleDocument> overlaid = source.ToArrayOf(document =>
            {
                if (document.ResourceId != owner)
                {
                    return document;
                }
                JsonObject root = JsonNode.Parse(document.Json.Span)!.AsObject();
                root[nativeTerm] = nativeTerm == "uav:nodes" ? new JsonArray() : new JsonObject();
                return document with { Json = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(root)) };
            });

            await Assert.ThatAsync(
                async () =>
                {
                    await WotAggregationDocumentGenerator.BindPumpDocumentsAsync(overlaid).ConfigureAwait(false);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains(
                    owner)).ConfigureAwait(false);
        }

        [Test]
        public async Task VerifiedPumpBindingsPreserveEveryNativePartition()
        {
            UANodeSet source = WotAggregationDocumentGenerator.ReadNodeSet(PumpSourcePath);
            ArrayOf<SampleDocument> declarations = await WotAggregationDocumentGenerator
                .GenerateVerifiedDocumentSetAsync(source, "sample-pump").ConfigureAwait(false);
            ArrayOf<SampleDocument> bound = await WotAggregationDocumentGenerator
                .BindPumpDocumentsAsync(declarations).ConfigureAwait(false);

            Assert.That(declarations.Count, Is.GreaterThan(2));
            Assert.That(bound.Count, Is.EqualTo(declarations.Count));
            int nativePartitions = 0;
            foreach (SampleDocument original in declarations)
            {
                JsonNode baseline = JsonNode.Parse(original.Json.Span)!;
                JsonNode updated = JsonNode.Parse(bound.ToList().Single(
                    document => document.ResourceId == original.ResourceId).Json.Span)!;
                Assert.That(JsonNode.DeepEquals(baseline["uav:nodes"], updated["uav:nodes"]), Is.True, original.Path);
                Assert.That(updated["uav:nodeSet"], Is.Null, original.Path);
                if (baseline["uav:nodes"] is not null)
                {
                    nativePartitions++;
                }
            }
            Assert.That(nativePartitions, Is.GreaterThan(1));
        }

        [Test]
        public async Task BindingEnrichmentRejectsReadableMembersMissingFromTheirNativePartition()
        {
            UANodeSet source = WotAggregationDocumentGenerator.ReadNodeSet(PumpSourcePath);
            ArrayOf<SampleDocument> declarations = await WotAggregationDocumentGenerator
                .GenerateVerifiedDocumentSetAsync(source, "sample-pump").ConfigureAwait(false);
            const string nodeId = "ns=1;s=Pump1.Operational.Measurements.DifferentialPressure";
            ArrayOf<SampleDocument> incomplete = declarations.ConvertAll(document =>
            {
                JsonObject root = JsonNode.Parse(document.Json.Span)!.AsObject();
                if (root["uav:nodes"]?["nodes"] is JsonArray nodes)
                {
                    JsonNode? node = nodes.SingleOrDefault(item => item?["nodeId"]?.GetValue<string>() == nodeId);
                    if (node is not null)
                    {
                        nodes.Remove(node);
                    }
                }
                return document with { Json = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(root)) };
            });

            await Assert.ThatAsync(
                () => WotAggregationDocumentGenerator.BindPumpDocumentsAsync(incomplete),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains(
                    "cannot overlay undeclared Property")).ConfigureAwait(false);
        }

        [TestCase("Pump1", "CavitationAcknowledge", "CavitationAlarm")]
        [TestCase("Pump2", "MotorOverheatConfirm", "MotorOverheatAlarm")]
        public async Task BindingEnrichmentRejectsConditionActionsSeparatedFromTheirEvent(
            string pumpName,
            string actionName,
            string eventName)
        {
            ArrayOf<SampleDocument> source = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            string nodeId = WotAggregationDocumentGenerator.LocalNodeId(pumpName, actionName);
            string reference = WotAggregationDocumentGenerator.AffordanceReference(source, "actions", nodeId);
            JsonObject action = ReadDeclaration(source, reference);
            ArrayOf<SampleDocument> separated = RemoveDeclaration(source, "actions", nodeId)
                .AddItem(CreateDocument("detached-condition", new JsonObject
                {
                    ["actions"] = new JsonObject { [actionName] = action.DeepClone() }
                }));

            await Assert.ThatAsync(
                async () =>
                {
                    await WotAggregationDocumentGenerator.BindPumpDocumentsAsync(separated).ConfigureAwait(false);
                },
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    $"'detached-condition' does not contain its '{eventName}' Condition event.")).ConfigureAwait(false);
        }

        [TestCase("Pump1", "properties", "Identification.SerialNumber")]
        [TestCase("Pump2", "properties", "Operational.Measurements.MassFlow")]
        [TestCase("Pump1", "actions", "SourceBStop")]
        [TestCase("Pump2", "actions", "CavitationConfirm")]
        [TestCase("Pump1", "events", "CavitationAlarm")]
        [TestCase("Pump2", "events", "MotorOverheatAlarm")]
        public async Task ProjectionsNeverSynthesizeMissingSourceDeclarations(
            string pumpName,
            string mapName,
            string localPath)
        {
            ArrayOf<SampleDocument> source = await ReadUnboundPumpDocumentsAsync().ConfigureAwait(false);
            string nodeId = WotAggregationDocumentGenerator.LocalNodeId(pumpName, localPath);
            ArrayOf<SampleDocument> incomplete = RemoveDeclaration(source, mapName, nodeId);

            Assert.That(
                () => WotAggregationDocumentGenerator.GenerateAssetProjectionDocuments(incomplete),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                    $"Expected exactly one '{mapName}' declaration for '{nodeId}'; found 0."));
        }

        private static SampleDocument CreateNodeDocument(
            string resourceId,
            string nodeId,
            string type,
            string browseName,
            JsonObject? root = null)
        {
            root ??= new JsonObject();
            bool isType = type != "uav:object";
            root["@context"] ??= new JsonObject
            {
                ["model"] = "urn:model",
                ["ua"] = "http://opcfoundation.org/UA/",
                ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/",
                ["tm"] = "https://www.w3.org/2019/wot/tm#"
            };
            root["@type"] ??= isType ? new JsonArray("tm:ThingModel", type) : new JsonArray(type);
            root["uav:id"] = nodeId;
            root["uav:browseName"] = "model:" + browseName;
            return CreateDocument(resourceId, root,
                isType ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription);
        }

        private static SampleDocument CreateDocument(
            string resourceId,
            JsonObject? root = null,
            WoTDocumentKindEnum kind = WoTDocumentKindEnum.ThingDescription,
            string? path = null)
        {
            return new SampleDocument(resourceId, path ?? resourceId + ".json", kind,
                ByteString.From(JsonSerializer.SerializeToUtf8Bytes(root ?? new JsonObject())));
        }

        private static async Task<ArrayOf<SampleDocument>> ReadUnboundPumpDocumentsAsync()
        {
            using var stream = new FileStream(
                PumpSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;
            UANodeSet source = UANodeSet.Read(buffer)
                ?? throw new InvalidOperationException("Could not read the pump source.");
            return WotAggregationDocumentGenerator.GeneratePumpDeclarationDocuments(source);
        }

        private static ArrayOf<SampleDocument> RemoveDeclaration(
            ArrayOf<SampleDocument> documents,
            string mapName,
            string nodeId)
        {
            string reference = WotAggregationDocumentGenerator.AffordanceReference(documents, mapName, nodeId);
            int fragment = reference.IndexOf('#', StringComparison.Ordinal);
            string resourceId = reference[..fragment];
            string[] tokens = PointerTokens(reference[(fragment + 1)..]);
            return documents.ToArrayOf(document =>
            {
                if (document.ResourceId != resourceId)
                {
                    return document;
                }
                JsonObject root = JsonNode.Parse(document.Json.Span)!.AsObject();
                JsonObject parent = root;
                foreach (string token in tokens.Take(tokens.Length - 1))
                {
                    parent = parent[token]!.AsObject();
                }
                Assert.That(parent.Remove(tokens[^1]), Is.True, reference);
                return document with { Json = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(root)) };
            });
        }

        private static JsonObject ReadDeclaration(ArrayOf<SampleDocument> documents, string reference)
        {
            int fragment = reference.IndexOf('#', StringComparison.Ordinal);
            SampleDocument document = documents.ToList().Single(
                candidate => candidate.ResourceId == reference[..fragment]);
            JsonNode node = JsonNode.Parse(document.Json.Span)!;
            foreach (string token in PointerTokens(reference[(fragment + 1)..]))
            {
                node = node[token]!;
            }
            return node.AsObject();
        }

        private static string[] PointerTokens(string pointer)
        {
            return pointer[1..].Split('/').Select(token =>
                token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
                .ToArray();
        }

        private static IEnumerable<string> DeclarationInventory(ArrayOf<SampleDocument> documents)
        {
            for (int documentIndex = 0; documentIndex < documents.Count; documentIndex++)
            {
                SampleDocument document = documents[documentIndex];
                JsonObject root = JsonNode.Parse(document.Json.Span)!.AsObject();
                foreach (string mapName in new[] { "properties", "actions", "events" })
                {
                    if (root[mapName] is JsonObject map)
                    {
                        foreach (string declaration in Visit(map, "/" + mapName))
                        {
                            yield return document.ResourceId + "#" + declaration;
                        }
                    }
                }
            }

            static IEnumerable<string> Visit(JsonObject map, string pointer)
            {
                foreach ((string name, JsonNode? value) in map)
                {
                    if (value is not JsonObject affordance)
                    {
                        continue;
                    }
                    string memberPointer = pointer + "/" +
                        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    if (affordance["uav:id"] is JsonNode id)
                    {
                        yield return memberPointer + "=" + id.GetValue<string>();
                    }
                    if (affordance["properties"] is JsonObject children)
                    {
                        foreach (string child in Visit(children, memberPointer + "/properties"))
                        {
                            yield return child;
                        }
                    }
                }
            }
        }

        private static string PumpSourcePath => Path.Combine(
            RepositoryRoot, "samples", "WotCon", "AggregationClient", "Documents", "SamplePump.NodeSet2.xml");

        private static string RepositoryRoot
        {
            get
            {
                DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    directory = directory.Parent;
                }
                return directory?.FullName
                    ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
            }
        }

        private static readonly string[] s_independentOrder = ["z-model", "a-view", "b-independent", "c-independent"];
        private static readonly string[] s_modelDependency = ["z-model"];
        private static readonly string[] s_logicalOrder = ["model", "owner", "view"];
        private static readonly string[] s_logicalDependencies = ["model", "owner"];
        private static readonly string[] s_manifestFields =
        [
            "resourceId", "path", "documentKind", "groupId", "dependsOn"
        ];
        private static readonly string[] s_portableDependencies = ["feeds-type", "pump-type", "reading-type"];
        private static readonly string[] s_namedDependencies = ["feeds-type", "pump-type"];
        private static readonly string[] s_pinnedReferenceDependency = ["second-reference"];
        private static readonly string[] s_parentChildOrder = ["z-parent", "a-child"];
        private static readonly string[] s_parentDependency = ["z-parent"];
        private static readonly string[] s_hashIdentityDependencies = ["logical-model", "type"];
    }
}
