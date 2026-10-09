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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Many requests in flight on each session complete without stalling the channels.
    /// </summary>
    [TestFixture]
    [Category("SessionServices")]
    [NonParallelizable]
    public class PipelinedRequestTests : TestFixture
    {
        /// <inheritdoc/>
        protected override void ConfigureServer(ApplicationConfiguration configuration)
        {
            base.ConfigureServer(configuration);
            // admit the whole pipeline: 10 sessions x 90 requests
            configuration.ServerConfiguration!.MaxQueuedRequestCount = 2000;
        }

        /// <summary>
        /// The server used to send each response through a synchronous wait on the channel
        /// gate from a thread-pool worker. With many responses ready at once, the workers
        /// parked on the gate while the receive loop that was handed the gate waited for a
        /// free pool thread, and the channel stalled until the pool grew. 90 Reads stay below
        /// the channel's limit of 100 queued writes.
        /// </summary>
        [Test]
        public async Task ManyConcurrentReadsOnEachSessionCompleteAsync()
        {
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sessions = new List<ISession> { Session };
            try
            {
                for (int ii = 1; ii < 10; ii++)
                {
                    sessions.Add(await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.None).ConfigureAwait(false));
                }
                var elapsed = Stopwatch.StartNew();

                for (int round = 0; round < 20; round++)
                {
                    IEnumerable<Task<ReadResponse>> reads = sessions.SelectMany(session => Enumerable.Range(0, 90).Select(
                        _ => session.ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, deadline.Token).AsTask()));
                    ReadResponse[] responses = await Task.WhenAll(reads).ConfigureAwait(false);
                    Assert.That(responses.All(r => StatusCode.IsGood(r.Results[0].StatusCode)), Is.True);
                }

                // 18000 small Reads take under a second when the channels keep flowing; the
                // stalled channel took about 10 s while the pool injected threads.
                Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(8)));
            }
            finally
            {
                foreach (ISession session in sessions.Skip(1))
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
        }
    }
}
