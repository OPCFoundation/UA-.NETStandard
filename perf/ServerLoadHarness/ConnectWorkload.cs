/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Closed-loop connection establishment from <c>--workers</c> concurrent workers.
    /// <c>connect</c>: open a secure channel, CreateSession, ActivateSession (anonymous, or
    /// the <c>--identity</c> token), CloseSession and close the channel; one operation per cycle.
    /// <c>channel</c>: open a secure channel (OpenSecureChannel handshake) and close it;
    /// one operation per channel.
    /// </summary>
    internal sealed class ConnectWorkload : Workload
    {
        public ConnectWorkload(
            string kind,
            ClientFixture client,
            ConfiguredEndpoint endpoint,
            int workers,
            IUserIdentity? identity = null)
        {
            m_kind = kind;
            m_identity = identity;
            m_client = client;
            m_endpoint = endpoint;
            m_workers = workers;
        }

        public override double Operations => Interlocked.Read(ref m_operations);

        public override string OperationName => m_kind == "channel" ? "channel" : "session";

        public override async Task SetupAsync()
        {
            // one cycle up front, so a broken configuration fails before the warm-up;
            // a rejected identity (--wrong-credentials) is measured as errors instead
            try
            {
                await CycleAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (
                m_identity != null &&
                (ex.StatusCode == StatusCodes.BadUserAccessDenied ||
                 ex.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                 ex.StatusCode == StatusCodes.BadIdentityTokenInvalid))
            {
                Console.WriteLine("identity rejected: " + ex.Message);
            }
            Console.WriteLine(FormattableString.Invariant($"{m_kind}: {m_workers} workers"));
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            var tasks = new List<Task>();
            for (int i = 0; i < m_workers; i++)
            {
                tasks.Add(Task.Run(() => LoopAsync(ct), CancellationToken.None));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public override void ResetStats()
        {
            Interlocked.Increment(ref m_epoch);
            m_latency.Clear();
            Interlocked.Exchange(ref m_operations, 0);
            Interlocked.Exchange(ref m_errors, 0);
        }

        public override string Report(double seconds)
        {
            long operations = Interlocked.Read(ref m_operations);
            return FormattableString.Invariant($"{OperationName}s/s={operations / seconds:F1} ") +
                FormattableString.Invariant($"errors={Interlocked.Read(ref m_errors)} latency {m_latency.Percentiles()}");
        }

        private async Task CycleAsync(CancellationToken ct)
        {
            if (m_kind == "channel")
            {
                ITransportChannel channel = await m_client.SessionFactory
                    .CreateChannelAsync(m_client.Config, null!, m_endpoint, false, false, ct)
                    .ConfigureAwait(false);
                try
                {
                    await channel.CloseAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    channel.Dispose();
                }
                return;
            }

            ISession session = await m_client.SessionFactory
                .CreateAsync(
                    m_client.Config,
                    m_endpoint,
                    false,
                    false,
                    "PerfConnect",
                    60000,
                    m_identity,
                    default,
                    ct)
                .ConfigureAwait(false);
            try
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                session.Dispose();
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                int epoch = Volatile.Read(ref m_epoch);
                try
                {
                    await CycleAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ServiceResultException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (ServiceResultException ex)
                {
                    if (Interlocked.Increment(ref m_errors) < 5)
                    {
                        Console.WriteLine("error: " + ex.Message);
                    }
                    continue;
                }
                if (epoch == Volatile.Read(ref m_epoch))
                {
                    m_latency.Record(System.Diagnostics.Stopwatch.GetTimestamp() - start);
                    Interlocked.Increment(ref m_operations);
                }
            }
        }

        private readonly string m_kind;
        private readonly IUserIdentity? m_identity;
        private readonly ClientFixture m_client;
        private readonly ConfiguredEndpoint m_endpoint;
        private readonly int m_workers;
        private readonly LatencyRecorder m_latency = new();
        private int m_epoch;
        private long m_operations;
        private long m_errors;
    }
}
