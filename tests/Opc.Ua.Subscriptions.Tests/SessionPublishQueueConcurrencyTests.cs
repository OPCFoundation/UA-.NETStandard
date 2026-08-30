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
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Covers queue races around publish, requeue, expiration and transfer claims.
    /// </summary>
    [TestFixture]
    public class SessionPublishQueueConcurrencyTests
    {
        [Test]
        public async Task ConcurrentPublishRequeueAndExpirationDoesNotDuplicateSubscriptionAsync()
        {
            SessionPublishQueue queue = CreateQueue(out Mock<ISession> session);
            Mock<ISubscriptionPublishPipeline> subscription = CreateSubscription(1, session.Object);
            queue.Add(subscription.Object);
            IReadOnlyList<SessionPublishQueue.QueuedSubscription> snapshot = queue.CapturePublishTimerSnapshot();

            using var start = new Barrier(3);
            using var cancellationTokenSource = new CancellationTokenSource();
            Task<ISubscriptionPublishPipeline?> publishTask = Task.Run(
                async () =>
                {
                    start.SignalAndWait();
                    try
                    {
                        return await queue.PublishAsync(
                                "secure-channel",
                                DateTime.MaxValue,
                                requeue: false,
                                parkSink: null,
                                cancellationTokenSource.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (ServiceResultException exception)
                        when (exception.StatusCode == StatusCodes.BadNoSubscription)
                    {
                        return null;
                    }
                });
            Task requeueTask = Task.Run(
                () =>
                {
                    start.SignalAndWait();
                    queue.Requeue(subscription.Object);
                });
            Task<bool> expirationTask = Task.Run(
                () =>
                {
                    start.SignalAndWait();
                    return queue.TryRemoveForExpiration(snapshot[0]);
                });

            await Task.WhenAll(requeueTask, expirationTask).ConfigureAwait(false);
            cancellationTokenSource.Cancel();
            ISubscriptionPublishPipeline? publishedSubscription = await publishTask.ConfigureAwait(false);

            Assert.That(
                publishedSubscription == null || ReferenceEquals(publishedSubscription, subscription.Object),
                Is.True);

            if (queue.ContainsSubscription(subscription.Object))
            {
                queue.Requeue(subscription.Object);
                Task<ISubscriptionPublishPipeline> firstPublish = queue.PublishAsync(
                    "secure-channel",
                    DateTime.MaxValue,
                    requeue: false,
                    parkSink: null,
                    CancellationToken.None);
                Task<ISubscriptionPublishPipeline> secondPublish = queue.PublishAsync(
                    "secure-channel",
                    DateTime.MaxValue,
                    requeue: false,
                    parkSink: null,
                    CancellationToken.None);

                // Status == RanToCompletion rather than Task.IsCompletedSuccessfully,
                // which is .NET 5+ only and would break the net48/net472 builds.
                Assert.That(firstPublish.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(await firstPublish.ConfigureAwait(false), Is.SameAs(subscription.Object));
                Assert.That(secondPublish.Status, Is.Not.EqualTo(TaskStatus.RanToCompletion));

                queue.Remove(subscription.Object, removeQueuedRequests: true);
            }
            else
            {
                Assert.That(await expirationTask.ConfigureAwait(false), Is.True);
            }
        }

        [Test]
        public async Task TransferClaimProtectsClaimedEntryFromExpirationAndPreservesRequeueAsync()
        {
            SessionPublishQueue queue = CreateQueue(out Mock<ISession> session);
            Subscription subscription = CreateConcreteSubscription(2, session.Object);
            queue.Add(subscription);
            IReadOnlyList<SessionPublishQueue.QueuedSubscription> snapshot = queue.CapturePublishTimerSnapshot();

            bool claimed = queue.TryClaimForTransfer(
                subscription,
                session.Object,
                out SessionPublishQueue.SubscriptionTransferClaim? claim);

            Assert.That(claimed, Is.True);
            Assert.That(claim, Is.Not.Null);

            using var start = new Barrier(3);
            Task<bool> expirationTask = Task.Run(
                () =>
                {
                    start.SignalAndWait();
                    return queue.TryRemoveForExpiration(snapshot[0]);
                });
            Task<bool> secondClaimTask = Task.Run(
                () =>
                {
                    start.SignalAndWait();
                    return queue.TryClaimForTransfer(subscription, session.Object, out _);
                });
            Task requeueTask = Task.Run(
                () =>
                {
                    start.SignalAndWait();
                    queue.Requeue(subscription);
                });

            await Task.WhenAll(expirationTask, secondClaimTask, requeueTask).ConfigureAwait(false);

            Assert.That(await expirationTask.ConfigureAwait(false), Is.False);
            Assert.That(await secondClaimTask.ConfigureAwait(false), Is.False);
            Assert.That(queue.RestoreTransferClaim(claim!), Is.True);

            Task<ISubscriptionPublishPipeline> publishTask = queue.PublishAsync(
                "secure-channel",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                CancellationToken.None);

            Assert.That(publishTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(await publishTask.ConfigureAwait(false), Is.SameAs(subscription));
        }

        private static SessionPublishQueue CreateQueue(out Mock<ISession> session)
        {
            return CreateQueue(out session, out _);
        }

        private static SessionPublishQueue CreateQueue(
            out Mock<ISession> session,
            out Mock<IServerInternal> server)
        {
            var telemetry = new TestTelemetryContext();
            server = new Mock<IServerInternal>(MockBehavior.Loose);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);

            session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(value => value.Id).Returns(new NodeId(Guid.NewGuid()));
            session.SetupGet(value => value.Identity).Returns(new UserIdentity());
            session.SetupGet(value => value.IdentityToken).Returns(CreateIdentityToken().Object);
            session.SetupGet(value => value.ClientApplicationUri).Returns("urn:test");
            session.Setup(value => value.IsSecureChannelValid(It.IsAny<string>())).Returns(true);

            return new SessionPublishQueue(server.Object, session.Object, 10, TimeProvider.System);
        }

        private static Mock<IUserIdentityTokenHandler> CreateIdentityToken()
        {
            var identityToken = new Mock<IUserIdentityTokenHandler>(MockBehavior.Loose);
            identityToken.SetupGet(value => value.TokenType).Returns(UserTokenType.Anonymous);
            identityToken.SetupGet(value => value.Token).Returns(new AnonymousIdentityToken());
            return identityToken;
        }

        private static Mock<ISubscriptionPublishPipeline> CreateSubscription(uint id, ISession session)
        {
            var subscription = new Mock<ISubscriptionPublishPipeline>(MockBehavior.Loose);
            subscription.SetupGet(value => value.Id).Returns(id);
            subscription.SetupGet(value => value.Session).Returns(session);
            subscription.SetupGet(value => value.SessionId).Returns(session.Id);
            subscription.SetupGet(value => value.Priority).Returns(0);
            return subscription;
        }

        private static Subscription CreateConcreteSubscription(uint id, ISession session)
        {
            // RuntimeHelpers.GetUninitializedObject is .NET 5+ only; net48/net472 have the
            // equivalent on FormatterServices, which is obsolete on modern targets.
#if NET5_0_OR_GREATER
            var subscription = (Subscription)RuntimeHelpers.GetUninitializedObject(typeof(Subscription));
#else
            var subscription = (Subscription)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(Subscription));
#endif
            SetField(subscription, "m_lock", new Lock());
            SetField(subscription, "<Id>k__BackingField", id);
            SetField(subscription, "<Session>k__BackingField", session);
            SetField(subscription, "<Priority>k__BackingField", (byte)0);
            return subscription;
        }

        private static void SetField<TValue>(Subscription subscription, string fieldName, TValue value)
        {
            typeof(Subscription)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(subscription, value);
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
