// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// One owner (here an anonymous SecurityPolicy None Session) that pipelines slow requests
    /// must not take every request worker and queue slot of the server under the default
    /// (Balanced) resource isolation, and its work must stop once it drops its channel
    /// without closing its Session.
    /// </summary>
    [TestFixture]
    [Category("SessionServices")]
    [NonParallelizable]
    public class SingleOwnerRequestFloodTests : TestFixture
    {
        private const int kAttackerRequests = 300;
        private const int kVictimPeriodMs = 200;
        private const int kVictimReadTimeoutMs = 5000;
        private const int kMaxVictimLatencyMs = 2000;

        private TaskCompletionSource<bool> m_gate = NewGate();
        private int m_entered;
        private int m_left;
        private int m_cancelled;

        /// <summary>
        /// The flooding owner fills its share of the workers (and its share of the queue,
        /// then gets BadServerTooBusy), while another owner is still served promptly. Once the
        /// flooding client drops its channel without CloseSession, the server cancels its
        /// running requests and never starts the queued ones.
        /// </summary>
        [Test]
        public async Task OneOwnerFloodingSlowCallsDoesNotStarveOtherOwnersAsync()
        {
            ServerConfiguration serverConfiguration = ServerFixture.Config.ServerConfiguration!;
            TestContext.Out.WriteLine(
                "isolation={0} MaxQueuedRequestCount={1} MaxRequestThreadCount={2}",
                ReferenceServer.ResourceIsolationOptions.Mode,
                serverConfiguration.MaxQueuedRequestCount,
                serverConfiguration.MaxRequestThreadCount);

            HookSlowMethod();
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));

            // the flooding owner: anonymous, SecurityPolicy None => keyed by its peer address
            ISessionFactory savedFactory = ClientFixture.SessionFactory;
            ISession attacker;
            try
            {
                ClientFixture.SessionFactory = new ZeroTimeoutHintSessionFactory(Telemetry);
                attacker = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            }
            finally
            {
                ClientFixture.SessionFactory = savedFactory;
            }
            // another owner: anonymous over a secure channel => keyed by its application certificate
            ISession victim = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);

            var attackerTasks = new List<Task<StatusCode>>(kAttackerRequests);
            try
            {
                // TimeoutHint 0: the slow calls run until the gate opens or they are cancelled
                ((ZeroTimeoutHintSession)attacker).ZeroTimeoutHint = true;
                for (int ii = 0; ii < kAttackerRequests; ii++)
                {
#pragma warning disable CA2025 // deliberate: the flood is not awaited and outlives the channel
                    attackerTasks.Add(SlowCallAsync(attacker, objectId, methodId));
#pragma warning restore CA2025
                }
                await WaitForStableAsync(() => Volatile.Read(ref m_entered), 5000).ConfigureAwait(false);
                int executing = Volatile.Read(ref m_entered) - Volatile.Read(ref m_left);
                TestContext.Out.WriteLine(
                    "flood: executing={0}, attacker answered={1} [{2}]",
                    executing,
                    attackerTasks.Count(t => t.IsCompleted),
                    Summarize(attackerTasks.Where(t => t.IsCompleted).Select(t => t.Result)));

                List<Sample> underAttack = await ProbeAsync(victim, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Report("under attack", underAttack);
                Assert.That(executing, Is.GreaterThan(0), "The slow calls did not start.");
                AssertServed("while another owner floods the server", underAttack);

                // The flooding client goes away without CloseSession. The server keeps a channel
                // that lost its socket for a reconnect, and cancels its requests once the
                // reconnect grace period (10 s) passed without one.
                int enteredBeforeClose = Volatile.Read(ref m_entered);
                attacker.TransportChannel.Dispose();
                var watch = Stopwatch.StartNew();
                while (Volatile.Read(ref m_entered) != Volatile.Read(ref m_left) &&
                    watch.ElapsedMilliseconds < 30000)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
                await Task.Delay(500).ConfigureAwait(false);
                TestContext.Out.WriteLine(
                    "after the channel closed ({0} ms): entered={1} left={2} cancelled={3}",
                    watch.ElapsedMilliseconds,
                    Volatile.Read(ref m_entered),
                    Volatile.Read(ref m_left),
                    Volatile.Read(ref m_cancelled));
                Assert.That(
                    Volatile.Read(ref m_entered) - Volatile.Read(ref m_left),
                    Is.Zero,
                    "Calls of the dropped channel are still running.");
                Assert.That(
                    Volatile.Read(ref m_cancelled),
                    Is.EqualTo(Volatile.Read(ref m_entered)),
                    "The running calls of the dropped channel were not cancelled.");
                Assert.That(
                    Volatile.Read(ref m_entered),
                    Is.EqualTo(enteredBeforeClose),
                    "Queued calls of the dropped channel were started after it closed.");

                List<Sample> afterClose = await ProbeAsync(victim, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                Report("after close", afterClose);
                AssertServed("after the flooding owner dropped its channel", afterClose);
            }
            finally
            {
                m_gate.TrySetResult(true);
                await WaitForStableAsync(() => Volatile.Read(ref m_left), 5000).ConfigureAwait(false);
                await CloseQuietlyAsync(victim).ConfigureAwait(false);
                attacker.Dispose();
            }
        }

        private static void AssertServed(string phase, List<Sample> samples)
        {
            int notGood = samples.Count(s => !s.Good);
            int slow = samples.Count(s => s.Good && s.LatencyMs > kMaxVictimLatencyMs);
            Assert.That(
                notGood + slow,
                Is.Zero,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Reads of another owner {0}: {1} of {2} not Good [{3}], {4} slower than {5} ms.",
                    phase,
                    notGood,
                    samples.Count,
                    Summarize(samples.Select(s => s.Status)),
                    slow,
                    kMaxVictimLatencyMs));
        }

        private void HookSlowMethod()
        {
            m_gate = NewGate();
            Volatile.Write(ref m_entered, 0);
            Volatile.Write(ref m_left, 0);
            Volatile.Write(ref m_cancelled, 0);
            NodeId methodId = ToNodeId(new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));
            Server.IMasterNodeManager master = ReferenceServer.CurrentInstance.NodeManager;
            MethodState? method = null;
            foreach (Server.IAsyncNodeManager manager in master.AsyncNodeManagers)
            {
                NodeState? node = manager is Server.AsyncCustomNodeManager asyncManager
                    ? asyncManager.Find(methodId)
                    : null;
                if (node is MethodState found)
                {
                    method = found;
                    break;
                }
            }
            Assert.That(method, Is.Not.Null, "Methods_Void not found in the reference server.");
            // waits without blocking a thread until the gate opens, and stops at once when the
            // server cancels the request
            method!.OnCallMethod2Async = async (context, state, objectId, inputs, outputs, ct) =>
            {
                Interlocked.Increment(ref m_entered);
                try
                {
                    await m_gate.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref m_cancelled);
                    return new ServiceResult(StatusCodes.BadRequestCancelledByClient);
                }
                finally
                {
                    Interlocked.Increment(ref m_left);
                }
            };
        }

        private static async Task<StatusCode> SlowCallAsync(ISession session, NodeId objectId, NodeId methodId)
        {
            try
            {
                CallResponse response = await session.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new() { ObjectId = objectId, MethodId = methodId, InputArguments = default }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                return response.Results[0].StatusCode;
            }
            catch (ServiceResultException sre)
            {
                return sre.StatusCode;
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
        }

        /// <summary>
        /// One Read of Server_ServerStatus_State every 200 ms for the duration.
        /// </summary>
        private static async Task<List<Sample>> ProbeAsync(ISession session, TimeSpan duration)
        {
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }
            };
            var pending = new List<Task<Sample>>();
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < duration)
            {
                double start = watch.Elapsed.TotalMilliseconds;
                pending.Add(ReadOnceAsync(session, nodesToRead));
                double wait = start + kVictimPeriodMs - watch.Elapsed.TotalMilliseconds;
                if (wait > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(wait)).ConfigureAwait(false);
                }
            }
            return [.. await Task.WhenAll(pending).ConfigureAwait(false)];
        }

        private static async Task<Sample> ReadOnceAsync(ISession session, ArrayOf<ReadValueId> nodesToRead)
        {
            var watch = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(kVictimReadTimeoutMs);
            StatusCode status;
            bool timedOut = false;
            try
            {
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither, nodesToRead, timeout.Token).ConfigureAwait(false);
                status = response.Results[0].StatusCode;
            }
            catch (ServiceResultException sre)
            {
                status = sre.StatusCode;
                timedOut = timeout.IsCancellationRequested || sre.StatusCode == StatusCodes.BadRequestTimeout;
            }
            catch (OperationCanceledException)
            {
                status = StatusCodes.BadRequestTimeout;
                timedOut = true;
            }
            return new Sample(watch.Elapsed.TotalMilliseconds, status, timedOut);
        }

        private static void Report(string phase, List<Sample> samples)
        {
            double[] latency = [.. samples.Select(s => s.LatencyMs).OrderBy(v => v)];
            TestContext.Out.WriteLine(
                "other owner {0}: reads={1} Good={2} [{3}] max latency={4:F1} ms",
                phase,
                samples.Count,
                samples.Count(s => s.Good),
                Summarize(samples.Select(s => s.Status)),
                latency.Length > 0 ? latency[^1] : 0);
        }

        private static string Summarize(IEnumerable<StatusCode> codes)
        {
            return string.Join(
                ", ",
                codes.GroupBy(c => c.ToString()).OrderByDescending(g => g.Count()).Select(g => g.Key + " x" + g.Count()));
        }

        private static async Task WaitForStableAsync(Func<int> read, int maxMs)
        {
            var watch = Stopwatch.StartNew();
            int last = read();
            int stableSince = 0;
            while (watch.ElapsedMilliseconds < maxMs)
            {
                await Task.Delay(100).ConfigureAwait(false);
                int now = read();
                if (now == last)
                {
                    stableSince += 100;
                    if (stableSince >= 500)
                    {
                        return;
                    }
                }
                else
                {
                    stableSince = 0;
                    last = now;
                }
            }
        }

        private static async Task CloseQuietlyAsync(ISession session)
        {
            try
            {
                using var cts = new CancellationTokenSource(10000);
                await session.CloseAsync(5000, true, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine("close failed: " + ex.Message);
            }
            session.Dispose();
        }

        private static TaskCompletionSource<bool> NewGate()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly record struct Sample(double LatencyMs, StatusCode Status, bool TimedOut)
        {
            public bool Good => !TimedOut && StatusCode.IsGood(Status);
        }

        /// <summary>
        /// A session that can send TimeoutHint 0 (the client otherwise replaces 0 with its
        /// operation timeout).
        /// </summary>
        private sealed class ZeroTimeoutHintSession : Session
        {
            public ZeroTimeoutHintSession(
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                Certificate? clientCertificate,
                CertificateCollection? clientCertificateChain,
                ArrayOf<EndpointDescription> availableEndpoints,
                ArrayOf<string> discoveryProfileUris,
                ISubscriptionEngineFactory? engineFactory)
                : base(
                    channel,
                    configuration,
                    endpoint,
                    clientCertificate,
                    clientCertificateChain,
                    availableEndpoints,
                    discoveryProfileUris,
                    engineFactory)
            {
            }

            public bool ZeroTimeoutHint { get; set; }

            protected override void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                base.UpdateRequestHeader(request, useDefaults);
                if (ZeroTimeoutHint && request is CallRequest)
                {
                    request.RequestHeader.TimeoutHint = 0;
                }
            }
        }

        private sealed class ZeroTimeoutHintSessionFactory : DefaultSessionFactory
        {
            public ZeroTimeoutHintSessionFactory(ITelemetryContext telemetry)
                : base(telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            }

            public override ISession Create(
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                Certificate? clientCertificate = null,
                CertificateCollection? clientCertificateChain = null,
                ArrayOf<EndpointDescription> availableEndpoints = default,
                ArrayOf<string> discoveryProfileUris = default)
            {
                return new ZeroTimeoutHintSession(
                    channel,
                    configuration,
                    endpoint,
                    clientCertificate,
                    clientCertificateChain,
                    availableEndpoints,
                    discoveryProfileUris,
                    SubscriptionEngineFactory);
            }
        }
    }
}
