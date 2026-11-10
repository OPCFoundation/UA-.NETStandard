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
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Pins session identity, admitted activity and the DI inactivity boundary.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Locking")]
    public sealed class DefaultLockActivityTests
    {
        /// <summary>
        /// The application tag and authenticated user cannot impersonate another session's lock.
        /// </summary>
        [Test]
        public void ActivityRejectsOtherSessionWithSameUser()
        {
            using var service = new DefaultLockService();
            SessionSystemContext owner = Context("owner");
            SessionSystemContext other = Context("other");
            Assert.That(owner.UserId, Is.EqualTo(other.UserId));
            Assert.That(service.InitLock(owner, s_element, "same-tag"), Is.Zero);
            Assert.That(service.InitLock(other, s_element, "same-tag"), Is.EqualTo(-1));
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                service.EnterActivity(other, s_element));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadLocked));
            Assert.That(service.RenewLock(other, s_element), Is.EqualTo(-2));
            Assert.That(service.ExitLock(other, s_element), Is.EqualTo(-2));
            using IDisposable? admitted = service.EnterActivity(owner, s_element);
            Assert.That(admitted, Is.Not.Null);
        }

        /// <summary>
        /// An active owner cannot expire, and only the last completion starts the full inactivity period.
        /// </summary>
        [Test]
        public void ActivityRetainsLockUntilLastCompletion()
        {
            var clock = new Clock();
            using var service = new DefaultLockService(TimeSpan.FromSeconds(30), clock);
            SessionSystemContext owner = Context("owner");
            SessionSystemContext other = Context("other");
            Assert.That(service.InitLock(owner, s_element, "owner"), Is.Zero);
            using IDisposable? first = service.EnterActivity(owner, s_element);
            using IDisposable? second = service.EnterActivity(owner, s_element);
            clock.Advance(300);
            Assert.That(service.GetState(s_element).Locked, Is.True);
            Assert.That(service.InitLock(other, s_element, "other"), Is.EqualTo(-1));
            first!.Dispose();
            first.Dispose();
            clock.Advance(300);
            Assert.That(service.GetState(s_element).Locked, Is.True);
            second!.Dispose();
            Assert.That(service.GetState(s_element).RemainingLockTimeSeconds, Is.EqualTo(30));
            clock.Advance(29);
            Assert.That(service.GetState(s_element).RemainingLockTimeSeconds, Is.EqualTo(1));
            clock.Advance(1);
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.InitLock(other, s_element, "other"), Is.Zero);
        }

        /// <summary>
        /// A lock cannot exclude a foreign mutation that was already admitted while unlocked.
        /// </summary>
        [Test]
        public void UnlockedActivityRejectsConflictingAcquisition()
        {
            using var service = new DefaultLockService();
            SessionSystemContext first = Context("first");
            SessionSystemContext second = Context("second");
            using IDisposable? activity = service.EnterActivity(first, s_element);
            Assert.That(activity, Is.Not.Null);
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.InitLock(second, s_element, "second"), Is.EqualTo(-2));
            Assert.That(service.InitLock(first, s_element, "first"), Is.Zero);
            activity!.Dispose();
            Assert.That(service.ExitLock(first, s_element), Is.Zero);
            Assert.That(service.InitLock(second, s_element, "second"), Is.Zero);
        }

        /// <summary>
        /// Closing a session waits for its admitted operation and cannot release another element's owner.
        /// </summary>
        [Test]
        public void ClosingSessionReleasesAfterActivityDrains()
        {
            using var service = new DefaultLockService();
            SessionSystemContext owner = Context("owner");
            SessionSystemContext other = Context("other");
            var independent = new NodeId("independent", 2);
            Assert.That(service.InitLock(owner, s_element, "owner"), Is.Zero);
            Assert.That(service.InitLock(other, independent, "other"), Is.Zero);
            using IDisposable? activity = service.EnterActivity(owner, s_element);
            service.ReleaseSession(s_element, new NodeId("other", 2));
            Assert.That(service.GetState(s_element).Locked, Is.True);
            service.ReleaseSession(s_element, new NodeId("owner", 2));
            Assert.That(service.GetState(s_element).Locked, Is.True);
            activity!.Dispose();
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.GetState(independent).LockingClient, Is.EqualTo("other"));
            service.ReleaseSession(independent, new NodeId("other", 2));
            Assert.That(service.GetState(independent).Locked, Is.False);
        }

        /// <summary>
        /// Element removal retains ownership until admitted work drains, then releases it permanently.
        /// </summary>
        [Test]
        public void ElementReleaseWaitsForActivityAndNeverRestoresLock()
        {
            using var service = new DefaultLockService();
            SessionSystemContext owner = Context("owner");
            SessionSystemContext other = Context("other");
            Assert.That(service.InitLock(owner, s_element, "owner"), Is.Zero);
            using IDisposable? activity = service.EnterActivity(owner, s_element);

            service.ReleaseElement(s_element);

            Assert.That(service.GetState(s_element).Locked, Is.True);
            Assert.That(service.InitLock(other, s_element, "other"), Is.EqualTo(-1));
            activity!.Dispose();
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.InitLock(other, s_element, "other"), Is.Zero);
        }

        /// <summary>
        /// Explicit release or break never recreates a lock when an older request completes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CompletingOldActivityDoesNotRestoreOwnership(bool breakLock)
        {
            using var service = new DefaultLockService();
            SessionSystemContext owner = Context("owner");
            SessionSystemContext other = Context("other");
            Assert.That(service.InitLock(owner, s_element, "owner"), Is.Zero);
            using IDisposable? activity = service.EnterActivity(owner, s_element);
            int released = breakLock
                ? service.BreakLock(AdminContext(), s_element)
                : service.ExitLock(owner, s_element);
            Assert.That(released, Is.Zero);
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.InitLock(other, s_element, "other"), Is.EqualTo(-2));
            activity!.Dispose();
            Assert.That(service.GetState(s_element).Locked, Is.False);
            Assert.That(service.InitLock(other, s_element, "other"), Is.Zero);
            activity.Dispose();
            Assert.That(service.GetState(s_element).LockingClient, Is.EqualTo("other"));
        }

        /// <summary>
        /// Foreign reads do not extend expiry; owner reads restart inactivity only after completion.
        /// </summary>
        [Test]
        public void ReadActivityOnlyRenewsOwner()
        {
            var clock = new Clock();
            using var service = new DefaultLockService(TimeSpan.FromSeconds(30), clock);
            SessionSystemContext owner = Context("owner");
            Assert.That(service.EnterActivity(owner, s_element, modifying: false), Is.Null);
            Assert.That(service.InitLock(owner, s_element, "owner"), Is.Zero);
            clock.Advance(20);
            Assert.That(service.EnterActivity(Context("other"), s_element, modifying: false), Is.Null);
            Assert.That(service.GetState(s_element).RemainingLockTimeSeconds, Is.EqualTo(10));
            using IDisposable? read = service.EnterActivity(owner, s_element, modifying: false);
            clock.Advance(50);
            Assert.That(service.GetState(s_element).Locked, Is.True);
            read!.Dispose();
            Assert.That(service.GetState(s_element).RemainingLockTimeSeconds, Is.EqualTo(30));
        }

        /// <summary>
        /// The generated Lock properties read the live service, with Duration encoded in milliseconds.
        /// </summary>
        [Test]
        public async Task BoundPropertiesReadLiveStateAndMilliseconds()
        {
            var clock = new Clock();
            using var service = new DefaultLockService(TimeSpan.FromSeconds(30), clock);
            var node = new LockingServicesState(null);
            node.Locked = PropertyState<bool>.With<VariantBuilder>(node);
            node.LockingClient = PropertyState<string>.With<VariantBuilder>(node);
            node.LockingUser = PropertyState<string>.With<VariantBuilder>(node);
            node.RemainingLockTime = PropertyState<double>.With<VariantBuilder>(node);
            node.BindToLockService(s_element, service);
            SessionSystemContext context = Context("owner");
            Assert.That(service.InitLock(context, s_element, "application"), Is.Zero);
            AttributeSimpleReadResult locked = await node.Locked.OnSimpleReadValueAsync!(
                context, node.Locked, CancellationToken.None).ConfigureAwait(false);
            Assert.That(locked.Value.TryGetValue(out bool isLocked), Is.True);
            Assert.That(isLocked, Is.True);
            clock.Advance(7);
            AttributeSimpleReadResult remaining = await node.RemainingLockTime.OnSimpleReadValueAsync!(
                context, node.RemainingLockTime, CancellationToken.None).ConfigureAwait(false);
            Assert.That(remaining.Value.TryGetValue(out double remainingMilliseconds), Is.True);
            Assert.That(remainingMilliseconds, Is.EqualTo(23000));
            clock.Advance(23);
            locked = await node.Locked.OnSimpleReadValueAsync!(
                context, node.Locked, CancellationToken.None).ConfigureAwait(false);
            Assert.That(locked.Value.TryGetValue(out isLocked), Is.True);
            Assert.That(isLocked, Is.False);
            remaining = await node.RemainingLockTime.OnSimpleReadValueAsync!(
                context, node.RemainingLockTime, CancellationToken.None).ConfigureAwait(false);
            Assert.That(remaining.Value.TryGetValue(out remainingMilliseconds), Is.True);
            Assert.That(remainingMilliseconds, Is.Zero);
        }

        /// <summary>
        /// Invalid inactivity periods cannot create immediately expired or unbounded locks.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidInactivityDurationIsRejected(int seconds)
        {
            Assert.That(() => new DefaultLockService(TimeSpan.FromSeconds(seconds)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Creates distinct sessions with the same anonymous user.
        /// </summary>
        private static SessionSystemContext Context(string session)
        {
            return new SessionSystemContext(new Mock<ITelemetryContext>().Object)
            {
                SessionId = new NodeId(session, 2),
                UserIdentity = new UserIdentity(new AnonymousIdentityToken())
            };
        }

        /// <summary>
        /// Creates an authenticated SecurityAdmin caller on an encrypted channel.
        /// </summary>
        private static SessionSystemContext AdminContext()
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds)
                .Returns([global::Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin]);
            var channel = new SecureChannelContext(
                "test-channel",
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                RequestEncoding.Binary);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            return new SessionSystemContext(operation, telemetry: null!);
        }

        /// <summary>The protected element.</summary>
        private static readonly NodeId s_element = new("device", 2);

        /// <summary>
        /// An explicitly advanced inactivity clock; no timing-sensitive sleeps are needed.
        /// </summary>
        private sealed class Clock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow() => m_now;

            /// <summary>
            /// Advances the authority clock.
            /// </summary>
            public void Advance(int seconds) => m_now += TimeSpan.FromSeconds(seconds);

            /// <summary>The current test time.</summary>
            private DateTimeOffset m_now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
