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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using ClientSession = Opc.Ua.Client.ISession;
using ClientSubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Tests.Integration
{
    public sealed partial class CompanionSuiteTests
    {
        /// <summary>
        /// Failed mixed startup retains its original cause and drains already constructed native resources.
        /// </summary>
        [Test]
        public async Task MixedStartupFailurePreservesCauseAndDrainsAlreadyBuiltRuntimesAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            var startupLogger = new Mock<ILogger>();
            startupLogger.Setup(logger => logger.IsEnabled(LogLevel.Error)).Returns(true);
            var loggerProvider = new Mock<ILoggerProvider>();
            loggerProvider.Setup(provider => provider.CreateLogger(It.IsAny<string>()))
                .Returns((string category) => category == "Opc.Ua.Di.Server.Hosting.DiPostSetupRunner"
                    ? startupLogger.Object : NullLogger.Instance);
            fixture.Services.AddLogging(logging => logging.AddProvider(loggerProvider.Object));
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.StartAsync(failStartup: true).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(failure.LocalizedText.Text, Is.EqualTo("Unexpected error starting application"));
            Assert.That(failure.InnerResult, Is.Not.Null);
            Assert.That(failure.InnerResult!.StatusCode, Is.EqualTo(StatusCodes.Bad));
            string managerType = typeof(MachineryNodeManager).FullName!;
            string expectedWrapper =
                $"DI post-setup configurator #0 (target '{managerType}') " +
                $"failed for manager '{managerType}'. See inner exception.";
            Assert.That(failure.InnerResult.LocalizedText.Text, Does.StartWith("[InvalidOperationException] "));
            Assert.That(failure.InnerResult.LocalizedText.Text, Does.Contain(expectedWrapper));
            Moq.IInvocation[] loggedFailures = startupLogger.Invocations
                .Where(invocation => invocation.Method.Name == nameof(ILogger.Log)).ToArray();
            Assert.That(loggedFailures, Has.Length.EqualTo(1));
            Assert.That(loggedFailures[0].Arguments[0], Is.EqualTo(LogLevel.Error));
            Assert.That(loggedFailures[0].Arguments[3], Is.TypeOf<ServiceResultException>());
            var originalCause = (ServiceResultException)loggedFailures[0].Arguments[3];
            Assert.That(originalCause.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(originalCause.LocalizedText.Text, Is.EqualTo("Composition startup rejection."));
            var diagnostics = new List<string>();
            for (ServiceResult? result = failure.Result; result != null; result = result.InnerResult)
            {
                diagnostics.Add(result.AdditionalInfo ?? string.Empty);
                diagnostics.Add(result.LocalizedText.Text ?? string.Empty);
            }
            string diagnosticChain = string.Join(Environment.NewLine, diagnostics);
#if DEBUG
            Assert.That(diagnosticChain, Does.Contain("Composition startup rejection."));
#else
            Assert.That(diagnosticChain, Does.Not.Contain("Composition startup rejection."));
            Assert.That(diagnosticChain, Does.Not.Contain(nameof(CompanionSuiteFixture)));
#endif
            Assert.That(diagnosticChain, Does.Not.Contain(nameof(IndexOutOfRangeException)));
            Assert.That(fixture.Cranes, Has.Count.EqualTo(2));
            Assert.That(fixture.Cranes.All(value => value.IsDisposed), Is.True);
            foreach (MachineToolRuntime<MachineToolState> machine in fixture.Machines)
            {
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await machine.RefreshAsync().ConfigureAwait(false));
            }
            foreach (GmsHandle system in fixture.Systems)
            {
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await system.RefreshAsync().ConfigureAwait(false));
            }
            foreach (WmtpPeripheral peripheral in fixture.Peripherals)
            {
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await peripheral.RefreshAsync().ConfigureAwait(false));
            }
            Assert.That(fixture.Inspections.All(value => value.ConformanceUnits.IsEmpty), Is.True);
            Assert.That(fixture.Assets.All(value => value.ConformanceUnits.IsEmpty), Is.True);
        }

        /// <summary>
        /// Disposed contributors must withdraw without reading diagnostics from an already disposed server.
        /// </summary>
        [Test]
        public async Task StoppedMixedHostWithdrawsConformanceWithoutDereferencingDisposedServerAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.StartAsync(failStartup: true).ConfigureAwait(false));
            foreach (MachineToolRuntime<MachineToolState> machine in fixture.Machines)
            {
                MachineToolCapabilities capabilities = machine.Capabilities;
                Assert.Multiple(() =>
                {
                    Assert.That(capabilities.MachineId, Is.EqualTo(machine.NodeId));
                    Assert.That(capabilities.ConformanceUnits.IsEmpty, Is.True);
                    Assert.That(capabilities.ServerProfiles.IsEmpty, Is.True);
                    Assert.That(capabilities.Monitoring.PotentialMonitoredItems, Is.Zero);
                    Assert.That(capabilities.Monitoring.MaximumSessions, Is.Zero);
                    Assert.That(capabilities.Monitoring.MaximumSubscriptions, Is.Zero);
                    Assert.That(capabilities.Monitoring.MaximumSubscriptionsPerSession, Is.Zero);
                    Assert.That(capabilities.Monitoring.MaximumMonitoredItems, Is.Zero);
                    Assert.That(capabilities.Monitoring.MaximumMonitoredItemsPerSubscription, Is.Zero);
                    Assert.That(capabilities.Monitoring.SupportsMinimum, Is.False);
                    Assert.That(capabilities.Monitoring.SupportsAll, Is.False);
                });
            }
            Assert.That(fixture.Systems.All(value => value.ConformanceUnits.IsEmpty), Is.True);
            Assert.That(fixture.Peripherals.All(value => value.ConformanceUnits.IsEmpty), Is.True);
            Assert.That(fixture.Inspections.All(value => value.ConformanceUnits.IsEmpty), Is.True);
            Assert.That(fixture.Assets.All(value => value.ConformanceUnits.IsEmpty), Is.True);
        }

        /// <summary>
        /// A late native binding failure releases all staged nodes/resources without withdrawing peer families.
        /// </summary>
        [Test]
        public async Task FailedCompositionRollsBackOnlyItsOwnRootAndAllowsSameNameRetryAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ClientSession session = await fixture.ConnectAsync("RollbackReader").ConfigureAwait(false);
            DataValue before = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits)
                .ConfigureAwait(false);
            var released = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            released.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var staged = new Dictionary<NodeId, NodeState>();
            GmsBuilder builder = RetryGms(fixture).WithMachinery(machine =>
            {
                machine.WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(released.Object));
                machine.WithRegistrationBinding((_, context, _) =>
                {
                    IndexTree(context, machine.State, staged);
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, "Rejected composed binding.");
                });
            });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            released.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(staged, Has.Count.GreaterThan(1));
            foreach (NodeId id in staged.Keys)
            {
                Assert.That(fixture.Context.Manager.FindPredefinedNode(id), Is.Null, id.ToString());
            }
            foreach (BaseInstanceState root in Roots(fixture))
            {
                Assert.That(fixture.Context.Manager.FindPredefinedNode(root.NodeId), Is.SameAs(root));
            }
            DataValue after = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits)
                .ConfigureAwait(false);
            Assert.That(after.WrappedValue, Is.EqualTo(before.WrappedValue));
            await using GmsHandle retry = await RetryGms(fixture).BuildAsync().ConfigureAwait(false);
            await retry.Results.PublishAsync(CompanionSuiteData.Result(3)).ConfigureAwait(false);
            await using var client = new GmsClient(session, fixture.Telemetry);
            Assert.That((await client.GetResults(retry.NodeId).GetLatestAsync().ConfigureAwait(false))
                .ResultMetaData!.ResultId, Is.EqualTo("Result3"));
            Assert.That((await client.GetResults(fixture.Systems[0].NodeId).GetLatestAsync().ConfigureAwait(false))
                .ResultMetaData!.ResultId, Is.EqualTo("Result1"));
        }

        /// <summary>
        /// The real source pair preserves identical order IDs in different machines and serves coherent typed views.
        /// </summary>
        [Test]
        public async Task JobsAndRecordsStayPairedWithTheirOwnMachineAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using var firstSource = JobSource("LEFT.NC");
            await using var secondSource = JobSource("RIGHT.NC");
            await using MachineToolRuntime<MachineToolState> first = await JobMachine(fixture.Context, "JobsLeft",
                firstSource).BuildAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> second = await JobMachine(fixture.Context, "JobsRight",
                secondSource).BuildAsync().ConfigureAwait(false);
            ClientSession session = await fixture.ConnectAsync("JobReader").ConfigureAwait(false);
            await using var client = new MachineToolClient(session, fixture.Telemetry);
            var firstManagement = await client.GetJobManagementAsync(first.NodeId).ConfigureAwait(false);
            var secondManagement = await client.GetJobManagementAsync(second.NodeId).ConfigureAwait(false);
            V2.ISA95JobOrderReceiverObjectTypeClient? receiver = await firstManagement!
                .GetJobOrderControlAsync(fixture.Telemetry).ConfigureAwait(false);
            V2.ISA95JobOrderReceiverObjectTypeClient? other = await secondManagement!
                .GetJobOrderControlAsync(fixture.Telemetry).ConfigureAwait(false);
            var order = new V2.ISA95JobOrderDataType
            {
                JobOrderID = "SameOrder",
                EncodingMask = (uint)V2.ISA95JobOrderDataTypeFields.WorkMasterID,
                WorkMasterID = [new V2.ISA95WorkMasterDataType { ID = "SameWorkMaster" }]
            };
            Assert.That(await receiver!.StoreAsync(order, []).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(await other!.StoreAsync(order, []).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(await receiver.StartAsync("SameOrder", []).ConfigureAwait(false),
                Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That((await firstSource.TransitionAsync("SameOrder", Isa95JobExecutionTransition.BeginExecution)
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            await firstSource.DrainAsync(first.RefreshAsync).ConfigureAwait(false);
            await secondSource.DrainAsync(second.RefreshAsync).ConfigureAwait(false);
            MachineToolProductionReadSnapshot left = await client.ReadProductionAsync(first.NodeId)
                .ConfigureAwait(false);
            MachineToolProductionReadSnapshot right = await client.ReadProductionAsync(second.NodeId)
                .ConfigureAwait(false);
            Assert.That(left.ActiveProgram.Name.WrappedValue.TryGetValue(out string program), Is.True);
            Assert.That(program, Is.EqualTo("LEFT.NC"));
            Assert.That(right.Jobs.Count, Is.EqualTo(1));
            Assert.That(right.Jobs[0].Programs[0].Name.WrappedValue.TryGetValue(out string otherProgram), Is.True);
            Assert.That(otherProgram, Is.EqualTo("RIGHT.NC"));
            Assert.That(right.ActiveProgram.Name.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(right.ActiveProgram.Name.WrappedValue.IsNull, Is.True);
            Assert.That(left.ActiveProgram.Name.StatusCode, Is.EqualTo(StatusCodes.UncertainSubstituteValue));
            var capture = await firstSource.CaptureAsync().ConfigureAwait(false);
            Assert.That((await firstSource.ReceiveJobResponseAsync(new V2.ISA95JobResponseDataType
            {
                JobResponseID = "LeftResponse", JobOrderID = "SameOrder", JobState = capture.JobOrders[0].State
            }).ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            V2.ISA95JobResponseProviderObjectTypeClient? responses = await firstManagement
                .GetJobOrderResultsAsync(fixture.Telemetry).ConfigureAwait(false);
            (V2.ISA95JobResponseDataType response, ulong status) =
                await responses!.RequestJobResponseByJobOrderIDAsync("SameOrder").ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(response.JobResponseID, Is.EqualTo("LeftResponse"));
            Assert.That(await secondSource.GetJobResponsesV2Async().ConfigureAwait(false), Is.Empty);
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That((await firstSource.CaptureAsync().ConfigureAwait(false)).JobOrders.Count, Is.EqualTo(1),
                "The per-root binding borrows the explicitly owned source.");
            Assert.That(fixture.Context.Manager.FindPredefinedNode(second.NodeId), Is.SameAs(second.State));
            WmtpRecordClient records = await client.GetWirelessPeripheral(fixture.Peripherals[1].NodeId)
                .GetWorkRecordsAsync().ConfigureAwait(false);
            Assert.That((await records.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false))[0].ActualValue,
                Is.EqualTo(202.0));
        }

        /// <summary>
        /// Selecting either half of the job provider pair alone fails without allocating a machine or stealing a peer.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IncompleteJobProviderPairsAreRejectedBeforePublicationAsync(bool missingReceiver)
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using var source = JobSource("PAIRED.NC");
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("MissingPair")
                .WithProvider(source)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "MissingPair"))
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = missingReceiver ? null : source,
                    ResponseProvider = missingReceiver ? source : null
                });
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Context.MachinesFolder.FindChild(fixture.Context.Context,
                new QualifiedName("MissingPair", fixture.Context.InstanceNamespaceIndex)), Is.Null);
            await using MachineToolRuntime<MachineToolState> retry = await JobMachine(
                fixture.Context, "MissingPair", source).BuildAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(retry.NodeId), Is.SameAs(retry.State));
            Assert.That(fixture.Systems[0].Observation, Is.Not.Null);
        }

        /// <summary>
        /// Deleting one GMS cancels its admitted acquisition and transient file while peer observations remain live.
        /// </summary>
        [Test]
        public async Task RemovingOneRootDrainsItsResourcesWhileOtherFamiliesSurviveAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ClientSession session = await fixture.ConnectAsync("RemovalReader").ConfigureAwait(false);
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = window.Token;
            var transfer = new TemporaryFileTransferTypeClient(session,
                fixture.Systems[0].State.ResultManagement!.ResultTransfer!.NodeId, fixture.Telemetry);
            (NodeId removedFile, _, _) = await transfer.GenerateFileForReadAsync(Variant.FromStructure(
                new ResultTransferOptionsDataType { ResultId = "Result1" }), ct).ConfigureAwait(false);
            var siblingTransfer = new TemporaryFileTransferTypeClient(session,
                fixture.Systems[1].State.ResultManagement!.ResultTransfer!.NodeId, fixture.Telemetry);
            (NodeId siblingFile, uint siblingHandle, _) = await siblingTransfer.GenerateFileForReadAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "Result2" }), ct)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GmsProviders[0].Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        finished.TrySetResult(true);
                    }
                    return CompanionSuiteData.Gms(1);
                });
            Task pending = fixture.Systems[0].RefreshAsync(ct).AsTask();
            await entered.Task.WaitAsync(ct).ConfigureAwait(false);
            Assert.That(session.TryGetSubscriptionManager(out ClientSubscriptionManager? subscriptions), Is.True);
            int initialCount = subscriptions!.Count;
            await using var observer = new MachineToolClient(session, fixture.Telemetry);
            Task<MachineToolDataObservation> observation = observer.ObserveDataAsync(fixture.Machines[1].NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(5), MaximumNotifications = 1 }, ct)
                .AsTask();
            await fixture.Context.Manager.DeleteNodeAsync(fixture.Context.Manager.SystemContext,
                fixture.Systems[0].NodeId, ct).ConfigureAwait(false);
            Assert.That(() => pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(finished.Task.IsCompleted, Is.True);
            Assert.That(fixture.Systems[0].ConformanceUnits, Is.Empty);
            await AssertMissingAsync(session, fixture.Systems[0].NodeId, ct).ConfigureAwait(false);
            await AssertMissingAsync(session, removedFile, ct).ConfigureAwait(false);
            var file = new FileTypeClient(session, siblingFile, fixture.Telemetry);
            Assert.That((await file.ReadAsync(siblingHandle, 10, ct).ConfigureAwait(false)).ToArray(),
                Is.EqualTo(new byte[] { 2, 7, 9 }));
            await file.CloseAsync(siblingHandle, ct).ConfigureAwait(false);
            MachineToolDataObservation survived = await observation.ConfigureAwait(false);
            Assert.That(survived.Changes, Is.Not.Empty);
            Assert.That(survived.Changes.ToArray()!.All(value => value.Value.StatusCode == StatusCodes.Good), Is.True);
            Assert.That(subscriptions.Count, Is.EqualTo(initialCount));
            await using var gms = new GmsClient(session, fixture.Telemetry);
            Assert.That((await gms.GetResults(fixture.Systems[1].NodeId).GetLatestAsync(ct).ConfigureAwait(false))
                .ResultMetaData!.ResultId, Is.EqualTo("Result2"));
            Assert.That(fixture.Cranes[0].IsDisposed, Is.False);
            AssertValue((await observer.GetWirelessPeripheral(fixture.Peripherals[0].NodeId).ReadSnapshotAsync(ct)
                .ConfigureAwait(false)).Battery, 71.0);
        }

        /// <summary>
        /// Concurrent disposal joins a late provider callback, rejects its stale result, and never locks peer families.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposalWaitsForLateProviderWithoutAffectingOtherFamiliesAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ClientSession session = await fixture.ConnectAsync("LateProviderReader").ConfigureAwait(false);
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = window.Token;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.MachineProviders[0].Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    MachineToolSnapshot original = CompanionSuiteData.Machine(1);
                    return original with { Revision = 1, Operation = original.Operation with { FeedOverride = 5 } };
                });
            Task pending = fixture.Machines[0].RefreshAsync(ct).AsTask();
            await entered.Task.WaitAsync(ct).ConfigureAwait(false);
            BaseVariableState observedFeed = fixture.Machines[0].State.Monitoring!
                .MachineTool!.FeedOverride!;
            Task firstDisposal = fixture.Machines[0].DisposeAsync().AsTask();
            Task secondDisposal = fixture.Machines[0].DisposeAsync().AsTask();
            try
            {
                await cancelled.Task.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(firstDisposal.IsCompleted, Is.False);
                Assert.That(secondDisposal.IsCompleted, Is.False);
                await using var gms = new GmsClient(session, fixture.Telemetry);
                Assert.That((await gms.GetResults(fixture.Systems[1].NodeId).GetLatestAsync(ct)
                    .ConfigureAwait(false)).ResultMetaData!.ResultId, Is.EqualTo("Result2"));
                release.TrySetResult(true);
                await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(ct).ConfigureAwait(false);
                Assert.That(() => pending, Throws.InstanceOf<OperationCanceledException>());
                Assert.That(observedFeed.WrappedValue.TryGetValue(out double retainedFeed), Is.True);
                Assert.That(retainedFeed,
                    Is.EqualTo(81.0), "A provider that finishes after cancellation must not publish its late sample.");
                await AssertMissingAsync(session, fixture.Machines[0].NodeId, ct).ConfigureAwait(false);
                await using var tools = new MachineToolClient(session, fixture.Telemetry);
                AssertValue((await tools.ReadSnapshotAsync(fixture.Machines[1].NodeId, ct).ConfigureAwait(false))
                    .FeedOverride, 82.0);
                Assert.That(fixture.Cranes.All(value => !value.IsDisposed), Is.True);
            }
            finally
            {
                release.TrySetResult(true);
                await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A closed real session loses metadata, transient files and WMTP admission, never another client's state.
        /// </summary>
        [Test]
        public async Task ClosingOneSessionReleasesOnlyItsMetadataFilesAndPendingWmtpCallAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ClientSession first = await fixture.ConnectAsync("ClosingOwner").ConfigureAwait(false);
            ClientSession second = await fixture.ConnectAsync("SurvivingOwner").ConfigureAwait(false);
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = window.Token;
            var firstMetadata = new GMSResultManagementTypeClient(first,
                fixture.Systems[0].State.ResultManagement!.NodeId, fixture.Telemetry);
            var secondMetadata = new GMSResultManagementTypeClient(second,
                fixture.Systems[0].State.ResultManagement!.NodeId, fixture.Telemetry);
            (uint firstHandle, _, _) = await firstMetadata.GetLatestResultAsync(0, ct).ConfigureAwait(false);
            (uint secondHandle, _, _) = await secondMetadata.GetLatestResultAsync(0, ct).ConfigureAwait(false);
            ServiceResultException foreign = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await secondMetadata.ReleaseResultHandleAsync(firstHandle, ct).ConfigureAwait(false))!;
            Assert.That(foreign.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            var firstTransfer = new TemporaryFileTransferTypeClient(first,
                fixture.Systems[0].State.ResultManagement!.ResultTransfer!.NodeId, fixture.Telemetry);
            var secondTransfer = new TemporaryFileTransferTypeClient(second,
                fixture.Systems[1].State.ResultManagement!.ResultTransfer!.NodeId, fixture.Telemetry);
            (NodeId abandoned, _, _) = await firstTransfer.GenerateFileForReadAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "Result1" }), ct)
                .ConfigureAwait(false);
            (NodeId retained, uint retainedHandle, _) = await secondTransfer.GenerateFileForReadAsync(
                Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "Result2" }), ct)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<IWmtpRecordStore>(MockBehavior.Strict);
            int calls = 0;
            store.Setup(value => value.ReadAsync(It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery _, CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        using CancellationTokenRegistration registration =
                            token.Register(() => cancelled.TrySetResult(true));
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                    }
                    return new[] { CompanionSuiteData.Record(3) }.ToArrayOf();
                });
            var device = new Mock<IWmtpDeviceProvider>(MockBehavior.Strict);
            device.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WmtpDeviceSnapshot>(new WmtpDeviceSnapshot
                {
                    Observation = CompanionSuiteData.Value(Variant.Null)
                }));
            var authorization = new Mock<IWmtpAuthorization>(MockBehavior.Strict);
            authorization.Setup(value => value.AuthorizeAsync(It.IsAny<WmtpRequest>(), It.IsAny<WmtpOperation>(),
                It.IsAny<CancellationToken>())).Returns(new ValueTask<StatusCode>(StatusCodes.Good));
            await using WmtpPeripheral peripheral = await fixture.Context.AddWirelessPeripheral("SessionPeripheral")
                .WithProvider(device.Object).WithAuthorization(authorization.Object).WithWorkRecords(store.Object)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "SessionPeripheral"))
                .BuildAsync(ct).ConfigureAwait(false);
            await using var firstTools = new MachineToolClient(first, fixture.Telemetry);
            await using var secondTools = new MachineToolClient(second, fixture.Telemetry);
            WmtpRecordClient records = await firstTools.GetWirelessPeripheral(peripheral.NodeId)
                .GetWorkRecordsAsync(ct).ConfigureAwait(false);
            Task<ArrayOf<WMTPOutputDataType>> pending = records.ReadAsync(new WmtpRecordQuery(),
                cancellationToken: ct).AsTask();
            try
            {
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                await first.CloseAsync(ct).ConfigureAwait(false);
                ServiceResultException released = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await secondMetadata.ReleaseResultHandleAsync(firstHandle, ct).ConfigureAwait(false))!;
                Assert.That(released.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(await secondMetadata.ReleaseResultHandleAsync(secondHandle, ct).ConfigureAwait(false),
                    Is.Zero);
                await AssertMissingAsync(second, abandoned, ct).ConfigureAwait(false);
                var file = new FileTypeClient(second, retained, fixture.Telemetry);
                Assert.That((await file.ReadAsync(retainedHandle, 10, ct).ConfigureAwait(false)).ToArray(),
                    Is.EqualTo(new byte[] { 2, 7, 9 }));
                await file.CloseAsync(retainedHandle, ct).ConfigureAwait(false);
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                release.TrySetResult(true);
                Assert.That(() => pending,
                    Throws.InstanceOf<ServiceResultException>().Or.InstanceOf<OperationCanceledException>());
                WmtpRecordClient surviving = await secondTools.GetWirelessPeripheral(peripheral.NodeId)
                    .GetWorkRecordsAsync(ct).ConfigureAwait(false);
                Assert.That((await surviving.ReadAsync(new WmtpRecordQuery(), cancellationToken: ct)
                    .ConfigureAwait(false))[0].ActualValue, Is.EqualTo(203.0));
                AssertValue((await secondTools.ReadSnapshotAsync(fixture.Machines[1].NodeId, ct)
                    .ConfigureAwait(false)).ActiveProgramName, "CELL2.NC");
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException)
                {
                    // A closed transport must not turn its abandoned operation into a successful protocol response.
                }
            }
        }

        /// <summary>
        /// Constructs a native retry candidate with a unique result store and a selected optional loading capability.
        /// </summary>
        private static GmsBuilder RetryGms(CompanionSuiteFixture fixture)
        {
            var encoding = ServiceMessageContext.Create(fixture.Telemetry);
            encoding.NamespaceUris = fixture.Context.Context.NamespaceUris;
            encoding.Factory.Builder.AddGmsTypeSystem().Commit();
            return fixture.Context.AddGms("Rollback")
                .WithProvider(new InMemoryGmsProvider(new GmsSystemSnapshot
                {
                    Loading = new GmsLoadingObservation { LoadStatus = CompanionSuiteData.Value(Variant.From(1u)) }
                }))
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, "Rollback"))
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Loading })
                .WithResults(new InMemoryGmsResultStore(encoding), new GmsResultBindingOptions
                {
                    PublishedResults = 1,
                    IncludeUsedTools = true,
                    EnableFileTransfer = true
                });
        }

        /// <summary>
        /// Supplies independent accepted-job state with deliberately identical externally assigned order IDs.
        /// </summary>
        private static MachineToolJobSource JobSource(string program)
        {
            return new MachineToolJobSource(new MachineToolJobSourceOptions
            {
                Jobs =
                [
                    new MachineToolJobSourceDescriptor
                    {
                        JobOrderId = "SameOrder", WorkMasterId = "SameWorkMaster", ProgramName = program
                    }
                ]
            });
        }

        /// <summary>
        /// Explicitly passes the same source to both protocol provider facets and retained observations.
        /// </summary>
        private static MachineToolBuilder<MachineToolState> JobMachine(
            IMachineryBuildContext context, string name, MachineToolJobSource source)
        {
            return context.AddMachineTool(name).WithProvider(source)
                .WithIdentification(identity => CompanionSuiteData.Identify(identity, name))
                .WithFeatures(MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan)
                .WithJobManagement(new MachineToolJobManagementOptions
                {
                    Receiver = source, ResponseProvider = source, Catalog = source
                });
        }

        /// <summary>
        /// Requires a real service-level missing-node result, rather than only absence from an in-memory child list.
        /// </summary>
        private static async Task AssertMissingAsync(ClientSession session, NodeId node, CancellationToken ct)
        {
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = node, AttributeId = Attributes.NodeClass }], ct).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }
    }
}
