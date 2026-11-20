/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies browse continuation ownership when saving points races session closure or mirrored persistence.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class BrowseSaveLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that a closed session rejects a browse save without persisting or disposing the caller's point.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClosedSessionRejectsBrowseSaveWithoutTakingCallerOwnership(bool mirrored)
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(
                () => s_sessionId, 2, 2, mirrored ? store.Object : null);
            var payload = new Mock<IDisposable>();
            using var point = new ContinuationPoint { Id = Guid.NewGuid(), Data = payload.Object };
            holder.Clear();
            ServiceResultException error = Assert.Throws<ServiceResultException>(() => holder.SaveBrowse(point))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            store.Verify(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()), Times.Never);
            holder.Clear();
            Assert.That(holder.RestoreBrowse(ByteString.From(point.Id.ToByteArray())), Is.Null);
            payload.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// Verifies that closure during persistence removes a late mirror entry and leaves ownership with the caller.
        /// </summary>
        [Test]
        public async Task CloseDuringBrowsePersistenceCannotResurrectTheMirrorAsync()
        {
            var stored = new ConcurrentDictionary<Guid, ContinuationPointEnvelope>();
            var store = new Mock<IContinuationPointStore>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            store.Setup(value => value.StoreContinuationPoint(It.IsAny<ContinuationPointEnvelope>()))
                .Callback<ContinuationPointEnvelope>(envelope =>
                {
                    entered.TrySetResult(true);
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The synchronous persistence barrier was not released.");
                    }
                    stored[envelope.Id] = envelope;
                });
            store.Setup(value => value.RemoveContinuationPoint(
                    s_sessionId, ContinuationPointKind.Browse, It.IsAny<Guid>()))
                .Callback<NodeId, ContinuationPointKind, Guid>((_, _, id) => stored.TryRemove(id, out _));
            var holder = new SessionContinuationPoints(() => s_sessionId, 2, 2, store.Object);
            var payload = new Mock<IDisposable>();
            using var point = new ContinuationPoint { Id = Guid.NewGuid(), Data = payload.Object };
            Task saving = Task.Run(() => holder.SaveBrowse(point));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await Task.Run(holder.Clear).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                release.Set();
            }
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await saving.ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(stored, Is.Empty);
            Assert.That(holder.RestoreBrowse(ByteString.From(point.Id.ToByteArray())), Is.Null);
            holder.Clear();
            payload.Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>
        /// Verifies that an accepted save transfers ownership so repeated clearing disposes and removes the point once.
        /// </summary>
        [Test]
        public void AcceptedBrowseSaveTransfersOwnershipAndClearDisposesExactlyOnce()
        {
            var store = new Mock<IContinuationPointStore>();
            var holder = new SessionContinuationPoints(() => s_sessionId, 2, 2, store.Object);
            var payload = new Mock<IDisposable>();
            using var point = new ContinuationPoint { Id = Guid.NewGuid(), Data = payload.Object };
            holder.SaveBrowse(point);
            holder.Clear();
            holder.Clear();
            payload.Verify(value => value.Dispose(), Times.Once);
            store.Verify(value => value.RemoveContinuationPoint(
                s_sessionId, ContinuationPointKind.Browse, point.Id), Times.Once);
            point.Data = null;
        }

        /// <summary>
        /// Identifies the session owning the local and mirrored browse continuation entries.
        /// </summary>
        private static readonly NodeId s_sessionId = new(100, 1);
    }
}
