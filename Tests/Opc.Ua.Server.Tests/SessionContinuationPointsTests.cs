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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for browse continuation point retention on a <see cref="Session"/>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    public class SessionContinuationPointsTests
    {
        /// <summary>
        /// Regression test for the off-by-one browse continuation point limit.
        /// The oldest continuation point must be evicted as soon as the session
        /// reaches (not exceeds) the configured MaxBrowseContinuationPoints limit,
        /// so that at most the configured number of points remain active.
        /// </summary>
        [Test]
        public async Task SaveContinuationPointEvictsOldestWhenCountReachesConfiguredLimit()
        {
            var fixture = new ServerFixture<StandardServer>();
            await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                StandardServer server = fixture.Server;
                int maxBrowse = fixture.Config.ServerConfiguration.MaxBrowseContinuationPoints;

                (RequestHeader requestHeader, SecureChannelContext _) =
                    await ServerFixtureUtils.CreateAndActivateSessionAsync(
                        server,
                        "ContinuationPointLimitTest").ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken);
                Assert.That(session, Is.Not.Null, "Session should exist after Create/Activate.");

                // save one more than the configured limit.
                var points = new List<ContinuationPoint>();
                for (int ii = 0; ii <= maxBrowse; ii++)
                {
                    var cp = new ContinuationPoint { Id = Guid.NewGuid() };
                    points.Add(cp);
                    session.SaveContinuationPoint(cp);
                }

                // the oldest continuation point must have been evicted at capacity.
                Assert.That(
                    session.RestoreContinuationPoint(points[0].Id.ToByteArray()),
                    Is.Null,
                    "The oldest continuation point should be evicted once the limit is reached.");

                // exactly the configured number of points remain retained.
                for (int ii = 1; ii <= maxBrowse; ii++)
                {
                    ContinuationPoint restored =
                        session.RestoreContinuationPoint(points[ii].Id.ToByteArray());
                    Assert.That(restored, Is.SameAs(points[ii]));
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// OPC 10000-4 7.9: points of prior requests are freed when needed, but a
        /// request that already holds MaxHistoryContinuationPoints points gets no
        /// more (the caller reports Bad_NoContinuationPoints). The legacy
        /// SaveHistoryContinuationPoint keeps evicting the oldest point.
        /// </summary>
        [Test]
        public async Task TrySaveHistoryContinuationPointFreesPriorRequestsButRefusesWithinRequestAsync()
        {
            var fixture = new ServerFixture<StandardServer>();
            await fixture.StartAsync().ConfigureAwait(false);

            try
            {
                StandardServer server = fixture.Server;
                int max = fixture.Config.ServerConfiguration.MaxHistoryContinuationPoints;

                (RequestHeader requestHeader, SecureChannelContext _) =
                    await ServerFixtureUtils.CreateAndActivateSessionAsync(
                        server,
                        "HistoryContinuationPointLimitTest").ConfigureAwait(false);

                var session = server.CurrentInstance.SessionManager
                    .GetSession(requestHeader.AuthenticationToken) as Session;
                Assert.That(session, Is.Not.Null);

                // request 1 fills the session.
                var request1 = new List<Guid>();
                for (int ii = 0; ii < max; ii++)
                {
                    var id = Guid.NewGuid();
                    request1.Add(id);
                    Assert.That(session.TrySaveHistoryContinuationPoint(id, new object(), 1), Is.True);
                }

                // one more point for request 1 is refused and nothing is evicted.
                var refused = Guid.NewGuid();
                Assert.That(session.TrySaveHistoryContinuationPoint(refused, new object(), 1), Is.False);
                Assert.That(session.RestoreHistoryContinuationPoint(refused.ToByteArray()), Is.Null);
                object first = session.RestoreHistoryContinuationPoint(request1[0].ToByteArray());
                Assert.That(first, Is.Not.Null, "The oldest point of the request must not be evicted.");
                Assert.That(
                    session.TrySaveHistoryContinuationPoint(request1[0], first, 1),
                    Is.True,
                    "Restoring freed a slot that the same request can use again.");

                // request 2 frees the points of the prior request 1.
                var request2 = new List<Guid>();
                for (int ii = 0; ii < max; ii++)
                {
                    var id = Guid.NewGuid();
                    request2.Add(id);
                    Assert.That(session.TrySaveHistoryContinuationPoint(id, new object(), 2), Is.True);
                }

                Assert.That(
                    request1.All(id => session.RestoreHistoryContinuationPoint(id.ToByteArray()) == null),
                    Is.True,
                    "The points of the prior request must have been freed.");

                // the legacy method keeps evicting the oldest point.
                var legacy = Guid.NewGuid();
                session.SaveHistoryContinuationPoint(legacy, new object());
                Assert.That(session.RestoreHistoryContinuationPoint(request2[0].ToByteArray()), Is.Null);
                Assert.That(session.RestoreHistoryContinuationPoint(legacy.ToByteArray()), Is.Not.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
