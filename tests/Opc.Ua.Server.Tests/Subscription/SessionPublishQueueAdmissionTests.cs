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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class SessionPublishQueueAdmissionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedOutAndCancelledPublishRequestsReleaseCapacityAsync(bool cancel)
        {
            var clock = new FakeTimeProvider();
            (IServerInternal server, ISession session) = CreateHost();
            using var subscription = new Subscription(
                server, session, 1, 60000, 30, 10, 0, 0, true, 10, clock);
            using var queue = new SessionPublishQueue(server, session, 1, clock);
            using var cancellation = new CancellationTokenSource();
            queue.Add(subscription);

            Task<ISubscriptionPublishPipeline> expired = queue.PublishAsync(
                "channel", clock.GetUtcNow().UtcDateTime.AddMilliseconds(100), false, null, cancellation.Token);
            Assert.That(expired.IsCompleted, Is.False);

            if (cancel)
            {
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await expired.ConfigureAwait(false));
            }
            else
            {
                clock.Advance(TimeSpan.FromMilliseconds(600));
                ServiceResultException exception = Assert.CatchAsync<ServiceResultException>(
                    async () => await expired.ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            }

            Task<ISubscriptionPublishPipeline> replacement = queue.PublishAsync(
                "channel", DateTime.MaxValue, false, null, CancellationToken.None);
            Assert.That(replacement.IsCompleted, Is.False, "Only live Publish requests consume admission capacity.");
            ServiceResultException full = Assert.CatchAsync<ServiceResultException>(
                () => queue.PublishAsync("channel", DateTime.MaxValue, false, null, CancellationToken.None));
            Assert.That(full.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));

            queue.PublishCompleted(subscription, true);
            Assert.That(await replacement.ConfigureAwait(false), Is.SameAs(subscription));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationDuringAdmissionRetiresOnlyItsOwnRequestAsync(bool cancelBeforeAdmission)
        {
            var clock = new FakeTimeProvider();
            (IServerInternal server, ISession session) = CreateHost();
            using var subscription = new Subscription(
                server, session, 1, 60000, 30, 10, 0, 0, true, 10, clock);
            using var queue = new SessionPublishQueue(server, session, 2, clock);
            using var cancellation = new CancellationTokenSource();
            queue.Add(subscription);
            if (cancelBeforeAdmission)
            {
                cancellation.Cancel();
            }

            Task<ISubscriptionPublishPipeline> cancelled = queue.PublishAsync(
                "channel",
                clock.GetUtcNow().UtcDateTime.AddSeconds(1),
                false,
                new ActionParkSink(cancellation.Cancel),
                cancellation.Token);
            Assert.CatchAsync<OperationCanceledException>(async () => await cancelled.ConfigureAwait(false));

            Task<ISubscriptionPublishPipeline> first = queue.PublishAsync(
                "channel", DateTime.MaxValue, false, null, CancellationToken.None);
            Task<ISubscriptionPublishPipeline> second = queue.PublishAsync(
                "channel", DateTime.MaxValue, false, null, CancellationToken.None);
            cancellation.Cancel();
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
            });
            ServiceResultException full = Assert.CatchAsync<ServiceResultException>(
                () => queue.PublishAsync("channel", DateTime.MaxValue, false, null, CancellationToken.None));
            Assert.That(full.StatusCode, Is.EqualTo(StatusCodes.BadTooManyPublishRequests));

            Assert.That(queue.TryPublishCustomStatus(StatusCodes.Good), Is.True);
            Assert.That(await second.ConfigureAwait(false), Is.Null);
            queue.PublishCompleted(subscription, true);
            Assert.That(await first.ConfigureAwait(false), Is.SameAs(subscription));
        }

        [Test]
        public async Task CancellingWhileParkedAndDisposingDoesNotDeadlockAsync()
        {
            var clock = new FakeTimeProvider();
            (IServerInternal server, ISession session) = CreateHost();
            using var subscription = new Subscription(
                server, session, 1, 60000, 30, 10, 0, 0, true, 10, clock);
            using var queue = new SessionPublishQueue(server, session, 1, clock);
            using var cancellation = new CancellationTokenSource();
            using var parked = new ManualResetEventSlim();
            using var cancellationStarted = new ManualResetEventSlim();
            using var dispose = new ManualResetEventSlim();
            queue.Add(subscription);

            Task<ISubscriptionPublishPipeline> publish = Task.Run(
                () => queue.PublishAsync(
                    "channel",
                    DateTime.MaxValue,
                    false,
                    new ActionParkSink(() =>
                    {
                        parked.Set();
                        Assert.That(dispose.Wait(TimeSpan.FromSeconds(10)), Is.True, "Disposal barrier was not released.");
                        queue.Dispose();
                    }),
                    cancellation.Token));

            Task cancellationTask = Task.CompletedTask;
            try
            {
                Assert.That(parked.Wait(TimeSpan.FromSeconds(10)), Is.True, "Publish did not reach the park boundary.");
                using CancellationTokenRegistration started = cancellation.Token.Register(cancellationStarted.Set);
                cancellationTask = Task.Run(cancellation.Cancel);
                Assert.That(cancellationStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            }
            finally
            {
                dispose.Set();
            }

            await cancellationTask.ConfigureAwait(false);
            Exception exception = Assert.CatchAsync(async () => await publish.ConfigureAwait(false));
            if (exception is ServiceResultException serviceException)
            {
                Assert.That(serviceException.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            }
            else
            {
                Assert.That(exception, Is.InstanceOf<OperationCanceledException>());
            }
            queue.Dispose();
        }

        private static (IServerInternal Server, ISession Session) CreateHost()
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(s => s.MonitoredItemQueueFactory).Returns(Mock.Of<IMonitoredItemQueueFactory>());
            server.SetupGet(s => s.DiagnosticsNodeManager).Returns(Mock.Of<IDiagnosticsNodeManager>());
            server.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.SetupGet(s => s.ServerUris).Returns(new StringTable());
            server.SetupGet(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

            var identity = new UserIdentity();
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(1));
            session.SetupGet(s => s.Identity).Returns(identity);
            session.SetupGet(s => s.IdentityToken).Returns(identity.TokenHandler);
            session.SetupGet(s => s.ClientApplicationUri).Returns("urn:publish-admission");
            session.Setup(s => s.IsSecureChannelValid("channel")).Returns(true);
            return (server.Object, session.Object);
        }

        private sealed class ActionParkSink(Action action) : IRequestParkSink
        {
            public void NotifyParked()
            {
                action();
            }
        }
    }
}
