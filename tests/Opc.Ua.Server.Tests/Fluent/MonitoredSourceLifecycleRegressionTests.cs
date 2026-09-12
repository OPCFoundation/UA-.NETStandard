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
    [TestFixture]
    [Category("Fluent")]
    public sealed class MonitoredSourceLifecycleRegressionTests
    {
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
