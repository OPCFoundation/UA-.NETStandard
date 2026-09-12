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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    public sealed class BrowseSaveLifetimeRegressionTests
    {
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
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await saving.ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(stored, Is.Empty);
            Assert.That(holder.RestoreBrowse(ByteString.From(point.Id.ToByteArray())), Is.Null);
            holder.Clear();
            payload.Verify(value => value.Dispose(), Times.Never);
        }

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

        private static readonly NodeId s_sessionId = new(100, 1);
    }
}
