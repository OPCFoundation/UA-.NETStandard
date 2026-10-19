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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Verifies authorized commands, exact extrusion jobs/cuts and absolute production events.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ExtrusionProtocolTests
    {
        /// <summary>
        /// Loads the real standard model owner.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new ExtrusionFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases every owned capability.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Authorization sees the original caller and denial prevents provider execution.
        /// </summary>
        [Test]
        public async Task ClampCommandsRetainCallerAndRejectWrongTargetsAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("clamp");
            options.Features.Include("OpenClamp").Include("CloseClamp");
            var provider = (CommandProvider)options.Commands!;
            provider.Deny = true;
            var device = await m_fixture.Runtime.CreateHaulOffAsync(options).ConfigureAwait(false);
            MethodState open = device.Node.OpenClamp!;
            var caller = m_fixture.Runtime.SystemContext.Copy();
            ServiceResult denied = await CallAsync(open, caller, device.NodeId).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
                Assert.That(provider.Executions, Is.Zero);
                Assert.That(provider.LastCommand!.Context, Is.SameAs(caller));
            });
            provider.Deny = false;
            ServiceResult accepted = await CallAsync(open, caller, device.NodeId).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(accepted), Is.True);
            Assert.That(provider.Executions, Is.EqualTo(1));
            Assert.That(device.Node.ClampClosed!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData),
                "Acknowledgement is not invented physical clamp feedback.");
            ServiceResult wrong = await open.OnCallMethod2Async!(
                caller, open, new NodeId("wrong-target", 1), [], [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(wrong.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdInvalid));
            Assert.That(provider.Executions, Is.EqualTo(1));
        }

        /// <summary>
        /// Writes cannot publish the optimistic UA cache instead of a provider's committed observation.
        /// </summary>
        [Test]
        public async Task WritesPublishOnlyCommittedProviderStateAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("writes");
            options.Features.Include("BackflushPressure");
            var provider = (CommandProvider)options.Commands!;
            var filter = await m_fixture.Runtime.CreateFilterAsync(options).ConfigureAwait(false);
            BaseVariableState pressure = filter.Node.BackflushPressure
                ?? throw new AssertionException("The selected BackflushPressure was not created.");
            provider.Deny = true;
            AttributeWriteResult denied = await pressure.OnWriteValueAsync!(m_fixture.Runtime.SystemContext,
                pressure, NumericRange.Null, Variant.From(5.0), CancellationToken.None).ConfigureAwait(false);
            Assert.That(denied.Result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(provider.Executions, Is.Zero);
            provider.Deny = false;
            AttributeWriteResult accepted = await pressure.OnWriteValueAsync(m_fixture.Runtime.SystemContext,
                pressure, NumericRange.Null, Variant.From(5.0), CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(accepted.Result), Is.True);
            AttributeReadResult read = await pressure.OnReadValueAsync!(m_fixture.Runtime.SystemContext,
                pressure, NumericRange.Null, QualifiedName.Null, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(read.Value.TryGetValue(out double value) && value == 5.0, Is.True);
                Assert.That(read.SourceTimestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
                Assert.That(read.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            });
        }

        /// <summary>
        /// Concurrent disposal cancels active and queued commands without stopping unrelated owners.
        /// </summary>
        [Test]
        public async Task DeviceDisposalCancelsPendingCommandsAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("dispose");
            options.Features.Include("OpenClamp");
            var provider = (CommandProvider)options.Commands!;
            provider.Blocking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var device = await m_fixture.Runtime.CreateHaulOffAsync(options).ConfigureAwait(false);
            var operations = new List<Task<ServiceResult>>();
            for (int index = 0; index < 4; index++)
            {
                operations.Add(CallAsync(device.Node.OpenClamp!, m_fixture.Runtime.SystemContext, device.NodeId));
            }
            Assert.That(provider.Executions, Is.EqualTo(1), "Three calls must be queued behind the active provider.");
            Task firstDisposal = device.DisposeAsync().AsTask();
            Task secondDisposal = device.DisposeAsync().AsTask();
            await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            foreach (Task<ServiceResult> operation in operations)
            {
                try
                {
                    ServiceResult result = await operation.ConfigureAwait(false);
                    Assert.That(ServiceResult.IsBad(result), Is.True);
                }
                catch (OperationCanceledException)
                {
                    Assert.That(provider.Blocking.Task.IsCompleted, Is.False);
                }
            }
            Assert.That(provider.Executions, Is.EqualTo(1), "Queued commands must not reach the provider at shutdown.");
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.ApplySnapshotAsync(new ExtrusionSnapshot(1, [])).ConfigureAwait(false));
        }

        /// <summary>
        /// Preserves the signed lower offsets required by OPC 40083 and drives common status from absolute values.
        /// </summary>
        [Test]
        public async Task SignedToleranceBandsRemainNegativeAndDriveRealStatusAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("signed-tolerances");
            options.Features.Include("Throughput");
            var device = await m_fixture.Runtime.CreateExtruderAsync(options, node =>
            {
                MonitoredParameterState parameter = node.Throughput!;
                parameter.SetValue!.Value = 190;
                parameter.MinValue!.Value = 100;
                parameter.MaxValue!.Value = 250;
                parameter.LowerTolerance!.Value = -8;
                parameter.UpperTolerance!.Value = 8;
                parameter.LowerTolerance2!.Value = -15;
                parameter.UpperTolerance2!.Value = 15;
            }).ConfigureAwait(false);
            await device.ApplySnapshotAsync(new ExtrusionSnapshot(1,
            [
                new ExtrusionValue(device.Node.Throughput!.ActualValue!.NodeId,
                    new DataValue(Variant.From(181.0), StatusCodes.Good, ExtrusionFixture.Timestamp))
            ])).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(device.Node.Throughput.LowerTolerance!.Value, Is.EqualTo(-8));
                Assert.That(device.Node.Throughput.LowerTolerance2!.Value, Is.EqualTo(-15));
                Assert.That(device.Node.Throughput.Status!.Value, Is.EqualTo(4));
                Assert.That(device.Node.Throughput.ActualValue.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
            });
        }

        /// <summary>
        /// Job identifiers are group-local; parameter defaults and strand sequencing are not ISA-95 semantics.
        /// </summary>
        [Test]
        public async Task JobGroupsKeepScopedIdsDefaultsAndStrandConstraintsAsync()
        {
            var line = await CreateJobLineAsync("job-scopes").ConfigureAwait(false);
            ExtrusionJobController jobs = line.Jobs!;
            JobGroupState first = await jobs.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("first"))
                .ConfigureAwait(false);
            await jobs.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("second")).ConfigureAwait(false);
            JobState a = await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "first", Job("shared"))
                .ConfigureAwait(false);
            JobState b = await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "second", Job("shared"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(a.NodeId, Is.Not.EqualTo(b.NodeId));
                Assert.That(a.ParameterSetting!.Value, Has.Count.EqualTo(1));
                Assert.That(a.ParameterSetting.Value[0].Value.TryGetValue(out double length) && length == 25.0,
                    Is.True);
                Assert.That(first.BrowseName.Name, Is.EqualTo("JobGroup_001"));
                Assert.That(a.BrowseName.Name, Is.EqualTo("Job_001"));
            });
            ServiceResultException duplicate = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "first", Job("shared")).ConfigureAwait(false)))!;
            Assert.That(duplicate.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEntryExists));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "first",
                    Job("same-sequence")).ConfigureAwait(false));
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext, ExtrusionJobOperation.StartGroup, "first")
                .ConfigureAwait(false);
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext, ExtrusionJobOperation.StartJob, "first", "shared")
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                    ExtrusionJobOperation.RemoveJob, "first", "shared").ConfigureAwait(false));
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.InterruptJob, "first", "shared").ConfigureAwait(false);
            Assert.That(a.Status!.Value, Is.EqualTo(JobStatusEnumeration.JOB_INTERRUPTED));
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.StartJob, "first", "shared").ConfigureAwait(false);
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.FinishJob, "first", "shared").ConfigureAwait(false);
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.RemoveJob, "first", "shared").ConfigureAwait(false);
            Assert.That(m_fixture.Manager.FindPredefinedNode(a.NodeId), Is.Null);
        }

        /// <summary>
        /// Provider refusal, wrong parameter encodings and unknown state targets cannot create phantom jobs.
        /// </summary>
        [Test]
        public async Task JobValidationAndAuthorizationFailBeforePublicationAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("job-failures");
            options.Features.Include("JobGroups");
            var authority = (JobProvider)options.Jobs!;
            var line = await m_fixture.Runtime.CreateLineAsync(options).ConfigureAwait(false);
            authority.Deny = true;
            ServiceResultException denied = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await line.Jobs!.AddGroupAsync(m_fixture.Runtime.SystemContext,
                    Group("denied")).ConfigureAwait(false)))!;
            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
                Assert.That(authority.Executions, Is.Zero);
                Assert.That(line.Jobs!.Groups, Is.Empty);
            });
            authority.Deny = false;
            await line.Jobs!.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("valid")).ConfigureAwait(false);
            ExtrusionJobDefinition invalid = Job("invalid") with
            {
                ParameterSetting = [new ParameterSettingType { Id = 1, Value = Variant.From("not a Double") }]
            };
            ServiceResultException mismatch = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await line.Jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "valid", invalid).ConfigureAwait(false)))!;
            Assert.That(mismatch.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await line.Jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                    ExtrusionJobOperation.StartJob, "valid", "missing").ConfigureAwait(false));
            Assert.That(authority.Executions, Is.EqualTo(1));
        }

        /// <summary>
        /// Exact committed completion facts produce generated events once without counter increments on replay.
        /// </summary>
        [Test]
        public async Task ProductionCommitsPreserveCountsQualityAndEventSourcesAsync()
        {
            var line = await CreateJobLineAsync("production").ConfigureAwait(false);
            ExtrusionJobController jobs = line.Jobs!;
            await jobs.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("group")).ConfigureAwait(false);
            JobState job = await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "group", Job("job"))
                .ConfigureAwait(false);
            var events = new List<BaseEventState>();
            jobs.Node.EventReported += (_, _, notification) =>
            {
                if (notification is BaseEventState state)
                {
                    events.Add(state);
                }
            };
            var commit = new ExtrusionJobProductionSnapshot(
                7, "group", "job", JobStatusEnumeration.JOB_IN_PRODUCTION, 1,
                2, 1, 1, 0, 120, false, ExtrusionFixture.Timestamp,
                [new ExtrusionFinishedUnit(1, true), new ExtrusionFinishedUnit(2, false)], [1]);
            await jobs.ApplyProductionAsync(commit).ConfigureAwait(false);
            await jobs.ApplyProductionAsync(commit).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(job.ActualOutput!.Value, Is.EqualTo(2));
                Assert.That(job.ActualGoodOutput!.Value, Is.EqualTo(1));
                Assert.That(job.ActualBadOutput!.Value, Is.EqualTo(1));
                Assert.That(job.ActualOutput.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
                Assert.That(line.Node.ProductionParameters!.GoodProduct!.Value, Is.False);
                Assert.That(events.OfType<UnitFinishedEventState>().Count(), Is.EqualTo(2));
                Assert.That(events.OfType<LotFinishedEventState>().Count(), Is.EqualTo(1));
            });
            foreach (BaseEventState notification in events)
            {
                Assert.That(notification.SourceNode!.Value, Is.EqualTo(job.NodeId));
                Assert.That(notification.Time!.Value, Is.EqualTo(ExtrusionFixture.Timestamp));
                Assert.That(m_fixture.Manager.FindPredefinedNode<BaseObjectTypeState>(
                    notification.EventType!.Value)!.IsAbstract, Is.False);
            }
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await jobs.ApplyProductionAsync(commit with { ActualOutput = 3 }).ConfigureAwait(false));
        }

        /// <summary>
        /// Empty event batches retain finished-lot history, and a rejected duplicate leaves its revision reusable.
        /// </summary>
        [Test]
        public async Task FinishedLotReplayAcrossEmptyCommitsDoesNotConsumeRevisionAsync()
        {
            var line = await CreateJobLineAsync("finished-lot-replay").ConfigureAwait(false);
            ExtrusionJobController jobs = line.Jobs!;
            await jobs.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("group")).ConfigureAwait(false);
            JobState job = await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "group", Job("job"))
                .ConfigureAwait(false);
            var events = new List<LotFinishedEventState>();
            jobs.Node.EventReported += (_, _, notification) =>
            {
                if (notification is LotFinishedEventState finished)
                {
                    events.Add(finished);
                }
            };
            var first = new ExtrusionJobProductionSnapshot(
                7, "group", "job", JobStatusEnumeration.JOB_IN_PRODUCTION, 2,
                2, 2, 0, 0, 120, true, ExtrusionFixture.Timestamp, [], [1]);
            await jobs.ApplyProductionAsync(first).ConfigureAwait(false);
            await jobs.ApplyProductionAsync(first with { Revision = 8, FinishedLots = [] }).ConfigureAwait(false);
            ExtrusionJobProductionSnapshot duplicate = first with { Revision = 9 };

            ServiceResultException rejected = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await jobs.ApplyProductionAsync(duplicate).ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Lot!.Value, Is.EqualTo(1u));
                Assert.That(events[0].SourceNode!.Value, Is.EqualTo(job.NodeId));
                Assert.That(job.ActualOutput!.Value, Is.EqualTo(2));
                Assert.That(job.ActualLot!.Value, Is.EqualTo(2u));
                Assert.That(job.ActualOutput.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
            });

            ExtrusionJobProductionSnapshot next = duplicate with
            {
                ActualLot = 3,
                ActualOutput = 4,
                ActualGoodOutput = 4,
                FinishedLots = [2]
            };
            await jobs.ApplyProductionAsync(next).ConfigureAwait(false);
            await jobs.ApplyProductionAsync(next).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(events.Select(notification => notification.Lot!.Value),
                    Is.EqualTo(new uint[] { 1, 2 }));
                Assert.That(job.ActualOutput!.Value, Is.EqualTo(4));
                Assert.That(job.ActualGoodOutput!.Value, Is.EqualTo(4));
                Assert.That(job.ActualLot!.Value, Is.EqualTo(3u));
            });
        }

        /// <summary>
        /// Status commands update the line quality constraint without waiting for another production observation.
        /// </summary>
        [Test]
        public async Task JobStatusCommandsRefreshTheActiveQualityConstraintAsync()
        {
            var line = await CreateJobLineAsync("job-quality-status").ConfigureAwait(false);
            ExtrusionJobController jobs = line.Jobs!;
            await jobs.AddGroupAsync(m_fixture.Runtime.SystemContext, Group("quality")).ConfigureAwait(false);
            await jobs.AddJobAsync(m_fixture.Runtime.SystemContext, "quality", Job("job")).ConfigureAwait(false);
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext, ExtrusionJobOperation.StartGroup, "quality")
                .ConfigureAwait(false);
            await jobs.ApplyProductionAsync(new ExtrusionJobProductionSnapshot(
                1, "quality", "job", JobStatusEnumeration.JOB_IN_PRODUCTION, 0,
                0, 0, 0, 0, 0, false, ExtrusionFixture.Timestamp, [], [])).ConfigureAwait(false);
            NodeId quality = line.Node.ProductionParameters!.GoodProduct!.NodeId;
            var good = new ExtrusionSnapshot(10,
                [new ExtrusionValue(quality, new DataValue(Variant.From(true), StatusCodes.Good,
                    ExtrusionFixture.Timestamp))]);
            await Assert.ThrowsAsync<ServiceResultException>(() => line.ApplySnapshotAsync(good).AsTask());

            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.InterruptJob, "quality", "job").ConfigureAwait(false);
            await line.ApplySnapshotAsync(good).ConfigureAwait(false);
            Assert.That(line.Node.ProductionParameters.GoodProduct.Value, Is.True);

            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.StartJob, "quality", "job").ConfigureAwait(false);
            Assert.That(line.Node.ProductionParameters.GoodProduct.Value, Is.False);
            await Assert.ThrowsAsync<ServiceResultException>(() =>
                line.ApplySnapshotAsync(good with { Revision = 11 }).AsTask());

            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.FinishJob, "quality", "job").ConfigureAwait(false);
            await jobs.ExecuteAsync(m_fixture.Runtime.SystemContext,
                ExtrusionJobOperation.RemoveJob, "quality", "job").ConfigureAwait(false);
            await line.ApplySnapshotAsync(good with { Revision = 11 }).ConfigureAwait(false);
            Assert.That(line.Node.ProductionParameters.GoodProduct.Value, Is.True);
        }

        /// <summary>
        /// Manual-cut acceptance alone never changes counts; physical cut commits drive the actual CutEvent.
        /// </summary>
        [Test]
        public async Task CutterCountsAndEventsComeOnlyFromCommittedCutsAsync()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("cut-events");
            options.Features.Include("ManualCut").Include("SampleCut");
            var cutter = await m_fixture.Runtime.CreateCutterAsync(options).ConfigureAwait(false);
            Cutter.CuttingProductState product = ExtrusionNodeAccess.Children(
                m_fixture.Runtime.SystemContext,
                    cutter.Node.CuttingProducts!).OfType<Cutter.CuttingProductState>().First();
            var events = new List<Cutter.CutEventState>();
            cutter.Node.EventReported += (_, _, notification) =>
            {
                if (notification is Cutter.CutEventState cut)
                {
                    events.Add(cut);
                }
            };
            ServiceResult accepted = await CallAsync(
                cutter.Node.ManualCut!, m_fixture.Runtime.SystemContext, cutter.NodeId).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(accepted), Is.True);
            Assert.That(events, Is.Empty);
            var commit = new ExtrusionCutCommit(12, product.Id!.Value, product.Id.Value, 5, 105,
                ExtrusionFixture.Timestamp);
            await cutter.Cuts!.ApplyAsync(commit).ConfigureAwait(false);
            await cutter.Cuts.ApplyAsync(commit).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(product.ActualOutput!.Value, Is.EqualTo(5));
                Assert.That(product.TotalOutput!.Value, Is.EqualTo(105));
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].CuttingProductId!.Value, Is.EqualTo(product.Id.Value));
                Assert.That(events[0].Time!.Value, Is.EqualTo(ExtrusionFixture.Timestamp));
            });
        }

        /// <summary>
        /// Selects real job capabilities while keeping unrelated optional hardware disabled.
        /// </summary>
        private ValueTask<ExtrusionDevice<ExtrusionLine_InterfaceState>> CreateJobLineAsync(string serial)
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options(serial);
            options.Features.Include("JobGroups");
            return m_fixture.Runtime.CreateLineAsync(options);
        }

        /// <summary>
        /// Provides complete typed group metadata.
        /// </summary>
        internal static ExtrusionJobGroupDefinition Group(string id)
        {
            return new ExtrusionJobGroupDefinition(
                id, "Production group", string.Empty, string.Empty, [], 1,
                DateTimeUtc.MinValue, 0, 0, DateTimeUtc.MinValue);
        }

        /// <summary>
        /// Provides complete typed job metadata with default parameter selection.
        /// </summary>
        internal static ExtrusionJobDefinition Job(string id)
        {
            return new ExtrusionJobDefinition(
                id, "Production job", "Customer", "Product", "Product description", 1, 1, [], 10, 2);
        }

        /// <summary>
        /// Executes the real method validation and asynchronous dispatch path.
        /// </summary>
        private static async Task<ServiceResult> CallAsync(MethodState method, ISystemContext context, NodeId owner)
        {
            return await method.CallAsync(context, owner, [], new List<ServiceResult>(), new List<Variant>())
                .ConfigureAwait(false);
        }

        /// <summary>The serial real-model test owner.</summary>
        private ExtrusionFixture m_fixture = null!;
    }
}
