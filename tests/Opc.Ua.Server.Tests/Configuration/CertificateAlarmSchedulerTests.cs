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
using System.Threading;
using System.Threading.Tasks;
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
    /// alarm nodes is covered by <see cref="CertificateAlarmMonitoringTests"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Category("Alarms")]
    [Parallelizable(ParallelScope.All)]
    public class CertificateAlarmSchedulerTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly DateTime s_now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        private static CertificateAlarmScheduler CreateScheduler(out FakeTimeProvider time)
        {
            time = new FakeTimeProvider(new DateTimeOffset(s_now, TimeSpan.Zero));
            return new CertificateAlarmScheduler(time, s_telemetry.CreateLogger<CertificateAlarmScheduler>());
        }

        private static ServerSystemContext CreateContext()
        {
            return new ServerSystemContext(DeterministicServerMock.Create(out _).Object);
        }

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
            ISystemContext context = CreateContext();

            scheduler.Start(context, TimeSpan.FromSeconds(30));
            Assert.That(scheduler.IsActive, Is.True);

            // The injected timer fires without throwing even with no monitors.
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));

            // Starting again while running is a no-op.
            Assert.DoesNotThrow(() => scheduler.Start(context, TimeSpan.FromSeconds(30)));
            Assert.That(scheduler.IsActive, Is.True);

            scheduler.Stop();
            Assert.That(scheduler.IsActive, Is.False);

            // Stopping again and evaluating after stop are safe no-ops.
            Assert.DoesNotThrow(scheduler.Stop);
            Assert.DoesNotThrow(() => scheduler.UpdateAndEvaluate(context, emitEvents: true));
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void StartAfterStopResumesEvaluation()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);
            ISystemContext context = CreateContext();

            scheduler.Start(context, TimeSpan.FromSeconds(30));
            scheduler.Stop();
            Assert.That(scheduler.IsActive, Is.False);

            scheduler.Start(context, TimeSpan.FromSeconds(30));
            Assert.That(scheduler.IsActive, Is.True);
        }

        [Test]
        public void DisposeStopsMonitoringAndIsIdempotent()
        {
            CertificateAlarmScheduler scheduler = CreateScheduler(out FakeTimeProvider time);
            scheduler.Start(CreateContext(), TimeSpan.FromSeconds(30));

            scheduler.Dispose();
            Assert.That(scheduler.IsActive, Is.False);
            Assert.DoesNotThrow(scheduler.Dispose);
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void CreateMonitorValidatesGroup()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);
            ISystemContext context = CreateContext();
            var groupWithoutNode = new ServerCertificateGroup { BrowseName = "NoNode", Node = null! };

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => scheduler.CreateMonitor(context, null!));
                Assert.Throws<ArgumentException>(() => scheduler.CreateMonitor(context, groupWithoutNode));
                Assert.That(scheduler.Monitors, Is.Empty);
            });
        }

        [Test]
        public void ConcurrentEvaluateStartAndStopIsRaceFree()
        {
            using CertificateAlarmScheduler scheduler = CreateScheduler(out _);
            ISystemContext context = CreateContext();
            scheduler.Start(context, TimeSpan.FromSeconds(30));

            Exception? failure = null;
            using var release = new ManualResetEventSlim(false);
            var workers = new Task[4];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Run(() =>
                {
                    try
                    {
                        for (int n = 0; n < 200; n++)
                        {
                            scheduler.UpdateAndEvaluate(context, emitEvents: true);
                        }

                        release.Wait(TimeSpan.FromSeconds(10));
                        scheduler.UpdateAndEvaluate(context, emitEvents: true);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref failure, ex, null);
                    }
                });
            }

            for (int r = 0; r < 20; r++)
            {
                scheduler.Stop();
                scheduler.Start(context, TimeSpan.FromSeconds(30));
            }

            scheduler.Stop();
            release.Set();

            Assert.DoesNotThrow(() => Task.WaitAll(workers));
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Null);
                Assert.That(scheduler.IsActive, Is.False);
            });
        }
    }
}
