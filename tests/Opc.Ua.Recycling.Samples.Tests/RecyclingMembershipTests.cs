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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Controlled revision conflicts exercise the real bounded membership loop without network timing or sleeps.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingMembershipTests
    {
        /// <summary>
        /// Creates a borrowed authenticated connection and a deterministic committed-state peer.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_clock = new TestClock();
            m_connectionId = "original-session";
            m_connected = true;
            m_conflictsRemaining = 0;
            m_commands.Clear();
            m_telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static logging => logging.ClearProviders());
            var configuration = new RecyclingConfiguration { PlannedSourceBatches = 0 };
            m_snapshot = await CoordinatorTests.CreateInitialSnapshotAsync(configuration, m_clock, m_telemetry)
                .ConfigureAwait(false);
            m_snapshot = m_snapshot with
            {
                Lifecycle = RunLifecycle.Running,
                Revision = 1,
                SourceTimestampUtc = m_clock.GetUtcNow().UtcDateTime,
                Participants =
                [
                    new ProcessParticipant(
                        RecyclingProcess.Collection, "view-identity", m_connectionId, true,
                        m_clock.GetUtcNow().UtcDateTime.AddSeconds(30), 0)
                ]
            };
            m_client = new Mock<IRecyclingMembershipClient>(MockBehavior.Strict);
            m_client.SetupGet(static client => client.ConnectionId).Returns(() => m_connectionId);
            m_client.SetupGet(static client => client.IsConnected).Returns(() => m_connected);
            m_client.Setup(client => client.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<CommittedSnapshot>(m_snapshot));
            m_client.Setup(client => client.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                .Returns((RecyclingCommand command, CancellationToken _) => ExecutePeer(command));
            var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
            adapter.Setup(view => view.ApplyAsync(It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            adapter.Setup(view => view.MarkUnavailableAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            m_services = new ServiceCollection().BuildServiceProvider();
            m_runtime = new RecyclingRuntime(
                new RecyclingHostOptions
                {
                    Role = RecyclingHostRole.ProcessView,
                    LineId = "membership-line",
                    Simulation = configuration
                },
                new RecyclingAuthorization([]), adapter.Object, Mock.Of<IRecyclingUserCommandForwarder>(),
                m_services, m_clock, m_telemetry);
        }

        /// <summary>
        /// Releases only fixture-owned resources; membership never owns the borrowed client.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            await m_runtime.DisposeAsync().ConfigureAwait(false);
            m_services.Dispose();
            m_telemetry.Dispose();
        }

        /// <summary>
        /// Seventy-five interfering material commits exhaust multiple bounded rounds without replacing the session.
        /// </summary>
        [Test]
        public async Task SeventyFiveRevisionConflictsKeepTheHealthySessionAndEventuallyRenewMembership()
        {
            m_conflictsRemaining = 75;
            string connection = m_client.Object.ConnectionId;
            for (int round = 0; round < 4; round++)
            {
                RecyclingMembershipState state = await m_runtime.RefreshMembershipAsync(m_client.Object)
                    .ConfigureAwait(false);
                Assert.That(state, Is.EqualTo(RecyclingMembershipState.RevisionContention));
                Assert.That(m_runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(m_commands, Has.Count.EqualTo((round + 1) * 16));
                Assert.That(m_client.Object.ConnectionId, Is.EqualTo(connection));
                Assert.That(m_runtime.IsAvailable, Is.False);
                m_clock.Advance(TimeSpan.FromSeconds(2));
            }
            RecyclingMembershipState ready = await m_runtime.RefreshMembershipAsync(m_client.Object)
                .ConfigureAwait(false);
            Assert.That(ready, Is.EqualTo(RecyclingMembershipState.Ready));
            Assert.That(m_runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(m_commands, Has.Count.EqualTo(76));
            Assert.That(m_commands.All(static command => command.Kind == RecyclingCommandKind.Heartbeat), Is.True);
            Assert.That(m_commands.Select(static command => command.RequestId).Distinct().Count(), Is.EqualTo(76));
            Assert.That(m_snapshot.Tick, Is.EqualTo(75));
            Assert.That(m_snapshot.SourceBatches, Is.Zero);
            Assert.That(m_snapshot.Participants[0].ConnectionId, Is.EqualTo(connection));
            m_client.Verify(static client => client.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Authorization failures remain exact failures, not contention or an implicit takeover.
        /// </summary>
        [Test]
        public async Task UserAccessDeniedIsNotRetriedOrMaskedAsRevisionContention()
        {
            m_client.Setup(client => client.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                m_runtime.RefreshMembershipAsync(m_client.Object).AsTask()))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.Rejected));
            Assert.That(m_runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            m_client.Verify(client => client.ExecuteAsync(
                It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Once);
            m_client.Verify(static client => client.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Genuine loss propagates to the connection owner.
        /// Membership does not continue commands on a broken transport.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActualTransportLossPropagatesInsteadOfBecomingRevisionContention(bool failsDuringRead)
        {
            m_connected = failsDuringRead;
            if (failsDuringRead)
            {
                m_client.Setup(client => client.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadNoCommunication));
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                m_runtime.RefreshMembershipAsync(m_client.Object).AsTask()))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(m_runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.TransportUnavailable));
            m_client.Verify(client => client.ExecuteAsync(
                It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
            m_client.Verify(client => client.ReadSnapshotAsync(It.IsAny<CancellationToken>()),
                failsDuringRead ? Times.Once() : Times.Never());
            m_client.Verify(static client => client.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Request timeouts must enter transport recovery instead of permanently faulting a live process view.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RequestTimeoutRemainsRecoverableDuringReadOrMembershipCommand(bool failsDuringRead)
        {
            if (failsDuringRead)
            {
                m_client.Setup(client => client.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadRequestTimeout));
            }
            else
            {
                m_client.Setup(client => client.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadRequestTimeout));
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                m_runtime.RefreshMembershipAsync(m_client.Object).AsTask()))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
            Assert.That(m_runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.TransportUnavailable));
            Assert.That(m_runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
            m_client.Verify(static client => client.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// A replacement transport waits for its former owned lease, then registers and initializes without takeover.
        /// </summary>
        [Test]
        public async Task ReplacementSessionWaitsForItsPreviousLeaseThenReinitializesSafely()
        {
            await m_runtime.RefreshMembershipAsync(m_client.Object).ConfigureAwait(false);
            Assert.That(m_commands, Has.Count.EqualTo(1));
            m_connectionId = "replacement-session";
            RecyclingMembershipState waiting = await m_runtime.RefreshMembershipAsync(m_client.Object)
                .ConfigureAwait(false);
            Assert.That(waiting, Is.EqualTo(RecyclingMembershipState.WaitingForPreviousLease));
            Assert.That(m_commands, Has.Count.EqualTo(1), "A live lease must not receive a takeover registration.");
            m_clock.Advance(TimeSpan.FromSeconds(31));
            RecyclingMembershipState ready = await m_runtime.RefreshMembershipAsync(m_client.Object)
                .ConfigureAwait(false);
            Assert.That(ready, Is.EqualTo(RecyclingMembershipState.Ready));
            Assert.That(m_commands.Select(static command => command.Kind),
                Is.EqualTo(new[]
                {
                    RecyclingCommandKind.Heartbeat,
                    RecyclingCommandKind.Register,
                    RecyclingCommandKind.Initialize
                }));
            Assert.That(m_snapshot.Participants[0].ConnectionId, Is.EqualTo("replacement-session"));
            Assert.That(m_snapshot.Participants[0].Initialized, Is.True);
            Assert.That(m_snapshot.Tick, Is.Zero);
            m_client.Verify(static client => client.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// An unrelated live owner still reaches the coordinator's unchanged authorization guard.
        /// </summary>
        [Test]
        public async Task UnrelatedLiveLeaseRemainsAnExplicitAuthorizationFailure()
        {
            m_snapshot = m_snapshot with
            {
                Participants =
                [
                    m_snapshot.Participants[0] with
                    {
                        Identity = "other-identity",
                        ConnectionId = "other-session"
                    }
                ]
            };
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                m_runtime.RefreshMembershipAsync(m_client.Object).AsTask()))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_commands, Has.Count.EqualTo(1));
            Assert.That(m_runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.Rejected));
            Assert.That(m_snapshot.Participants[0].ConnectionId, Is.EqualTo("other-session"));
        }

        /// <summary>
        /// BadInvalidState without a changed revision/run or expired own lease is not a transient revision conflict.
        /// </summary>
        [Test]
        public async Task NonRevisionInvalidStateIsNotRetriedAsContention()
        {
            m_client.Setup(client => client.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadInvalidState));
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(() =>
                m_runtime.RefreshMembershipAsync(m_client.Object).AsTask()))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.Faulted));
            m_client.Verify(client => client.ExecuteAsync(
                It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private ValueTask<RecyclingCommandOutcome> ExecutePeer(RecyclingCommand command)
        {
            m_commands.Add(command);
            if (m_conflictsRemaining > 0)
            {
                m_conflictsRemaining--;
                m_snapshot = m_snapshot with
                {
                    Revision = m_snapshot.Revision + 1,
                    Tick = m_snapshot.Tick + 1,
                    LogicalSeconds = m_snapshot.LogicalSeconds + 30
                };
                throw new ServiceResultException(StatusCodes.BadInvalidState, "Controlled intervening revision.");
            }
            if (command.ExpectedRevision != m_snapshot.Revision || command.RunId != m_snapshot.RunId)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            ProcessParticipant participant = m_snapshot.Participants[0];
            DateTime now = m_clock.GetUtcNow().UtcDateTime;
            if (participant.Identity != "view-identity" ||
                (participant.ConnectionId != m_connectionId && participant.LeaseExpiresUtc > now))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            participant = participant with
            {
                ConnectionId = m_connectionId,
                Initialized = command.Kind != RecyclingCommandKind.Register,
                LeaseExpiresUtc = now.AddSeconds(30),
                ObservedRevision = m_snapshot.Revision
            };
            m_snapshot = m_snapshot with
            {
                Revision = m_snapshot.Revision + 1,
                Participants = [participant]
            };
            return new ValueTask<RecyclingCommandOutcome>(new RecyclingCommandOutcome(
                command.RequestId, m_snapshot.RunId, m_snapshot.Revision,
                m_snapshot.Tick, StatusCodes.Good.Code, "Applied"));
        }

        private readonly List<RecyclingCommand> m_commands = [];
        private TestClock m_clock = null!;
        private DefaultTelemetry m_telemetry = null!;
        private ServiceProvider m_services = null!;
        private RecyclingRuntime m_runtime = null!;
        private Mock<IRecyclingMembershipClient> m_client = null!;
        private CommittedSnapshot m_snapshot = null!;
        private string m_connectionId = string.Empty;
        private bool m_connected;
        private int m_conflictsRemaining;
    }
}
