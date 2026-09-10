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
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises callbacks already queued when their reverse-connect timer is disposed.
    /// </summary>
    [TestFixture]
    [Category("ReverseConnect")]
    [Parallelizable]
    public sealed class ReverseConnectServerTimerTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task QueuedCallbackAfterShutdownCannotConnectOrRearmAsync(bool dispose)
        {
            var scheduler = new QueuedTimerScheduler();
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create(), scheduler.Clock.Object);
            server.AddReverseConnection(s_clientUrl);
            Action queuedCallback = scheduler.Callbacks[0];

            if (dispose)
            {
                server.Dispose();
            }
            else
            {
                await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            queuedCallback();

            Assert.That(scheduler.Timers, Has.Count.EqualTo(1));
            scheduler.Timers[0].Verify(timer => timer.Dispose(), Times.Once);
            Assert.That(server.GetReverseConnections()[s_clientUrl].LastState, Is.EqualTo(ReverseConnectState.Closed));
        }

        [Test]
        public void QueuedCallbackFromRemovedTimerDoesNotReplaceCurrentTimer()
        {
            var scheduler = new QueuedTimerScheduler();
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create(), scheduler.Clock.Object);
            server.AddReverseConnection(s_clientUrl, enabled: false);
            Action queuedCallback = scheduler.Callbacks[0];

            Assert.That(server.RemoveReverseConnection(s_clientUrl), Is.True);
            server.AddReverseConnection(s_clientUrl, enabled: false);
            queuedCallback();

            Assert.That(scheduler.Timers, Has.Count.EqualTo(2));
            scheduler.Timers[0].Verify(timer => timer.Dispose(), Times.Once);
            scheduler.Timers[1].Verify(timer => timer.Dispose(), Times.Never);
        }

        [Test]
        public void ActiveCallbackSchedulesTheNextAttempt()
        {
            var scheduler = new QueuedTimerScheduler();
            using var server = new ReverseConnectServer(NUnitTelemetryContext.Create(), scheduler.Clock.Object);
            server.AddReverseConnection(s_clientUrl, enabled: false);

            scheduler.Callbacks[0]();

            Assert.That(scheduler.Timers, Has.Count.EqualTo(2));
            scheduler.Timers[0].Verify(timer => timer.Dispose(), Times.Once);
            scheduler.Timers[1].Verify(timer => timer.Dispose(), Times.Never);
            Assert.That(scheduler.DueTimes[0], Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(scheduler.DueTimes[1],
                Is.EqualTo(TimeSpan.FromMilliseconds(ReverseConnectServer.DefaultReverseConnectInterval)));
        }

        private sealed class QueuedTimerScheduler
        {
            public QueuedTimerScheduler()
            {
                Clock.Setup(clock => clock.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                    .Returns((TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) =>
                    {
                        Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
                        var timer = new Mock<ITimer>();
                        Timers.Add(timer);
                        Callbacks.Add(() => callback(state));
                        DueTimes.Add(dueTime);
                        return timer.Object;
                    });
            }

            public Mock<TimeProvider> Clock { get; } = new();
            public List<Mock<ITimer>> Timers { get; } = [];
            public List<Action> Callbacks { get; } = [];
            public List<TimeSpan> DueTimes { get; } = [];
        }

        private static readonly Uri s_clientUrl = new("opc.tcp://localhost:4840/queued-timer");
    }
}
