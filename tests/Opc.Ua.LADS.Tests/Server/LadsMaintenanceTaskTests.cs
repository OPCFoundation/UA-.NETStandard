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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the maintenance task state machine, its methods and the provider hook.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsMaintenanceTaskTests
    {
        [Test]
        public async Task MaintenanceTaskCyclesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceTaskCyclesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            var states = new List<LadsMaintenanceStateValue>();
            task.StateChanged += states.Add;

            Assert.That(task.Name, Is.EqualTo(LadsIncubator.MaintenanceName));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
            Assert.That(task.LastExecutionDate, Is.EqualTo(DateTimeUtc.MinValue));

            DateTime before = DateTime.UtcNow.AddSeconds(-1);
            await task.StartAsync().ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            await task.StopAsync(MaintenanceTaskResultEnum.Success, new LocalizedText("Calibrated."))
                .ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Success));
            Assert.That(task.LastExecutionDate.ToDateTime(), Is.GreaterThanOrEqualTo(before));

            await task.ResetAsync().ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsMaintenanceStateValue.Executing,
                    LadsMaintenanceStateValue.Finished,
                    LadsMaintenanceStateValue.Planned
                }));

            NodeId planned = await server.ChildAsync(task.NodeId, "PlannedDate").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(planned).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc next), Is.True);
            Assert.That(
                next.ToDateTime(),
                Is.EqualTo(task.LastExecutionDate.ToDateTime() + TimeSpan.FromDays(30)));
        }

        [Test]
        public async Task InvalidMaintenanceTransitionsAreRejectedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(InvalidMaintenanceTransitionsAreRejectedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StopAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.ResetAsync()).ConfigureAwait(false);
            await task.StartAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StartAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.ResetAsync()).ConfigureAwait(false);
            await task.StopAsync(MaintenanceTaskResultEnum.Failure).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StartAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StopAsync()).ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Failure));
        }

        [Test]
        public async Task MaintenanceMethodsAreCallableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceMethodsAreCallableAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            NodeId node = await server.ChildAsync(device.NodeId, "Maintenance", LadsIncubator.MaintenanceName)
                .ConfigureAwait(false);
            Assert.That(node, Is.EqualTo(task.NodeId));

            CallMethodResult start = await server.CallByNameAsync(node, "StartTask").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));

            CallMethodResult invalid = await server.CallByNameAsync(
                node,
                "StopTask",
                Variant.From(99),
                Variant.From(new LocalizedText("Bad code."))).ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));

            CallMethodResult stop = await server.CallByNameAsync(
                node,
                "StopTask",
                Variant.From((int)MaintenanceTaskResultEnum.Undetermined),
                Variant.From(new LocalizedText("Partly done."))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stop.StatusCode), Is.True, stop.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Undetermined));

            CallMethodResult again = await server.CallByNameAsync(node, "StartTask").ConfigureAwait(false);
            Assert.That(again.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));

            CallMethodResult reset = await server.CallByNameAsync(node, "ResetTask").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(reset.StatusCode), Is.True, reset.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));

            NodeId lastExecution = await server.ChildAsync(node, "LastExecutionDate").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(lastExecution).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc date), Is.True);
            Assert.That(date, Is.EqualTo(task.LastExecutionDate));
        }

        [Test]
        public async Task DeviceProviderCanVetoMaintenanceCommandsAsync()
        {
            var provider = new Mock<ILadsDeviceProvider>();
            provider
                .Setup(p => p.OnMaintenanceTaskAsync(
                    It.IsAny<ILadsMaintenanceTask>(),
                    LadsMaintenanceCommand.Start,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            provider
                .Setup(p => p.OnMaintenanceTaskAsync(
                    It.IsAny<ILadsMaintenanceTask>(),
                    LadsMaintenanceCommand.Stop,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceProviderCanVetoMaintenanceCommandsAsync),
                services: builder => builder.Services.AddSingleton(provider.Object)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);

            await task.StartAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadUserAccessDenied,
                () => task.StopAsync()).ConfigureAwait(false);

            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            provider.Verify(
                p => p.OnMaintenanceTaskAsync(task, LadsMaintenanceCommand.Start, It.IsAny<CancellationToken>()),
                Times.Once);
            provider.Verify(
                p => p.OnMaintenanceTaskAsync(task, LadsMaintenanceCommand.Stop, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
