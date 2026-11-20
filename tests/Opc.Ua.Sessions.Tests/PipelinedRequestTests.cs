// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            // admit the whole pipeline: up to 4 sessions x 1000 requests
            configuration.ServerConfiguration!.MaxQueuedRequestCount = 10000;
        }

        /// <summary>
        /// More requests in flight on a session than the channel's write queue holds (100)
        /// complete and leave the channel connected. When responses back up, the server
        /// stops reading until they drain instead of faulting the channel with
        /// BadTcpNotEnoughResources ("The channel write queue is full"). In process the
        /// client reads fast enough that the queue rarely fills; the out-of-process load
        /// harness (perf/ServerLoadHarness, 10 clients x 1000 Reads) exercises the full queue.
        /// </summary>
        [Test]
        public async Task PipelinesDeeperThanTheWriteQueueAreThrottledNotFaultedAsync()
        {
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sessions = new List<ISession> { Session };
            try
            {
                for (int ii = 1; ii < 4; ii++)
                {
                    sessions.Add(await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.None).ConfigureAwait(false));
                }

                for (int round = 0; round < 5; round++)
                {
                    IEnumerable<Task<ReadResponse>> reads = sessions.SelectMany(session => Enumerable.Range(0, 1000).Select(
                        _ => session.ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, deadline.Token).AsTask()));
                    ReadResponse[] responses = await Task.WhenAll(reads).ConfigureAwait(false);
                    Assert.That(responses.All(r => StatusCode.IsGood(r.Results[0].StatusCode)), Is.True);
                }

                Assert.That(sessions.All(session => session.Connected), Is.True);
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
