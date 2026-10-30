/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Federation.Tests.FederationTestSupport;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    [TestFixture]
    [Category("EndpointRegistry")]
    public sealed class PythonOracleTests
    {
        public static IEnumerable<TestCaseData> ResolutionCases() => Cases("resolutionCalls");
        public static IEnumerable<TestCaseData> SelectionCases() => Cases("selectionCalls");

        [TestCaseSource(nameof(ResolutionCases))]
        public async Task PythonResolutionOracle(JsonElement fixture)
        {
            var provider = new OracleProvider(fixture);
            RegistryEntityReferenceDataType local = Origin(fixture.GetProperty("localOrigin"));
            JsonElement controls = fixture.GetProperty("inputs");
            var descriptors = new List<MessageReferenceBindingDataType>();
            if (controls.TryGetProperty("references", out JsonElement references))
            {
                foreach (JsonElement item in references.EnumerateArray())
                {
                    descriptors.Add(new MessageReferenceBindingDataType
                    {
                        Context = Origin(item.GetProperty("contextOrigin")),
                        ReferenceUri = item.GetProperty("uri").GetString(),
                        Target = NormalizeClaim(item.GetProperty("reference"), fixture)
                    });
                }
            }
            NativeMessageResolutionResultDataType actual = await new EndpointRegistryMessageResolver().ResolveControlsAsync(
                fixture.GetProperty("reference").GetString()!,
                Json(controls.GetRawText()), [.. descriptors],
                new EndpointRegistryMessageResolutionContext
                {
                    LocalOrigin = local,
                    Provider = provider,
                    Mapper = Mapper,
                    RequireExplicitReferences = true
                }).ConfigureAwait(false);
            JsonElement expected = fixture.GetProperty("expected");
            Assert.That(actual.Status, Is.EqualTo(expected.GetProperty("Status").GetString()), fixture.GetProperty("test").GetString());
            JsonElement.ArrayEnumerator issues = expected.GetProperty("Issues").EnumerateArray();
            Assert.That(actual.Issues.Count, Is.EqualTo(expected.GetProperty("Issues").GetArrayLength()));
            int index = 0;
            foreach (JsonElement issue in issues)
            {
                Assert.That(actual.Issues[index].Code, Is.EqualTo(issue.GetProperty("Code").GetString()));
                string path = issue.GetProperty("Path").GetString()!;
                string normalized = path.Length > 0 && path[0] == '/' ? path[1..] : path;
                Assert.That(actual.Issues[index].Path.ToArray(),
                    Is.EqualTo(normalized.Length == 0 ? Array.Empty<string>() : normalized.Split('/')));
                Assert.That(actual.Issues[index].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                index++;
            }
            Assert.That(actual.StatusCode, Is.EqualTo(actual.Status == "complete" ? StatusCodes.Good : StatusCodes.BadInvalidArgument));
            if (expected.TryGetProperty("MaterializedMetadata", out JsonElement metadata))
            {
                Assert.That(actual.Definition, Is.Not.Null);
                Assert.That(RegistryValues.Identical(Mapper.Restore(actual.Definition), Json(metadata.GetString()!)), Is.True,
                    Encoding.UTF8.GetString(RegistryValues.ToJson(Mapper.Restore(actual.Definition)).ToArray()));
            }
            else
            {
                Assert.That(actual.Definition, Is.Null);
                Assert.That(actual.Schema, Is.Null);
            }
            JsonElement sources = expected.GetProperty("Sources");
            Assert.That(actual.Sources.Count, Is.EqualTo(sources.GetArrayLength()));
            index = 0;
            foreach (JsonElement source in sources.EnumerateArray())
            {
                RegistryEntityReferenceDataType claim = Reference(source, resolveTransport: false);
                Assert.That(new FederationSourceKey(actual.Sources[index]), Is.EqualTo(new FederationSourceKey(claim)));
                Assert.That(actual.Sources[index].LocalNode, Is.EqualTo(claim.LocalNode));
                Assert.That(actual.Sources[index].NativeTarget,
                    Is.EqualTo(NormalizeClaim(source, fixture).NativeTarget));
                index++;
            }
            JsonElement schemaCalls = fixture.GetProperty("schemaCalls");
            Assert.That(provider.SchemaCalls, Is.EqualTo(schemaCalls.GetArrayLength()));
            if (provider.SchemaOrigin is not null)
            {
                Assert.That(new RegistryOriginKey(provider.SchemaOrigin),
                    Is.EqualTo(new RegistryOriginKey(Origin(schemaCalls[0].GetProperty("origin")))));
            }
        }

        [TestCaseSource(nameof(SelectionCases))]
        public void PythonMetadataSelectionOracle(JsonElement fixture)
        {
            void Select()
            {
                JsonElement reference = fixture.GetProperty("reference");
                JsonElement arguments = fixture.GetProperty("arguments");
                FederationTrustBinding binding = BindingFor(reference, fixture.GetProperty("bindings"),
                    Node(arguments.GetProperty("expected_type")), Node(arguments.GetProperty("expected_registry_type")));
                RegistryEntityReferenceDataType claim = Reference(reference, resolveTransport: false);
                FederationMetadataObservation observed = EvidenceFor(claim, arguments, binding, out RegistryEntityReferenceDataType portable);
                FederationReferenceSnapshot? previous = null;
                if (arguments.TryGetProperty("previous", out JsonElement old))
                {
                    previous = Previous(old);
                }
                RegistryEntityReferenceDataType selected = new FederationMetadataSelector().Select(portable, binding, observed, previous);
                JsonElement expected = fixture.GetProperty("expected");
                JsonElement snapshot = expected[0];
                Assert.That(new RegistryOriginKey(selected), Is.EqualTo(new RegistryOriginKey(Origin(snapshot.GetProperty("OriginRegistry")))));
                Assert.That(selected.Role, Is.EqualTo(snapshot.GetProperty("ReferenceRole").GetString()));
                Assert.That(selected.Xid, Is.EqualTo(snapshot.GetProperty("Xid").GetString()));
                Assert.That(selected.NativeTarget, Is.EqualTo(Node(snapshot.GetProperty("RemoteNodeId"))));
                Assert.That(binding.RegistryRoot, Is.EqualTo(Node(snapshot.GetProperty("RegistryNodeId"))));
                Assert.That(binding.ApplicationUri, Is.EqualTo(snapshot.GetProperty("ServerUri").GetString()));
                Assert.That(binding.RequiredTargetType, Is.EqualTo(Node(snapshot.GetProperty("ExpectedType"))));
                Assert.That(binding.RequiredRegistryType, Is.EqualTo(Node(snapshot.GetProperty("ExpectedRegistryType"))));
                Assert.That(selected.Locator, Is.EqualTo(snapshot.GetProperty("MetadataUrl").GetString()));
                if (expected[1].ValueKind != JsonValueKind.Null)
                {
                    NamespaceTable namespaces = NamespacesFrom(arguments.GetProperty("session").GetProperty("NamespaceUris"));
                    Assert.That(ExpandedNodeId.ToNodeId(selected.NativeTarget, namespaces),
                        Is.EqualTo(TransportNode(expected[1])));
                }
            }
            if (fixture.TryGetProperty("error", out _))
            {
                Assert.Catch<ArgumentException>(Select);
            }
            else
            {
                Select();
            }
        }

        [Test]
        public void RecorderHasSourceCommitHashAndEveryCapturedOutcomeIsReplayed()
        {
            using JsonDocument document = Load();
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("format").GetString(), Is.EqualTo("OPC30455.FederationOracle/1"));
            JsonElement provenance = root.GetProperty("provenance");
            Assert.That(provenance.GetProperty("sourceCommit").GetString(), Does.Match("^[0-9a-f]{40}$"));
            Assert.That(provenance.GetProperty("recordedSourceTests").GetInt32(), Is.EqualTo(28));
            Assert.That(provenance.GetProperty("recordedBindingTests").GetInt32(), Is.EqualTo(8));
            Assert.That(provenance.GetProperty("deterministicMutations").GetInt32(), Is.EqualTo(16));
            foreach (JsonProperty hash in provenance.GetProperty("sha256").EnumerateObject())
            {
                Assert.That(hash.Value.GetString(), Does.Match("^[0-9a-f]{64}$"));
            }
            Assert.That(root.GetProperty("resolutionCalls").GetArrayLength(), Is.EqualTo(39));
            Assert.That(root.GetProperty("selectionCalls").GetArrayLength(), Is.EqualTo(61));
        }

        private static IEnumerable<TestCaseData> Cases(string section)
        {
            using JsonDocument document = Load();
            foreach (JsonElement item in document.RootElement.GetProperty(section).EnumerateArray())
            {
                yield return new TestCaseData(item.Clone()).SetName(
                    (section == "resolutionCalls" ? "PythonResolutionOracle_" : "PythonMetadataSelectionOracle_") +
                    item.GetProperty("id").GetString());
            }
        }

        private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "oracle.json")));

        private static RegistryEntityReferenceDataType Origin(JsonElement value) => new()
        {
            OriginUri = value.GetProperty("OriginUri").GetString(),
            ApplicationUri = value.GetProperty("ServerUri").GetString(),
            RegistryNode = Node(value.GetProperty("RegistryNodeId"))
        };

        private static RegistryEntityReferenceDataType Reference(JsonElement value, bool resolveTransport)
        {
            int count = 0;
            foreach (JsonProperty _ in value.EnumerateObject())
            {
                count++;
            }
            if (count != 6 || resolveTransport)
            {
                throw new ArgumentException("Native metadata reference ABI has exactly six fields and cannot carry trust flags.");
            }
            RegistryEntityReferenceDataType reference = Origin(value.GetProperty("OriginRegistry"));
            reference.LocalNode = Node(value.GetProperty("LocalNodeId"));
            reference.Role = value.GetProperty("ReferenceRole").GetString();
            reference.Xid = value.GetProperty("Xid").GetString();
            reference.Locator = value.GetProperty("MetadataUrl").GetString();
            reference.NativeTarget = Node(value.GetProperty("ExternalReference"));
            reference.HasNativeTarget = !reference.NativeTarget.IsNull;
            return reference;
        }

        private static ExpandedNodeId Node(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return ExpandedNodeId.Null;
            }
            string? namespaceUri = value.GetProperty("nsUri").ValueKind == JsonValueKind.Null
                ? null : value.GetProperty("nsUri").GetString();
            return new ExpandedNodeId(TransportNode(value.GetProperty("node")), namespaceUri, value.GetProperty("srv").GetUInt32());
        }

        private static NodeId TransportNode(JsonElement value)
        {
            ushort ns = value.GetProperty("ns").GetUInt16();
            JsonElement id = value.GetProperty("id");
            return value.GetProperty("t").GetInt32() switch
            {
                0 => new NodeId(id.GetUInt32(), ns),
                1 => new NodeId(id.GetString()!, ns),
                2 => new NodeId(Guid.Parse(id.GetString()!), ns),
                3 => new NodeId(ByteString.From(Convert.FromBase64String(id.GetString()!)), ns),
                _ => throw new ArgumentException("Unknown NodeId discriminator.")
            };
        }

        private static ArrayOf<string> Strings(JsonElement array)
        {
            var result = new List<string>();
            foreach (JsonElement value in array.EnumerateArray())
            {
                result.Add(value.GetString()!);
            }
            return [.. result];
        }

        private static NamespaceTable NamespacesFrom(JsonElement array) => new(Strings(array).ToArray()!);

        private static FederationTrustBinding BindingFor(JsonElement reference, JsonElement bindings,
            ExpandedNodeId targetType = default, ExpandedNodeId registryType = default)
        {
            RegistryEntityReferenceDataType origin = Origin(reference.GetProperty("OriginRegistry"));
            var key = new RegistryOriginKey(origin);
            FederationTrustBinding? selected = null;
            var seen = new HashSet<RegistryOriginKey>();
            foreach (JsonElement configured in bindings.EnumerateArray())
            {
                var binding = new FederationTrustBinding(Origin(configured.GetProperty("OriginRegistry")),
                    configured.GetProperty("ServerUri").GetString()!, Node(configured.GetProperty("RegistryNodeId")),
                    Strings(configured.GetProperty("EndpointUrls")), targetType, registryType);
                if (!seen.Add(binding.Origin))
                {
                    throw new ArgumentException("Ambiguous configured trust binding.");
                }
                if (key.Equals(binding.Origin))
                {
                    selected = binding;
                }
            }
            return selected ?? throw new ArgumentException("No configured origin binding.");
        }

        private static RegistryEntityReferenceDataType NormalizeClaim(JsonElement claim, JsonElement fixture)
        {
            RegistryEntityReferenceDataType reference = Reference(claim, resolveTransport: false);
            if (!reference.HasNativeTarget)
            {
                return reference;
            }
            var source = new FederationSourceKey(reference);
            foreach (JsonElement provider in fixture.GetProperty("providers").EnumerateArray())
            {
                JsonElement key = provider.GetProperty("key");
                if (key[0].GetString() == "uri" && key[1].GetString() == source.Origin.OriginUri &&
                    key[key.GetArrayLength() - 2].GetString() == source.Role &&
                    key[key.GetArrayLength() - 1].GetString() == source.Xid)
                {
                    JsonElement tables = provider.GetProperty("record").GetProperty("SourceTables");
                    if (tables.ValueKind != JsonValueKind.Null)
                    {
                        return FederationPortableIdentity.ResolveReference(reference,
                            NamespacesFrom(tables.GetProperty("NamespaceUris")),
                            new StringTable(Strings(tables.GetProperty("ServerUris")).ToArray()!),
                            BindingFor(claim, fixture.GetProperty("bindings")));
                    }
                }
            }
            return reference;
        }

        private static FederationMetadataObservation EvidenceFor(
            RegistryEntityReferenceDataType reference, JsonElement record,
            FederationTrustBinding binding, out RegistryEntityReferenceDataType portable)
        {
            JsonElement session = record.GetProperty(record.TryGetProperty("session", out _) ? "session" : "Session");
            JsonElement tables = record.GetProperty(record.TryGetProperty("source_tables", out _) ? "source_tables" : "SourceTables");
            JsonElement http = record.GetProperty(record.TryGetProperty("provider_observation", out _) ? "provider_observation" : "ProviderObservation");
            portable = (RegistryEntityReferenceDataType)reference.Clone();
            if (binding.ApplicationUri.Length == 0)
            {
                if (session.ValueKind != JsonValueKind.Null || tables.ValueKind != JsonValueKind.Null || reference.HasNativeTarget ||
                    http.ValueKind == JsonValueKind.Null)
                {
                    throw new ArgumentException("Non-UA observation is incomplete or invents a Session.");
                }
                return new FederationMetadataObservation(Origin(http.GetProperty("OriginRegistry")),
                    http.GetProperty("EndpointUrl").GetString()!, string.Empty, null, null, ExpandedNodeId.Null,
                    http.GetProperty("Xid").GetString()!, http.GetProperty("VersionId").GetString()!,
                    http.GetProperty("HasDocument").GetBoolean(), http.GetProperty("MaxVersions").GetUInt32());
            }
            if (http.ValueKind != JsonValueKind.Null || session.ValueKind == JsonValueKind.Null || tables.ValueKind == JsonValueKind.Null)
            {
                throw new ArgumentException("UA observations require independent Session and source tables only.");
            }
            NamespaceTable sourceNamespaces = NamespacesFrom(tables.GetProperty("NamespaceUris"));
            var sourceServers = new StringTable(Strings(tables.GetProperty("ServerUris")).ToArray()!);
            portable = FederationPortableIdentity.ResolveReference(reference, sourceNamespaces, sourceServers, binding);
            NamespaceTable namespaces = NamespacesFrom(session.GetProperty("NamespaceUris"));
            JsonElement root = session.GetProperty("RegistryRoot");
            JsonElement target = session.GetProperty("Target");
            try
            {
                return new FederationMetadataObservation(reference, session.GetProperty("EndpointUrl").GetString()!,
                    session.GetProperty("ApplicationUri").GetString()!, ObservedNode(root, namespaces), ObservedNode(target, namespaces),
                    FederationPortableIdentity.FromNode(TransportNode(target.GetProperty("RegistryNodeId")), namespaces),
                    target.GetProperty("Xid").GetString()!, target.GetProperty("VersionId").GetString()!,
                    target.GetProperty("HasDocument").GetBoolean(), target.GetProperty("MaxVersions").GetUInt32());
            }
            catch (InvalidOperationException error)
            {
                throw new ArgumentException("The independent provider observation has the wrong native type.", error);
            }
        }

        private static FederationNodeObservation ObservedNode(JsonElement value, NamespaceTable namespaces)
        {
            var types = new List<ExpandedNodeId>();
            foreach (JsonElement type in value.GetProperty("TypeAncestors").EnumerateArray())
            {
                types.Add(Node(type));
            }
            string nodeClass = value.GetProperty("NodeClass").GetString()!;
            return new FederationNodeObservation(FederationPortableIdentity.FromNode(TransportNode(value.GetProperty("NodeId")), namespaces),
                nodeClass == "Object" ? NodeClass.Object : nodeClass == "Variable" ? NodeClass.Variable : NodeClass.Unspecified,
                value.GetProperty("Role").GetString()!, [.. types]);
        }

        private static FederationReferenceSnapshot Previous(JsonElement value)
        {
            string application = value.GetProperty("ServerUri").GetString()!;
            ExpandedNodeId root = Node(value.GetProperty("RegistryNodeId"));
            ExpandedNodeId target = Node(value.GetProperty("RemoteNodeId"));
            RegistryEntityReferenceDataType source = Origin(value.GetProperty("OriginRegistry"));
            source.Xid = value.GetProperty("Xid").GetString();
            source.Role = value.GetProperty("ReferenceRole").GetString();
            source.Locator = value.GetProperty("MetadataUrl").GetString();
            source.NativeTarget = target;
            source.HasNativeTarget = !target.IsNull;
            var binding = new FederationTrustBinding(source, application, root, [source.Locator!],
                Node(value.GetProperty("ExpectedType")), Node(value.GetProperty("ExpectedRegistryType")));
            var evidence = new FederationMetadataObservation(Origin(value.GetProperty("OriginRegistry")),
                value.GetProperty("MetadataUrl").GetString()!, application,
                application.Length == 0 ? null : new FederationNodeObservation(root, NodeClass.Object, "RegistryRoot",
                    [XRegistry.ObjectTypeIds.RegistryType, Node(value.GetProperty("ExpectedRegistryType"))]),
                application.Length == 0 ? null : new FederationNodeObservation(target, NodeClass.Object, "MetadataResource",
                    [XRegistry.ObjectTypeIds.MetadataResourceType, Node(value.GetProperty("ExpectedType"))]),
                root, new FederationSourceKey(source).LogicalXid, "1", false, 1);
            return new FederationReferenceSnapshot(source, binding, evidence);
        }

        private sealed class OracleProvider : IEndpointRegistryResolutionProvider
        {
            public OracleProvider(JsonElement fixture)
            {
                m_fixture = fixture;
                foreach (JsonElement provider in fixture.GetProperty("providers").EnumerateArray())
                {
                    JsonElement key = provider.GetProperty("key");
                    RegistryEntityReferenceDataType source = key[0].GetString() == "uri"
                        ? new RegistryEntityReferenceDataType { OriginUri = key[1].GetString() }
                        : throw new ArgumentException("This harvested provider key requires an explicit portable UA origin decoder.");
                    source.Role = key[key.GetArrayLength() - 2].GetString();
                    source.Xid = key[key.GetArrayLength() - 1].GetString();
                    m_records.Add(new FederationSourceKey(source), provider.GetProperty("record"));
                }
            }

            public int SchemaCalls { get; private set; }
            public RegistryEntityReferenceDataType? SchemaOrigin { get; private set; }

            public ValueTask<EndpointRegistryMessageObservation?> ReadMessageAsync(
                RegistryEntityReferenceDataType reference, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_records.TryGetValue(new FederationSourceKey(reference), out JsonElement record))
                {
                    return new ValueTask<EndpointRegistryMessageObservation?>((EndpointRegistryMessageObservation?)null);
                }
                JsonElement descriptor = FindDescriptor(reference);
                FederationTrustBinding binding = BindingFor(descriptor, m_fixture.GetProperty("bindings"));
                FederationMetadataObservation evidence = EvidenceFor(Reference(descriptor, resolveTransport: false), record, binding,
                    out RegistryEntityReferenceDataType portable);
                RegistryEntityReferenceDataType selected = new FederationMetadataSelector().Select(portable, binding, evidence);
                return new ValueTask<EndpointRegistryMessageObservation?>(new EndpointRegistryMessageObservation
                {
                    Source = selected,
                    Metadata = Json(record.GetProperty("RawMetadata").GetString()!),
                    Epoch = record.GetProperty("Epoch").GetUInt32(),
                    VersionId = evidence.VersionId
                });
            }

            public ValueTask<SchemaDocumentDataType?> ResolveSchemaAsync(
                MessageDefinitionDataType definition, RegistryEntityReferenceDataType origin, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_fixture.GetProperty("hasSchemaProvider").GetBoolean())
                {
                    return new ValueTask<SchemaDocumentDataType?>((SchemaDocumentDataType?)null);
                }
                SchemaOrigin = origin;
                JsonElement call = m_fixture.GetProperty("schemaCalls")[SchemaCalls++];
                if (call.TryGetProperty("code", out JsonElement code))
                {
                    throw new RegistryRuleException(code.GetString()!, call.GetProperty("path").GetString()!,
                        call.GetProperty("detail").GetString()!);
                }
                return new ValueTask<SchemaDocumentDataType?>(new SchemaDocumentDataType { Epoch = 1 });
            }

            private JsonElement FindDescriptor(RegistryEntityReferenceDataType source)
            {
                foreach (JsonElement descriptor in m_fixture.GetProperty("inputs").GetProperty("references").EnumerateArray())
                {
                    JsonElement claim = descriptor.GetProperty("reference");
                    if (new FederationSourceKey(Reference(claim, resolveTransport: false)).Equals(new FederationSourceKey(source)))
                    {
                        return claim;
                    }
                }
                throw new ArgumentException("No explicit descriptor exists for this provider source.");
            }

            private readonly JsonElement m_fixture;
            private readonly Dictionary<FederationSourceKey, JsonElement> m_records = [];
        }
    }
}
