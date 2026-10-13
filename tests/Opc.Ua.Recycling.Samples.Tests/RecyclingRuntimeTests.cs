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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Exercises absolute process-view projection without timers, sockets or a second material authority.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingRuntimeTests
    {
        /// <summary>
        /// Recovering data quality reprojects an unchanged image, without advancing or duplicating production.
        /// </summary>
        [Test]
        public async Task RestoringAvailabilityReappliesSameRevisionWithoutChangingCommittedProduction()
        {
            var clock = new TestClock();
            DateTime sourceTimestamp = clock.GetUtcNow().UtcDateTime;
            var engine = new RecyclingEngine(new RecyclingConfiguration { PlannedSourceBatches = 1 });
            CommittedSnapshot snapshot = engine.CreateRun("projection-run") with
            {
                Lifecycle = RunLifecycle.Running,
                Revision = 7
            };
            ArrayOf<RecyclingProcess> processes = Enum.GetValues<RecyclingProcess>().ToArrayOf();
            for (int tick = 0; tick < 80; tick++)
            {
                snapshot = engine.Advance(snapshot, sourceTimestamp, processes);
            }
            snapshot = snapshot with { Lifecycle = RunLifecycle.Paused };
            Assert.That(snapshot.Packages.Count, Is.EqualTo(1));
            Assert.That(snapshot.History.Count, Is.GreaterThan(0));

            bool nativeValuesAvailable = false;
            int nativePackageCount = 0;
            DateTime nativeSourceTimestamp = DateTime.UnixEpoch;
            var applied = new List<CommittedSnapshot>();
            var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
            adapter.Setup(view => view.ApplyAsync(
                    It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<CommittedSnapshot, CancellationToken>((committed, _) =>
                {
                    applied.Add(committed);
                    nativeValuesAvailable = true;
                    nativePackageCount = committed.Packages.Count;
                    nativeSourceTimestamp = committed.SourceTimestampUtc;
                })
                .Returns(ValueTask.CompletedTask);
            adapter.Setup(view => view.MarkUnavailableAsync(
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, CancellationToken>((timestamp, _) =>
                {
                    nativeValuesAvailable = false;
                    nativeSourceTimestamp = timestamp;
                })
                .Returns(ValueTask.CompletedTask);
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            await using var runtime = new RecyclingRuntime(
                new RecyclingHostOptions { Role = RecyclingHostRole.ProcessView },
                new RecyclingAuthorization([]),
                adapter.Object,
                Mock.Of<IRecyclingUserCommandForwarder>(),
                services,
                clock,
                telemetry);

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            await runtime.ApplyProjectionAsync(snapshot, available: false).ConfigureAwait(false);
            Assert.That(nativeValuesAvailable, Is.False);
            Assert.That(applied, Has.Count.EqualTo(1));
            clock.Advance(TimeSpan.FromMinutes(5));

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            Assert.That(nativeValuesAvailable, Is.True);
            Assert.That(applied, Has.Count.EqualTo(2));
            Assert.That(applied[1], Is.SameAs(snapshot));
            Assert.That(nativePackageCount, Is.EqualTo(1));
            Assert.That(nativeSourceTimestamp, Is.EqualTo(sourceTimestamp));
            CommittedSnapshot projected = runtime.Latest ??
                throw new InvalidOperationException("No committed image was projected.");
            Assert.That(projected, Is.SameAs(snapshot));
            Assert.That(projected.Revision, Is.EqualTo(7));
            Assert.That(projected.Tick, Is.EqualTo(80));
            Assert.That(projected.History, Is.EqualTo(applied[0].History));
            Assert.That(projected.SourceMass, Is.EqualTo(applied[0].SourceMass));

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            Assert.That(applied, Has.Count.EqualTo(2), "Ordinary duplicate delivery is still coalesced.");
            adapter.Verify(
                view => view.MarkUnavailableAsync(sourceTimestamp, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
