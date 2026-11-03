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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using static Opc.Ua.ECM.Server.EcmModelNodes;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises generated ECM callbacks, provider publication and authorization without sockets.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmPowerBindingTests
    {
        /// <summary>
        /// Optional controls expose the published argument metadata and execute through service validation.
        /// </summary>
        [Test]
        public async Task OptionalControlsRetainArgumentsAndDispatchThroughMethodService()
        {
            SystemContext context = CreateContext();
            var types = new TypeTable(context.NamespaceUris);
            types.AddSubtype(Opc.Ua.DataTypeIds.Double, NodeId.Null);
            types.AddSubtype(Opc.Ua.DataTypeIds.Duration, Opc.Ua.DataTypeIds.Double);
            context.TypeTable = types;
            await using EcmPowerBinding binding = CreateBinding(context, CreateProvider());
            MethodState start = Required(binding.Standby.StartPause);
            MethodState end = Required(binding.Standby.EndPause);
            MethodState change = Required(binding.Standby.SwitchToEnergySavingMode);
            var inputs = Required(start.InputArguments).Value;
            Assert.That(inputs.Count, Is.EqualTo(1));
            Assert.That(inputs[0].Name, Is.EqualTo("PauseTime"));
            Assert.That(inputs[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
            Assert.That(Required(start.OutputArguments).Value.Count, Is.EqualTo(5));
            Assert.That(Required(end.OutputArguments).Value.Count, Is.EqualTo(2));
            Assert.That(Required(change.InputArguments).Value.Count, Is.EqualTo(1));
            Assert.That(Required(change.OutputArguments).Value.Count, Is.EqualTo(5));
            Assert.That(start.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(end.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(change.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            var errors = new List<ServiceResult>();
            var outputs = new List<Variant>();
            ServiceResult rejected = await start.CallAsync(
                context, binding.Standby.NodeId, [new Variant(1d)], errors, outputs).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(5));
            Assert.That(outputs[4].TryGetValue(out byte rejectionCode), Is.True);
            Assert.That(rejectionCode, Is.EqualTo(0x50));

            errors.Clear();
            outputs.Clear();
            ServiceResult ended = await end.CallAsync(
                context, binding.Standby.NodeId, [], errors, outputs).ConfigureAwait(false);
            Assert.That(ended.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(errors, Is.Empty);
            Assert.That(outputs, Has.Count.EqualTo(2));
            Assert.That(outputs[0].TryGetValue(out double recovery), Is.True);
            Assert.That(recovery, Is.Zero);
            Assert.That(outputs[1].TryGetValue(out byte returnCode), Is.True);
            Assert.That(returnCode, Is.Zero);

            errors.Clear();
            outputs.Clear();
            ServiceResult changed = await change.CallAsync(
                context, binding.Standby.NodeId, [new Variant((byte)1)], errors, outputs).ConfigureAwait(false);
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(5));
            Assert.That(outputs[0].TryGetValue(out byte destination), Is.True);
            Assert.That(destination, Is.EqualTo(1));
            Assert.That(outputs[4].TryGetValue(out byte switchCode), Is.True);
            Assert.That(switchCode, Is.Zero);
        }

        /// <summary>
        /// Proves a nonzero variable write and zero reversal use the provider rather than a cached write.
        /// </summary>
        [Test]
        public async Task PauseTimeWriteAndZeroReversalUseControlPipeline()
        {
            SystemContext context = CreateContext();
            var provider = CreateProvider();
            await using EcmPowerBinding binding = CreateBinding(context, provider);
            await binding.RefreshAsync().ConfigureAwait(false);

            var pauseTime = Required(binding.Standby.PauseTime);
            var status = Required(binding.Standby.StandbyManagementStatus);
            AttributeWriteResult write = await pauseTime.OnSimpleWriteValueAsync!(
                context, pauseTime, new Variant(10000d), CancellationToken.None).ConfigureAwait(false);

            Assert.That(write.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(write.SuppressCacheUpdate, Is.True);
            Assert.That(pauseTime.Value, Is.EqualTo(10000));
            Assert.That(status.Value,
                Is.EqualTo((byte)EcmStandbyStatus.MovingToEnergySavingMode));

            AttributeWriteResult reverse = await pauseTime.OnSimpleWriteValueAsync!(
                context, pauseTime, new Variant(0d), CancellationToken.None).ConfigureAwait(false);

            Assert.That(reverse.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reverse.SuppressCacheUpdate, Is.True);
            Assert.That(pauseTime.Value, Is.Zero);
            Assert.That(status.Value,
                Is.EqualTo((byte)EcmStandbyStatus.MovingToReadyToOperate));
        }

        /// <summary>
        /// Proves a rejected write retains the authoritative accepted pause and its timestamp.
        /// </summary>
        [Test]
        public async Task RejectedPauseWriteDoesNotReplaceAcceptedCache()
        {
            SystemContext context = CreateContext();
            await using EcmPowerBinding binding = CreateBinding(context, CreateProvider());
            var pauseTime = Required(binding.Standby.PauseTime);
            await pauseTime.OnSimpleWriteValueAsync!(
                context, pauseTime, new Variant(10000d), CancellationToken.None).ConfigureAwait(false);
            DateTimeUtc timestamp = pauseTime.Timestamp;

            AttributeWriteResult rejected = await pauseTime.OnSimpleWriteValueAsync!(
                context, pauseTime, new Variant(1d), CancellationToken.None).ConfigureAwait(false);

            Assert.That(rejected.Result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(rejected.SuppressCacheUpdate, Is.True);
            Assert.That(pauseTime.Value, Is.EqualTo(10000));
            Assert.That(pauseTime.Timestamp, Is.EqualTo(timestamp));
        }

        /// <summary>
        /// Proves the default role gate applies equally to method calls and variable writes.
        /// </summary>
        [Test]
        public async Task MissingControlRoleDeniesMethodsAndWrites()
        {
            SystemContext context = CreateContext();
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            await using var binding = new EcmPowerBinding(context, standby, provider.Object);

            var startPause = Required(standby.StartPause);
            var pauseTime = Required(standby.PauseTime);
            var call = await startPause.OnCallAsync!(
                context, startPause, standby.NodeId, 10000, CancellationToken.None).ConfigureAwait(false);
            AttributeWriteResult write = await pauseTime.OnSimpleWriteValueAsync!(
                context, pauseTime, new Variant(10000d), CancellationToken.None).ConfigureAwait(false);

            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(write.Result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A cancelled authorization result cannot reach a standby command or PauseTime write.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task CancellationDuringAuthorizationDoesNotInvokeStandbyProvider(int command)
        {
            SystemContext context = CreateContext();
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            using var cancellation = new CancellationTokenSource();
            int checks = 0;
            await using var binding = new EcmPowerBinding(context, standby, provider.Object,
                authorize: (_, _) =>
                {
                    checks++;
                    cancellation.Cancel();
                    return new ValueTask<bool>(true);
                });

            await Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                switch (command)
                {
                    case 0:
                        await Required(standby.StartPause).OnCallAsync!(
                            context, Required(standby.StartPause), standby.NodeId, 10000, cancellation.Token);
                        break;
                    case 1:
                        await Required(standby.EndPause).OnCallAsync!(
                            context, Required(standby.EndPause), standby.NodeId, cancellation.Token);
                        break;
                    case 2:
                        await Required(standby.SwitchToEnergySavingMode).OnCallAsync!(
                            context, Required(standby.SwitchToEnergySavingMode), standby.NodeId, 1, cancellation.Token);
                        break;
                    default:
                        await Required(standby.PauseTime).OnSimpleWriteValueAsync!(context, Required(standby.PauseTime),
                            new Variant(command == 3 ? 10000d : 0d), cancellation.Token);
                        break;
                }
            });

            Assert.That(checks, Is.EqualTo(1));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The default role policy admits only control roles on methods and variable writes.
        /// </summary>
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        public async Task DefaultControlRolePolicyAppliesToMethodsAndWrites(int scenario, bool allowed)
        {
            uint roleId = scenario switch
            {
                0 => Opc.Ua.Objects.WellKnownRole_Operator,
                1 => Opc.Ua.Objects.WellKnownRole_Engineer,
                2 => Opc.Ua.Objects.WellKnownRole_Supervisor,
                3 => Opc.Ua.Objects.WellKnownRole_Observer,
                4 => Opc.Ua.Objects.WellKnownRole_AuthenticatedUser,
                5 => Opc.Ua.Objects.WellKnownRole_Anonymous,
                _ => Opc.Ua.Objects.WellKnownRole_SecurityAdmin
            };
            SystemContext context = CreateContext();
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            await using var binding = new EcmPowerBinding(context, standby, CreateProvider());
            await binding.RefreshAsync();
            var identity = new Mock<IUserIdentity>(MockBehavior.Strict);
            identity.SetupGet(value => value.GrantedRoleIds).Returns([new NodeId(roleId)]);
            var caller = new Mock<ISessionSystemContext>(MockBehavior.Strict);
            caller.SetupGet(value => value.UserIdentity).Returns(identity.Object);

            var call = await Required(standby.StartPause).OnCallAsync!(caller.Object, Required(standby.StartPause),
                standby.NodeId, 10000, CancellationToken.None);
            AttributeWriteResult write = await Required(standby.PauseTime).OnSimpleWriteValueAsync!(
                caller.Object, Required(standby.PauseTime), new Variant(0d), CancellationToken.None);

            StatusCode expected = allowed ? StatusCodes.Good : StatusCodes.BadUserAccessDenied;
            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(expected));
            Assert.That(write.Result.StatusCode, Is.EqualTo(expected));
            Assert.That(Required(standby.StandbyManagementStatus).Value, Is.EqualTo((byte)(allowed
                ? EcmStandbyStatus.MovingToReadyToOperate
                : EcmStandbyStatus.ReadyToOperate)));
        }

        /// <summary>
        /// Proves an application authorization override cannot bypass configured session ownership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExclusiveControlRequiresOwnershipCapabilityAndActualOwner(bool supportsOwnership)
        {
            SystemContext context = CreateContext();
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            var locks = new Mock<ILockService>(MockBehavior.Strict);
            if (supportsOwnership)
            {
                locks.As<ILockOwnershipService>().Setup(service =>
                    service.IsLockOwner(context, It.IsAny<NodeId>())).Returns(false);
            }
            await using EcmPowerBinding binding = CreateBinding(context, provider.Object, locks.Object);
            var call = await Required(binding.Standby.EndPause).OnCallAsync!(context,
                Required(binding.Standby.EndPause), binding.Standby.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            AttributeWriteResult write = await Required(binding.Standby.PauseTime).OnSimpleWriteValueAsync!(context,
                Required(binding.Standby.PauseTime), new Variant(0d), CancellationToken.None).ConfigureAwait(false);

            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(write.Result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.VerifyNoOtherCalls();
            if (supportsOwnership)
            {
                locks.As<ILockOwnershipService>().Verify(service =>
                    service.IsLockOwner(context, binding.Standby.NodeId), Times.Exactly(2));
            }
        }

        /// <summary>
        /// Proves unavailable providers retain the last values and source time without damaging unit metadata.
        /// </summary>
        [Test]
        public async Task ProviderFailureRetainsAcquisitionAndMarksOnlyAcquiredValuesUnavailable()
        {
            SystemContext context = CreateContext();
            EcmPowerSnapshot snapshot = await CreateProvider().ReadAsync().ConfigureAwait(false);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            provider.SetupSequence(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmPowerSnapshot>(snapshot))
                .Throws(new InvalidOperationException("Simulated acquisition failure."));
            await using EcmPowerBinding binding = CreateBinding(context, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            var status = Required(binding.Standby.StandbyManagementStatus);
            DateTimeUtc timestamp = status.Timestamp;
            byte state = status.Value;

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.RefreshAsync().ConfigureAwait(false));

            Assert.That(status.Value, Is.EqualTo(state));
            Assert.That(status.Timestamp, Is.EqualTo(timestamp));
            Assert.That(status.StatusCode,
                Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(Required(status.EnumStrings).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Proves shutdown removes handlers and does not dispose borrowed device providers.
        /// </summary>
        [Test]
        public async Task RepeatedDisposalDetachesHandlersAndBorrowsProvider()
        {
            SystemContext context = CreateContext();
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            Mock<IAsyncDisposable> disposable = provider.As<IAsyncDisposable>();
            EcmPowerBinding binding = CreateBinding(context, provider.Object);

            await binding.DisposeAsync().ConfigureAwait(false);
            await binding.DisposeAsync().ConfigureAwait(false);

            Assert.That(Required(binding.Standby.StartPause).OnCallAsync, Is.Null);
            Assert.That(Required(binding.Standby.PauseTime).OnSimpleWriteValueAsync, Is.Null);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await binding.RefreshAsync().ConfigureAwait(false));
            disposable.Verify(service => service.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Proves the WOL callback publishes the six-byte MAC and keeps acceptance distinct from arrival.
        /// </summary>
        [Test]
        public async Task WolBindingPublishesMetadataAndAllControlOutputs()
        {
            SystemContext context = CreateContext();
            SimulatedEcmPowerProvider provider = CreateProvider();
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            EnergyDevicePowerOffState powerOff = context.CreateInstanceOfEnergyDevicePowerOffType(
                standby, new QualifiedName("Sleep", 4));
            await using var binding = new EcmPowerBinding(context, standby, provider, powerOff, provider,
                authorize: static (_, _) => new ValueTask<bool>(true));
            await binding.RefreshAsync().ConfigureAwait(false);

            var call = await Required(powerOff.SwitchOffWOL).OnCallAsync!(
                context, Required(powerOff.SwitchOffWOL), powerOff.NodeId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Required(powerOff.WOLMagicPacket).Value, Is.EqualTo(provider.MacAddress));
            Assert.That(Required(powerOff.WOLMagicPacket).Value.Length, Is.EqualTo(6));
            Assert.That(Required(powerOff.RegularTimeToOperate).Value, Is.EqualTo(provider.RegularTimeToOperate));
            Assert.That(Required(powerOff.TimeMinPause).Value, Is.EqualTo(provider.TimeMinPause));
            Assert.That(Required(powerOff.ModePowerConsumption).Value, Is.EqualTo(provider.ModePowerConsumption));
            Assert.That(call.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(call.ReturnCode, Is.Zero);
            Assert.That(call.ModeID, Is.EqualTo(0xFE));
            Assert.That(call.CurrentTimeToDestination, Is.EqualTo(1000));
            Assert.That(call.RegularTimeToOperate, Is.EqualTo(2000));
            Assert.That(call.TimeMinLengthOfStay, Is.EqualTo(1000));
            Assert.That(Required(standby.StandbyManagementStatus).Value,
                Is.EqualTo((byte)EcmStandbyStatus.MovingToSleepWol));
            var information = Required(Required(standby.EnergySavingModeStatus).StateInformation).Value;
            Assert.That(information.IDSource, Is.EqualTo(0xFF));
            Assert.That(information.IDDestination, Is.EqualTo(0xFE));
        }

        /// <summary>
        /// A failed WOL metadata read leaves no ECM control callback capturing an unreturned binding.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorFailureDoesNotInstallControlHandlers(bool exclusiveAccess)
        {
            SystemContext context = CreateContext();
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            EnergyDevicePowerOffState powerOff = context.CreateInstanceOfEnergyDevicePowerOffType(
                standby, new QualifiedName("Sleep", 4));
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            var wol = new Mock<IEcmWolProvider>(MockBehavior.Strict);
            wol.SetupGet(value => value.MacAddress)
                .Returns(ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()));
            wol.SetupGet(value => value.RegularTimeToOperate).Returns(2000);
            wol.SetupGet(value => value.TimeMinPause).Returns(1000);
            wol.SetupGet(value => value.ModePowerConsumption)
                .Throws(new InvalidOperationException("WOL metadata unavailable."));
            var locks = new Mock<ILockService>(MockBehavior.Strict);
            if (exclusiveAccess)
            {
                standby.CreateChild(context, new QualifiedName(
                    Di.BrowseNames.Lock, context.NamespaceUris.GetIndexOrAppend(Di.Namespaces.OpcUaDi)));
                Assert.That(standby.Lock, Is.Not.Null);
                var lockState = Required(standby.Lock);
                lockState.InitLock = new Di.InitLockMethodState(lockState);
                lockState.RenewLock = new Di.RenewLockMethodState(lockState);
                lockState.ExitLock = new Di.ExitLockMethodState(lockState);
                lockState.BreakLock = new Di.BreakLockMethodState(lockState);
                lockState.Locked = PropertyState<bool>.With<VariantBuilder>(lockState);
                lockState.LockingClient = PropertyState<string>.With<VariantBuilder>(lockState);
                lockState.LockingUser = PropertyState<string>.With<VariantBuilder>(lockState);
                lockState.RemainingLockTime = PropertyState<double>.With<VariantBuilder>(lockState);
            }

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                new EcmPowerBinding(context, standby, provider.Object, powerOff, wol.Object,
                    exclusiveAccess ? locks.Object : null))!;

            Assert.That(error.Message, Is.EqualTo("WOL metadata unavailable."));
            Assert.That(Required(standby.PauseTime).OnSimpleWriteValueAsync, Is.Null);
            Assert.That(Required(standby.StartPause).OnCallAsync, Is.Null);
            Assert.That(Required(standby.EndPause).OnCallAsync, Is.Null);
            Assert.That(Required(standby.SwitchToEnergySavingMode).OnCallAsync, Is.Null);
            Assert.That(Required(powerOff.SwitchOffWOL).OnCallAsync, Is.Null);
            if (standby.Lock != null)
            {
                Assert.That(Required(standby.Lock.InitLock).OnCall, Is.Null);
                Assert.That(Required(standby.Lock.RenewLock).OnCall, Is.Null);
                Assert.That(Required(standby.Lock.ExitLock).OnCall, Is.Null);
                Assert.That(Required(standby.Lock.BreakLock).OnCall, Is.Null);
                Assert.That(Required(standby.Lock.Locked).OnSimpleReadValueAsync, Is.Null);
                Assert.That(Required(standby.Lock.LockingClient).OnSimpleReadValueAsync, Is.Null);
                Assert.That(Required(standby.Lock.LockingUser).OnSimpleReadValueAsync, Is.Null);
                Assert.That(Required(standby.Lock.RemainingLockTime).OnSimpleReadValueAsync, Is.Null);
            }
            provider.VerifyNoOtherCalls();
            locks.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Proves inconsistent state cannot partially replace the last coherent acquisition.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task InvalidSnapshotRetainsPublishedStateAndTimestamp(int invalidCase)
        {
            SystemContext context = CreateContext();
            EcmPowerSnapshot original = await CreateProvider().ReadAsync().ConfigureAwait(false);
            EcmPowerSnapshot invalid = invalidCase switch
            {
                0 => original with
                {
                    SourceTimestamp = DateTime.SpecifyKind(original.SourceTimestamp, DateTimeKind.Local)
                },
                1 => original with { PauseTime = double.NaN },
                2 => original with { CurrentTimeToDestination = 1 },
                3 => original with { EnergyConsumptionToDestination = 1 },
                4 => original with { SourceModeId = 1 },
                5 => original with
                {
                    Status = EcmStandbyStatus.MovingToEnergySavingMode,
                    SourceModeId = 99,
                    DestinationModeId = 1
                },
                6 => original with
                {
                    Status = EcmStandbyStatus.MovingToReadyToOperate,
                    SourceModeId = 99
                },
                _ => original with
                {
                    Status = EcmStandbyStatus.MovingToEnergySavingMode,
                    DestinationModeId = 1,
                    RegularTimeToOperate = 1
                }
            };
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            provider.SetupSequence(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmPowerSnapshot>(original))
                .Returns(new ValueTask<EcmPowerSnapshot>(invalid));
            await using EcmPowerBinding binding = CreateBinding(context, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            var status = Required(binding.Standby.StandbyManagementStatus);
            DateTimeUtc timestamp = status.Timestamp;

            if (invalidCase == 1)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await binding.RefreshAsync().ConfigureAwait(false));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await binding.RefreshAsync().ConfigureAwait(false));
            }

            Assert.That(status.Value,
                Is.EqualTo((byte)EcmStandbyStatus.ReadyToOperate));
            Assert.That(status.Timestamp, Is.EqualTo(timestamp));
            var information = Required(Required(binding.Standby.EnergySavingModeStatus).StateInformation).Value;
            Assert.That(information.IDSource, Is.EqualTo(0xFF));
            Assert.That(information.IDDestination, Is.EqualTo(0xFF));
            Assert.That(Required(binding.Standby.PauseTime).Value, Is.Zero);
            Assert.That(status.StatusCode,
                Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        /// <summary>
        /// Proves only a declared dynamic mode may revise its characteristics without changing identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ModeCharacteristicsCanChangeOnlyWhenDynamic(bool dynamicData)
        {
            SystemContext context = CreateContext();
            EcmPowerSnapshot original = await CreateProvider().ReadAsync().ConfigureAwait(false);
            original = original with
            {
                Modes = [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000,
                    3000, 1, 0.1f, 0.2f, dynamicData)]
            };
            EcmPowerSnapshot updated = original with
            {
                Modes = [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000,
                    3000, 0.5f, 0.1f, 0.2f, dynamicData)]
            };
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            provider.SetupSequence(service => service.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<EcmPowerSnapshot>(original))
                .Returns(new ValueTask<EcmPowerSnapshot>(updated));
            await using EcmPowerBinding binding = CreateBinding(context, provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            EnergySavingModeState savingMode = Required(binding.Standby.EnergySavingModes).FindChild(
                context, new QualifiedName("Pause", context.NamespaceUris.GetIndexOrAppend(Namespaces.ECM)))
                as EnergySavingModeState ?? throw new InvalidOperationException("The mode was not published.");
            Assert.That(savingMode.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(Required(binding.Standby.EnergySavingModes).TypeDefinitionId, Is.EqualTo(NodeId.Create(
                ObjectTypes.EnergySavingModesContainerType, Namespaces.ECM, context.NamespaceUris)));
            NodeId identity = savingMode.NodeId;
            var power = Required(savingMode.ModePowerConsumption);

            if (dynamicData)
            {
                await binding.RefreshAsync().ConfigureAwait(false);
                Assert.That(power.Value, Is.EqualTo(0.5f));
                Assert.That(power.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await binding.RefreshAsync().ConfigureAwait(false));
                Assert.That(power.Value, Is.EqualTo(1f));
                Assert.That(power.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoCommunication));
            }
            Assert.That(savingMode.NodeId, Is.EqualTo(identity));
            Assert.That(Required(power.EngineeringUnits).Value.UnitId, Is.EqualTo(0x4B5754));
            Assert.That(Required(power.EngineeringUnits).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Proves shutdown cancels active provider work and queued acquisitions before releasing handlers.
        /// </summary>
        [Test]
        public async Task ShutdownCancelsActiveCommandAndQueuedRefresh()
        {
            SystemContext context = CreateContext();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IEcmStandbyProvider>(MockBehavior.Strict);
            provider.Setup(service => service.StartPauseAsync(10000, It.IsAny<CancellationToken>()))
                .Returns((double _, CancellationToken ct) => new ValueTask<EcmControlResult>(WaitForShutdownAsync(ct)));
            EcmPowerBinding binding = CreateBinding(context, provider.Object);
            try
            {
                var startPause = Required(binding.Standby.StartPause);
                var callback = startPause.OnCallAsync!;
                Task command = callback(context, startPause,
                    binding.Standby.NodeId, 10000, CancellationToken.None).AsTask();
                await entered.Task.ConfigureAwait(false);
                Task queued = binding.RefreshAsync().AsTask();

                await binding.DisposeAsync().ConfigureAwait(false);

                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await command.ConfigureAwait(false));
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await queued.ConfigureAwait(false));
                Assert.That(cancellationObserved.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(startPause.OnCallAsync, Is.Null);
                provider.Verify(service => service.StartPauseAsync(10000,
                    It.IsAny<CancellationToken>()), Times.Once);
                provider.VerifyNoOtherCalls();
            }
            finally
            {
                await binding.DisposeAsync().ConfigureAwait(false);
            }

            async Task<EcmControlResult> WaitForShutdownAsync(CancellationToken ct)
            {
                using CancellationTokenRegistration registration = ct.Register(
                    () => cancellationObserved.TrySetResult(true));
                entered.SetResult(true);
                await cancellationObserved.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return new EcmControlResult(StatusCodes.Good, 0);
            }
        }

        /// <summary>
        /// Creates a generated graph with explicit simulated-control authorization.
        /// </summary>
        private static EcmPowerBinding CreateBinding(
            SystemContext context,
            IEcmStandbyProvider provider,
            ILockService? locks = null)
        {
            EnergyStandbyManagementState standby = context.CreateInstanceOfEnergyStandbyManagementType(
                browseName: new QualifiedName("Power", 4));
            return new EcmPowerBinding(context, standby, provider, exclusiveAccess: locks,
                authorize: static (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(true);
                });
        }

        /// <summary>
        /// Creates the in-memory reference provider without host or network power operations.
        /// </summary>
        private static SimulatedEcmPowerProvider CreateProvider()
        {
            return new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000, 3000, 1, 0.1f, 0.2f)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                timeProvider: new FixedTimeProvider());
        }

        /// <summary>
        /// Supplies a real namespace table, encodeable factory and deterministic instance identifiers.
        /// </summary>
        private static SystemContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(Di.Namespaces.OpcUaDi);
            messageContext.NamespaceUris.Append(IA.Namespaces.IA);
            messageContext.NamespaceUris.Append(Namespaces.ECM);
            ushort instances = messageContext.NamespaceUris.GetIndexOrAppend("urn:ecm-tests:instances");
            messageContext.Factory.Builder.AddOpcUaECM().Commit();
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory,
                NodeIdFactory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, instances)
            };
        }

        /// <summary>
        /// Keeps simulated transitions stationary unless a test explicitly supplies another clock.
        /// </summary>
        private sealed class FixedTimeProvider : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }
        }
    }
}
