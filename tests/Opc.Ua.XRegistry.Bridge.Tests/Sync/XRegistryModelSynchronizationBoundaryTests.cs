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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    /// <summary>
    /// Exercises guarded model planning, replay evidence and retained scope through the synchronizer API.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryModelSynchronizationBoundaryTests
    {
        /// <summary>
        /// An edit after the final inventory read is caught by model preflight before any mutation is dispatched.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ModelSourceChangedAfterInventoryBlocksDispatchAsync(bool changeDestination)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            SyncEndpoint changed = changeDestination ? fixture.Http : fixture.Native;
            bool injected = false;
            changed.AfterExecuteAsync = async (request, _, _) =>
            {
                if (!request.IsMutation && request.Path == "/" && changed.Inspections == 2)
                {
                    changed.AfterExecuteAsync = null;
                    injected = true;
                    await changed.ChangeAsync("/modelsource", k_laterExtension, XRegistryAction.Merge)
                        .ConfigureAwait(false);
                }
            };

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistryResponse current = await changed.ReadAsync("/modelsource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(injected, Is.True);
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.Applied, Is.Zero);
                Assert.That(conflicts.ToList().Single().Path, Is.EqualTo("/modelsource"));
                Assert.That(conflicts[0].Reason, Is.EqualTo("concurrent_edit"));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(current.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes").GetProperty("later").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            });
        }

        /// <summary>
        /// A committed model journal response verifies the intent without replaying the lost request.
        /// </summary>
        [Test]
        public async Task ConfirmedModelJournalOutcomeVerifiesWithoutRepeatingTheMutationAsync()
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            fixture.Http.FailAfterMutation = true;

            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport restarted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync("/modelsource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(first));
                Assert.That(restarted.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(restarted));
                Assert.That(restarted.Applied, Is.Zero);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Mutations[0].Path, Is.EqualTo("/"));
                Assert.That(fixture.Http.Mutations[0].OperationId, Is.Not.Null.And.Not.Empty);
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Http.Acknowledged, Is.Zero);
                Assert.That(fixture.Http.JournalQueries, Is.EqualTo(1));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.EqualTo(1));
                Assert.That(state.Verified, Is.EqualTo(1));
                Assert.That(state.Pending, Is.Zero);
                Assert.That(target.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes").GetProperty("site").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            });
        }

        /// <summary>
        /// Both direct and journal-confirmed model rejections require a new explicit decision before another write.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedModelOutcomeIsHeldUntilAnExplicitFreshDecisionAsync(bool loseResponse)
        {
            await using var fixture = new XRegistrySyncFixture(journal: true);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            fixture.Http.FailAfterMutation = loseResponse;
            fixture.Http.TransformRequest = request =>
            {
                if (!request.IsMutation)
                {
                    return request;
                }
                fixture.Http.TransformRequest = null;
                JsonObject metadata = JsonNode.Parse(request.Metadata.GetRawText())!.AsObject();
                metadata["epoch"] = 999;
                return request with { Metadata = Json(metadata.ToJsonString()) };
            };

            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport held = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus rejected = await fixture.State.ReadStatusAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Applied, Is.Zero);
                Assert.That(held.InventoryComplete, Is.False);
                Assert.That(conflicts.ToList().Single().Reason, Is.EqualTo("mutation_rejected"));
                Assert.That(conflicts[0].Path, Is.EqualTo("/modelsource"));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(fixture.Http.JournalQueries, Is.EqualTo(loseResponse ? 1 : 0));
                Assert.That(rejected.Outcomes, Is.EqualTo(1));
                Assert.That(rejected.Verified, Is.Zero);
                Assert.That(rejected.Pending, Is.Zero);
            });

            await fixture.State.ResolveConflictAsync(conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa)
                .ConfigureAwait(false);
            XRegistrySyncReport resolved = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> remaining = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistryResponse model = await fixture.Http.ReadAsync("/modelsource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(resolved));
                Assert.That(remaining, Is.Empty);
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(2));
                Assert.That(fixture.Http.Committed, Is.EqualTo(1));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(model.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes").GetProperty("site").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            });
        }

        /// <summary>
        /// Pending model work blocks dependent entities, including during a read-only preview and after restart.
        /// </summary>
        [Test]
        public async Task PendingModelIntentBlocksDependentWritesAndDryRunRecoveryAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"site":"west"}""", XRegistryAction.Merge)
                .ConfigureAwait(false);
            fixture.Http.FailBeforeMutation = true;

            XRegistrySyncReport first = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ByteString before = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            XRegistrySyncReport preview = await fixture.Engine().RunOnceAsync(dryRun: true).ConfigureAwait(false);
            ByteString after = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            XRegistrySyncReport restarted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse group = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.Pending, Is.EqualTo(1), Details(first));
                Assert.That(preview.DryRun, Is.True);
                Assert.That(preview.Pending, Is.EqualTo(1), Details(preview));
                Assert.That(after.Span.SequenceEqual(before.Span), Is.True);
                Assert.That(restarted.Pending, Is.EqualTo(1), Details(restarted));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                Assert.That(fixture.Http.Mutations[0].Path, Is.EqualTo("/"));
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(state.Intents, Is.EqualTo(1));
                Assert.That(state.Outcomes, Is.Zero);
                Assert.That(state.Verified, Is.Zero);
                Assert.That(group.Metadata.TryGetProperty("site", out _), Is.False);
            });
        }

        /// <summary>
        /// A dry run plans only the model dependency and never persists an intent or prematurely plans its entities.
        /// </summary>
        [Test]
        public async Task ModelDryRunPlansTheSchemaBeforeDependentEntityWritesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"site":"west"}""", XRegistryAction.Merge)
                .ConfigureAwait(false);
            ByteString before = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);

            XRegistrySyncReport preview = await fixture.Engine().RunOnceAsync(dryRun: true).ConfigureAwait(false);
            ByteString after = await fixture.State.ExportSnapshotAsync().ConfigureAwait(false);
            XRegistrySyncRecord planned = preview.Records.ToList()
                .Single(record => record.Kind == XRegistrySyncRecordKind.Planned);

            Assert.Multiple(() =>
            {
                Assert.That(planned.Path, Is.EqualTo("/modelsource"));
                Assert.That(preview.DryRun, Is.True);
                Assert.That(preview.Applied, Is.Zero);
                Assert.That(preview.Pending, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(after.Span.SequenceEqual(before.Span), Is.True);
            });
        }

        /// <summary>
        /// A saved preference is usable only while both model observations still match the operator's decision.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ModelResolutionRevalidatesBothSavedObservationsAsync(bool editAfterDecision)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_laterExtension, XRegistryAction.Merge)
                .ConfigureAwait(false);
            await fixture.Http.ChangeAsync("/modelsource", k_laterExtension, XRegistryAction.Merge)
                .ConfigureAwait(false);
            XRegistrySyncReport divergent = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            Assert.That(divergent.Conflicts, Is.EqualTo(1), Details(divergent));
            XRegistrySyncConflict decision = await fixture.State.ResolveConflictAsync(
                conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa).ConfigureAwait(false);
            Assert.That(decision.Status, Is.EqualTo(XRegistrySyncConflictStatus.ResolutionRequested));
            if (editAfterDecision)
            {
                await fixture.Native.ChangeAsync("/modelsource", /*lang=json,strict*/ """
                    {"groups":{"schemagroups":{"attributes":{"newest":{"type":"string"}}}}}
                    """, XRegistryAction.Merge).ConfigureAwait(false);
            }

            XRegistrySyncReport result = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> remaining = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            if (editAfterDecision)
            {
                XRegistrySyncReport held = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(result.InventoryComplete, Is.False);
                    Assert.That(remaining.ToList().Single().Reason, Is.EqualTo("resolution_stale"));
                    Assert.That(remaining[0].Path, Is.EqualTo("/modelsource"));
                    Assert.That(held.Applied, Is.Zero);
                    Assert.That(fixture.Native.Mutations, Is.Empty);
                    Assert.That(fixture.Http.Mutations, Is.Empty);
                });
            }
            else
            {
                XRegistryResponse target = await fixture.Http.ReadAsync("/modelsource").ConfigureAwait(false);
                JsonElement attributes = target.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes");
                Assert.Multiple(() =>
                {
                    Assert.That(result.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(result));
                    Assert.That(remaining, Is.Empty);
                    Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(1));
                    Assert.That(fixture.Native.Mutations, Is.Empty);
                    Assert.That(attributes.GetProperty("site").GetProperty("type").GetString(), Is.EqualTo("string"));
                    Assert.That(attributes.GetProperty("later").GetProperty("type").GetString(), Is.EqualTo("string"));
                });
            }
        }

        /// <summary>
        /// Initial model divergence follows an explicit preference in either direction without requiring a baseline.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InitialCompatibleModelPreferenceChoosesTheConfiguredSideAsync(bool preferHttp)
        {
            await using var fixture = new XRegistrySyncFixture();
            fixture.Options = fixture.Options with
            {
                ConflictPolicy = preferHttp
                    ? XRegistrySyncConflictPolicy.PreferHttp : XRegistrySyncConflictPolicy.PreferOpcUa
            };
            SyncEndpoint source = preferHttp ? fixture.Http : fixture.Native;
            SyncEndpoint target = preferHttp ? fixture.Native : fixture.Http;
            await source.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse actual = await target.ReadAsync("/modelsource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(source.Mutations, Is.Empty);
                Assert.That(target.Mutations, Has.Count.EqualTo(1));
                Assert.That(target.Mutations[0].Path, Is.EqualTo("/"));
                Assert.That(actual.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes").GetProperty("site").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            });
        }

        /// <summary>
        /// A different authoritative registry cannot inherit model baselines from the original endpoint.
        /// </summary>
        [Test]
        public async Task ChangedRegistryIdentityCannotRebindTheModelBaselineAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            using var replacement = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = "replacement-registry",
                Model = Json(DefaultModel)
            }, new InMemoryXRegistryTransactionStore(), fixture.Clock);
            var engine = new XRegistrySynchronizer(
                replacement, fixture.Http, fixture.Store, fixture.Options, fixture.Telemetry, fixture.Clock);

            XRegistrySyncReport report = await engine.RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistryResponse root = await replacement.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/") { Context = Writer }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.Applied, Is.Zero);
                Assert.That(conflicts.ToList().Single().Reason, Is.EqualTo("scope_changed"));
                Assert.That(conflicts[0].Path, Is.EqualTo("/"));
                Assert.That(root.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        /// <summary>
        /// Legacy state cannot silently adopt different models even when both endpoints now agree on the extension.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyScopeRequiresRevalidationBeforeAdoptingChangedModelsAsync(bool changeBoth)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await MakeLegacyAsync(fixture).ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            if (changeBoth)
            {
                await fixture.Http.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge)
                    .ConfigureAwait(false);
            }

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistrySyncState retained = new XRegistrySyncStateCodec().Decode(
                await fixture.State.ExportSnapshotAsync().ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(conflicts.ToList().Single().Reason, Is.EqualTo("scope_changed"));
                Assert.That(conflicts[0].Path, Is.EqualTo("/"));
                Assert.That(retained.RegistryScope, Is.Empty);
                Assert.That(retained.ModelBaseline, Is.Null);
                Assert.That(retained.ModelDefinition.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        /// <summary>
        /// New required or defaulted attributes are not treated as harmless model extensions.
        /// </summary>
        [TestCase("""{"type":"string","required":true}""")]
        [TestCase("""{"type":"string","required":true,"default":"implicit-value"}""")]
        public async Task RequiredOrDefaultedNewAttributesCannotBeReplicatedImplicitlyAsync(string rule)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            var patch = new JsonObject
            {
                ["groups"] = new JsonObject
                {
                    ["schemagroups"] = new JsonObject
                    {
                        ["attributes"] = new JsonObject { ["site"] = JsonNode.Parse(rule) }
                    }
                }
            };
            await fixture.Native.ChangeAsync("/modelsource", patch.ToJsonString(), XRegistryAction.Merge)
                .ConfigureAwait(false);

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync("/modelsource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.Records.ToList().Any(record =>
                    record.Path == "/modelsource" && record.Kind == XRegistrySyncRecordKind.Unsupported), Is.True);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(state.Intents, Is.Zero);
                Assert.That(target.Metadata.GetProperty("groups").GetProperty("schemagroups")
                    .GetProperty("attributes").TryGetProperty("site", out _), Is.False);
            });
        }

        /// <summary>
        /// Divergent effective models cannot be copied without independent model-source evidence on both sides.
        /// </summary>
        [Test]
        public async Task MissingModelSourceCapabilityPreventsModelReplicationAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            XRegistryEndpointDescription description = await fixture.Http.InspectAsync(Writer).ConfigureAwait(false);
            fixture.Http.InspectOverrideAsync = _ => new ValueTask<XRegistryEndpointDescription>(description with
            {
                Capabilities = Json(/*lang=json,strict*/ """{"available":{}}""")
            });

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(conflicts.ToList().Single().Reason, Is.EqualTo("incompatible_model"));
                Assert.That(conflicts[0].Path, Is.EqualTo("/"));
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        /// <summary>
        /// An invalid source definition cannot authorize copying an otherwise valid advertised effective schema.
        /// </summary>
        [Test]
        public async Task InvalidModelSourceCannotAuthorizeAnAdvertisedEffectiveExtensionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.ChangeAsync("/modelsource", k_extension, XRegistryAction.Merge).ConfigureAwait(false);
            fixture.Native.TransformResponse = (request, response) =>
                request.Action == XRegistryAction.Read && request.Path == "/modelsource"
                    ? response with { Metadata = Json(/*lang=json,strict*/ """{"groups":"invalid"}""") } : response;

            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.Records.ToList().Any(record =>
                    record.Path == "/modelsource" && record.Kind == XRegistrySyncRecordKind.Unsupported), Is.True);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(state.Intents, Is.Zero);
            });
        }

        private static async Task MakeLegacyAsync(XRegistrySyncFixture fixture)
        {
            IXRegistrySyncStateSession session = await fixture.Store.OpenAsync(readOnly: false).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                var codec = new XRegistrySyncStateCodec();
                XRegistrySyncState state = codec.Decode(await session.ReadAsync().ConfigureAwait(false));
                state.RegistryScope = string.Empty;
                state.ModelBaseline = null;
                state.ModelDefinition = default;
                await session.CommitAsync(codec.Encode(state)).ConfigureAwait(false);
            }
        }

        private const string k_extension = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"attributes":{"site":{"type":"string"}}}}}
            """;

        private const string k_laterExtension = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"attributes":{"later":{"type":"string"}}}}}
            """;
    }
}
