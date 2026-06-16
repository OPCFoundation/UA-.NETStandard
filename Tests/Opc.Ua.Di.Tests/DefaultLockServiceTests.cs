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
using NUnit.Framework;
using Opc.Ua.Di.Server.Locking;

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
                TestSystemContext("session-B", "bob"),
                s_elementId);

            Assert.That(status, Is.EqualTo(LockStatus.Ok));
            Assert.That(service.GetState(s_elementId).Locked, Is.False);
        }

        [Test]
        public void BreakLockReturnsNotLockedWhenUnlocked()
        {
            using var service = new DefaultLockService();
            int status = service.BreakLock(
                TestSystemContext("session-A", "alice"),
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

        /// <summary>
        /// Minimal <see cref="ISystemContext"/> implementation backed by
        /// a stock <see cref="SystemContext"/> so the lock service can
        /// distinguish callers via <see cref="ISystemContext.UserId"/>.
        /// </summary>
        private static SystemContext TestSystemContext(string sessionId, string user)
        {
            // sessionId is informational only — the default lock service
            // falls back to UserId-derived synthetic session ids when
            // ServerSystemContext is not available.
            _ = sessionId;
            return new SystemContext(telemetry: null!)
            {
                UserId = user
            };
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
