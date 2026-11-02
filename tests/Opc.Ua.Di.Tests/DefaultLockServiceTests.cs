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
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for the in-memory <see cref="DefaultLockService"/>
    /// — verifies ownership, expiry, contention, and forcible-break
    /// semantics without going through the OPC UA stack.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Locking")]
    public sealed class DefaultLockServiceTests
    {
        private static readonly NodeId s_elementId = new("device-1", 2);

        [Test]
        public void InitLockSucceedsForUnlockedElement()
        {
            using var service = new DefaultLockService();
            int status = service.InitLock(
                TestSystemContext("session-A", "alice"),
                s_elementId,
                "tag-1");
            Assert.That(status, Is.EqualTo(LockStatus.Ok));
        }

        [Test]
        public void InitLockReturnsAlreadyLockedWhenHeldByOther()
        {
            using var service = new DefaultLockService();
            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");

            int status = service.InitLock(
                TestSystemContext("session-B", "bob"),
                s_elementId,
                "tag-B");

            Assert.That(status, Is.EqualTo(LockStatus.AlreadyLocked));
        }

        [Test]
        public void GetStateReportsOwnerAndTimeRemaining()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(
                lockDuration: TimeSpan.FromMinutes(2),
                timeProvider: time);

            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");

            LockState state = service.GetState(s_elementId);
            Assert.That(state.Locked, Is.True);
            Assert.That(state.LockingClient, Is.EqualTo("tag-A"));
            Assert.That(state.LockingUser, Is.EqualTo("alice"));
            Assert.That(state.RemainingLockTimeSeconds, Is.EqualTo(120.0).Within(0.01));
        }

        [Test]
        public void RenewLockExtendsExpiry()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(
                lockDuration: TimeSpan.FromMinutes(2),
                timeProvider: time);

            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");
            time.Advance(TimeSpan.FromSeconds(90));

            int status = service.RenewLock(
                TestSystemContext("session-A", "alice"),
                s_elementId);

            Assert.That(status, Is.EqualTo(LockStatus.Ok));
            LockState state = service.GetState(s_elementId);
            Assert.That(state.RemainingLockTimeSeconds, Is.EqualTo(120.0).Within(0.01));
        }

        [Test]
        public void RenewLockRejectsWrongClient()
        {
            using var service = new DefaultLockService();
            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");

            int status = service.RenewLock(
                TestSystemContext("session-B", "bob"),
                s_elementId);

            Assert.That(status, Is.EqualTo(LockStatus.WrongClient));
        }

        [Test]
        public void ExitLockReleasesOwnedLock()
        {
            using var service = new DefaultLockService();
            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");
            int status = service.ExitLock(
                TestSystemContext("session-A", "alice"),
                s_elementId);

            Assert.That(status, Is.EqualTo(LockStatus.Ok));
            Assert.That(service.GetState(s_elementId).Locked, Is.False);
        }

        [Test]
        public void ExitLockRejectsWrongClient()
        {
            using var service = new DefaultLockService();
            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");
            int status = service.ExitLock(
                TestSystemContext("session-B", "bob"),
                s_elementId);
            Assert.That(status, Is.EqualTo(LockStatus.WrongClient));
        }

        [Test]
        public void BreakLockReleasesRegardlessOfOwner()
        {
            using var service = new DefaultLockService();
            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");
            int status = service.BreakLock(
                AdminContext(),
                s_elementId);

            Assert.That(status, Is.EqualTo(LockStatus.Ok));
            Assert.That(service.GetState(s_elementId).Locked, Is.False);
        }

        [Test]
        public void BreakLockReturnsNotLockedWhenUnlocked()
        {
            using var service = new DefaultLockService();
            int status = service.BreakLock(
                AdminContext(),
                s_elementId);
            Assert.That(status, Is.EqualTo(LockStatus.NotLocked));
        }

        [Test]
        public void ExpiredLockTreatedAsUnlocked()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(
                lockDuration: TimeSpan.FromSeconds(30),
                timeProvider: time);

            service.InitLock(TestSystemContext("session-A", "alice"), s_elementId, "tag-A");
            time.Advance(TimeSpan.FromMinutes(2));

            // After expiry, another client may acquire.
            int status = service.InitLock(
                TestSystemContext("session-B", "bob"),
                s_elementId,
                "tag-B");
            Assert.That(status, Is.EqualTo(LockStatus.Ok));

            LockState state = service.GetState(s_elementId);
            Assert.That(state.LockingUser, Is.EqualTo("bob"));
        }

        [Test]
        public void GetStateReturnsUnlockedForUnknownElement()
        {
            using var service = new DefaultLockService();
            LockState state = service.GetState(new NodeId("never-locked", 2));
            Assert.That(state.Locked, Is.False);
            Assert.That(state.LockingClient, Is.Empty);
            Assert.That(state.LockingUser, Is.Empty);
            Assert.That(state.RemainingLockTimeSeconds, Is.Zero);
        }

        [Test]
        public void IsLockOwnerUsesServerSessionIdRatherThanUserOrClientContext()
        {
            using var service = new DefaultLockService();
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            ServerSystemContext other = TestSystemContext("session-B", "alice");
            Assert.That(service.InitLock(owner, s_elementId, "session-B"), Is.EqualTo(LockStatus.Ok));

            ILockOwnershipService ownership = service;
            Assert.That(ownership.IsLockOwner(owner, s_elementId), Is.True);
            Assert.That(ownership.IsLockOwner(other, s_elementId), Is.False);
            Assert.That(service.RenewLock(other, s_elementId), Is.EqualTo(LockStatus.WrongClient));
            Assert.That(service.ExitLock(other, s_elementId), Is.EqualTo(LockStatus.WrongClient));
            Assert.That(ownership.IsLockOwner(TestSystemContext("session-A", "bob"), s_elementId), Is.True);
        }

        [Test]
        public void IsLockOwnerSupportsSessionSystemContext()
        {
            using var service = new DefaultLockService();
            var context = new SessionSystemContext(telemetry: null!)
            {
                SessionId = new NodeId("session-A", 1)
            };
            Assert.That(service.InitLock(context, s_elementId, "tag"), Is.EqualTo(LockStatus.Ok));
            Assert.That(service.IsLockOwner(context, s_elementId), Is.True);
            Assert.That(service.IsLockOwner(TestSystemContext("session-A", "alice"), s_elementId), Is.True);
        }

        [Test]
        public void IsLockOwnerDoesNotRenewOrChangeLockState()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(TimeSpan.FromMinutes(2), time);
            ServerSystemContext context = TestSystemContext("session-A", "alice");
            service.InitLock(context, s_elementId, "tag-A");
            time.Advance(TimeSpan.FromSeconds(90));
            LockState before = service.GetState(s_elementId);

            Assert.That(service.IsLockOwner(context, s_elementId), Is.True);
            Assert.That(service.IsLockOwner(context, s_elementId), Is.True);
            Assert.That(service.GetState(s_elementId), Is.EqualTo(before));
            Assert.That(before.RemainingLockTimeSeconds, Is.EqualTo(30));

            time.Advance(TimeSpan.FromSeconds(30));
            Assert.That(service.IsLockOwner(context, s_elementId), Is.False);
        }

        [TestCase(29, true)]
        [TestCase(30, false)]
        [TestCase(31, false)]
        public void IsLockOwnerDeniesAtAndAfterExpiry(int elapsedSeconds, bool expected)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(TimeSpan.FromSeconds(30), time);
            ServerSystemContext context = TestSystemContext("session-A", "alice");
            service.InitLock(context, s_elementId, "tag");
            time.Advance(TimeSpan.FromSeconds(elapsedSeconds));

            Assert.That(service.IsLockOwner(context, s_elementId), Is.EqualTo(expected));
        }

        [Test]
        public void IsLockOwnerDeniesUnknownAndReleasedElements()
        {
            using var service = new DefaultLockService();
            ServerSystemContext context = TestSystemContext("session-A", "alice");
            Assert.That(service.IsLockOwner(context, s_elementId), Is.False);
            service.InitLock(context, s_elementId, "tag");
            Assert.That(service.ExitLock(context, s_elementId), Is.EqualTo(LockStatus.Ok));
            Assert.That(service.IsLockOwner(context, s_elementId), Is.False);
        }

        [Test]
        public void MissingSessionCannotAcquireOrAuthorizeByUserName()
        {
            using var service = new DefaultLockService();
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            var noSession = new SystemContext(telemetry: null!) { UserId = "alice" };
            ServerSystemContext nullSession = TestSystemContext("session-A", "alice");
            nullSession.SessionId = NodeId.Null;
            ServerSystemContext absentSession = TestSystemContext("session-A", "alice");
            absentSession.SessionId = null;

            foreach (ISystemContext context in new ISystemContext[] { noSession, nullSession, absentSession })
            {
                Assert.That(service.InitLock(context, s_elementId, "tag"), Is.EqualTo(LockStatus.CouldNotLock));
                Assert.That(service.IsLockOwner(context, s_elementId), Is.False);
            }
            Assert.That(service.InitLock(owner, s_elementId, "tag"), Is.EqualTo(LockStatus.Ok));
            foreach (ISystemContext context in new ISystemContext[] { noSession, nullSession, absentSession })
            {
                Assert.That(service.IsLockOwner(context, s_elementId), Is.False);
                Assert.That(service.RenewLock(context, s_elementId), Is.EqualTo(LockStatus.WrongClient));
                Assert.That(service.ExitLock(context, s_elementId), Is.EqualTo(LockStatus.WrongClient));
            }
            Assert.That(service.IsLockOwner(owner, s_elementId), Is.True);
        }

        [Test]
        public void SessionClosingRevokesAllOwnedLocksButNotSameUserOtherSession()
        {
            var manager = new Mock<ISessionManager>();
            using var service = new DefaultLockService();
            service.AttachToSessionManager(manager.Object);
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            ServerSystemContext other = TestSystemContext("session-B", "alice");
            var secondElement = new NodeId("device-2", 2);
            var otherElement = new NodeId("device-3", 2);
            service.InitLock(owner, s_elementId, "first");
            service.InitLock(owner, secondElement, "second");
            service.InitLock(other, otherElement, "third");
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId("session-A", 1));

            manager.Raise(value => value.SessionClosing += null, session.Object, SessionEventReason.Closing);

            Assert.That(service.IsLockOwner(owner, s_elementId), Is.False);
            Assert.That(service.IsLockOwner(owner, secondElement), Is.False);
            Assert.That(service.GetState(s_elementId).Locked, Is.False);
            Assert.That(service.GetState(secondElement).Locked, Is.False);
            Assert.That(service.IsLockOwner(other, otherElement), Is.True);
            Assert.That(service.InitLock(other, s_elementId, "replacement"), Is.EqualTo(LockStatus.Ok));
            Assert.That(service.IsLockOwner(owner, s_elementId), Is.False);
            Assert.That(service.IsLockOwner(other, s_elementId), Is.True);
        }

        [Test]
        public void BreakLockRevokesOwnership()
        {
            using var service = new DefaultLockService();
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            service.InitLock(owner, s_elementId, "tag");

            Assert.That(service.BreakLock(AdminContext(), s_elementId), Is.EqualTo(LockStatus.Ok));
            Assert.That(service.IsLockOwner(owner, s_elementId), Is.False);
        }

        [TestCase(UserTokenType.UserName, MessageSecurityMode.SignAndEncrypt, false, StatusCodes.BadUserAccessDenied)]
        [TestCase(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt, true, StatusCodes.BadUserAccessDenied)]
        [TestCase(UserTokenType.UserName, MessageSecurityMode.Sign, true, StatusCodes.BadSecurityModeInsufficient)]
        [TestCase(UserTokenType.UserName, MessageSecurityMode.None, true, StatusCodes.BadSecurityModeInsufficient)]
        public void BreakLockRejectsUnauthorizedCallerWithoutChangingLock(
            UserTokenType tokenType,
            MessageSecurityMode securityMode,
            bool grantAdmin,
            uint expectedStatus)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var service = new DefaultLockService(TimeSpan.FromMinutes(2), time);
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            service.InitLock(owner, s_elementId, "tag");
            LockState before = service.GetState(s_elementId);

            ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                () => service.BreakLock(AdminContext(tokenType, securityMode, grantAdmin), s_elementId));

            Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(service.GetState(s_elementId), Is.EqualTo(before));
            Assert.That(service.IsLockOwner(owner, s_elementId), Is.True);
        }

        [Test]
        public void IsLockOwnerValidatesArguments()
        {
            using var service = new DefaultLockService();
            Assert.Throws<ArgumentNullException>(() => service.IsLockOwner(null!, s_elementId));
            Assert.Throws<ArgumentNullException>(
                () => service.IsLockOwner(TestSystemContext("session-A", "alice"), NodeId.Null));
        }

        /// <summary>
        /// Creates the server context used by method dispatch with a real session identifier.
        /// </summary>
        private static ServerSystemContext TestSystemContext(string sessionId, string user)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.DisplayName).Returns(user);
            return new ServerSystemContext(Mock.Of<IServerInternal>())
            {
                SessionId = new NodeId(sessionId, 1),
                UserIdentity = identity.Object
            };
        }

        /// <summary>
        /// Creates a caller for the existing SecurityAdmin authorization gate.
        /// </summary>
        private static SessionSystemContext AdminContext(
            UserTokenType tokenType = UserTokenType.UserName,
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt,
            bool grantAdmin = true)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(tokenType);
            identity.SetupGet(value => value.GrantedRoleIds)
                .Returns(grantAdmin ? [ObjectIds.WellKnownRole_SecurityAdmin] : ArrayOf<NodeId>.Empty);
            var channel = new SecureChannelContext(
                "test-channel",
                new EndpointDescription { SecurityMode = securityMode },
                RequestEncoding.Binary);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            return new SessionSystemContext(operation, telemetry: null!);
        }

        [Test]
        public void DisposeRevokesOwnershipAndDetachesSessionClosing()
        {
            var manager = new Mock<ISessionManager>();
            var service = new DefaultLockService();
            service.AttachToSessionManager(manager.Object);
            ServerSystemContext owner = TestSystemContext("session-A", "alice");
            service.InitLock(owner, s_elementId, "tag");

            service.Dispose();

            Assert.That(service.IsLockOwner(owner, s_elementId), Is.False);
            manager.VerifyRemove(value => value.SessionClosing -= It.IsAny<SessionEventHandler>(), Times.Once);
        }
        /// <summary>
        /// Minimal mutable <see cref="TimeProvider"/> for tests — only
        /// overrides <c>GetUtcNow</c>. Avoids dependency on
        /// Microsoft.Extensions.TimeProvider.Testing.
        /// </summary>
        private sealed class FakeTimeProvider : TimeProvider
        {
            private DateTimeOffset m_now;

            public FakeTimeProvider(DateTimeOffset start)
            {
                m_now = start;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return m_now;
            }

            public void Advance(TimeSpan delta)
            {
                m_now += delta;
            }
        }
    }
}
