/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Additional coverage-oriented tests for <see cref="SessionManager"/> targeting
    /// constructor guards, session lookup, event accessors, event raising and
    /// lockout maintenance that are not exercised by the higher-level fixtures.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionManagerCoverageTests
    {
        private Mock<IServerInternal> m_serverMock = null!;
        private ITelemetryContext m_telemetry = null!;
        private ApplicationConfiguration m_config = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());

            m_config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
        }

        private CoverageSessionManager CreateManager()
        {
            return new CoverageSessionManager(m_serverMock.Object, m_config);
        }

        [Test]
        public void ConstructorWithNullConfigurationThrows()
        {
            Assert.That(
                () => new SessionManager(m_serverMock.Object, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorWithNullServerThrows()
        {
            Assert.That(
                () => new SessionManager(null!, m_config),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GetSessionWithUnknownTokenReturnsNull()
        {
            using CoverageSessionManager manager = CreateManager();

            ISession? session = manager.GetSession(new NodeId(Guid.NewGuid()));

            Assert.That(session, Is.Null);
        }

        [Test]
        public void GetSessionsWhenEmptyReturnsEmptyList()
        {
            using CoverageSessionManager manager = CreateManager();

            Assert.That(manager.GetSessions(), Is.Empty);
        }

        [Test]
        public void RaiseSessionDiagnosticsChangedEventWithNullSessionThrows()
        {
            using CoverageSessionManager manager = CreateManager();

            Assert.That(
                () => manager.RaiseSessionDiagnosticsChangedEvent(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ClearAuthenticationLockoutsDoesNotThrow()
        {
            using CoverageSessionManager manager = CreateManager();

            Assert.That(manager.ClearAuthenticationLockouts, Throws.Nothing);
        }

        [Test]
        public void ValidateSessionLessRequestEventCanBeSubscribedAndRemoved()
        {
            using CoverageSessionManager manager = CreateManager();
            static void Handler(object? sender, ValidateSessionLessRequestEventArgs e)
            {
            }

            Assert.That(
                () =>
                {
                    manager.ValidateSessionLessRequest += Handler;
                    manager.ValidateSessionLessRequest -= Handler;
                },
                Throws.Nothing);
        }

        [Test]
        public void ImpersonateUserEventCanBeSubscribedAndRemoved()
        {
            using CoverageSessionManager manager = CreateManager();
            static void Handler(ISession session, ImpersonateEventArgs e)
            {
            }

#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(
                () =>
                {
                    manager.ImpersonateUser += Handler;
                    manager.ImpersonateUser -= Handler;
                },
                Throws.Nothing);
#pragma warning restore CS0618
        }

        [TestCase(SessionEventReason.Created)]
        [TestCase(SessionEventReason.Activated)]
        [TestCase(SessionEventReason.Closing)]
        [TestCase(SessionEventReason.DiagnosticsChanged)]
        [TestCase(SessionEventReason.ChannelKeepAlive)]
        public void SubscribedEventHandlerIsInvokedOnMatchingReason(SessionEventReason reason)
        {
            using CoverageSessionManager manager = CreateManager();
            var session = new Mock<ISession>();

            ISession? received = null;
            SessionEventReason? receivedReason = null;
            void Handler(ISession s, SessionEventReason r)
            {
                received = s;
                receivedReason = r;
            }

            manager.Subscribe(reason, Handler);
            manager.PublicRaiseSessionEvent(session.Object, reason);

            Assert.That(received, Is.SameAs(session.Object));
            Assert.That(receivedReason, Is.EqualTo(reason));

            // Unsubscribing must stop further callbacks.
            received = null;
            manager.Unsubscribe(reason, Handler);
            manager.PublicRaiseSessionEvent(session.Object, reason);
            Assert.That(received, Is.Null);
        }

        [Test]
        public void RaiseSessionEventSwallowsHandlerException()
        {
            using CoverageSessionManager manager = CreateManager();
            var session = new Mock<ISession>();

            manager.SessionDiagnosticsChanged += (_, _) => throw new InvalidOperationException("boom");

            Assert.That(
                () => manager.RaiseSessionDiagnosticsChangedEvent(session.Object),
                Throws.Nothing);
        }

        [Test]
        public void RaiseSessionEventWithImpersonatingReasonDoesNotThrow()
        {
            using CoverageSessionManager manager = CreateManager();
            var session = new Mock<ISession>();

            Assert.That(
                () => manager.PublicRaiseSessionEvent(session.Object, SessionEventReason.Impersonating),
                Throws.Nothing);
        }

        [Test]
        public void RaiseSessionEventWithUnknownReasonThrows()
        {
            using CoverageSessionManager manager = CreateManager();
            var session = new Mock<ISession>();

            Assert.That(
                () => manager.PublicRaiseSessionEvent(session.Object, (SessionEventReason)999),
                Throws.TypeOf<ServiceResultException>());
        }

        private sealed class CoverageSessionManager : SessionManager
        {
            public CoverageSessionManager(IServerInternal server, ApplicationConfiguration config)
                : base(server, config)
            {
            }

            public void PublicRaiseSessionEvent(ISession session, SessionEventReason reason)
            {
                RaiseSessionEvent(session, reason);
            }

            public void Subscribe(SessionEventReason reason, SessionEventHandler handler)
            {
                switch (reason)
                {
                    case SessionEventReason.Created:
                        SessionCreated += handler;
                        break;
                    case SessionEventReason.Activated:
                        SessionActivated += handler;
                        break;
                    case SessionEventReason.Closing:
                        SessionClosing += handler;
                        break;
                    case SessionEventReason.DiagnosticsChanged:
                        SessionDiagnosticsChanged += handler;
                        break;
                    case SessionEventReason.ChannelKeepAlive:
                        SessionChannelKeepAlive += handler;
                        break;
                }
            }

            public void Unsubscribe(SessionEventReason reason, SessionEventHandler handler)
            {
                switch (reason)
                {
                    case SessionEventReason.Created:
                        SessionCreated -= handler;
                        break;
                    case SessionEventReason.Activated:
                        SessionActivated -= handler;
                        break;
                    case SessionEventReason.Closing:
                        SessionClosing -= handler;
                        break;
                    case SessionEventReason.DiagnosticsChanged:
                        SessionDiagnosticsChanged -= handler;
                        break;
                    case SessionEventReason.ChannelKeepAlive:
                        SessionChannelKeepAlive -= handler;
                        break;
                }
            }
        }
    }
}
