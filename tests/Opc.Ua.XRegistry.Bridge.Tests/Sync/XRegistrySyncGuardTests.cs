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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistryReconciliationTests;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    [TestFixture]
    public sealed class XRegistrySyncGuardTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task EmptyGroupDeletionUsesDestinationEpochAsync(bool nativeDeleted)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"same"}""").ConfigureAwait(false);
            SyncEndpoint source = nativeDeleted ? fixture.Native : fixture.Http;
            SyncEndpoint target = nativeDeleted ? fixture.Http : fixture.Native;
            await target.ChangeAsync(Group, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            await target.ChangeAsync(Group, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await source.DeleteAsync(Group).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse missing = await target.ReadAsync(Group).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Options.PropagateDeletes, Is.True);
                Assert.That(report.Deleted, Is.EqualTo(1), Details(report));
                Assert.That(missing.StatusCode, Is.EqualTo(404));
                Assert.That(target.Mutations.Single().Parameters[0], Is.EqualTo(new XRegistryParameter("epoch", "2")));
                Assert.That(state.Tombstones, Is.EqualTo(1));
                Assert.That(state.Verified, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DeletesOffRetainsTheBaselineWithoutMutatingTheSurvivorAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"same"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Options = fixture.Options with { PropagateDeletes = false };
            XRegistrySyncReport held = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus retained = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            XRegistryResponse existing = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(held.Deleted, Is.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(existing.StatusCode, Is.EqualTo(200));
                Assert.That(retained.Baselines, Is.EqualTo(2));
                Assert.That(retained.Tombstones, Is.Zero);
            });
            fixture.Options = fixture.Options with { PropagateDeletes = true };
            XRegistrySyncReport deleted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.That(deleted.Deleted, Is.EqualTo(1), Details(deleted));
        }

        [TestCase(403)]
        [TestCase(404)]
        [TestCase(500)]
        public async Task IncompleteInventoryNeverDeletesButIndependentUpdatesProceedAsync(int status)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
            await fixture.SeedBothAsync("/schemagroups/deleted", "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync("/schemagroups/deleted").ConfigureAwait(false);
            await fixture.Native.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"changed"}""").ConfigureAwait(false);
            fixture.Native.ReadFailurePath = "/othergroups";
            fixture.Native.ReadFailureStatus = status;
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse survivor = await fixture.Http.ReadAsync("/schemagroups/deleted").ConfigureAwait(false);
            XRegistryResponse changed = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Incomplete), Details(report));
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(report.Applied, Is.EqualTo(1));
                Assert.That(survivor.StatusCode, Is.EqualTo(200));
                Assert.That(changed.Metadata.GetProperty("name").GetString(), Is.EqualTo("changed"));
                Assert.That(fixture.Http.Mutations.Any(request => request.Action == XRegistryAction.Delete), Is.False);
            });
        }

        [Test]
        public async Task ParentDeletionProtectsDescendantsAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource,
                                     /*lang=json,strict*/
                                     """{"versionid":"v1","name":"base","schemabase64":"AQ=="}""").ConfigureAwait(
                                         false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            await fixture.Http.ChangeAsync(VersionPath,
                                     /*lang=json,strict*/
                                     """{"name":"concurrent-descendant","schemabase64":"Ag=="}""").ConfigureAwait(
                                         false);
            fixture.Options = fixture.Options with { ConflictPolicy = XRegistrySyncConflictPolicy.PreferOpcUa };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse live = await fixture.Http.ReadAsync(VersionPath, XRegistryView.Default)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Deleted, Is.Zero, Details(report));
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(live.Metadata.GetProperty("name").GetString(), Is.EqualTo("concurrent-descendant"));
                Assert.That(live.Document.ToArray(), Is.EqualTo(new byte[] { 2 }));
            });
        }

        [Test]
        public async Task VersionDeletionCannotRaceDefaultSelectionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Resource, /*lang=json,strict*/ """
                {"meta":{"defaultversionid":"v1","defaultversionsticky":true},
                 "versions":{"v1":{"ancestorid":"v1","schemabase64":""},
                             "v2":{"ancestorid":"v1","schemabase64":""}}}
                """).ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Resource + "/versions/v2").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Resource + "/meta",
                                     /*lang=json,strict*/
                                     """{"defaultversionid":"v2","defaultversionsticky":true}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse preserved = await fixture.Http.ReadAsync(Resource + "/versions/v2").ConfigureAwait(false);
            XRegistryResponse meta = await fixture.Http.ReadAsync(Resource + "/meta").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(report.Conflicts, Is.GreaterThan(0));
                Assert.That(preserved.StatusCode, Is.EqualTo(200));
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("v2"));
                Assert.That(fixture.Http.Mutations.Any(request => request.Action == XRegistryAction.Delete), Is.False);
            });
        }

        [Test]
        public async Task ChildCreatedImmediatelyBeforeParentDeleteRejectsTheStaleGuardAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Http.BeforeExecuteAsync = async (request, _) =>
            {
                if (request.Action == XRegistryAction.Delete)
                {
                    fixture.Http.BeforeExecuteAsync = null;
                    await fixture.Http.ChangeAsync(Resource,
                        /*lang=json,strict*/ """{"versionid":"v1","schemabase64":"AQ=="}""")
                        .ConfigureAwait(false);
                }
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse child = await fixture.Http.ReadAsync(VersionPath).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(fixture.Http.Committed, Is.Zero);
                Assert.That(child.StatusCode, Is.EqualTo(200));
                Assert.That(report.Conflicts, Is.EqualTo(1), Details(report));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingRootOrLostAuthorizationNeverDeletesAsync(bool denied)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Native.FailInspection = denied;
            if (!denied)
            {
                fixture.Native.ReadFailurePath = "/";
                fixture.Native.ReadFailureStatus = 404;
            }
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.ExitCode, Is.Not.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task ChangedModelNeverReusesOldBaselinesForDeletionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            JsonObject changed = JsonNode.Parse(DefaultModel)!.AsObject();
            changed["attributes"]!["newmodelattribute"] = new JsonObject { ["type"] = "string" };
            fixture.Native.ModelOverride = Json(changed.ToJsonString());
            fixture.Http.ModelOverride = Json(changed.ToJsonString());
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False, Details(report));
                Assert.That(report.Conflicts, Is.EqualTo(1));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(state.Baselines, Is.EqualTo(2));
                Assert.That(state.Tombstones, Is.Zero);
            });
        }

        [Test]
        public async Task ConfiguredEndpointIdentityAndCallerScopeAreBoundBeforeUpstreamContactAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.BaselineAsync().ConfigureAwait(false);
            ByteString before = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            var different = new XRegistrySyncOptions(fixture.Options.JobId,
                fixture.Options.OpcUaEndpointIdentity, "https://different.example/registry")
            {
                OpcUaContext = Writer,
                HttpContext = Writer
            };
            XRegistrySyncReport report = await fixture.Engine(options: different).RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncReport caller = await fixture.Engine(options: fixture.Options with
            {
                HttpContext = new XRegistryCallContext("different-operator") { IsAuthenticated = true }
            }).RunOnceAsync().ConfigureAwait(false);
            ByteString after = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Failed));
                Assert.That(caller.Status, Is.EqualTo(XRegistrySyncStatus.Failed));
                Assert.That(fixture.Native.Inspections, Is.Zero);
                Assert.That(fixture.Http.Inspections, Is.Zero);
                Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
            });
        }

        [Test]
        public async Task TombstonePreventsAutomaticResurrectionAndNeverExpiresByAgeAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            XRegistrySyncReport deleted = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromDays(36_500));
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"old"}""").ConfigureAwait(false);
            XRegistrySyncReport resurrection = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse absent = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(deleted.Deleted, Is.EqualTo(1));
                Assert.That(resurrection.Applied, Is.Zero, Details(resurrection));
                Assert.That(resurrection.Conflicts, Is.EqualTo(1));
                Assert.That(absent.StatusCode, Is.EqualTo(404));
                Assert.That(state.Tombstones, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task BothAbsentStatesRetainATombstoneWithoutSendingDeletesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            await fixture.Http.DeleteAsync(Group).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistrySyncStateStatus state = await fixture.State.ReadStatusAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
                Assert.That(report.Deleted, Is.Zero);
                Assert.That(state.Baselines, Is.EqualTo(1));
                Assert.That(state.Tombstones, Is.EqualTo(1));
                Assert.That(state.Intents, Is.Zero);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }

        [Test]
        public async Task ConflictCommandsDoNotContactEndpointsAndApplyOnlyOnNextPassAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            fixture.ResetCounts();
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            XRegistrySyncConflict requested = await fixture.State.ResolveConflictAsync(
                conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(requested.Status, Is.EqualTo(XRegistrySyncConflictStatus.ResolutionRequested));
                Assert.That(requested.ResolutionRequestedAt, Is.EqualTo(fixture.Clock.GetUtcNow()));
                Assert.That(fixture.Native.Reads + fixture.Http.Reads, Is.Zero);
                Assert.That(fixture.Native.Inspections + fixture.Http.Inspections, Is.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
            XRegistrySyncReport applied = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(applied.Applied, Is.EqualTo(1), Details(applied));
                Assert.That(target.Metadata.GetProperty("name").GetString(), Is.EqualTo("native"));
                Assert.That(applied.Conflicts, Is.Zero);
            });
        }

        [Test]
        public async Task ResolutionDoesNotOverrideANewerEditAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            await fixture.State.ResolveConflictAsync(conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa)
                .ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group,
                /*lang=json,strict*/ """{"name":"later-http-edit"}""").ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> held = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(target.Metadata.GetProperty("name").GetString(), Is.EqualTo("later-http-edit"));
                Assert.That(held.Count, Is.EqualTo(1));
                Assert.That(held[0].Reason, Is.EqualTo("resolution_stale"));
                Assert.That(held[0].Resolution, Is.EqualTo(XRegistrySyncConflictPolicy.Manual));
            });
        }

        [Test]
        public async Task ResolutionAlsoRetainsTheOriginalLocalEpochGuardAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"native"}""").ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"http"}""").ConfigureAwait(false);
            await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> conflicts = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            await fixture.State.ResolveConflictAsync(conflicts[0].Id, XRegistrySyncConflictPolicy.PreferOpcUa)
                .ConfigureAwait(false);
            await fixture.Http.ChangeAsync(Group, "{}", XRegistryAction.Merge).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            ArrayOf<XRegistrySyncConflict> held = await fixture.State.ListConflictsAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Is.Zero, Details(report));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(held[0].Reason, Is.EqualTo("resolution_stale"));
                Assert.That(held[0].Http!.Epoch, Is.EqualTo("1"));
                Assert.That(held[0].Http!.Fingerprint, Is.EqualTo(conflicts[0].Http!.Fingerprint));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DryRunDoesNotChangeStateOrEndpointsAsync(bool existingState)
        {
            await using var fixture = new XRegistrySyncFixture();
            if (existingState)
            {
                await fixture.SeedBothAsync(Group, /*lang=json,strict*/ """{"name":"base"}""").ConfigureAwait(false);
                await fixture.BaselineAsync().ConfigureAwait(false);
            }
            await fixture.Native.ChangeAsync(Group, /*lang=json,strict*/ """{"name":"source"}""").ConfigureAwait(false);
            ByteString before = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync(dryRun: true).ConfigureAwait(false);
            ByteString after = await ReadStateAsync(fixture.Store).ConfigureAwait(false);
            XRegistryResponse target = await fixture.Http.ReadAsync(Group).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.DryRun, Is.True);
                Assert.That(report.Planned, Is.EqualTo(1), Details(report));
                Assert.That(report.Applied, Is.Zero);
                Assert.That(after.IsNull, Is.EqualTo(before.IsNull));
                Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()));
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(target.StatusCode, Is.EqualTo(existingState ? 200 : 404));
            });
        }

        [TestCase(1, 100, 1_000_000)]
        [TestCase(100, 1, 1_000_000)]
        [TestCase(100, 100, 100)]
        public async Task InventoryBudgetsFailClosedAsync(int entities, int pages, int bytes)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.SeedBothAsync(Group, "{}").ConfigureAwait(false);
            await fixture.BaselineAsync().ConfigureAwait(false);
            await fixture.Native.DeleteAsync(Group).ConfigureAwait(false);
            fixture.Options = fixture.Options with
            {
                MaximumEntities = entities,
                MaximumPages = pages,
                MaximumInventoryBytes = bytes
            };
            XRegistrySyncReport report = await fixture.Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(report.InventoryComplete, Is.False);
                Assert.That(report.ExitCode, Is.Not.Zero);
                Assert.That(fixture.Http.Mutations, Is.Empty);
            });
        }
    }
}
