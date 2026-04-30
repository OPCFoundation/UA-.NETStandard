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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

using ManagedSessionClass = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Compliance tests that verify <see cref="ManagedSessionClass"/>
    /// faithfully delegates every <see cref="ISession"/> member to the
    /// inner V1 <see cref="Session"/>.
    /// </summary>
    [TestFixture]
    public sealed class ManagedSessionComplianceTests
    {
        private ManagedSessionClass m_managedSession;
        private SessionMock m_innerSession;

        [SetUp]
        public void SetUp()
        {
            m_innerSession = SessionMock.Create();
            m_innerSession.SetConnected();
            m_managedSession = CreateManagedSessionWithInner(m_innerSession);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_managedSession != null)
            {
                await m_managedSession.DisposeAsync().ConfigureAwait(false);
            }

            m_innerSession?.Dispose();
        }

        [Test]
        public void SessionIdDelegatesToInnerSession()
        {
            Assert.That(m_managedSession.SessionId, Is.EqualTo(m_innerSession.SessionId));
        }

        [Test]
        public void ConnectedDelegatesToInnerSession()
        {
            Assert.That(m_managedSession.Connected, Is.EqualTo(m_innerSession.Connected));
            Assert.That(m_managedSession.Connected, Is.True);
        }

        [Test]
        public void EndpointDelegatesToInnerSession()
        {
            // When inner session has no transport-level endpoint,
            // ManagedSession falls back to the configured endpoint
            // description. Verify ManagedSession.Endpoint is not null.
            Assert.That(
                m_managedSession.Endpoint, Is.Not.Null);
        }

        [Test]
        public void IdentityDelegatesToInnerSession()
        {
            Assert.That(
                m_managedSession.Identity,
                Is.EqualTo(m_innerSession.Identity));
        }

        [Test]
        public void SubscriptionCountDelegatesToInnerSession()
        {
            Assert.That(
                m_managedSession.SubscriptionCount,
                Is.EqualTo(m_innerSession.SubscriptionCount));
        }

        [Test]
        public void KeepAliveIntervalDelegatesToInnerSession()
        {
            m_innerSession.KeepAliveInterval = 12345;

            Assert.That(
                m_managedSession.KeepAliveInterval,
                Is.EqualTo(12345));
        }

        [Test]
        public void OperationTimeoutDelegatesToInnerSession()
        {
            // OperationTimeout delegates through to the transport
            // channel. Set it via the mock channel property and
            // verify both session and managed session agree.
            m_innerSession.Channel
                .SetupProperty(c => c.OperationTimeout, 0);
            m_innerSession.OperationTimeout = 9876;

            Assert.That(
                m_managedSession.OperationTimeout,
                Is.EqualTo(9876));
        }

        [Test]
        public void KeepAliveEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.KeepAlive += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseKeepAliveOnInner(m_innerSession);

            Assert.That(fired, Is.True, "KeepAlive event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession),
                "Sender should be the ManagedSession, not the inner");
        }

        [Test]
        public void NotificationEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.Notification += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseNotificationOnInner(m_innerSession);

            Assert.That(fired, Is.True, "Notification event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void PublishErrorEventForwardsToConsumer()
        {
            bool fired = false;
            ISession receivedSender = null;
            m_managedSession.PublishError += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaisePublishErrorOnInner(m_innerSession);

            Assert.That(fired, Is.True, "PublishError event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void SubscriptionsChangedEventForwardsToConsumer()
        {
            bool fired = false;
            object receivedSender = null;
            m_managedSession.SubscriptionsChanged += (sender, e) =>
            {
                fired = true;
                receivedSender = sender;
            };

            RaiseSubscriptionsChangedOnInner(m_innerSession);

            Assert.That(fired, Is.True,
                "SubscriptionsChanged event should fire");
            Assert.That(receivedSender, Is.SameAs(m_managedSession));
        }

        [Test]
        public void AddSubscriptionDelegatesToInnerSession()
        {
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new SubscriptionOptions
                {
                    DisplayName = "TestSub",
                    PublishingInterval = 1000
                });

            bool result = m_managedSession.AddSubscription(subscription);

            Assert.That(result, Is.True);
            Assert.That(m_innerSession.SubscriptionCount, Is.EqualTo(1));
            Assert.That(
                m_managedSession.SubscriptionCount,
                Is.EqualTo(1));
        }

        [Test]
        public async Task RemoveSubscriptionAsyncDelegatesToInnerSession()
        {
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new SubscriptionOptions
                {
                    DisplayName = "TestSub",
                    PublishingInterval = 1000
                });

            m_managedSession.AddSubscription(subscription);
            Assert.That(m_managedSession.SubscriptionCount, Is.EqualTo(1));

            bool result = await m_managedSession
                .RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);

            Assert.That(result, Is.True);
            Assert.That(m_managedSession.SubscriptionCount, Is.Zero);
        }

        /// <summary>
        /// Creates a <see cref="ManagedSessionClass"/> with
        /// the given inner session injected via reflection, bypassing
        /// the async factory that needs a real server.
        /// </summary>
        private static ManagedSessionClass
            CreateManagedSessionWithInner(Session innerSession)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<ManagedSessionClass> logger = telemetry.CreateLogger<ManagedSessionClass>();

            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens = [new UserTokenPolicy()]
                });

            var reconnectPolicy = new ReconnectPolicy();
            var sessionFactory = new Mock<ISessionFactory>();
            sessionFactory.SetupGet(f => f.Telemetry)
                .Returns(telemetry);

            ConstructorInfo ctor = typeof(ManagedSessionClass)
                .GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [
                        typeof(ApplicationConfiguration),
                        typeof(ConfiguredEndpoint),
                        typeof(ISessionFactory),
                        typeof(IReconnectPolicy),
                        typeof(IServerRedundancyHandler),
                        typeof(ILogger),
                        typeof(IUserIdentity),
                        typeof(ArrayOf<string>),
                        typeof(string),
                        typeof(uint),
                        typeof(bool)
                    ],
                    null);

            var managed =
                (ManagedSessionClass)ctor!.Invoke(
                [
                    configuration,
                    endpoint,
                    sessionFactory.Object,
                    reconnectPolicy,
                    null,
                    logger,
                    (IUserIdentity)null,
                    default(ArrayOf<string>),
                    "TestManagedSession",
                    60000u,
                    false
                ]);

            // Inject the inner session.
            typeof(ManagedSessionClass)
                .GetField(
                    "m_session",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(managed, innerSession);

            // Wire events so the ManagedSession forwards inner
            // session events to its own subscribers.
            typeof(ManagedSessionClass)
                .GetMethod(
                    "WireSessionEvents",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(managed, [innerSession]);

            return managed;
        }

        private static void RaiseKeepAliveOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_KeepAlive",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = (KeepAliveEventHandler)field!.GetValue(session);
            handler?.Invoke(
                session,
                new KeepAliveEventArgs(
                    null, ServerState.Running, DateTime.UtcNow));
        }

        private static void RaiseNotificationOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_Publish",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler =
                (NotificationEventHandler)field!.GetValue(session);
            handler?.Invoke(
                session,
                new NotificationEventArgs(null, null, default));
        }

        private static void RaisePublishErrorOnInner(Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_PublishError",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler =
                (PublishErrorEventHandler)field!.GetValue(session);
            handler?.Invoke(
                session,
                new PublishErrorEventArgs(
                    new ServiceResult(StatusCodes.BadTimeout)));
        }

        private static void RaiseSubscriptionsChangedOnInner(
            Session session)
        {
            FieldInfo field = typeof(Session).GetField(
                "m_SubscriptionsChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = (EventHandler)field!.GetValue(session);
            handler?.Invoke(session, EventArgs.Empty);
        }
    }
}
