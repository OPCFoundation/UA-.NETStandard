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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the device state machine, the Machinery building blocks and the device provider.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsDeviceTests
    {
        [Test]
        public async Task DeviceStateMovesThroughTheValidTransitionsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceStateMovesThroughTheValidTransitionsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var states = new List<LadsDeviceStateValue>();
            device.StateChanged += states.Add;

            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));
            await device.GotoSleepAsync().ConfigureAwait(false);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Sleep));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotAvailable));
            await device.GotoOperateAsync().ConfigureAwait(false);
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));
            await device.GotoShutdownAsync().ConfigureAwait(false);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.OutOfService));

            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsDeviceStateValue.Sleep,
                    LadsDeviceStateValue.Operate,
                    LadsDeviceStateValue.Shutdown
                }));
            NodeId current = await server.ChildAsync(device.NodeId, "DeviceState", "CurrentState")
                .ConfigureAwait(false);
            DataValue value = await server.ReadAsync(current).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("Shutdown"));
        }

        [Test]
        public async Task InvalidDeviceTransitionsAreRejectedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(InvalidDeviceTransitionsAreRejectedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoOperateAsync()).ConfigureAwait(false);
            await device.GotoSleepAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoShutdownAsync()).ConfigureAwait(false);
            await device.GotoOperateAsync().ConfigureAwait(false);
            await device.GotoShutdownAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoOperateAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoSleepAsync()).ConfigureAwait(false);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
        }

        [Test]
        public async Task SleepIsRejectedWhileAFunctionalUnitRunsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(SleepIsRejectedWhileAFunctionalUnitRunsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);

            await unit.StartAsync().ConfigureAwait(false);
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoSleepAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => device.GotoShutdownAsync()).ConfigureAwait(false);

            await unit.AbortAsync().ConfigureAwait(false);
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));
            await device.GotoSleepAsync().ConfigureAwait(false);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Sleep));
            await unit.ClearAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => unit.StartAsync()).ConfigureAwait(false);
        }

        [Test]
        public async Task OperationModeChangesInEveryDirectionAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(OperationModeChangesInEveryDirectionAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var modes = new List<MachineryOperationModeValue>();
            device.OperationModeChanged += modes.Add;

            await device.SetOperationModeAsync(MachineryOperationModeValue.Maintenance).ConfigureAwait(false);
            await device.SetOperationModeAsync(MachineryOperationModeValue.Setup).ConfigureAwait(false);
            await device.SetOperationModeAsync(MachineryOperationModeValue.Setup).ConfigureAwait(false);
            await device.SetOperationModeAsync(MachineryOperationModeValue.Processing).ConfigureAwait(false);

            Assert.That(device.OperationMode, Is.EqualTo(MachineryOperationModeValue.Processing));
            Assert.That(
                modes,
                Is.EqualTo(new[]
                {
                    MachineryOperationModeValue.Maintenance,
                    MachineryOperationModeValue.Setup,
                    MachineryOperationModeValue.Processing
                }));
        }

        [Test]
        public async Task DeviceStateMethodsAreCallableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceStateMethodsAreCallableAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            NodeId state = await server.ChildAsync(device.NodeId, "DeviceState").ConfigureAwait(false);

            CallMethodResult sleep = await server.CallByNameAsync(state, "GotoSleep").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sleep.StatusCode), Is.True, sleep.StatusCode.ToString());
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Sleep));
            CallMethodResult shutdown = await server.CallByNameAsync(state, "GotoShutdown").ConfigureAwait(false);
            Assert.That(shutdown.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            CallMethodResult operate = await server.CallByNameAsync(state, "GotoOperate").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(operate.StatusCode), Is.True, operate.StatusCode.ToString());

            NodeId mode = await server.ChildAsync(
                device.NodeId,
                "MachineryBuildingBlocks",
                "MachineryOperationMode").ConfigureAwait(false);
            CallMethodResult maintenance = await server.CallByNameAsync(mode, "GotoMaintenance")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(maintenance.StatusCode), Is.True, maintenance.StatusCode.ToString());
            Assert.That(device.OperationMode, Is.EqualTo(MachineryOperationModeValue.Maintenance));
        }

        [Test]
        public async Task DeviceProviderCanVetoChangesAsync()
        {
            var provider = new Mock<ILadsDeviceProvider>(MockBehavior.Strict);
            provider
                .Setup(p => p.OnDeviceStateChangingAsync(
                    It.IsAny<ILadsDevice>(),
                    It.IsAny<LadsDeviceStateValue>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));
            provider
                .Setup(p => p.OnOperationModeChangingAsync(
                    It.IsAny<ILadsDevice>(),
                    MachineryOperationModeValue.Setup,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadNotSupported)));
            provider
                .Setup(p => p.OnOperationModeChangingAsync(
                    It.IsAny<ILadsDevice>(),
                    MachineryOperationModeValue.Maintenance,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceProviderCanVetoChangesAsync),
                services: builder => builder.Services.AddSingleton(provider.Object)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadUserAccessDenied,
                () => device.GotoSleepAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported,
                () => device.SetOperationModeAsync(MachineryOperationModeValue.Setup)).ConfigureAwait(false);
            await device.SetOperationModeAsync(MachineryOperationModeValue.Maintenance).ConfigureAwait(false);

            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(device.OperationMode, Is.EqualTo(MachineryOperationModeValue.Maintenance));
            provider.Verify(
                p => p.OnDeviceStateChangingAsync(device, LadsDeviceStateValue.Sleep, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task PerDeviceProviderOverridesTheServiceAsync()
        {
            var provider = new Mock<ILadsDeviceProvider>();
            provider
                .Setup(p => p.OnDeviceStateChangingAsync(
                    It.IsAny<ILadsDevice>(),
                    It.IsAny<LadsDeviceStateValue>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadDeviceFailure)));
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(PerDeviceProviderOverridesTheServiceAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.Provider(provider.Object)))).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadDeviceFailure,
                () => device.GotoSleepAsync()).ConfigureAwait(false);
        }

        [Test]
        public async Task InitialStateIsAppliedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(InitialStateIsAppliedAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.InitialState(
                        LadsDeviceStateValue.Sleep,
                        MachineryOperationModeValue.Setup)))).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Sleep));
            Assert.That(device.OperationMode, Is.EqualTo(MachineryOperationModeValue.Setup));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotAvailable));
            ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => programs.StartProgramAsync(LadsIncubator.QuickTemplate)).ConfigureAwait(false);
        }

        [Test]
        public async Task LookupsAndCountersAreExposedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(LookupsAndCountersAreExposedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);

            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => device.GetFunctionalUnit("Missing"));
            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => device.GetMaintenanceTask("Missing"));
            Assert.That(device.MaintenanceTasks.Count, Is.EqualTo(1));
            Assert.That(device.GetMaintenanceTask(LadsIncubator.MaintenanceName).Device, Is.SameAs(device));

            uint before = device.OperationCycleCounter;
            device.IncrementOperationCycles();
            Assert.That(device.OperationCycleCounter, Is.EqualTo(before + 1));
            await Task.Delay(20).ConfigureAwait(false);
            Assert.That(device.PowerOnDuration, Is.GreaterThan(System.TimeSpan.Zero));

            NodeId counter = await server.ChildAsync(
                device.NodeId,
                "MachineryBuildingBlocks",
                "OperationCounters",
                "OperationCycleCounter").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(counter).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out uint cycles), Is.True, value.WrappedValue.ToString());
            Assert.That(cycles, Is.EqualTo(before + 1));
            NodeId powerOn = await server.ChildAsync(
                device.NodeId,
                "MachineryBuildingBlocks",
                "OperationCounters",
                "PowerOnDuration").ConfigureAwait(false);
            DataValue duration = await server.ReadAsync(powerOn).ConfigureAwait(false);
            Assert.That(duration.WrappedValue.TryGetValue(out double ms), Is.True, duration.WrappedValue.ToString());
            Assert.That(ms, Is.GreaterThan(0));
        }
    }
}
