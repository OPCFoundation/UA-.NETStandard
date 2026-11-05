/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PackML.Server;
using Unit = Opc.Ua.PackML.Server.PackMLUnit;

namespace Opc.Ua.PackML.Tests
{
    [TestFixture]
    [Category("PackML")]
    public sealed class PackMLUnitTests
    {
        private static PackMLRequest Command(PackMLCommand command) =>
            new() { Operation = PackMLOperation.Command, Command = command };

        private static void Good(ServiceResult result) =>
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());

        /// <summary>
        /// Collects every instance below a node.
        /// </summary>
        private static List<BaseInstanceState> Descendants(ISystemContext context, NodeState root)
        {
            var result = new List<BaseInstanceState>();
            var children = new List<BaseInstanceState>();
            root.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                result.Add(child);
                result.AddRange(Descendants(context, child));
            }
            return result;
        }

        [Test]
        public async Task EveryCommandSeparatesAcceptanceFromCorrelatedCompletion()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            (PackMLCommand Command, uint Acting, uint Stable)[] sequence =
            [
                (PackMLCommand.Reset, PackMLStateNumbers.Resetting, PackMLStateNumbers.Idle),
                (PackMLCommand.Start, PackMLStateNumbers.Starting, PackMLStateNumbers.Execute),
                (PackMLCommand.Hold, PackMLStateNumbers.Holding, PackMLStateNumbers.Held),
                (PackMLCommand.Unhold, PackMLStateNumbers.Unholding, PackMLStateNumbers.Execute),
                (PackMLCommand.Suspend, PackMLStateNumbers.Suspending, PackMLStateNumbers.Suspended),
                (PackMLCommand.Unsuspend, PackMLStateNumbers.Unsuspending, PackMLStateNumbers.Execute),
                (PackMLCommand.ToComplete, PackMLStateNumbers.Completing, PackMLStateNumbers.Complete),
                (PackMLCommand.Reset, PackMLStateNumbers.Resetting, PackMLStateNumbers.Idle),
                (PackMLCommand.Stop, PackMLStateNumbers.Stopping, PackMLStateNumbers.Stopped),
                (PackMLCommand.Abort, PackMLStateNumbers.Aborting, PackMLStateNumbers.Aborted),
                (PackMLCommand.Clear, PackMLStateNumbers.Clearing, PackMLStateNumbers.Stopped)
            ];
            long previousId = 0;
            foreach (var step in sequence)
            {
                Good(await unit.RequestAsync(context, Command(step.Command)));
                long id = unit.PendingRequestId;
                Assert.That(id, Is.GreaterThan(previousId));
                Assert.That(provider.Executed[^1].RequestId, Is.EqualTo(id));
                Assert.That(unit.CurrentState, Is.EqualTo(step.Acting), step.Command.ToString());
                Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.True);
                Assert.That((await unit.CompleteActingStateAsync(id + 1, step.Acting)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That((await unit.CompleteActingStateAsync(id, step.Stable)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(unit.CurrentState, Is.EqualTo(step.Acting));
                Good(await unit.CompleteActingStateAsync(id, step.Acting));
                Assert.That(unit.CurrentState, Is.EqualTo(step.Stable));
                Assert.That(unit.PendingRequestId, Is.Zero);
                Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.False);
                Assert.That((await unit.CompleteActingStateAsync(id, step.Acting)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState), "duplicate completion");
                previousId = id;
            }
            Assert.That(provider.Executed.Select(r => r.Command), Is.EqualTo(sequence.Select(s => s.Command)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyAdministrationPermissionsAreRejectedBeforeUnitCreation(bool useNullArray)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var options = new PackMLUnitOptions
            {
                AdministrationRolePermissions = useNullArray ? default : ArrayOf<RolePermissionType>.Empty
            };
            var provider = new EquipmentProvider();

            ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(
                async () => await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider, options));

            Assert.That(error!.Message, Does.Contain("explicit role"));
            Assert.That(provider.Executed, Is.Empty);
        }

        [TestCase("opc.tcp://localhost:62563/PackML", UriHostNameType.IPv4, "127.0.0.1")]
        [TestCase("opc.tcp://LOCALHOST:62563/PackML", UriHostNameType.IPv4, "127.0.0.1")]
        [TestCase("opc.tcp://127.0.0.1:62563/PackML", UriHostNameType.IPv4, "127.0.0.1")]
        [TestCase("opc.tcp://[::1]:62563/PackML", UriHostNameType.IPv6, "[::1]")]
        public void SampleEndpointUsesNumericLoopback(
            string endpoint, UriHostNameType expectedType, string expectedHost)
        {
            var normalized = new Uri(global::PackMLSample.SampleEndpoint.Normalize(endpoint));

            Assert.That(normalized.IsLoopback, Is.True);
            Assert.That(normalized.HostNameType, Is.EqualTo(expectedType));
            Assert.That(normalized.Host, Is.EqualTo(expectedHost));
            Assert.That(normalized.Port, Is.EqualTo(62563));
            Assert.That(normalized.AbsolutePath, Is.EqualTo("/PackML"));
        }

        [TestCase("opc.tcp://0.0.0.0:62563/PackML")]
        [TestCase("opc.tcp://[::]:62563/PackML")]
        [TestCase("opc.tcp://192.0.2.1:62563/PackML")]
        [TestCase("opc.tcp://example.com:62563/PackML")]
        [TestCase("https://localhost:62563/PackML")]
        [TestCase("/PackML")]
        public void SampleEndpointRejectsNonLoopbackOrInvalidEndpoints(string endpoint)
        {
            Assert.Throws<ArgumentException>(() => global::PackMLSample.SampleEndpoint.Normalize(endpoint));
        }

        [Test]
        public async Task NestedMachinesActivateAndDeactivateWithTheirParents()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            PackMLMachineStateMachineState machine = unit.Node.BaseStateMachine.Required().MachineState.Required();
            PackMLExecuteStateMachineState execute = machine.ExecuteState.Required();
            Assert.That(machine.CurrentState.Required().StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(execute.CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            Assert.That(execute.CurrentState.Required().StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Abort)));
            Assert.That(machine.CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(execute.CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborting));
        }

        [Test]
        public async Task CustomModesConfirmBooleanRequestAndApplyConfiguredStateSubset()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            var options = new PackMLUnitOptions
            {
                Modes =
                [
                    new PackMLMode(),
                    new PackMLMode
                    {
                        Value = 7, Name = "Clean",
                        States = [PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped,
                            PackMLStateNumbers.Aborting, PackMLStateNumbers.Aborted]
                    }
                ]
            };
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), provider, options);
            Good(await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetUnitMode, Mode = 7 }));
            Assert.That(unit.Node.Status.Required().UnitModeRequested.Required().Value, Is.True);
            Assert.That(unit.Node.Status.Required().UnitModeRequested.Required().DataType,
                Is.EqualTo(Ua.DataTypeIds.Boolean));
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().Value, Is.EqualTo(1));
            Assert.That(unit.Node.Status.Required().UnitModeChangeInProcess.Required().Value, Is.True);
            Assert.That((await unit.RequestAsync(context, Command(PackMLCommand.Reset))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            provider.Snapshot = provider.Snapshot with { CurrentMode = 7 };
            await unit.RefreshAsync();
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().Value, Is.EqualTo(7));
            Assert.That(unit.Node.Status.Required().UnitModeRequested.Required().Value, Is.False);
            Assert.That(unit.Node.Status.Required().UnitModeChangeInProcess.Required().Value, Is.False);
            Assert.That(unit.Node.Status.Required().UnitSupportedModes.Required().Value,
                Is.EqualTo(unit.ModeDataType.NodeId));
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().DataType,
                Is.EqualTo(unit.ModeDataType.NodeId));
            Assert.That((await unit.RequestAsync(context, Command(PackMLCommand.Reset))).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState), "excluded Running/Resetting graph");
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
        }

        [TestCase(0f, true)]
        [TestCase(1000f, true)]
        [TestCase(-0.01f, false)]
        [TestCase(1000.01f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        public async Task SpeedBoundariesNeverFabricateActualSpeed(float speed, bool valid)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            ServiceResult result = await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = speed });
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)(valid ? StatusCodes.Good : StatusCodes.BadInvalidArgument)));
            Assert.That(provider.Executed, Has.Count.EqualTo(valid ? 1 : 0));
            if (valid)
            {
                Assert.That(unit.Node.Status.Required().MachSpeed.Required().Value, Is.EqualTo(speed));
            }
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Value, Is.EqualTo(12.5f));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Timestamp,
                Is.EqualTo(provider.Snapshot.SourceTimestamp));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task DeniedOrRejectedRequestDoesNotPublishSetpoint(bool deny)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            Good(await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 10 }));
            provider.Executed.Clear();
            if (deny)
            {
                provider.Authorization = new ServiceResult(StatusCodes.BadUserAccessDenied);
            }
            else
            {
                provider.Acceptance = new ServiceResult(StatusCodes.BadOutOfService);
            }
            ServiceResult result = await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetMachSpeed, MachineSpeed = 99 });
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)(deny ? StatusCodes.BadUserAccessDenied : StatusCodes.BadOutOfService)));
            Assert.That(provider.Executed, Has.Count.EqualTo(deny ? 0 : 1));
            Assert.That(unit.Node.Status.Required().MachSpeed.Required().Value, Is.EqualTo(10f));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Value, Is.EqualTo(12.5f));
        }

        [TestCase(PackMLOperation.SetProduct)]
        [TestCase(PackMLOperation.SetParameter)]
        [TestCase(PackMLOperation.RemoteCommand)]
        public async Task NullStructureEntriesAreRejectedBeforeAuthorization(PackMLOperation operation)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions { EnableRemoteCommand = true });
            PackMLRequest request = operation switch
            {
                PackMLOperation.SetProduct => new PackMLRequest { Operation = operation, Products = [null!] },
                PackMLOperation.SetParameter => new PackMLRequest { Operation = operation, Parameters = [null!] },
                _ => new PackMLRequest { Operation = operation, RemoteInterfaces = [null!] }
            };
            ServiceResult result = await unit.RequestAsync(context, request);
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(provider.Authorized, Is.Empty);
            Assert.That(provider.Executed, Is.Empty);
        }

        [Test]
        public async Task NestedStateMachineEventsReachTheUnitNotifier()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            int reported = 0;
            unit.Node.EventReported += (_, _, _) => reported++;
            unit.Node.SetAreEventsMonitored(context, true, true);
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Assert.That(reported, Is.GreaterThan(0), "machine transitions reach the unit");
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            int beforeStart = reported;
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Start)));
            Assert.That(reported, Is.GreaterThan(beforeStart), "execute transitions reach the unit");
        }

        [Test]
        public async Task RejectedSupersedingCommandPreservesActingSequenceAndCompletionIdentity()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            long acceptedId = unit.PendingRequestId;
            provider.Acceptance = new ServiceResult(StatusCodes.BadUserAccessDenied);
            ServiceResult rejected = await unit.RequestAsync(context, Command(PackMLCommand.Abort));
            Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            Assert.That(unit.PendingRequestId, Is.EqualTo(acceptedId));
            Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.True);
            Assert.That(unit.Node.Status.Required().StateRequested.Required().Value,
                Is.EqualTo((int)PackMLStateNumbers.Idle));
            Good(await unit.CompleteActingStateAsync(acceptedId, PackMLStateNumbers.Resetting));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
        }

        [Test]
        public async Task ConcurrentDuplicateStartExecutesProviderExactlyOnce()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions { Modes = [new PackMLMode { InitialState = PackMLInitialState.Idle }] });
            var entered = PackMLTestSupport.Signal();
            var release = PackMLTestSupport.Signal();
            provider.Execute = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct);
                return ServiceResult.Good;
            };
            Task<ServiceResult> first = unit.RequestAsync(context, Command(PackMLCommand.Start)).AsTask();
            await entered.Task;
            Task<ServiceResult> second = unit.RequestAsync(context, Command(PackMLCommand.Start)).AsTask();
            try
            {
                Assert.That((await second.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle), "not accepted yet");
                Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Value, Is.EqualTo(12.5f));
                await using Unit other = await Unit.CreateAsync(
                    context, null, PackMLTestSupport.Name(context, "Other"), new EquipmentProvider());
                Good(await other.RequestAsync(context, Command(PackMLCommand.Reset))
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.That(other.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            }
            finally
            {
                release.TrySetResult(true);
            }
            Good(await first);
            Assert.That((await second).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Starting));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Execute));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationOrShutdownCancelsProviderWithoutPublishing(bool shutdown)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            var entered = PackMLTestSupport.Signal();
            provider.Execute = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return ServiceResult.Good;
            };
            using var cancellation = new CancellationTokenSource();
            Task<ServiceResult> request =
                unit.RequestAsync(context, Command(PackMLCommand.Reset), cancellation.Token).AsTask();
            await entered.Task;
            if (shutdown)
            {
                await Assert.ThatAsync(
                    async () => await unit.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)),
                    Throws.InvalidOperationException);
                Assert.That(request.IsCompleted, Is.False);
                await unit.ShutdownAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                cancellation.Cancel();
            }
            await Assert.ThatAsync(() => request, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(unit.PendingRequestId, Is.Zero);
            Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.False);
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
        }

        [TestCase(PackMLOperation.RemoteCommand)]
        [TestCase(PackMLOperation.SetInterlock)]
        public async Task UnsupportedOptionalOperationsNeverReachProvider(PackMLOperation operation)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            Assert.That((await unit.RequestAsync(context, new PackMLRequest { Operation = operation })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(provider.Authorized, Is.Empty);
            Assert.That(provider.Executed, Is.Empty);
        }

        [Test]
        public async Task ConfiguredOptionalOperationsPreservePayloadAndEquipmentReadiness()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider
            {
                Snapshot = new PackMLEquipmentSnapshot
                {
                    MaterialReadiness = [true, false],
                    CurrentMachineSpeed = 5,
                    StatusCode = StatusCodes.UncertainDataSubNormal,
                    SourceTimestamp = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc)
                }
            };
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions { MaterialCount = 2, EnableRemoteCommand = true });
            Good(await unit.RequestAsync(context,
                new PackMLRequest
                {
                    Operation = PackMLOperation.SetInterlock,
                    InterlockId = 1,
                    InterlockState = true
                }));
            Good(await unit.RequestAsync(context, new PackMLRequest { Operation = PackMLOperation.RemoteCommand }));
            Assert.That(provider.Executed[0].InterlockId, Is.EqualTo(1));
            Assert.That(provider.Executed[0].InterlockState, Is.True);
            Assert.That(provider.Executed[1].Operation, Is.EqualTo(PackMLOperation.RemoteCommand));
            Assert.That(unit.Node.Status.Required().MaterialInterlocked.Required().Value, Is.True);
            Assert.That(unit.Node.Status.Required().MaterialInterlock.Required().StatusCode,
                Is.EqualTo(provider.Snapshot.StatusCode));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().StatusCode,
                Is.EqualTo(provider.Snapshot.StatusCode));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Timestamp,
                Is.EqualTo(provider.Snapshot.SourceTimestamp));
            Assert.That((await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetInterlock, InterlockId = 2 })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(provider.Executed, Has.Count.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(5)]
        public async Task GeneratedFactoriesResolveNamespaceUrisAfterNamespaceIndexChanges(int padding)
        {
            SystemContext context = PackMLTestSupport.CreateContext(padding);
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            Assert.That(unit.Node.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                ObjectTypeIds.PackMLBaseObjectType, context.NamespaceUris)));
            Assert.That(unit.Node.BrowseName.NamespaceIndex, Is.EqualTo(1 + padding));
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().DataType.NamespaceIndex,
                Is.EqualTo(unit.Node.BrowseName.NamespaceIndex));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
        }

        [Test]
        public async Task ConfiguredStorePersistsCountersAcrossUnitRestartWithoutReplayingCommands()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var clock = new ManualTimeProvider();
            var store = new MemoryAdministrationStore();
            var options = new PackMLUnitOptions { TimeProvider = clock, AdministrationStore = store };
            Unit first = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context, "First"),
                new EquipmentProvider(), options);
            await using (first)
            {
                Good(await first.RequestAsync(context, Command(PackMLCommand.Reset)));
                Good(await first.CompleteActingStateAsync(first.PendingRequestId, PackMLStateNumbers.Resetting));
                clock.Advance(TimeSpan.FromSeconds(9));
                await first.CheckpointAsync();
            }
            Assert.That(store.Saves, Is.GreaterThanOrEqualTo(1));
            Assert.That(store.Snapshot!.ModeCumulativeTime[1], Is.EqualTo(9));
            clock.Advance(TimeSpan.FromDays(2));
            var provider = new EquipmentProvider();
            await using Unit second = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context, "Second"),
                provider, options);
            Assert.That(store.Loads, Is.EqualTo(2));
            Assert.That(second.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(second.PendingRequestId, Is.Zero);
            Assert.That(provider.Executed, Is.Empty);
            PackMLAdministrationSnapshot restored = await second.Administration.CaptureAsync();
            Assert.That(restored.ModeCumulativeTime[1], Is.EqualTo(9));
            Assert.That(restored.ModeCurrentTime[1], Is.Zero);
            Assert.That(restored.AccTimeSinceReset, Is.EqualTo(9));
        }

        [Test]
        public async Task ProductAndParameterOperationsPublishOnlyAcceptedStructuredPayloads()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            Good(await unit.RequestAsync(context, new PackMLRequest
            {
                Operation = PackMLOperation.SetProduct,
                Products = [new PackMLProductDataType { ProductID = 42 }]
            }));
            Good(await unit.RequestAsync(context, new PackMLRequest
            {
                Operation = PackMLOperation.SetParameter,
                Parameters = [new PackMLDescriptorDataType { ID = 9, Name = "Temperature", Value = 23.5f }]
            }));
            Assert.That(unit.Node.Status.Required().Product.Required().Value[0].ProductID,
                Is.EqualTo(42));
            ArrayOf<PackMLDescriptorDataType> parameters =
                unit.Node.Status.Required().Parameter.Required().Value;
            Assert.That(parameters[0].ID, Is.EqualTo(9));
            Assert.That(parameters[0].Name, Is.EqualTo("Temperature"));
            Assert.That(parameters[0].Value, Is.EqualTo(23.5f));
            Assert.That(provider.Executed[0].Products[0].ProductID, Is.EqualTo(42));
            Assert.That(provider.Executed[1].Parameters[0].Value, Is.EqualTo(23.5f));
            provider.Acceptance = new ServiceResult(StatusCodes.BadOutOfService);
            Assert.That((await unit.RequestAsync(context, new PackMLRequest
            {
                Operation = PackMLOperation.SetProduct,
                Products = [new PackMLProductDataType { ProductID = 99 }]
            })).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
            Assert.That(unit.Node.Status.Required().Product.Required().Value[0].ProductID,
                Is.EqualTo(42));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(-1)]
        public async Task UnknownModeDoesNotAuthorizeOrMutatePublishedMode(int mode)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            Assert.That((await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.SetUnitMode, Mode = mode })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(provider.Authorized, Is.Empty);
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().Value, Is.EqualTo(1));
            Assert.That(unit.Node.Status.Required().UnitModeRequested.Required().Value, Is.False);
        }

        [Test]
        public async Task InvalidSnapshotRejectsWholeSampleBeforePublication()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            provider.Snapshot = provider.Snapshot with { CurrentMachineSpeed = 88, MaterialReadiness = [true] };
            await Assert.ThatAsync(async () => await unit.RefreshAsync(), Throws.InvalidOperationException);
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().Value, Is.EqualTo(12.5f));
            Assert.That(unit.Node.Status.Required().UnitModeCurrent.Required().Value, Is.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
        }

        [TestCase(99u)]
        [TestCase(PackMLStateNumbers.Starting)]
        public async Task InvalidFutureModeIsRejectedBeforeEquipmentExecution(uint invalidState)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            var options = new PackMLUnitOptions
            {
                Modes =
                [
                    new PackMLMode(),
                    new PackMLMode
                    {
                        Value = 2,
                        Name = "Invalid",
                        States =
                        [
                            PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped,
                            PackMLStateNumbers.Running, invalidState
                        ]
                    }
                ]
            };
            await Assert.ThatAsync(async () => await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), provider, options), Throws.ArgumentException);
            Assert.That(provider.Authorized, Is.Empty);
            Assert.That(provider.Executed, Is.Empty);
        }

        [Test]
        public async Task ExecuteStateWithoutRunningParentIsRejectedBeforeEquipmentExecution()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            var options = new PackMLUnitOptions
            {
                Modes =
                [
                    new PackMLMode
                    {
                        States =
                        [
                            PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped, PackMLStateNumbers.Execute
                        ]
                    }
                ]
            };

            await Assert.ThatAsync(async () => await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), provider, options), Throws.ArgumentException);
            Assert.That(provider.Authorized, Is.Empty);
            Assert.That(provider.Executed, Is.Empty);
        }

        [Test]
        public async Task StateResetPreservesProductionCountsAndAdministrativeResetRequiresAuthorization()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            var clock = new ManualTimeProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions
                {
                    TimeProvider = clock,
                    AdministrationExtents = new PackMLAdministrationExtents { ProcessedCounts = 1 }
                });
            await unit.PublishAdministrationAsync(new PackMLAdministrationData
            {
                ProcessedCounts = [new PackMLCountDataType { Count = 5, AccCount = 100 }],
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow
            });
            clock.Advance(TimeSpan.FromSeconds(30));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            ArrayOf<PackMLCountDataType> counts =
                unit.Node.Admin.Required().ProdProcessedCount.Required().Value;
            Assert.That(counts[0].Count, Is.EqualTo(5));
            Assert.That(counts[0].AccCount, Is.EqualTo(100));
            Assert.That((await unit.Administration.CaptureAsync()).AccTimeSinceReset, Is.EqualTo(30));
            provider.Authorization = new ServiceResult(StatusCodes.BadUserAccessDenied);
            Assert.That((await unit.RequestAsync(context,
                new PackMLRequest { Operation = PackMLOperation.ResetCounters })).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That((await unit.Administration.CaptureAsync()).AccTimeSinceReset, Is.EqualTo(30));
            provider.Authorization = ServiceResult.Good;
            Good(await unit.RequestAsync(context, new PackMLRequest { Operation = PackMLOperation.ResetCounters }));
            counts = unit.Node.Admin.Required().ProdProcessedCount.Required().Value;
            Assert.That(counts[0].Count, Is.Zero);
            Assert.That(counts[0].AccCount, Is.EqualTo(100));
            Assert.That((await unit.Administration.CaptureAsync()).AccTimeSinceReset, Is.Zero);
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
        }

        [Test]
        public async Task RefreshAwaitsAsynchronousMonitorSinksInOrderWithoutBlockingTheCaller()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            PackMLStatusObjectState status = unit.Node.Status.Required();
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            var notified = new ConcurrentQueue<string>();
            status.CurMachSpeed.Required().OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                notified.Enqueue(nameof(PackMLStatusObjectState.CurMachSpeed));
                entered.TrySetResult(true);
                // Bounded so that a blocking regression fails instead of hanging.
                await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            };
            status.EquipmentBlocked.Required().OnStateChangedAsync = (_, _, _, _) =>
            {
                notified.Enqueue(nameof(PackMLStatusObjectState.EquipmentBlocked));
                return default;
            };
            provider.Snapshot = new PackMLEquipmentSnapshot
            {
                CurrentMode = 1,
                CurrentMachineSpeed = 20f,
                EquipmentBlocked = true,
                SourceTimestamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc),
                StatusCode = StatusCodes.Good
            };

            ValueTask refresh = unit.RefreshAsync();
            bool returnedPending = !refresh.IsCompleted;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(returnedPending, Is.True);
            Assert.That(notified, Is.EqualTo(new[] { nameof(PackMLStatusObjectState.CurMachSpeed) }));
            release.TrySetResult(true);
            await refresh.AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(notified, Is.EqualTo(new[]
            {
                nameof(PackMLStatusObjectState.CurMachSpeed),
                nameof(PackMLStatusObjectState.EquipmentBlocked)
            }));
            Assert.That(status.CurMachSpeed.Required().Value, Is.EqualTo(20f));
            Assert.That(status.EquipmentBlocked.Required().Value, Is.True);
        }

        [Test]
        public async Task AcceptedCommandAwaitsAsynchronousStateMachineSinksWithoutBlockingTheCaller()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            int notifications = 0;
            NodeStateChangedAsyncHandler sink = async (_, _, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref notifications) == 1)
                {
                    entered.TrySetResult(true);
                    // Bounded so that a blocking regression fails instead of hanging.
                    await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
                }
            };
            foreach (BaseInstanceState node in Descendants(context, unit.Node.BaseStateMachine.Required()))
            {
                node.OnStateChangedAsync = sink;
            }

            ValueTask<ServiceResult> request = unit.RequestAsync(context, Command(PackMLCommand.Reset));
            bool returnedPending = !request.IsCompleted;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(returnedPending, Is.True);
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            release.TrySetResult(true);
            Good(await request.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(unit.PendingRequestId, Is.EqualTo(provider.Executed[0].RequestId));
            Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.True);
        }

        [Test]
        public async Task FailedIndicatorNotificationRestoresPriorIndicatorsWithoutExecutingProvider()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            PackMLStatusObjectState status = unit.Node.Status.Required();
            BaseVariableState requested = status.StateRequested.Required();
            BaseVariableState inProcess = status.StateChangeInProcess.Required();
            Variant requestedBefore = requested.WrappedValue;
            Variant inProcessBefore = inProcess.WrappedValue;
            StatusCode inProcessStatusBefore = inProcess.StatusCode;
            DateTimeUtc inProcessTimestampBefore = inProcess.Timestamp;
            uint stateBefore = unit.CurrentState;
            var notificationError = new InvalidOperationException("Indicator subscriber failed.");
            int notifications = 0;
            // Fails only for the pending value, so publishing the restored value succeeds.
            inProcess.OnStateChangedAsync = (_, node, _, _) =>
            {
                Interlocked.Increment(ref notifications);
                return ((BaseVariableState)node).WrappedValue == Variant.From(true)
                    ? throw notificationError
                    : default;
            };

            InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await unit.RequestAsync(context, Command(PackMLCommand.Reset))))!;

            Assert.That(failure, Is.SameAs(notificationError));
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(provider.Executed, Is.Empty);
            Assert.That(unit.CurrentState, Is.EqualTo(stateBefore));
            Assert.That(unit.PendingRequestId, Is.Zero);
            Assert.That(requested.WrappedValue, Is.EqualTo(requestedBefore));
            Assert.That(inProcess.WrappedValue, Is.EqualTo(inProcessBefore));
            Assert.That(inProcess.StatusCode, Is.EqualTo(inProcessStatusBefore));
            Assert.That(inProcess.Timestamp, Is.EqualTo(inProcessTimestampBefore));

            inProcess.OnStateChangedAsync = null;
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(inProcess.WrappedValue, Is.EqualTo(Variant.From(true)));
        }

        [Test]
        public async Task FailedIndicatorRestorationReportsAdmissionAndRestorationFailures()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            PackMLStatusObjectState status = unit.Node.Status.Required();
            BaseVariableState requested = status.StateRequested.Required();
            BaseVariableState inProcess = status.StateChangeInProcess.Required();
            Variant requestedBefore = requested.WrappedValue;
            Variant inProcessBefore = inProcess.WrappedValue;
            var admissionError = new InvalidOperationException("Pending indicator subscriber failed.");
            var restorationError = new InvalidOperationException("Restored indicator subscriber failed.");
            int notifications = 0;
            inProcess.OnStateChangedAsync = (_, _, _, _) =>
                Interlocked.Increment(ref notifications) == 1
                    ? throw admissionError
                    : throw restorationError;

            AggregateException failure = (await Assert.ThrowsAsync<AggregateException>(
                async () => await unit.RequestAsync(context, Command(PackMLCommand.Reset))))!;

            Assert.That(failure.InnerExceptions, Is.EqualTo(new Exception[] { admissionError, restorationError }));
            Assert.That(provider.Executed, Is.Empty);
            Assert.That(unit.PendingRequestId, Is.Zero);
            Assert.That(requested.WrappedValue, Is.EqualTo(requestedBefore));
            Assert.That(inProcess.WrappedValue, Is.EqualTo(inProcessBefore));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedIndicatorRestorationAfterRejectedAcceptanceReportsProviderResult(bool uncertain)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            StatusCode acceptance = uncertain ? StatusCodes.Uncertain : StatusCodes.BadOutOfService;
            var provider = new EquipmentProvider { Acceptance = new ServiceResult(acceptance) };
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            PackMLStatusObjectState status = unit.Node.Status.Required();
            BaseVariableState requested = status.StateRequested.Required();
            BaseVariableState inProcess = status.StateChangeInProcess.Required();
            Variant requestedBefore = requested.WrappedValue;
            Variant inProcessBefore = inProcess.WrappedValue;
            var restorationError = new InvalidOperationException("Restored indicator subscriber failed.");
            inProcess.OnStateChangedAsync = (_, _, _, _) =>
                provider.Executed.Count > 0 ? throw restorationError : default(ValueTask);

            AggregateException failure = (await Assert.ThrowsAsync<AggregateException>(
                async () => await unit.RequestAsync(context, Command(PackMLCommand.Reset))))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Exception outcome = failure.InnerExceptions[0];
            if (StatusCode.IsBad(acceptance))
            {
                Assert.That(outcome, Is.TypeOf<ServiceResultException>());
            }
            else
            {
                Assert.That(outcome, Is.TypeOf<InvalidOperationException>());
                outcome = outcome.InnerException!;
                Assert.That(outcome, Is.TypeOf<ServiceResultException>());
            }
            Assert.That(((ServiceResultException)outcome).StatusCode, Is.EqualTo(acceptance));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(restorationError));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.PendingRequestId, Is.Zero);
            Assert.That(requested.WrappedValue, Is.EqualTo(requestedBefore));
            Assert.That(inProcess.WrappedValue, Is.EqualTo(inProcessBefore));
        }

        [Test]
        public async Task ConfiguredCapabilitiesExcludeDisabledOptionalConformanceUnits()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            string?[] names = unit.ConformanceUnits.Span.ToArray().Select(name => name.Name).ToArray();
            Assert.That(names, Does.Contain("PackML Base Functionality"));
            Assert.That(names, Does.Contain("PackML Admin machine speed."));
            Assert.That(names, Does.Not.Contain("PackML Admin"));
            Assert.That(names, Does.Not.Contain("PackML SetInterlock"));
            Assert.That(names, Does.Not.Contain("PackML RemoteCommand"));
            Assert.That(names, Does.Not.Contain("PackML Admin Alarm History"));
            Assert.That(unit.ServerProfiles.Count, Is.Zero);
            Assert.That(unit.Node.Status.Required().MachSpeed.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required()
                .EngineeringUnits.Required().Value.DisplayName.Text,
                Is.EqualTo("packages/min"));
            Assert.That(unit.Node.Status.Required().CurMachSpeed.Required().EURange.Required().Value.High,
                Is.EqualTo(1000));
        }

        /// <summary>
        /// Verifies every distinct server conformance unit defined in OPC 30050 v1.01 Table 63 can be enabled.
        /// </summary>
        [Test]
        public async Task ConfiguredUnitAdvertisesEveryDefinedServerConformanceUnit()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider(),
                new PackMLUnitOptions
                {
                    TagId = "All capabilities",
                    IncludeVersion = true,
                    EnableRemoteCommand = true,
                    MaterialCount = 1,
                    AdministrationExtents = new PackMLAdministrationExtents
                    {
                        Parameters = 1,
                        Alarms = 1,
                        AlarmHistory = 1,
                        Warnings = 1,
                        StopReasons = 1,
                        ConsumedCounts = 1,
                        ProcessedCounts = 1,
                        DefectiveCounts = 1
                    }
                });
            string?[] names = unit.ConformanceUnits.Span.ToArray().Select(name => name.Name).ToArray();
            Assert.That(names, Is.EquivalentTo(s_definedServerConformanceUnits));
            Assert.That(unit.ConformanceUnits.Span.ToArray().All(
                name => name.NamespaceIndex == context.NamespaceUris.GetIndex(Namespaces.PackML)), Is.True);
            Assert.That(unit.ServerProfiles.Count, Is.Zero);

            PackMLExecuteStateMachineState execute =
                unit.Node.BaseStateMachine.Required().MachineState.Required().ExecuteState.Required();
            Assert.That(execute.ToComplete.Required().BrowseName.Name, Is.EqualTo("ToComplete"));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Start)));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Starting));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.ToComplete)));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Completing));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Completing));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Complete));
        }

        [TestCase(0, 1, 1, false)]
        [TestCase(1, 0, 1, false)]
        [TestCase(1, 1, 0, false)]
        [TestCase(1, 1, 1, true)]
        public async Task CompleteProductionCountCapabilityRequiresAllThreeConfiguredCapacities(
            int consumedCapacity,
            int processedCapacity,
            int defectiveCapacity,
            bool complete)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider(),
                new PackMLUnitOptions
                {
                    AdministrationExtents = new PackMLAdministrationExtents
                    {
                        ConsumedCounts = consumedCapacity,
                        ProcessedCounts = processedCapacity,
                        DefectiveCounts = defectiveCapacity
                    }
                });
            string?[] names = unit.ConformanceUnits.Span.ToArray().Select(name => name.Name).ToArray();
            Assert.That(names.Contains("PackML Admin"), Is.EqualTo(complete));
            Assert.That(names.Contains("PackML ProdProcessedCount"), Is.EqualTo(processedCapacity > 0));
            Assert.That(names.Contains("PackML ProdDefectiveCount"), Is.EqualTo(defectiveCapacity > 0));
        }

        /// <summary>
        /// Verifies the five optional Hold transitions in the published state model.
        /// </summary>
        [TestCase(PackMLStateNumbers.Starting, Objects.PackMLExecuteStateMachineType_StartingToHolding)]
        [TestCase(PackMLStateNumbers.Suspending, Objects.PackMLExecuteStateMachineType_SuspendingToHolding)]
        [TestCase(PackMLStateNumbers.Suspended, Objects.PackMLExecuteStateMachineType_SuspendedToHolding)]
        [TestCase(PackMLStateNumbers.Unsuspending, Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding)]
        [TestCase(PackMLStateNumbers.Unholding, Objects.PackMLExecuteStateMachineType_UnholdingToHolding)]
        public async Task StateModelExtensionsExposeAndExecuteOptionalHoldTransitions(
            uint sourceState,
            uint transitionId)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));

            if (sourceState == PackMLStateNumbers.Starting)
            {
                Good(await unit.RequestAsync(context, Command(PackMLCommand.Start)));
            }
            else
            {
                Good(await unit.RequestAsync(context, Command(PackMLCommand.Start)));
                Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Starting));
                if (sourceState == PackMLStateNumbers.Unholding)
                {
                    Good(await unit.RequestAsync(context, Command(PackMLCommand.Hold)));
                    Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Holding));
                    Good(await unit.RequestAsync(context, Command(PackMLCommand.Unhold)));
                }
                else
                {
                    Good(await unit.RequestAsync(context, Command(PackMLCommand.Suspend)));
                    if (sourceState != PackMLStateNumbers.Suspending)
                    {
                        Good(await unit.CompleteActingStateAsync(
                            unit.PendingRequestId, PackMLStateNumbers.Suspending));
                        if (sourceState == PackMLStateNumbers.Unsuspending)
                        {
                            Good(await unit.RequestAsync(context, Command(PackMLCommand.Unsuspend)));
                        }
                    }
                }
            }

            Assert.That(unit.CurrentState, Is.EqualTo(sourceState));
            PackMLExecuteStateMachineState execute =
                unit.Node.BaseStateMachine.Required().MachineState.Required().ExecuteState.Required();
            NodeId expectedTransition = new(
                transitionId,
                checked((ushort)context.NamespaceUris.GetIndex(Namespaces.PackML)));
            Assert.That(execute.AvailableTransitions.Required().Value.Span.ToArray(),
                Does.Contain(expectedTransition));

            Good(await unit.RequestAsync(context, Command(PackMLCommand.Hold)));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Holding));
        }

        /// <summary>
        /// Verifies that the undefined optional conformance unit is not advertised.
        /// </summary>
        [Test]
        public async Task StateModelExtensionsConformanceUnitIsWithheld()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider());
            string?[] conformanceUnits =
                unit.ConformanceUnits.Span.ToArray().Select(name => name.Name).ToArray();
            Assert.That(conformanceUnits, Does.Not.Contain("PackML StateModel extensions"));
        }

        /// <summary>
        /// Verifies an optional Hold transition is omitted when its source state is not configured.
        /// </summary>
        [Test]
        public async Task HoldTransitionIsUnavailableWhenItsSourceStateIsNotConfigured()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var options = new PackMLUnitOptions
            {
                Modes =
                [
                    new PackMLMode
                    {
                        States =
                        [
                            PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped, PackMLStateNumbers.Running,
                            PackMLStateNumbers.Starting, PackMLStateNumbers.Execute, PackMLStateNumbers.Holding,
                            PackMLStateNumbers.Held, PackMLStateNumbers.Unholding, PackMLStateNumbers.Suspending,
                            PackMLStateNumbers.Suspended
                        ]
                    }
                ]
            };
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), new EquipmentProvider(), options);
            PackMLExecuteStateMachineState execute =
                unit.Node.BaseStateMachine.Required().MachineState.Required().ExecuteState.Required();
            ushort namespaceIndex = checked((ushort)context.NamespaceUris.GetIndex(Namespaces.PackML));
            NodeId unavailableTransition =
                new(Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding, namespaceIndex);
            Assert.That(execute.AvailableTransitions.Required().Value.Span.ToArray(),
                Does.Not.Contain(unavailableTransition));
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        public async Task ProviderReentryIsRejectedWithoutBlockingOrChangingAcceptedCommandOrder(
            int phase,
            int operation)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            int rejected = 0;
            provider.CallbackPhase = phase;
            provider.Callback = async token =>
            {
                await Task.Yield();
                await Assert.ThatAsync(
                    async () =>
                    {
                        ValueTask reentry = operation switch
                        {
                            0 => unit.RefreshAsync(token),
                            1 => unit.CheckpointAsync(token),
                            _ => unit.DisposeAsync()
                        };
                        await reentry.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    },
                    Throws.InvalidOperationException);
                rejected++;
            };
            if (phase == 2)
            {
                await unit.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(rejected, Is.EqualTo(1));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.PendingRequestId, Is.EqualTo(provider.Executed[0].RequestId));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            provider.Callback = null;
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Start)));
            Assert.That(provider.Executed, Has.Count.EqualTo(2));
            Assert.That(provider.Executed[1].RequestId, Is.GreaterThan(provider.Executed[0].RequestId));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
        }

        [Test]
        public async Task UnhandledProviderReentryRestoresIndicatorsAndAllowsTheNextCommand()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            provider.CallbackPhase = 1;
            provider.Callback = async token =>
            {
                await Task.Yield();
                await unit.RefreshAsync(token);
            };
            await Assert.ThatAsync(
                () => unit.RequestAsync(context, Command(PackMLCommand.Reset))
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InvalidOperationException);
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(unit.PendingRequestId, Is.Zero);
            Assert.That(unit.Node.Status.Required().StateChangeInProcess.Required().Value, Is.False);
            provider.Callback = null;
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            Assert.That(provider.Executed, Has.Count.EqualTo(2));
            Assert.That(unit.PendingRequestId, Is.EqualTo(provider.Executed[1].RequestId));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        public async Task SuppressedContextProviderMutationIsRejectedWithoutWaiting(int phase, bool request)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            int rejected = 0;
            provider.CallbackPhase = phase;
            provider.Callback = async token =>
            {
                Task reentry;
                using (ExecutionContext.SuppressFlow())
                {
                    reentry = Task.Run(async () =>
                    {
                        if (request)
                        {
                            Assert.That((await unit.RequestAsync(
                                context, Command(PackMLCommand.Abort), token)).StatusCode,
                                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                        }
                        else
                        {
                            await Assert.ThatAsync(
                                async () => await unit.RefreshAsync(token),
                                Throws.InstanceOf<InvalidOperationException>());
                        }
                    }, token);
                }
                await reentry.WaitAsync(TimeSpan.FromSeconds(5), token);
                rejected++;
            };
            if (phase == 2)
            {
                await unit.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(rejected, Is.EqualTo(1));
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            provider.Callback = null;
        }

        [Test]
        public async Task SuppressedContextPersistenceReentryCannotWaitBehindItsCheckpoint()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var store = new MemoryAdministrationStore();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions { AdministrationStore = store });
            store.SaveCallback = async token =>
            {
                Task reentry;
                using (ExecutionContext.SuppressFlow())
                {
                    reentry = Task.Run(async () =>
                    {
                        Assert.That((await unit.RequestAsync(
                            context, Command(PackMLCommand.Reset), token)).StatusCode,
                            Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                        await Assert.ThatAsync(
                            async () => await unit.CheckpointAsync(token),
                            Throws.InstanceOf<InvalidOperationException>());
                        await Assert.ThatAsync(async () => await unit.DisposeAsync(), Throws.InvalidOperationException);
                    }, token);
                }
                await reentry.WaitAsync(TimeSpan.FromSeconds(5), token);
            };
            await unit.CheckpointAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(store.Saves, Is.EqualTo(1));
            Assert.That(provider.Executed, Is.Empty);
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            store.SaveCallback = null;
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset)));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
        }

        [Test]
        public async Task IdleDirectDisposalClosesAdmissionAndCanBeRepeated()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            await unit.DisposeAsync();
            await unit.DisposeAsync();
            await Assert.ThatAsync(async () => await unit.RefreshAsync(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(provider.Executed, Is.Empty);
        }

        [Test]
        public async Task HostShutdownWaitsForProviderDrainAndSharesTheShutdownOutcome()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            var entered = PackMLTestSupport.Signal();
            var cancelled = PackMLTestSupport.Signal();
            var release = PackMLTestSupport.Signal();
            provider.Execute = async (_, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() => cancelled.TrySetResult(true));
                entered.TrySetResult(true);
                await release.Task;
                token.ThrowIfCancellationRequested();
                return ServiceResult.Good;
            };
            Task<ServiceResult> request = unit.RequestAsync(context, Command(PackMLCommand.Reset)).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task shutdown = unit.ShutdownAsync().AsTask();
            Task concurrentShutdown = unit.ShutdownAsync().AsTask();
            try
            {
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(concurrentShutdown.IsCompleted, Is.False);
                Assert.That(request.IsCompleted, Is.False);
                await Assert.ThatAsync(async () => await unit.DisposeAsync(), Throws.InvalidOperationException);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Task.WhenAll(shutdown, concurrentShutdown).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThatAsync(() => request, Throws.InstanceOf<OperationCanceledException>());
            await unit.DisposeAsync();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task SuppressedContextProviderDisposalIsRejectedWithoutClosingTheUnit(int phase)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            await using Unit unit = await Unit.CreateAsync(context, null, PackMLTestSupport.Name(context), provider);
            int rejected = 0;
            provider.CallbackPhase = phase;
            provider.Callback = async token =>
            {
                Task disposal;
                using (ExecutionContext.SuppressFlow())
                {
                    disposal = Task.Run(async () => await unit.DisposeAsync(), token);
                }
                await Assert.ThatAsync(
                    () => disposal.WaitAsync(TimeSpan.FromSeconds(5), token),
                    Throws.InvalidOperationException);
                rejected++;
            };
            if (phase == 2)
            {
                await unit.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            Good(await unit.RequestAsync(context, Command(PackMLCommand.Reset))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(rejected, Is.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            provider.Callback = null;
            Good(await unit.CompleteActingStateAsync(unit.PendingRequestId, PackMLStateNumbers.Resetting));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ShutdownPreservesCancellationAndCleanupFailures(bool cleanupFails)
        {
            var lifetime = new PackMLLifetime();
            var entered = PackMLTestSupport.Signal();
            var cancelled = PackMLTestSupport.Signal();
            var cancellationError = new InvalidOperationException("Cancellation callback failed.");
            var cleanupError = new InvalidOperationException("Owner cleanup failed.");
            Task operation = lifetime.RunAsync(async token =>
            {
                using CancellationTokenRegistration registration = token.Register(() =>
                {
                    cancelled.TrySetResult(true);
                    throw cancellationError;
                });
                entered.TrySetResult(true);
                await cancelled.Task;
                token.ThrowIfCancellationRequested();
            }, CancellationToken.None).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bool cleanupAttempted = false;
            AggregateException failure = (await Assert.ThrowsAsync<AggregateException>(
                async () => await lifetime.StopAsync(() =>
                {
                    cleanupAttempted = true;
                    if (cleanupFails)
                    {
                        throw cleanupError;
                    }
                    return default;
                })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5))))!;
            Assert.That(cleanupAttempted, Is.True);
            Assert.That(failure.Flatten().InnerExceptions, Does.Contain(cancellationError));
            Assert.That(failure.Flatten().InnerExceptions.Contains(cleanupError), Is.EqualTo(cleanupFails));
            Assert.That(failure.Flatten().InnerExceptions, Has.Count.EqualTo(cleanupFails ? 2 : 1));
            await Assert.ThatAsync(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task BusyAdmissionPreservesOperationsAdmittedBeforeTheProviderStarted()
        {
            await using var lifetime = new PackMLLifetime();
            var firstEntered = PackMLTestSupport.Signal();
            var beginProvider = PackMLTestSupport.Signal();
            var providerEntered = PackMLTestSupport.Signal();
            var releaseProvider = PackMLTestSupport.Signal();
            int order = 0;
            int firstOrder = 0;
            int secondOrder = 0;
            Task first = lifetime.RunAsync(async _ =>
            {
                firstEntered.TrySetResult(true);
                await beginProvider.Task;
                await lifetime.CallProviderAsync(async () =>
                {
                    providerEntered.TrySetResult(true);
                    await releaseProvider.Task;
                    return true;
                });
                firstOrder = ++order;
            }, CancellationToken.None).AsTask();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task second = lifetime.RunAsync(_ =>
            {
                secondOrder = ++order;
                return default;
            }, CancellationToken.None).AsTask();
            beginProvider.TrySetResult(true);
            await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                Assert.That(second.IsCompleted, Is.False);
                await Assert.ThatAsync(
                    async () => await lifetime.RunAsync(_ => default(ValueTask), CancellationToken.None),
                    Throws.InstanceOf<InvalidOperationException>());
            }
            finally
            {
                releaseProvider.TrySetResult(true);
            }
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(firstOrder, Is.EqualTo(1));
            Assert.That(secondOrder, Is.EqualTo(2));
            Assert.That(order, Is.EqualTo(2));
        }

        [Test]
        public async Task ShutdownPreservesCleanupFailureAfterSuccessfulDrain()
        {
            var lifetime = new PackMLLifetime();
            var cleanupError = new InvalidOperationException("Owner cleanup failed.");
            InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await lifetime.StopAsync(() => throw cleanupError)))!;
            Assert.That(failure, Is.SameAs(cleanupError));
            await lifetime.DisposeAsync();
        }

        [Test]
        public async Task StoreFailureAfterAcceptanceIsReportedWithoutReplayingEquipment()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            var provider = new EquipmentProvider();
            var store = new MemoryAdministrationStore();
            await using Unit unit = await Unit.CreateAsync(
                context, null, PackMLTestSupport.Name(context), provider,
                new PackMLUnitOptions { AdministrationStore = store });
            store.SaveException = new InvalidOperationException("The test store is unavailable.");
            await Assert.ThatAsync(async () => await unit.RequestAsync(context, Command(PackMLCommand.Reset)),
                Throws.InvalidOperationException);
            Assert.That(provider.Executed, Has.Count.EqualTo(1));
            Assert.That(unit.CurrentState, Is.EqualTo(PackMLStateNumbers.Resetting));
            Assert.That(unit.PendingRequestId, Is.EqualTo(provider.Executed[0].RequestId));
        }

        /// <summary>The distinct server titles in OPC 30050 v1.01 Table 63.</summary>
        private static readonly string[] s_definedServerConformanceUnits =
        [
            "PackML Base Functionality", "PackML Base TagID", "PackML Base Version",
            "PackML Base Admin", "PackML Base Status", "PackML State Information",
            "PackML State Abort", "PackML State Clear", "PackML State Stop", "PackML State Reset",
            "PackML State Complete", "PackML State Start", "PackML State Unhold",
            "PackML State Suspend", "PackML State Hold", "PackML State Unsuspend",
            "PackML SetUnitMode", "PackML Set Product", "PackML Machine Speed",
            "PackML RemoteCommand", "PackML SetInterlock", "PackML Status UnitModeRequested",
            "PackML Status UnitModeChangeInProcess", "PackML Status State", "PackML Status Interlock",
            "PackML Status RemoteParameter", "PackML Status Product", "PackML Admin Alarm",
            "PackML Admin Alarm History", "PackML Admin Warning", "PackML Admin Stop",
            "PackML Admin mode statistics", "PackML Admin state Statistics", "PackML Admin",
            "PackML Admin ResetTime", "PackML Admin machine speed.", "PackML Equipment Blocked",
            "PackML Equipment Starved", "PackML ProdDefectiveCount", "PackML ProdProcessedCount"
        ];
    }
}
