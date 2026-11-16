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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A faulted server channel cancels its requests when the grace period of the current
    /// fault ends. A timer callback left over from an earlier fault (it was already waiting
    /// for the channel gate when the client reconnected) must not cut the grace period of a
    /// later fault short.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [Parallelizable(ParallelScope.All)]
    public sealed class TcpChannelFaultGraceTests
    {
        [Test]
        public void StaleGraceCallbackDoesNotCancelALaterFault()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var channel = CreateChannel(time);

            // first fault: its timer callback fires but waits for the gate
            using (channel.Gate.Enter())
            {
                channel.SetState(TcpChannelState.Faulted);
                channel.ScheduleFaultedRequestCancellation();
            }
            // the gate is not re-entrant: read the generation after leaving it
            long first = channel.FaultedRequestGeneration;

            // meanwhile the client reconnects and the new transport faults again
            using (channel.Gate.Enter())
            {
                channel.SetState(TcpChannelState.Open);
                channel.OnRequestTransportReconnected();
                channel.SetState(TcpChannelState.Faulted);
                channel.ScheduleFaultedRequestCancellation();
            }
            Assert.That(channel.FaultedRequestGeneration, Is.Not.EqualTo(first));

            // the stale callback gets the gate: the new fault keeps its grace period
            channel.OnFaultedRequestGraceElapsed(first);
            // the cancellation would run on the thread pool: give it time to show
            Assert.That(
                SpinWait.SpinUntil(() => channel.RequestCancellationToken.IsCancellationRequested, 500),
                Is.False,
                "a stale grace callback cancelled the requests of a later fault");

            // the current fault's grace period ends
            time.Advance(TcpListenerChannel.FaultedRequestGracePeriod);
            Assert.That(
                SpinWait.SpinUntil(() => channel.RequestCancellationToken.IsCancellationRequested, 5000),
                Is.True,
                "the current fault cancels the requests once its grace period ends");
        }

        [Test]
        public void ReconnectWithinTheGracePeriodKeepsTheRequests()
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var channel = CreateChannel(time);
            CancellationToken token = channel.RequestCancellationToken;

            using (channel.Gate.Enter())
            {
                channel.SetState(TcpChannelState.Faulted);
                channel.ScheduleFaultedRequestCancellation();
            }
            using (channel.Gate.Enter())
            {
                channel.SetState(TcpChannelState.Open);
                channel.OnRequestTransportReconnected();
            }
            time.Advance(TcpListenerChannel.FaultedRequestGracePeriod + TimeSpan.FromSeconds(1));

            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(channel.RequestCancellationToken, Is.EqualTo(token));
        }

        private static TestChannel CreateChannel(TimeProvider time)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("fault-grace-test", 8192, telemetry);
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(telemetry));
            return new TestChannel(new TcpTransportListener(telemetry), buffers, quotas, telemetry, time);
        }

        private sealed class TestChannel : TcpServerChannel
        {
            public TestChannel(
                TcpTransportListener listener,
                BufferManager buffers,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                TimeProvider time)
                : base("fault-grace-test", listener, buffers, quotas, null!, [], telemetry, time)
            {
            }

            public void SetState(TcpChannelState state)
            {
                State = state;
            }
        }
    }
}
