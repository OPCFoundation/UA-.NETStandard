/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Verifies ownership of a monitored source when queued lifecycle updates supersede a stop.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class MonitoredSourceLifecycleRegressionTests
    {
        /// <summary>
        /// Verifies that a queued restart supersedes a stop without releasing or reacquiring the still-live source.
        /// </summary>
        [Test]
        public async Task SupersededStopDoesNotReleaseOrReacquireTheLiveSourceAsync()
        {
            var node = new BaseDataVariableState(null) { NodeId = new NodeId(1, 1) };
            var context = new SystemContext(NUnitTelemetryContext.Create());
            await using var registration = new MonitoredSourceRegistration(
                node.NodeId, new FakeTimeProvider(), NullLogger.Instance, CancellationToken.None);
            int acquired = 0;
            int released = 0;
            bool active = false;
            registration.SetFirstSubscriber((_, _, _) =>
            {
                acquired++;
                active = true;
                return default;
            });
            registration.SetLastSubscriber((_, _, _) =>
            {
                released++;
                active = false;
                return default;
            });
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            item.SetupGet(value => value.Id).Returns(1);
            item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
            item.SetupGet(value => value.SamplingInterval).Returns(100);
            await registration.OnCreatedAsync(context, node, item.Object).ConfigureAwait(false);
            var gate = (SemaphoreSlim)typeof(MonitoredSourceRegistration)
                .GetField("m_updateLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registration)!;
            await gate.WaitAsync().ConfigureAwait(false);
            Task stop;
            Task restart;
            try
            {
                stop = registration.OnMonitoringModeChangedAsync(context, node, item.Object, MonitoringMode.Disabled)
                    .AsTask();
                restart = registration.OnMonitoringModeChangedAsync(context, node, item.Object, MonitoringMode.Reporting)
                    .AsTask();
            }
            finally
            {
                gate.Release();
            }
            await Task.WhenAll(stop, restart).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(acquired, Is.EqualTo(1));
                Assert.That(released, Is.Zero);
                Assert.That(active, Is.True);
            });
            await registration.ReleaseAsync(context).ConfigureAwait(false);
            Assert.That(released, Is.EqualTo(1));
            Assert.That(active, Is.False);
        }
    }
}
