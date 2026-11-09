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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Preserves the WMTP exclusion contract under explicitly scheduled test-clock maintenance.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("CompanionSuite")]
    [NonParallelizable]
    public sealed class CompanionSuiteWmtpOperationTests
    {
        /// <summary>
        /// Keeps UTC, monotonic time and due callbacks on the same deterministic timeline.
        /// </summary>
        [Test]
        public async Task AcquisitionClockSchedulesOnlyAtExplicitLogicalDueTimesAsync()
        {
            var clock = new CompanionSuiteFixture.AcquisitionClock();
            DateTimeOffset epoch = clock.GetUtcNow();
            int callbacks = 0;
            await using ITimer timer = clock.CreateTimer(_ => callbacks++, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.Zero);
            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(callbacks, Is.Zero);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(clock.GetUtcNow(), Is.EqualTo(epoch.AddSeconds(1)));
            Assert.That(clock.GetTimestamp(), Is.EqualTo(TimeSpan.TicksPerSecond));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan), Is.True);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan), Is.True);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.EqualTo(3));
            await timer.DisposeAsync().ConfigureAwait(false);
            Assert.That(timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan), Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.EqualTo(3));
            Assert.That(clock.GetElapsedTime(0), Is.EqualTo(TimeSpan.FromSeconds(5)));
        }

        /// <summary>
        /// A deliberately admitted quality refresh excludes reports until its awaited publication is drained.
        /// </summary>
        [Test]
        public async Task ControlledQualityMaintenanceStillExcludesOverlappingReportsAsync()
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync(reordered: true, standalone: true).ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync("MaintenanceRaceReader").ConfigureAwait(false);
            await using var client = new MachineToolClient(session, fixture.Telemetry);
            WmtpPeripheral peripheral = fixture.Peripherals[0];
            WmtpRecordClient records = await client.GetWirelessPeripheral(peripheral.NodeId)
                .GetWorkRecordsAsync().ConfigureAwait(false);
            WmtpRecordClient independent = await client.GetWirelessPeripheral(fixture.Peripherals[1].NodeId)
                .GetWorkRecordsAsync().ConfigureAwait(false);
            CompanionSuiteFixture.AcquisitionClock clock = fixture.PeripheralClocks[0];
            DateTimeOffset utcBefore = clock.GetUtcNow();
            long ticksBefore = clock.GetTimestamp();
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int gated = 0;
            async ValueTask GateAsync(
                ISystemContext _, NodeState node, NodeStateChangeMasks changes, CancellationToken token)
            {
                if (Interlocked.Exchange(ref gated, 1) == 0)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
            }
            peripheral.State.StateChangedAsync += GateAsync;
            peripheral.State.UpdateChangeMasks(NodeStateChangeMasks.NonValue);
            try
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                await entered.Task.WaitAsync(window.Token).ConfigureAwait(false);
                Assert.That(clock.GetUtcNow(), Is.EqualTo(utcBefore.AddSeconds(1)));
                Assert.That(clock.GetTimestamp(), Is.EqualTo(ticksBefore + TimeSpan.TicksPerSecond));
                ServiceResultException busy = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await records.ReadAsync(new WmtpRecordQuery(), cancellationToken: window.Token)
                        .ConfigureAwait(false)))!;
                Assert.That(busy.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
                ArrayOf<WMTPOutputDataType> report = await independent.ReadAsync(
                    new WmtpRecordQuery(), cancellationToken: window.Token).ConfigureAwait(false);
                Assert.That(report.Count, Is.EqualTo(1));
                Assert.That(report[0].ActualValue, Is.EqualTo(202.0));
            }
            finally
            {
                release.TrySetResult(true);
                peripheral.State.StateChangedAsync -= GateAsync;
                await peripheral.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            Assert.That(fixture.Context.Manager.FindPredefinedNode(peripheral.NodeId), Is.Null);
            ArrayOf<WMTPOutputDataType> survivingReport = await independent.ReadAsync(
                new WmtpRecordQuery(), cancellationToken: window.Token).ConfigureAwait(false);
            Assert.That(survivingReport.Count, Is.EqualTo(1));
            Assert.That(survivingReport[0].ActualValue, Is.EqualTo(202.0));
            Assert.That(await fixture.Records[0].CountAsync(new WmtpRecordQuery()).ConfigureAwait(false),
                Is.EqualTo(1));
        }
    }
}
