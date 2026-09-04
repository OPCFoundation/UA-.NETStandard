/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CertificateAlarmScheduler"/> in isolation:
    /// start/stop/dispose transitions, timer wiring through an injected
    /// <see cref="TimeProvider"/> and argument validation. Evaluation of real
    /// alarm nodes and the concurrency of start/stop against evaluation are
    /// covered by <see cref="CertificateAlarmMonitoringTests"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Category("Alarms")]
    [Parallelizable(ParallelScope.All)]
    public class CertificateAlarmSchedulerTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly DateTime s_now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        private static readonly ServerSystemContext s_context =
            new(DeterministicServerMock.Create(out _).Object);

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            ILogger logger = s_telemetry.CreateLogger<CertificateAlarmScheduler>();
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => new CertificateAlarmScheduler(null!, logger));
                Assert.Throws<ArgumentNullException>(() => new CertificateAlarmScheduler(TimeProvider.System, null!));
            });
        }

        [Test]
        public void NewSchedulerIsInactiveWithoutMonitors()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);
            Assert.Multiple(() =>
            {
                Assert.That(scheduler.IsActive, Is.False);
                Assert.That(scheduler.Monitors, Is.Empty);
            });
        }

        [Test]
        public void StartArmsTimerAndStopDisarmsIt()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out FakeTimeProvider time);

            scheduler.Start(s_context, TimeSpan.FromSeconds(30));
            Assert.That(scheduler.IsActive, Is.True);

            // The injected timer fires without throwing even with no monitors.
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));

            // Starting again while running is a no-op.
            Assert.DoesNotThrow(() => scheduler.Start(s_context, TimeSpan.FromSeconds(30)));
            Assert.That(scheduler.IsActive, Is.True);

            scheduler.Stop();
            Assert.That(scheduler.IsActive, Is.False);

            // Stopping again and evaluating after stop are safe no-ops.
            Assert.DoesNotThrow(scheduler.Stop);
            Assert.DoesNotThrow(() => scheduler.UpdateAndEvaluate(s_context, emitEvents: true));
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void StartAfterStopResumesEvaluation()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);

            scheduler.Start(s_context, TimeSpan.FromSeconds(30));
            scheduler.Stop();
            Assert.That(scheduler.IsActive, Is.False);

            scheduler.Start(s_context, TimeSpan.FromSeconds(30));
            Assert.That(scheduler.IsActive, Is.True);
        }

        [Test]
        public void DisposeIsTerminalAndIdempotent()
        {
            CertificateAlarmScheduler scheduler = CreateScheduler(out FakeTimeProvider time);
            scheduler.Start(s_context, TimeSpan.FromSeconds(30));

            scheduler.Dispose();
            Assert.That(scheduler.IsActive, Is.False);
            Assert.DoesNotThrow(scheduler.Dispose);
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));

            // Unlike Stop, Dispose cannot be undone by a later Start.
            scheduler.Start(s_context, TimeSpan.FromSeconds(30));
            Assert.That(scheduler.IsActive, Is.False, "Start after Dispose must not re-arm the timer");
        }

        [Test]
        public void AddRejectsNullArguments()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);
            var group = new ServerCertificateGroup { BrowseName = "Group" };
            var node = new CertificateGroupState(null);
            var monitor = new CertificateGroupAlarmMonitor(
                node,
                group.BrowseName,
                TimeProvider.System,
                s_telemetry.CreateLogger<CertificateGroupAlarmMonitor>());

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => scheduler.Add(null!, group));
                Assert.Throws<ArgumentNullException>(() => scheduler.Add(monitor, null!));
                Assert.That(scheduler.Monitors, Is.Empty);
            });

            scheduler.Add(monitor, group);
            Assert.That(scheduler.Monitors, Is.EqualTo(new[] { monitor }));
        }

        private static CertificateAlarmScheduler CreateScheduler(out FakeTimeProvider time)
        {
            time = new FakeTimeProvider(new DateTimeOffset(s_now, TimeSpan.Zero));
            return new CertificateAlarmScheduler(time, s_telemetry.CreateLogger<CertificateAlarmScheduler>());
        }
    }
}
