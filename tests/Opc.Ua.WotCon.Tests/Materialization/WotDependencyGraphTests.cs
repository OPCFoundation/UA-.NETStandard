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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the TD/TM dependency graph: reference extraction, closure
    /// partitioning (weakly-connected components), topological ordering, and
    /// missing-dependency and cycle detection.
    /// </summary>
    [TestFixture]
    public sealed class WotDependencyGraphTests
    {
        /// <summary>
        /// A snapshot plus a reader for the bytes behind its versions. The
        /// snapshot carries only digests, so a caller that needs the content has
        /// to be able to fetch it; keeping the two together avoids a global
        /// registry and keeps each test self-contained.
        /// </summary>
        private sealed record SnapshotFixture(
            WotRegistrySnapshot Snapshot,
            Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> ReadContent);

        private static async Task<SnapshotFixture> Snapshot(
            params (WoTDocumentKindEnum Kind, string Id, byte[] Content)[] docs)
        {
            var byDigest = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            using var service = new WotRegistryService();
            foreach ((WoTDocumentKindEnum kind, string id, byte[] content) in docs)
            {
                ByteString bytes = ByteString.From(content);
                byDigest[WotContentDigest.ToHex(WotContentDigest.Compute(bytes))] = bytes;
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels
                        : WotRegistryGroups.ThingDescriptions,
                    ResourceId = id,
                    Kind = kind,
                    Content = bytes
                });
            }
            return new SnapshotFixture(
                service.Current,
                (version, _) => new ValueTask<ByteString>(byDigest[version.DigestHex]));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DataSchemaDefinitionReferenceJoinsItsOwnerClosureAndPreventsDeletion(
            bool independentIdentity, bool contextualReference)
        {
            using var registry = new WotRegistryService();
            string definitionId = independentIdentity ? "urn:r30:Reading" : "urn:r30:types#Reading";
            string referenceId = contextualReference ? "types:Reading" : definitionId;
            WotRegistryMutationResult definition = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = "reading-types",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(System.Text.Encoding.UTF8.GetBytes($$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "types": "urn:r30:model:"
                    }
                  ],
                  "@type": "tm:ThingModel",
                  "id": "urn:r30:types",
                  "title": "Reading types",
                  "uav:dataTypeDefinitions": [{
                    "@id": "{{definitionId}}",
                    "@type": "uav:StructureDefinition",
                    "uav:dataTypeName": "types:Reading",
                    "uav:structureType": "Structure",
                    "uav:fields": [{
                      "@type": "uav:StructureField",
                      "uav:fieldName": "Value",
                      "uav:fieldDataTypeId": "i=11",
                      "uav:valueRank": -1
                    }]
                  }]
                }
                """))
            }).ConfigureAwait(false);
            WotRegistryMutationResult source = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "sensor",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(System.Text.Encoding.UTF8.GetBytes($$"""
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "types": "urn:r30:"
                    }
                  ],
                  "id": "urn:r30:sensor",
                  "title": "Sensor",
                  "properties": {
                    "reading": {
                      "uav:dataTypeDefinition": {"@id": "{{referenceId}}"}
                    }
                  }
                }
                """))
            }).ConfigureAwait(false);
            Assert.That(definition.Changed, Is.True, definition.Message);
            Assert.That(source.Changed, Is.True, source.Message);
            WotResource model = definition.Resource!;
            await registry.SetEnabledAsync(model.GroupId, model.ResourceId, false).ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                before, [source.Resource!], 64, registry.ReadContentAsync, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures, Has.Length.EqualTo(1));
            WotDependencyClosure closure = closures[0];
            Assert.That(closure.Members.Select(member => member.Xid),
                Is.EquivalentTo(new[] { source.Resource!.Xid, model.Xid }));
            Assert.That(closure.IsProjectable, Is.True, string.Join("; ", closure.Diagnostics));
            Assert.That(closure.HasCycle, Is.False);
            Assert.That(closure.ActivationMembers.ToList().Select(member => member.Xid),
                Is.EqualTo(new[] { source.Resource.Xid }));
            WotDependency edge = closure.Dependencies.Single();
            Assert.That(edge.SourceXid, Is.EqualTo(source.Resource.Xid));
            Assert.That(edge.TargetXid, Is.EqualTo(model.Xid));
            Assert.That(edge.TargetHref, Is.EqualTo(referenceId));
            Assert.That(edge.RefType, Is.EqualTo("uav:dataTypeDefinition"));
            Assert.That(edge.Resolved, Is.True);

            WotDeleteResult deletion = await registry.DeleteResourceAsync(
                model.GroupId, model.ResourceId, WoTDeletePolicyEnum.Reject).ConfigureAwait(false);
            Assert.That(deletion.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(deletion.Dependents, Is.EqualTo(new[] { source.Resource.Xid }));
            Assert.That(registry.Current, Is.SameAs(before));
        }

        [Test]
        public void StructureFieldDefinitionReferenceRetainsItsNonOrderingDependency()
        {
            ByteString content = ByteString.From(System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "types": "urn:r30:model:"
                  },
                  "id": "urn:r30:outer-document",
                  "uav:dataTypeDefinitions": [{
                    "@id": "urn:r30:Outer",
                    "@type": "uav:StructureDefinition",
                    "uav:dataTypeName": "types:Outer",
                    "uav:structureType": "Structure",
                    "uav:fields": [{
                      "@type": "uav:StructureField",
                      "uav:fieldName": "Value",
                      "uav:fieldDataTypeDefinition": {"@id": "urn:r30:Inner"}
                    }]
                  }]
                }
                """));

            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);

            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.References.Count, Is.EqualTo(1));
            WotResourceReference reference = metadata.References[0];
            Assert.That(reference.TargetUri, Is.EqualTo("urn:r30:Inner"));
            Assert.That(reference.LookupUri, Is.EqualTo("urn:r30:Inner"));
            Assert.That(reference.RefType, Is.EqualTo("uav:fieldDataTypeDefinition"));
            Assert.That(reference.RequiresOrdering, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void DataTypeSubtypeReferenceRetainsItsOrderingDependency(int referenceForm)
        {
            string referenceJson = referenceForm switch
            {
                0 => """{"@id":"urn:r30:Base"}""",
                1 => "\"urn:r30:Base\"",
                2 => """{"uav:dataTypeName":"types:Base"}""",
                3 => "\"types:Base\"",
                4 => """{"uav:dataTypeId":"nsu=urn:r30:model:;s=Base"}""",
                _ => "\"nsu=urn:r30:model:;s=Base\""
            };
            string target = referenceForm < 2 ? "urn:r30:Base" :
                referenceForm < 4 ? "types:Base" : "nsu=urn:r30:model:;s=Base";
            ByteString content = ByteString.From(System.Text.Encoding.UTF8.GetBytes($$$"""
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "types": "urn:r30:model:"
                  },
                  "id": "urn:r30:derived-document",
                  "uav:dataTypeDefinitions": [{
                    "@id": "urn:r30:Derived",
                    "@type": "uav:SimpleDataType",
                    "uav:dataTypeName": "types:Derived",
                    "uav:dataTypeSubtypeOf": {{{referenceJson}}}
                  }]
                }
                """));

            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);

            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.References.Count, Is.EqualTo(1));
            WotResourceReference reference = metadata.References[0];
            Assert.That(reference.TargetUri, Is.EqualTo(target));
            Assert.That(reference.LookupUri,
                Is.EqualTo(referenceForm is 2 or 3 ? "nsu=urn:r30:model:;Base" : target));
            Assert.That(reference.RefType, Is.EqualTo("uav:dataTypeSubtypeOf"));
            Assert.That(reference.RequiresOrdering, Is.True);
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public async Task CrossDocumentDataTypeCyclesDistinguishFieldsFromInheritance(
            bool inheritance, bool namedFields, bool nativeFields)
        {
            static byte[] Document(string name, string target, bool subtype, bool names, bool nativeIds)
            {
                string fieldType = nativeIds
                    ? $"\"uav:fieldDataTypeId\":\"nsu=urn:r30:cycle:;s={target}\""
                    : names
                    ? $"\"uav:fieldDataTypeName\":\"types:{target}\""
                    : $"\"uav:fieldDataTypeDefinition\":{{\"@id\":\"urn:r30:{target}\"}}";
                string relation = subtype
                    ? $$$"""
                        "uav:dataTypeSubtypeOf": {"@id": "urn:r30:{{{target}}}"}
                        """
                    : $$$"""
                        "uav:structureType": "Structure",
                        "uav:fields": [{
                          "@type": "uav:StructureField",
                          "uav:fieldName": "Next",
                          "uav:isOptional": true,
                          {{{fieldType}}}
                        }]
                        """;
                string kind = subtype ? "uav:SimpleDataType" : "uav:StructureDefinition";
                return System.Text.Encoding.UTF8.GetBytes($$$"""
                    {
                      "@context": {
                        "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                        "types": "urn:r30:cycle:"
                      },
                      "@type": "tm:ThingModel",
                      "id": "urn:r30:document-{{{name}}}",
                      "uav:dataTypeDefinitions": [{
                        "@id": "urn:r30:{{{name}}}",
                        "@type": "{{{kind}}}",
                        "uav:dataTypeName": "types:{{{name}}}",
                        "uav:dataTypeId": "nsu=urn:r30:cycle:;s={{{name}}}",
                        {{{relation}}}
                      }]
                    }
                    """);
            }
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "first",
                    Document("First", "Second", inheritance, namedFields, nativeFields)),
                (WoTDocumentKindEnum.ThingModel, "second",
                    Document("Second", "First", inheritance, namedFields, nativeFields)));
            WotResource selected = fixture.Snapshot.FindResource(WotRegistryGroups.ThingModels, "first")!;

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [selected], 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);

            string[] expected = ["first", "second"];
            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].Members.Select(member => member.ResourceId),
                Is.EquivalentTo(expected));
            Assert.That(closures[0].Dependencies, Has.Length.EqualTo(2));
            Assert.That(closures[0].HasMissingDependency, Is.False);
            Assert.That(closures[0].HasCycle, Is.EqualTo(inheritance));
            Assert.That(closures[0].IsProjectable, Is.EqualTo(!inheritance));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public async Task TypedDocumentLinksJoinTheirTargetsAndRetainReverseDependencies(
            bool customReference, bool explicitIdentifier)
        {
            string relation = customReference ? "model:References" : "ua:HasComponent";
            string identifier = explicitIdentifier ? "\"uav:refId\":\"nsu=urn:r30:references:;i=1\"," : string.Empty;
            byte[] content = System.Text.Encoding.UTF8.GetBytes($$$"""
                {
                  "@context": {
                    "ua": "http://opcfoundation.org/UA/",
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "model": "urn:r30:references:"
                  },
                  "id": "urn:r30:source",
                  "title": "Source",
                  "links": [{
                    "rel": "{{{relation}}}",
                    {{{identifier}}}
                    "href": "urn:r30:target"
                  }]
                }
                """);
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "source", content),
                (WoTDocumentKindEnum.ThingDescription, "target", TestMaterialization.Td("urn:r30:target")));
            WotResource source = fixture.Snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "source")!;
            WotResource target = fixture.Snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "target")!;

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [source], 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures.Single().Members.Select(member => member.Xid),
                Is.EquivalentTo(new[] { source.Xid, target.Xid }));
            WotDependency edge = closures[0].Dependencies.Single();
            Assert.That(edge.TargetXid, Is.EqualTo(target.Xid));
            Assert.That(edge.TargetHref, Is.EqualTo("urn:r30:target"));
            Assert.That(edge.RefType, Is.EqualTo(customReference ? "urn:r30:references:References" : "ua:HasComponent"));
            Assert.That(closures[0].HasCycle, Is.False);
            ImmutableArray<WotDependent> dependents = await WotDependencyGraph.FindDependentsAsync(
                fixture.Snapshot, target, 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);
            Assert.That(dependents.Select(dependent => dependent.Xid), Is.EqualTo(new[] { source.Xid }));
        }

        [Test]
        public void OpaqueAndLiteralPayloadsDoNotAddDeclarationDependencies()
        {
            ByteString content = ByteString.From(System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                    "payload": {"@id":"urn:r30:payload","@type":"@json"}
                  },
                  "id": "urn:r30:opaque-source",
                  "uav:metadata": {
                    "uav:dataTypeDefinition": {"@id":"urn:r30:metadata-definition"}
                  },
                  "payload": {
                    "uav:dataTypeDefinitions": [{
                      "@id":"urn:r30:payload-definition","@type":"uav:SimpleDataType",
                      "uav:dataTypeName":"nsu=urn:r30:payload;Hidden",
                      "uav:dataTypeSubtypeOf":{"@id":"urn:r30:payload-base"}
                    }]
                  },
                  "literal": {
                    "@value":{"uav:dataTypeDefinition":{"@id":"urn:r30:literal-definition"}},
                    "@type":"@json"
                  }
                }
                """));

            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);

            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.References.Count, Is.Zero);
            Assert.That(metadata.DefinedNodeIds.Count, Is.Zero);
        }

        [Test]
        public async Task UnownedNativeAndQualifiedTypesRemainLoadedContextReferences()
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context": {
                    "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                    "loaded":"urn:r30:loaded"
                  },
                  "id":"urn:r30:loaded-consumer","title":"Consumer",
                  "properties":{
                    "named":{"uav:dataTypeName":"loaded:Reading"},
                    "native":{"uav:dataTypeId":"nsu=urn:r30:loaded;i=3000"},
                    "builtin":{"uav:dataTypeId":"i=6"}
                  }
                }
                """);
            SnapshotFixture fixture = await Snapshot((WoTDocumentKindEnum.ThingDescription, "consumer", content));
            WotResource source = fixture.Snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "consumer")!;
            var acquired = new List<string>();
            ValueTask<ByteString> ReadAsync(WotResourceVersion version, CancellationToken cancellationToken)
            {
                acquired.Add(version.DigestHex);
                return fixture.ReadContent(version, cancellationToken);
            }

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [source], 64, ReadAsync, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures.Single().IsProjectable, Is.True);
            Assert.That(closures[0].Dependencies, Is.Empty,
                "Loaded and builtin types are not missing document dependencies.");
            Assert.That(closures[0].Members.Single().Xid, Is.EqualTo(source.Xid));
            Assert.That(acquired, Is.EqualTo(new[] { source.DefaultVersion!.DigestHex }));
        }

        [Test]
        public async Task AmbiguousDefinitionIdentityDoesNotSelectAnArbitraryOwner()
        {
            static byte[] Definition(string owner)
            {
                return System.Text.Encoding.UTF8.GetBytes($$"""
                    {
                      "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/"},
                      "@type":"tm:ThingModel","id":"urn:r30:{{owner}}",
                      "uav:dataTypeDefinitions":[{
                        "@id":"urn:r30:duplicate","@type":"uav:SimpleDataType",
                        "uav:dataTypeName":"nsu=urn:r30:{{owner}};Value",
                        "uav:dataTypeSubtypeOf":{"uav:dataTypeId":"i=6"}
                      }]
                    }
                    """);
            }
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "left", Definition("left")),
                (WoTDocumentKindEnum.ThingModel, "right", Definition("right")),
                (WoTDocumentKindEnum.ThingDescription, "consumer", System.Text.Encoding.UTF8.GetBytes("""
                    {
                      "id":"urn:r30:consumer","title":"Consumer",
                      "properties":{"value":{"uav:dataTypeDefinition":{"@id":"urn:r30:duplicate"}}}
                    }
                    """)));
            WotResource consumer = fixture.Snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "consumer")!;

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [consumer], 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures.Single().IsProjectable, Is.False);
            Assert.That(closures[0].HasMissingDependency, Is.True);
            Assert.That(closures[0].Members.Single().Xid, Is.EqualTo(consumer.Xid));
            Assert.That(closures[0].Dependencies.Single().TargetHref, Is.EqualTo("urn:r30:duplicate"));
            Assert.That(closures[0].Dependencies[0].TargetXid, Is.Null);
            Assert.That(closures[0].Dependencies[0].Resolved, Is.False);
        }

        [Test]
        public async Task DerivedDataTypeIdentitySelectsTheSameStoredDefinitionAsConversion()
        {
            byte[] definitions = System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/","t":"urn:r30:derived-types"},
                  "@type":["tm:ThingModel","uav:objectType"],
                  "id":"urn:r30:derived-document","title":"Definitions",
                  "uav:id":"nsu=urn:r30:derived-types;s=Definitions","uav:browseName":"t:Definitions",
                  "uav:dataTypeDefinitions":[{
                    "@id":"urn:r30:derived-reading","@type":"uav:SimpleDataType",
                    "uav:dataTypeName":"t:Reading","uav:dataTypeSubtypeOf":{"uav:dataTypeId":"i=6"}
                  }]
                }
                """);
            byte[] content = System.Text.Encoding.UTF8.GetBytes("""
                {
                  "id":"urn:r30:derived-consumer","title":"Consumer",
                  "properties":{"reading":{"uav:dataTypeId":"nsu=urn:r30:derived-types;s=DataTypes/Reading"}}
                }
                """);
            using WotDocument document = WotDocument.Parse(definitions);
            WotConversionResult<UANodeSet> converted = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics.Select(item => item.Message)));
            UADataType type = converted.Value!.Items!.OfType<UADataType>().Single();
            Assert.That(NodeId.TryParse(type.NodeId!, out NodeId nativeId), Is.True);
            Assert.That(nativeId.TryGetValue(out string identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("DataTypes/Reading"));
            Assert.That(converted.Value.NamespaceUris![nativeId.NamespaceIndex - 1], Is.EqualTo("urn:r30:derived-types"));
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "definitions", definitions),
                (WoTDocumentKindEnum.ThingDescription, "consumer", content));
            WotResource source = fixture.Snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "consumer")!;
            WotResource target = fixture.Snapshot.FindResource(WotRegistryGroups.ThingModels, "definitions")!;

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [source], 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures.Single().Members.Select(member => member.Xid),
                Is.EquivalentTo(new[] { source.Xid, target.Xid }));
            Assert.That(closures[0].Dependencies.Single().TargetXid, Is.EqualTo(target.Xid));
            ImmutableArray<WotDependent> dependents = await WotDependencyGraph.FindDependentsAsync(
                fixture.Snapshot, target, 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);
            Assert.That(dependents.Select(dependent => dependent.Xid), Is.EqualTo(new[] { source.Xid }));
        }

        [Test]
        public async Task SameDocumentDataTypeInheritanceDoesNotCreateAResourceOrderingCycle()
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/","t":"urn:r30:local-types"},
                  "@type":"tm:ThingModel","id":"urn:r30:local-types-document","title":"Local types",
                  "uav:dataTypeDefinitions":[
                    {
                      "@id":"urn:r30:local-base","@type":"uav:SimpleDataType",
                      "uav:dataTypeName":"t:Base","uav:dataTypeSubtypeOf":{"uav:dataTypeId":"i=6"}
                    },
                    {
                      "@id":"urn:r30:local-derived","@type":"uav:SimpleDataType",
                      "uav:dataTypeName":"t:Derived","uav:dataTypeSubtypeOf":{"@id":"urn:r30:local-base"}
                    }
                  ]
                }
                """);
            SnapshotFixture fixture = await Snapshot((WoTDocumentKindEnum.ThingModel, "types", content));
            WotResource source = fixture.Snapshot.AllResources().Single();

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, [source], 64, fixture.ReadContent, CancellationToken.None).ConfigureAwait(false);

            Assert.That(closures.Single().HasCycle, Is.False,
                "A base and its derived definition inside one document are not a cycle between Resources.");
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(closures[0].Members.Single().Xid, Is.EqualTo(source.Xid));
            Assert.That(closures[0].Dependencies.Single().TargetXid, Is.EqualTo(source.Xid));
            Assert.That(closures[0].Dependencies[0].RefType, Is.EqualTo("uav:dataTypeSubtypeOf"));
        }

        [Test]
        public void ExplicitDataTypeIdentityDoesNotAcquireADerivedAlias()
        {
            ByteString content = ByteString.From(System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/","t":"urn:r30:explicit-types"},
                  "id":"urn:r30:explicit-document",
                  "uav:dataTypeDefinitions":[{
                    "@id":"urn:r30:explicit-reading","@type":"uav:SimpleDataType",
                    "uav:dataTypeName":"t:Reading","uav:dataTypeId":"nsu=urn:r30:explicit-types;i=3000",
                    "uav:dataTypeSubtypeOf":{"uav:dataTypeId":"i=6"}
                  }]
                }
                """));

            WotResourceDependencies metadata = WotDependencyGraph.ReadMetadata(content, 64);

            Assert.That(metadata.Error, Is.Empty);
            Assert.That(metadata.DefinedNodeIds.Count, Is.EqualTo(1));
            Assert.That(metadata.DefinedNodeIds[0], Is.EqualTo("nsu=urn:r30:explicit-types;i=3000"));
        }

        [TestCase("#/schemaDefinitions/Event")]
        [TestCase("self#/schemaDefinitions/Event")]
        [TestCase("urn:self#/schemaDefinitions/Event")]
        public async Task SameDocumentDefinitionFragmentsDoNotCreateDependencyCycles(string reference)
        {
            byte[] document = System.Text.Encoding.UTF8.GetBytes($$"""
                {
                  "@type": "tm:ThingModel",
                  "id": "urn:self",
                  "properties": { "Value": { "tm:ref": "{{reference}}" } },
                  "events": {
                    "Event": {
                      "tm:ref": "{{reference}}",
                      "uav:eventSelectClauses": [{ "tm:ref": "{{reference}}", "uav:browsePath": "EventId" }]
                    }
                  }
                }
                """);
            SnapshotFixture fixture = await Snapshot((WoTDocumentKindEnum.ThingModel, "self", document));

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, fixture.Snapshot.AllResources().ToArray(), 64,
                fixture.ReadContent, CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.True, string.Join("; ", closures[0].Diagnostics));
            Assert.That(closures[0].Dependencies, Is.Empty);
        }

        [Test]
        public async Task CycleDiagnosticsDescribeTheCycleRatherThanEveryDependentResource()
        {
            var documents = new List<(WoTDocumentKindEnum Kind, string Id, byte[] Content)>
            {
                (WoTDocumentKindEnum.ThingModel, "cycle-a", TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b")),
                (WoTDocumentKindEnum.ThingModel, "cycle-b", TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a"))
            };
            for (int i = 0; i < 100; i++)
            {
                string id = "dependent-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                documents.Add((WoTDocumentKindEnum.ThingModel, id, TestMaterialization.Tm("urn:" + id,
                    extendsHrefs: "urn:a")));
            }
            SnapshotFixture fixture = await Snapshot(documents.ToArray());

            ImmutableArray<WotDependencyClosure> closures = await WotDependencyGraph.BuildClosuresAsync(
                fixture.Snapshot, fixture.Snapshot.AllResources().ToArray(), 64,
                fixture.ReadContent, CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].HasCycle, Is.True);
            Assert.That(closures[0].Diagnostics.Single(), Has.Length.LessThan(512));
            Assert.That(closures[0].Diagnostics.Single(), Does.Contain("cycle-a").And.Contain("cycle-b"));
            Assert.That(closures[0].Diagnostics.Single(), Does.Not.Contain("dependent-"));
        }

        [Test]
        public void ExtractReferencesFindsTmExtendsLinks()
        {
            byte[] doc = TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm-1");

            IReadOnlyList<(string Href, string RefType)> references =
                WotDependencyGraph.ExtractReferences(doc, 64);

            Assert.That(references.Any(r => r.Href == "urn:tm-1" && r.RefType == "tm:extends"),
                Is.True);
        }

        /// <summary>
        /// An event affordance names the EventType definition its fields are
        /// selected from, and every explicit select clause names one too
        /// (WoT Binding Section 6.1). Both are dependency edges: the reference
        /// resolves against the documents a consumer holds, so the EventType
        /// Thing Model has to be a member of the same closure and has to be
        /// loaded before the document that selects from it.
        /// </summary>
        [Test]
        public void ExtractReferencesFindsEventTypeLinks()
        {
            byte[] doc = System.Text.Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"Thing\",\"id\":\"urn:td\",\"title\":\"Td\"," +
                "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                "\"tm:ref\":\"urn:tm-events#/events/alarm\"," +
                "\"uav:eventSelectClauses\":[" +
                "{\"tm:ref\":\"urn:tm-base\",\"uav:browsePath\":\"EventId\"}]}}}");

            IReadOnlyList<(string Href, string RefType)> references =
                WotDependencyGraph.ExtractReferences(doc, 64);

            Assert.Multiple(() =>
            {
                Assert.That(
                    references.Any(r =>
                        r.Href == "urn:tm-events#/events/alarm" &&
                        r.RefType == WotDependencyGraph.EventTypeRefType),
                    Is.True,
                    "The affordance-level fast path is an edge to the EventType definition.");
                Assert.That(
                    references.Any(r =>
                        r.Href == "urn:tm-base" &&
                        r.RefType == WotDependencyGraph.EventSelectClauseRefType),
                    Is.True,
                    "A clause names the EventType that declares the field it selects, which " +
                    "is an edge of its own.");
                Assert.That(
                    references.Count(r => r.Href == "urn:tm-events#/events/alarm"),
                    Is.EqualTo(1),
                    "The edge is stated once, under the label that says what it is.");
            });
        }

        /// <summary>
        /// The edge is what puts an EventType Thing Model in the closure of the
        /// document that selects from it, and the topological order loads it
        /// first.
        /// </summary>
        [Test]
        public async Task AnEventTypeModelIsLoadedBeforeTheDocumentThatSelectsFromIt()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "events", TestMaterialization.Tm("urn:tm-events")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    System.Text.Encoding.UTF8.GetBytes(
                        "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                        "\"@type\":\"Thing\",\"id\":\"urn:td\",\"title\":\"Td\"," +
                        "\"events\":{\"alarm\":{\"@type\":\"uav:eventType\"," +
                        "\"tm:ref\":\"urn:tm-events#/events/alarm\"}}}")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(
                closures[0].OrderedResources.Select(r => r.ResourceId),
                Is.EqualTo(s_eventTdResourceIds).AsCollection,
                "A consumer resolves the reference against the documents it holds, so the " +
                "definition is materialized before the document that names it.");
        }

        [Test]
        public async Task BuildClosuresSharedModelYieldsSingleClosureTmFirst()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "tm", TestMaterialization.Tm("urn:tm")),
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:tm")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].IsProjectable, Is.True);
            Assert.That(
                closures[0].OrderedResources.Select(r => r.ResourceId),
                Is.EqualTo(s_tmTdResourceIds));
        }

        [Test]
        public async Task BuildClosuresIndependentResourcesYieldSeparateClosures()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "a", TestMaterialization.Td("urn:a")),
                (WoTDocumentKindEnum.ThingDescription, "b", TestMaterialization.Td("urn:b")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(2));
            Assert.That(closures.All(c => c.OrderedResources.Length == 1), Is.True);
        }

        [Test]
        public async Task BuildClosuresMissingDependencyIsFlagged()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingDescription, "td",
                    TestMaterialization.Td("urn:td", extendsHrefs: "urn:missing")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].HasMissingDependency, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
        }

        [Test]
        public async Task BuildClosuresCycleIsDetected()
        {
            SnapshotFixture fixture = await Snapshot(
                (WoTDocumentKindEnum.ThingModel, "a",
                    TestMaterialization.Tm("urn:a", extendsHrefs: "urn:b")),
                (WoTDocumentKindEnum.ThingModel, "b",
                    TestMaterialization.Tm("urn:b", extendsHrefs: "urn:a")));

            ImmutableArray<WotDependencyClosure> closures =
                await WotDependencyGraph.BuildClosuresAsync(
                    fixture.Snapshot,
                    [.. fixture.Snapshot.AllResources()],
                    64,
                    fixture.ReadContent,
                    CancellationToken.None);

            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].HasCycle, Is.True);
            Assert.That(closures[0].IsProjectable, Is.False);
            Assert.That(closures[0].Members, Has.Length.EqualTo(2),
                "A cyclic closure must still report its members for diagnostics.");
        }

        private static readonly string[] s_tmTdResourceIds = ["tm", "td"];
        private static readonly string[] s_eventTdResourceIds = ["events", "td"];
    }
}
