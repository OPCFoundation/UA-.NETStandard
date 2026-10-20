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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.Hosting;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Registry;
using AggregationClient;
using AggregationServer;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Contains tests for linked native document converter.
    /// </summary>
    [TestFixture]
    public sealed class LinkedNativeDocumentConverterTests
    {
        /// <summary>
        /// Verifies that dependency observation exports cannot mutate captured graph or fingerprint.
        /// </summary>
        [Test]
        public void DependencyObservationExportsCannotMutateCapturedGraphOrFingerprint()
        {
            var edges = new WotDependency[]
            {
                new("urn:source", "urn:target", "urn:target", "ua:HasComponent", true)
            };
            ByteString digest = WotContentDigest.Compute("captured"u8);
            var targets = new WotDependencyTargetPin[]
            {
                new(0, null, string.Empty, "urn:target", default, digest)
            };
            WotDependencyTargetPin inputTarget = targets[0];
            var observation = new WotDependencySnapshot(
                "v1", 3, "capture", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                true, digest, edges.ToArrayOf(), targets.ToArrayOf());
            MutateDigest(inputTarget.ContentDigest);
            edges[0] = new WotDependency("urn:changed", "urn:changed", null, "changed", false);
            targets[0] = new WotDependencyTargetPin(
                0, null, string.Empty, "urn:changed", default, WotContentDigest.Compute("changed"u8));
            ArrayOf<WotDependency> exportedEdges = observation.Edges;
            Assert.That(MemoryMarshal.TryGetArray(exportedEdges.Memory, out ArraySegment<WotDependency> edgeArray),
                Is.True);
            edgeArray.Array![edgeArray.Offset] = edges[0];
            ArrayOf<WotDependencyTargetPin> exportedTargets = observation.Targets;
            Assert.That(MemoryMarshal.TryGetArray(exportedTargets.Memory,
                out ArraySegment<WotDependencyTargetPin> targetArray), Is.True);
            MutateDigest(exportedTargets[0].ContentDigest);
            targetArray.Array![targetArray.Offset] = targets[0];
            MutateDigest(observation.EffectiveInputDigest);
            WoTDependencySnapshotDataType wire = observation.ToDataType();
            wire.Edges[0].SourceXid = "urn:wire-changed";
            wire.Targets[0].DocumentUri = "urn:wire-changed";
            MutateDigest(wire.Targets[0].ContentDigest);
            MutateDigest(wire.EffectiveInputDigest);
            Assert.Multiple(() =>
            {
                Assert.That(observation.Edges[0].SourceXid, Is.EqualTo("urn:source"));
                Assert.That(observation.Targets[0].DocumentUri, Is.EqualTo("urn:target"));
                Assert.That(observation.Targets[0].ContentDigest, Is.EqualTo(digest));
                Assert.That(observation.EffectiveInputDigest, Is.EqualTo(digest));
                Assert.That(observation.ToDataType().Edges[0].SourceXid, Is.EqualTo("urn:source"));
                Assert.That(observation.ToDataType().Targets[0].ContentDigest, Is.EqualTo(digest));
            });
        }

        private static void MutateDigest(ByteString digest)
        {
            Assert.That(MemoryMarshal.TryGetArray(digest.Memory, out ArraySegment<byte> array), Is.True);
            array.Array![array.Offset] ^= 0xff;
        }

        /// <summary>
        /// Verifies that published manifest dependency metadata can be prepared without native conversion.
        /// </summary>
        /// <param name="ownerCount">The owner count used by this test case.</param>
        [TestCase(8)]
        [TestCase(533)]
        public async Task PublishedManifestDependencyMetadataCanBePreparedWithoutNativeConversion(int ownerCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var memoryStore = new InMemoryWotRegistryStore();
            using var registry = new WotRegistryService(memoryStore);
            string directory = FindDocuments();
            using JsonDocument manifest = JsonDocument.Parse(
                await File.ReadAllBytesAsync(Path.Combine(directory, "documents.json"), timeout.Token)
                    .ConfigureAwait(false));
            var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("resourceId").GetString()!;
                byte[] content = AggregationClientRunner.EnsureDocumentIdentity(
                    await File.ReadAllBytesAsync(
                        Path.Combine(directory, entry.GetProperty("path").GetString()!), timeout.Token)
                        .ConfigureAwait(false), name);
                documents.Add(name, content);
                using JsonDocument document = JsonDocument.Parse(content);
                identities.Add(name, document.RootElement.GetProperty("id").GetString()!);
            }
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("resourceId").GetString()!;
                WoTDocumentKindEnum kind = entry.GetProperty("documentKind").GetString() == "ThingModel"
                    ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription;
                WotRegistryMutationResult added = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                    ResourceId = name,
                    Kind = kind,
                    DetectProjectionFormat = true,
                    Content = ByteString.From(
                        AggregationClientRunner.RewriteDocumentReferences(documents[name], identities))
                }, timeout.Token).ConfigureAwait(false);
                Assert.That(added.Resource, Is.Not.Null, added.Message);
            }
            WotRegistrySnapshot snapshot = registry.Current;
            var resources = snapshot.AllResources().ToArray();
            var closures = await WotDependencyGraph.BuildClosuresAsync(
                snapshot, resources, 128, registry.ReadContentAsync, timeout.Token).ConfigureAwait(false);
            Assert.That(closures, Has.Length.EqualTo(1));
            var closure = closures[0];
            TestContext.Out.WriteLine("Resources={0}; Edges={1}; Resolved={2}",
                resources.Length, closure.Dependencies.Length, closure.Dependencies.Count(edge => edge.Resolved));
            ArrayOf<WotDependency> edges = closure.Dependencies.ToArrayOf();
            var pins = new List<WotDependencyTargetPin>();
            for (int index = 0; index < edges.Count; index++)
            {
                WotDependency edge = edges[index];
                if (edge.Resolved)
                {
                    WotResource target = snapshot.FindResourceByXid(edge.TargetXid!)!;
                    pins.Add(new WotDependencyTargetPin(
                        (uint)index, null, string.Empty, edge.TargetHref, default, target.DefaultVersion!.Digest));
                }
            }
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "metadata-probe-" +
                Guid.NewGuid().ToString("N"));
            try
            {
                using var contentStore = new RecordingLeasedResourceStore();
                using var fileStore = new FileWotRegistryStore(root, contentStore);
                await fileStore.LoadAsync(timeout.Token).ConfigureAwait(false);
                foreach (WotResource resource in resources)
                {
                    WotResourceVersion version = resource.DefaultVersion!;
                    await fileStore.ResourceStore.WriteAsync(
                        version.DigestHex, 0, await registry.ReadContentAsync(version, timeout.Token)
                            .ConfigureAwait(false), timeout.Token).ConfigureAwait(false);
                }
                await fileStore.CommitAsync(snapshot, timeout.Token).ConfigureAwait(false);
                using var persisted = new WotRegistryService(fileStore);
                await persisted.InitializeAsync(timeout.Token).ConfigureAwait(false);
                var projections = new List<WotResourceProjection>();
                foreach (WotResource resource in resources.Take(ownerCount))
                {
                    var observation = new WotDependencySnapshot(
                        resource.DefaultVersionId!, 1, "metadata-probe-" + resource.ResourceId, DateTime.UtcNow, true,
                        WotContentDigest.Compute("metadata-probe"u8), edges, pins.ToArrayOf());
                    var attempt = new WotDependencySnapshot(
                        resource.DefaultVersionId!, 1, "metadata-attempt-" + resource.ResourceId, DateTime.UtcNow, false,
                        WotContentDigest.Compute("metadata-probe"u8), edges, pins.ToArrayOf());
                    projections.Add(new WotResourceProjection(
                        resource.GroupId, resource.ResourceId, WoTLoadStateEnum.Active,
                        resource.DefaultVersionId, 1, 0, default, null, [], DateTime.UtcNow)
                    {
                        DependencySnapshot = observation,
                        LastDependencyAttempt = attempt
                    });
                }
                long before = GC.GetTotalAllocatedBytes(precise: true);
                IWotPreparedRegistryPublication publication = await persisted.PreparePublicationAsync(
                    persisted.Current, projections.ToArrayOf(), 1, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                await using (publication.ConfigureAwait(false))
                {
                    TestContext.Out.WriteLine("Preparation allocated {0} bytes",
                        GC.GetTotalAllocatedBytes(precise: true) - before);
                    Assert.That(publication.IntendedSnapshot.AllResources().Count(resource =>
                        resource.DefaultVersion!.DependencySnapshot is not null), Is.EqualTo(ownerCount));
                    await publication.DecideAsync(timeout.Token).ConfigureAwait(false);
                    publication.Publish();
                }
                using JsonDocument stored = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json"), timeout.Token)
                        .ConfigureAwait(false));
                Assert.That(stored.RootElement.GetProperty("DependencyGraphs").GetArrayLength(), Is.EqualTo(1));
                using var reloadedStore = new FileWotRegistryStore(root, contentStore);
                WotRegistrySnapshot reloaded = await reloadedStore.LoadAsync(timeout.Token).ConfigureAwait(false);
                foreach (WotResource source in resources.Take(ownerCount))
                {
                    WotDependencySnapshot restored = reloaded.FindResource(source.GroupId, source.ResourceId)!
                        .DefaultVersion!.DependencySnapshot!;
                    WotDependencySnapshot attempted = reloaded.FindResource(source.GroupId, source.ResourceId)!
                        .DefaultVersion!.LastDependencyAttempt!;
                    Assert.Multiple(() =>
                    {
                        Assert.That(restored.SourceVersionId, Is.EqualTo(source.DefaultVersionId));
                        Assert.That(restored.RequestId, Is.EqualTo("metadata-probe-" + source.ResourceId));
                        Assert.That(restored.Generation, Is.EqualTo(1));
                        Assert.That(restored.IsCommitted, Is.True);
                        Assert.That(attempted.IsCommitted, Is.False);
                        Assert.That(attempted.SourceVersionId, Is.EqualTo(source.DefaultVersionId));
                        Assert.That(attempted.RequestId, Is.EqualTo("metadata-attempt-" + source.ResourceId));
                        Assert.That(attempted.Generation, Is.EqualTo(1));
                        Assert.That(attempted.Edges.Count, Is.EqualTo(edges.Count));
                        Assert.That(attempted.Targets.Count, Is.EqualTo(pins.Count));
                        Assert.That(restored.Edges.Count, Is.EqualTo(edges.Count));
                        Assert.That(restored.Targets.Count, Is.EqualTo(pins.Count));
                        Assert.That(restored.Edges.ToList().Zip(edges.ToList(), (a, b) =>
                            a.SourceXid == b.SourceXid && a.TargetHref == b.TargetHref &&
                            a.TargetXid == b.TargetXid && a.RefType == b.RefType && a.Resolved == b.Resolved).All(
                            equal => equal), Is.True);
                        Assert.That(restored.Targets.ToList().Zip(pins, (a, b) =>
                            a.EdgeIndex == b.EdgeIndex && a.DocumentUri == b.DocumentUri &&
                            a.VersionXid == b.VersionXid && a.VersionNodeId == b.VersionNodeId &&
                            a.ContentDigest == b.ContentDigest && a.OriginRegistry is null).All(
                            equal => equal), Is.True);
                    });
                }
                WotDependencySnapshot firstRestored = reloaded.FindResource(
                    resources[0].GroupId, resources[0].ResourceId)!.DefaultVersion!.DependencySnapshot!;
                WotResource lastOwner = resources[ownerCount - 1];
                WotDependencySnapshot lastRestored = reloaded.FindResource(
                    lastOwner.GroupId, lastOwner.ResourceId)!.DefaultVersion!.DependencySnapshot!;
                Assert.That(MemoryMarshal.TryGetArray(firstRestored.Edges.Memory,
                    out ArraySegment<WotDependency> restoredEdges), Is.True);
                restoredEdges.Array![restoredEdges.Offset] = new WotDependency(
                    "urn:export-changed", "urn:export-changed", null, "changed", false);
                MutateDigest(firstRestored.Targets[0].ContentDigest);
                WoTDependencySnapshotDataType firstWire = firstRestored.ToDataType();
                firstWire.Edges[0].SourceXid = "urn:wire-changed";
                MutateDigest(firstWire.Targets[0].ContentDigest);
                Assert.Multiple(() =>
                {
                    Assert.That(firstRestored.Edges[0].SourceXid, Is.EqualTo(edges[0].SourceXid));
                    Assert.That(lastRestored.Edges[0].SourceXid, Is.EqualTo(edges[0].SourceXid));
                    Assert.That(firstRestored.Targets[0].ContentDigest, Is.EqualTo(pins[0].ContentDigest));
                    Assert.That(lastRestored.Targets[0].ContentDigest, Is.EqualTo(pins[0].ContentDigest));
                    Assert.That(firstRestored.RequestId, Is.EqualTo("metadata-probe-" + resources[0].ResourceId));
                    Assert.That(lastRestored.RequestId, Is.EqualTo("metadata-probe-" + lastOwner.ResourceId));
                });
                WotResource selected = resources[0];
                (WotResource Resource, WotResourceVersion Version)? replacement =
                    await persisted.TryCreateVersionAsync(
                        selected.GroupId, selected.ResourceId, string.Empty, selected.Kind, timeout.Token)
                        .ConfigureAwait(false);
                Assert.That(replacement.HasValue, Is.True);
                Assert.That(replacement!.Value.Version.VersionId, Is.Not.EqualTo(selected.DefaultVersionId));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that published pumps set retains its complete native graph.
        /// </summary>
        [Test]
        public async Task PublishedPumpsSetRetainsItsCompleteNativeGraph()
        {
            string directory = FindDocuments();
            using JsonDocument manifest = JsonDocument.Parse(
                await File.ReadAllBytesAsync(Path.Combine(directory, "documents.json")).ConfigureAwait(false));
            var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("resourceId").GetString()!;
                string path = entry.GetProperty("path").GetString()!;
                if (!path.StartsWith("opc-ua-pumps/", StringComparison.Ordinal))
                {
                    continue;
                }
                byte[] content = AggregationClientRunner.EnsureDocumentIdentity(
                    await File.ReadAllBytesAsync(Path.Combine(directory, path)).ConfigureAwait(false), name);
                documents.Add(name, content);
                using JsonDocument document = JsonDocument.Parse(content);
                identities.Add(name, document.RootElement.GetProperty("id").GetString()!);
            }
            var options = new WotNodeSetConverterOptions { MaxResolverDocuments = 1024 };
            var entries = new List<WotDocumentSetEntry>();
            try
            {
                foreach (KeyValuePair<string, byte[]> document in documents)
                {
                    entries.Add(new WotDocumentSetEntry(identities[document.Key], WotDocument.Parse(
                        AggregationClientRunner.RewriteDocumentReferences(document.Value, identities), options)));
                }
                using var set = new WotDocumentSet(identities["opc-ua-pumps"], entries.ToArrayOf());
                entries.Clear();
                WotConversionResult<Opc.Ua.Export.UANodeSet> result = await WotNodeSetConverter.ToNodeSetAsync(
                    set, options).ConfigureAwait(false);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
                Assert.That(result.Value!.Items, Has.Length.EqualTo(9624));
            }
            finally
            {
                foreach (WotDocumentSetEntry entry in entries)
                {
                    entry.Dispose();
                }
            }
        }

        /// <summary>
        /// Verifies that sibling declarations are validated without duplicating their native owner.
        /// </summary>
        /// <param name="missingSibling">The missing-sibling flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SiblingDeclarationsAreValidatedWithoutDuplicatingTheirNativeOwner(bool missingSibling)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            WotResource? source = null;
            ByteString sourceContent = default;
            foreach (string name in s_fragments)
            {
                if (missingSibling && name.EndsWith("-pendingversion", StringComparison.Ordinal))
                {
                    continue;
                }
                string path = Path.Combine(FindDocuments(), "opc-ua-di", name + ".json");
                byte[] bytes = AggregationClientRunner.EnsureDocumentIdentity(File.ReadAllBytes(path), name);
                ByteString content = ByteString.From(bytes);
                var identities = s_fragments.ToDictionary(
                    alias => alias,
                    alias => "urn:opcfoundation.org:UA:WotAggregation:document:" + alias,
                    StringComparer.Ordinal);
                content = ByteString.From(AggregationClientRunner.RewriteDocumentReferences(bytes, identities));
                WotRegistryMutationResult added = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = name == s_fragments[0] ? WotRegistryGroups.ThingModels :
                        WotRegistryGroups.ThingDescriptions,
                    ResourceId = name,
                    Kind = name == s_fragments[0] ? WoTDocumentKindEnum.ThingModel :
                        WoTDocumentKindEnum.ThingDescription,
                    Content = content
                }).ConfigureAwait(false);
                Assert.That(added.Resource, Is.Not.Null, added.Message);
                contents.Add(WotContentDigest.ToHex(WotContentDigest.Compute(content)), content);
                if (name == s_fragments[0])
                {
                    source = added.Resource;
                    sourceContent = content;
                }
            }

            WotConversionOutput result = await converter.ConvertAsync(
                source!, sourceContent, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            if (missingSibling)
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.NodeSet, Is.Null);
                Assert.That(string.Join("; ", result.Errors), Does.Contain("uav:declaration"));
            }
            else
            {
                Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
                string[] identities = result.NodeSet!.Items!.Select(node => node.NodeId!).ToArray();
                Assert.That(identities, Does.Contain("ns=1;i=171"));
                Assert.That(identities, Does.Not.Contain("ns=1;i=187").And.Not.Contain("ns=1;i=188"));
                Assert.That(result.ProjectedAffordances.Count, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Verifies that exact target must be present even when document identity matches.
        /// </summary>
        /// <param name="sameIdentity">The same-identity flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExactTargetMustBePresentEvenWhenDocumentIdentityMatches(bool sameIdentity)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            ByteString target = contents[s_fragments[0]];
            if (sameIdentity)
            {
                JsonNode changed = JsonNode.Parse(target.Memory.Span)!;
                changed["title"] = "Not the captured carrier";
                contents[s_fragments[0]] = ByteString.From(Encoding.UTF8.GetBytes(changed.ToJsonString()));
            }
            else
            {
                contents.Remove(s_fragments[0]);
            }
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);

            WotConversionOutput result = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.NodeSet, Is.Null);
                Assert.That(result.Errors, Has.Length.EqualTo(1));
                Assert.That(result.Errors[0], Is.EqualTo(
                        "The exact target document content is missing or mismatched in the captured image."));
            });
        }

        /// <summary>
        /// Verifies that changed sibling bytes invalidate validation under unchanged keys and snapshot.
        /// </summary>
        [Test]
        public async Task ChangedSiblingBytesInvalidateValidationUnderUnchangedKeysAndSnapshot()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            ByteString target = contents[s_fragments[0]];
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            WotRegistrySnapshot snapshot = registry.Current;
            WotConversionOutput first = await converter.ConvertAsync(
                resource, target, snapshot, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(first, "ns=1;i=171", 1);

            JsonNode changed = JsonNode.Parse(contents[s_fragments[1]].Span)!;
            changed["uav:browseName"] = "ns1:Corrupted";
            contents[s_fragments[1]] = ByteString.From(Encoding.UTF8.GetBytes(changed.ToJsonString()));
            WotConversionOutput second = await converter.ConvertAsync(
                resource, target, snapshot, contents, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(second.Succeeded, Is.False);
                Assert.That(second.NodeSet, Is.Null);
                Assert.That(string.Join("; ", second.Errors), Does.Contain("uav:browseName"));
            });
        }

        /// <summary>
        /// Verifies that changed target bytes replace the captured carrier under unchanged keys and snapshot.
        /// </summary>
        [Test]
        public async Task ChangedTargetBytesReplaceTheCapturedCarrierUnderUnchangedKeysAndSnapshot()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            ByteString target = contents[s_fragments[0]];
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            WotConversionOutput first = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(first, "ns=1;i=171", 1);

            target = ByteString.From(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(target.Span) + "\n"));
            contents[s_fragments[0]] = target;
            WotConversionOutput second = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            AssertNativeOwner(second, "ns=1;i=171", 1);
        }

        /// <summary>
        /// Verifies that malformed target is an explicit failure and does not prevent recovery.
        /// </summary>
        [TestCase("{")]
        [TestCase("""{"uav:nodes":{}}""")]
        public async Task MalformedTargetIsAnExplicitFailureAndDoesNotPreventRecovery(string invalid)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            ByteString target = contents[s_fragments[0]];
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            ByteString malformed = ByteString.From(Encoding.UTF8.GetBytes(invalid));
            contents["malformed-target"] = malformed;

            WotConversionOutput failed = await converter.ConvertAsync(
                resource, malformed, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(failed.Succeeded, Is.False);
                Assert.That(failed.NodeSet, Is.Null);
                Assert.That(failed.Errors, Has.Length.EqualTo(1));
                Assert.That(failed.Errors[0], Is.Not.Empty);
            });
            WotConversionOutput recovered = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(recovered, "ns=1;i=171", 1);
        }

        /// <summary>
        /// Verifies that malformed sibling supplies no proof but does not poison unrelated conversions.
        /// </summary>
        [TestCase("{")]
        [TestCase("[]")]
        public async Task MalformedSiblingSuppliesNoProofButDoesNotPoisonUnrelatedConversions(string invalid)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            ByteString target = contents[s_fragments[0]];
            ByteString sibling = contents[s_fragments[1]];
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            contents["unrelated-invalid"] = ByteString.From(Encoding.UTF8.GetBytes(invalid));
            WotConversionOutput unrelated = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(unrelated, "ns=1;i=171", 1);

            contents[s_fragments[1]] = ByteString.From("{"u8);
            WotConversionOutput dependent = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(dependent.Succeeded, Is.False);
                Assert.That(dependent.NodeSet, Is.Null);
                Assert.That(string.Join("; ", dependent.Errors), Does.Contain("uav:declaration"));
            });

            contents[s_fragments[1]] = sibling;
            WotConversionOutput recovered = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(recovered, "ns=1;i=171", 1);
        }

        /// <summary>
        /// Verifies that multiple independent roots retain every partition regardless of input order.
        /// </summary>
        /// <param name="reverse">The reverse flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MultipleIndependentRootsRetainEveryPartitionRegardlessOfInputOrder(bool reverse)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            contents.Add("opc-ua-di-connectsto", ReadFragment("opc-ua-di-connectsto"));
            contents.Add("opc-ua-di-connectstoparent", ReadFragment("opc-ua-di-connectstoparent"));
            if (reverse)
            {
                contents = contents.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value,
                    StringComparer.Ordinal);
            }
            ByteString target = contents[s_fragments[0]];
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            WotConversionOutput first = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);
            AssertNativeOwner(first, "ns=1;i=171", 1);
            Assert.That(first.NodeSet!.Items!.Select(node => node.NodeId), Does.Not.Contain("ns=1;i=6030"));

            WotConversionOutput second = await converter.ConvertAsync(
                resource, contents["opc-ua-di-connectsto"], registry.Current, contents, CancellationToken.None)
                .ConfigureAwait(false);
            AssertNativeOwner(second, "ns=1;i=6030", 0);
            Assert.That(second.NodeSet!.Items!.Select(node => node.NodeId), Does.Not.Contain("ns=1;i=171"));
        }

        /// <summary>
        /// Verifies that a set without a parent free root fails explicitly.
        /// </summary>
        [Test]
        public async Task ASetWithoutAParentFreeRootFailsExplicitly()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            Dictionary<string, ByteString> contents = ReadFragments();
            JsonNode changed = JsonNode.Parse(contents[s_fragments[0]].Span)!;
            changed["links"]!.AsArray().Add(new JsonObject
            {
                ["href"] = "urn:opcfoundation.org:UA:WotAggregation:document:" + s_fragments[1],
                ["rel"] = "uav:componentOf"
            });
            ByteString target = ByteString.From(Encoding.UTF8.GetBytes(changed.ToJsonString()));
            contents[s_fragments[0]] = target;
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);

            WotConversionOutput result = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.NodeSet, Is.Null);
                Assert.That(result.Errors, Has.Length.EqualTo(1));
                Assert.That(result.Errors[0], Is.EqualTo("The linked native document set has no parent-free root."));
            });
        }

        /// <summary>
        /// Verifies that host shares startup converter and repeated disposal is safe.
        /// </summary>
        [Test]
        public void HostSharesStartupConverterAndRepeatedDisposalIsSafe()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            IServerStartupTask[] tasks = host.Services.GetServices<IServerStartupTask>().ToArray();

            Assert.That(tasks.Count(task => ReferenceEquals(task, converter)), Is.EqualTo(1));
            IDisposable disposable = (IDisposable)converter;
            Assert.DoesNotThrow(() =>
            {
                disposable.Dispose();
                disposable.Dispose();
                host.Dispose();
            });
        }

        /// <summary>
        /// Verifies that fallback preserves per conversion declaration ownership and filters native summaries.
        /// </summary>
        /// <param name="projectedSeparately">The projected-separately flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FallbackPreservesPerConversionDeclarationOwnershipAndFiltersNativeSummaries(
            bool projectedSeparately)
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            ByteString target = ByteString.From(Encoding.UTF8.GetBytes(s_readableConsumer));
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            using WotDocument readable = WotDocument.Parse(Encoding.UTF8.GetBytes(s_readableDefinition));
            using WotDocument native = WotDocument.Parse(ReadFragment("opc-ua-di-devicehealthenumeration").Memory);
            var definitions = new[]
            {
                    new WotDataTypeDefinitionSource(readable,
                        readable.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
                    {
                        ProjectedSeparately = projectedSeparately
                    },
                    new WotDataTypeDefinitionSource(native,
                        native.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
                };
            var contents = new ConversionContents(new Dictionary<string, ByteString> { ["target"] = target },
                definitions.ToArrayOf());

            WotConversionOutput result = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
                Assert.That(contents.DefinitionRequests, Is.EqualTo(1));
                Assert.That(result.ProjectedAffordances.Count, Is.EqualTo(1));
                Assert.That(result.NodeSet!.Items!.OfType<UADataType>().Count(),
                    Is.EqualTo(projectedSeparately ? 0 : 1));
                Assert.That(native.RootElement.GetProperty("title").GetString(), Is.EqualTo("DeviceHealthEnumeration"));
                Assert.That(readable.RootElement.GetProperty("id").GetString(), Is.EqualTo("urn:test:enum-owner"));
            });
            if (!projectedSeparately)
            {
                UADataType emitted = result.NodeSet!.Items!.OfType<UADataType>().Single();
                Assert.That(emitted.NodeId, Is.EqualTo("ns=1;i=5000"));
                Assert.That(emitted.Definition!.Field!.Single().Name, Is.EqualTo("Ready"));
            }

            definitions[0] = new WotDataTypeDefinitionSource(readable,
                readable.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
            {
                ProjectedSeparately = !projectedSeparately
            };
            var nextContents = new ConversionContents(contents, definitions.ToArrayOf());
            WotConversionOutput next = await converter.ConvertAsync(
                resource, target, registry.Current, nextContents, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(next.Succeeded, Is.True, string.Join("; ", next.Errors));
                Assert.That(nextContents.DefinitionRequests, Is.EqualTo(1));
                Assert.That(next.NodeSet!.Items!.OfType<UADataType>().Count(),
                    Is.EqualTo(projectedSeparately ? 1 : 0));
            });
        }

        /// <summary>
        /// Verifies that fallback does not conceal missing native predecessors.
        /// </summary>
        [Test]
        public async Task FallbackDoesNotConcealMissingNativePredecessors()
        {
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions());
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            ByteString target = ByteString.From(Encoding.UTF8.GetBytes(s_readableConsumer));
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            JsonNode definition = JsonNode.Parse(s_readableDefinition)!;
            definition["uav:dataTypeDefinitions"]![0]!["uav:dataTypeSubtypeOf"]!["uav:dataTypeId"] =
                "nsu=http://opcfoundation.org/UA/DI/;i=6244";
            using WotDocument readable = WotDocument.Parse(Encoding.UTF8.GetBytes(definition.ToJsonString()));
            using WotDocument native = WotDocument.Parse(ReadFragment("opc-ua-di-devicehealthenumeration").Memory);
            var contents = new ConversionContents(new Dictionary<string, ByteString> { ["target"] = target },
                new WotDataTypeDefinitionSource[]
                {
                    new(readable, readable.RootElement.GetProperty("uav:dataTypeDefinitions")[0]),
                    new(native, native.RootElement.GetProperty("uav:dataTypeDefinitions")[0])
                }.ToArrayOf());

            WotConversionOutput result = await converter.ConvertAsync(
                resource, target, registry.Current, contents, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.NodeSet, Is.Null);
                Assert.That(string.Join("; ", result.Errors), Does.Contain("6244"));
                Assert.That(contents.DefinitionRequests, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Verifies that hosted fallback uses loaded address space after startup.
        /// </summary>
        [Test]
        public async Task HostedFallbackUsesLoadedAddressSpaceAfterStartupAsync()
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "linked-startup-" + Guid.NewGuid().ToString("N"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using IHost host = AggregationServerHost.Build(new AggregationServerOptions
            {
                EndpointUrl = $"opc.tcp://127.0.0.1:{TestPorts.GetFreePorts(1)[0]}/LinkedStartup",
                PkiRoot = Path.Combine(root, "pki"),
                StorageFolder = Path.Combine(root, "registry")
            });
            IWotDocumentConverter converter = host.Services.GetRequiredService<IWotDocumentConverter>();
            using var registry = new WotRegistryService();
            JsonNode document = JsonNode.Parse("""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1",
                    {"ua": "http://opcfoundation.org/UA/", "ex": "urn:test:linked:",
                     "uav": "http://opcfoundation.org/UA/WoT-Binding/"}],
                  "@type": "Thing", "id": "urn:test:loaded-asset", "title": "LoadedAsset",
                  "uav:id": "nsu=urn:test:linked:;i=1", "uav:browseName": "ex:LoadedFolder"
                }
                """)!;
            document["links"] = new JsonArray(new JsonObject
            {
                ["href"] = Opc.Ua.WotCon.ObjectTypeIds.WoTRegistryType.ToString(),
                ["rel"] = "ua:HasTypeDefinition"
            });
            ByteString target = ByteString.From(Encoding.UTF8.GetBytes(document.ToJsonString()));
            WotResource resource = await AddTargetAsync(registry, target).ConfigureAwait(false);
            var contents = new Dictionary<string, ByteString> { ["target"] = target };
            WotConversionOutput before = await converter.ConvertAsync(
                resource, target, registry.Current, contents, timeout.Token).ConfigureAwait(false);
            Assert.That(before.Succeeded, Is.False, "The unloaded local context must not guess WoTRegistryType.");

            try
            {
                await host.StartAsync(timeout.Token).ConfigureAwait(false);
                WotConversionOutput after = before;
                for (int attempt = 0; attempt < 200 && !after.Succeeded; attempt++)
                {
                    after = await converter.ConvertAsync(
                        resource, target, registry.Current, contents, timeout.Token).ConfigureAwait(false);
                    if (!after.Succeeded)
                    {
                        await Task.Delay(50, timeout.Token).ConfigureAwait(false);
                    }
                }
                Assert.That(after.Succeeded, Is.True, string.Join("; ", after.Errors));

                UAObject projected = after.NodeSet!.Items!.OfType<UAObject>().Single();
                var namespaceUris = new NamespaceTable();
                foreach (string uri in after.NodeSet.NamespaceUris!)
                {
                    namespaceUris.Append(uri);
                }
                Assert.Multiple(() =>
                {
                    Assert.That(projected.NodeId, Is.EqualTo("ns=1;i=1"));
                    Assert.That(projected.References!.Single(reference =>
                        reference.ReferenceType is "i=40" or "HasTypeDefinition").Value,
                        Is.EqualTo(ExpandedNodeId.ToNodeId(
                            Opc.Ua.WotCon.ObjectTypeIds.WoTRegistryType, namespaceUris).ToString()));
                    Assert.That(after.ProjectedAffordances.Count, Is.Zero);
                });
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                host.Dispose();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static Dictionary<string, ByteString> ReadFragments()
        {
            return s_fragments.ToDictionary(name => name, ReadFragment, StringComparer.Ordinal);
        }

        private static ByteString ReadFragment(string name)
        {
            byte[] bytes = AggregationClientRunner.EnsureDocumentIdentity(
                File.ReadAllBytes(Path.Combine(FindDocuments(), "opc-ua-di", name + ".json")), name);
            Dictionary<string, string> identities = s_fragments.ToDictionary(
                alias => alias,
                alias => "urn:opcfoundation.org:UA:WotAggregation:document:" + alias,
                StringComparer.Ordinal);
            return ByteString.From(AggregationClientRunner.RewriteDocumentReferences(bytes, identities));
        }

        private static async Task<WotResource> AddTargetAsync(WotRegistryService registry, ByteString target)
        {
            using WotDocument document = WotDocument.Parse(target.Memory);
            bool model = document.Kind == WotDocumentKind.ThingModel;
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = model ? WotRegistryGroups.ThingModels : WotRegistryGroups.ThingDescriptions,
                ResourceId = "target",
                Kind = model ? WoTDocumentKindEnum.ThingModel : WoTDocumentKindEnum.ThingDescription,
                Content = target
            }).ConfigureAwait(false);
            Assert.That(result.Resource, Is.Not.Null, result.Message);
            return result.Resource!;
        }

        private static void AssertNativeOwner(WotConversionOutput result, string root, int affordances)
        {
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            Assert.Multiple(() =>
            {
                Assert.That(result.NodeSet!.Items!.Select(node => node.NodeId), Does.Contain(root));
                Assert.That(result.NodeSet!.Items!.Select(node => node.NodeId),
                    Does.Not.Contain("ns=1;i=187").And.Not.Contain("ns=1;i=188"));
                Assert.That(result.ProjectedAffordances.Count, Is.EqualTo(affordances));
            });
        }

        private sealed class ConversionContents(
            IReadOnlyDictionary<string, ByteString> contents,
            ArrayOf<WotDataTypeDefinitionSource> definitions)
            : IReadOnlyDictionary<string, ByteString>, IWotDocumentConversionContext
        {
            public ByteString this[string key] => contents[key];

            /// <summary>
            /// Gets the keys for <c>ConversionContents</c>.
            /// </summary>
            public IEnumerable<string> Keys => contents.Keys;
            public IEnumerable<ByteString> Values => contents.Values;

            /// <summary>
            /// Gets the count for <c>ConversionContents</c>.
            /// </summary>
            public int Count => contents.Count;

            /// <summary>
            /// Gets or sets the definition requests for <c>ConversionContents</c>.
            /// </summary>
            public int DefinitionRequests { get; private set; }

            public ArrayOf<WotDataTypeDefinitionSource> GetDataTypeDefinitions(
                WotNodeSetConverterOptions options,
                CancellationToken cancellationToken = default)
            {
                DefinitionRequests++;
                return definitions;
            }

            public bool ContainsKey(string key)
            {
                return contents.ContainsKey(key);
            }

            /// <inheritdoc/>
            /// <param name="value">The byte string.</param>
            public bool TryGetValue(string key, out ByteString value)
            {
                return contents.TryGetValue(key, out value);
            }

            public IEnumerator<KeyValuePair<string, ByteString>> GetEnumerator()
            {
                return contents.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private static string FindDocuments()
        {
            return WotSampleEnvironment.FindDocumentsDirectory();
        }

        private static readonly string[] s_fragments =
        [
            "opc-ua-di-cachedloadingtype",
            "opc-ua-di-cachedloadingtype-pendingversion",
            "opc-ua-di-cachedloadingtype-fallbackversion"
        ];

        private const string s_readableConsumer = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1",
                {"ex": "urn:test:linked:", "uav": "http://opcfoundation.org/UA/WoT-Binding/"}],
              "@type": "Thing", "id": "urn:test:consumer", "title": "Consumer",
              "uav:id": "nsu=urn:test:linked:;i=1", "uav:browseName": "ex:Consumer",
              "properties": {
                "State": {"type": "integer", "uav:id": "nsu=urn:test:linked:;i=2",
                  "uav:browseName": "ex:State", "uav:mapToType": "nsu=urn:test:linked:;i=5000"}
              }
            }
            """;

        private const string s_readableDefinition = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1",
                {"ex": "urn:test:linked:", "uav": "http://opcfoundation.org/UA/WoT-Binding/"}],
              "@type": "tm:ThingModel", "id": "urn:test:enum-owner",
              "uav:dataTypeDefinitions": [{
                "@id": "nsu=urn:test:linked:;i=5000",
                "@type": "uav:EnumDefinition", "uav:dataTypeId": "nsu=urn:test:linked:;i=5000",
                "uav:dataTypeName": "ex:State", "uav:dataTypeSubtypeOf": {"uav:dataTypeId": "i=29"},
                "uav:enumFields": [{"@type": "uav:EnumField", "uav:enumName": "Ready", "uav:enumValue": 0}]
              }]
            }
            """;
    }
}
