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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that a sampling group reuses a good exact-source read verdict of a sampled item
    /// (the per-sample recheck #4834 added) until one of its inputs changes, never reuses a bad
    /// one, and still runs a registered Value read authorizer on every sample.
    /// </summary>
    /// <remarks>
    /// Each test changes the state from inside the queueing of a known sample, so the next
    /// sample is the first one that sees the change. The verdict keys include process-wide
    /// versions (role permissions, namespace defaults) that concurrent tests move, so the
    /// fixture does not run in parallel with them.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    [NonParallelizable]
    public sealed class SampledReadVerdictCacheTests
    {
        /// <summary>Sampling interval in milliseconds.</summary>
        private const double kInterval = 50;

        /// <summary>
        /// The exact-source check runs for the first sample only; later samples reuse its verdict.
        /// </summary>
        [Test]
        public async Task GoodVerdictIsReusedBetweenSamplesAsync()
        {
            await using var harness = new Harness();
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(8).ConfigureAwait(false);

            Assert.That(harness.ChecksIn(0), Is.GreaterThan(0));
            for (int ii = 1; ii <= 8; ii++)
            {
                Assert.That(harness.ChecksIn(ii), Is.Zero, $"sample {ii} reran the exact-source check");
                Assert.That(harness.Status(ii), Is.EqualTo(StatusCodes.Good));
            }
            TestContext.Out.WriteLine(
                $"Sample cycles 0-8: {harness.ChecksIn(0)} permission validations, " +
                "8 reused exact-source verdicts (88.89% cache-hit rate); interval 50 ms, 1 session, 1 item.");
        }

        /// <summary>
        /// Each input the verdict is keyed on retires it: the next sample runs the full check
        /// again, and the verdict it computes is reused afterwards.
        /// </summary>
        [TestCase("RolePermissionsVersion")]
        [TestCase("NamespaceDefaultsGeneration")]
        [TestCase("NamespaceMetadata")]
        [TestCase("IdentityGeneration")]
        [TestCase("Identity")]
        [TestCase("SecureChannelId")]
        [TestCase("Handle")]
        [TestCase("Endpoint")]
        [TestCase("SecurityMode")]
        [TestCase("Transport")]
        [TestCase("AccessRestrictions")]
        public async Task GoodVerdictIsRetiredByEachKeyAsync(string key)
        {
            await using var harness = new Harness();
            harness.AfterSample(3, () => harness.Change(key));
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(7).ConfigureAwait(false);

            Assert.That(harness.ChecksIn(2), Is.Zero);
            Assert.That(harness.ChecksIn(3), Is.Zero);
            Assert.That(harness.ChecksIn(4), Is.GreaterThan(0), $"{key} did not retire the verdict");
            Assert.That(harness.ChecksIn(6), Is.Zero, "the new verdict is reused");
            Assert.That(harness.ChecksIn(7), Is.Zero, "the new verdict is reused");
            for (int ii = 0; ii <= 7; ii++)
            {
                Assert.That(harness.Status(ii), Is.EqualTo(StatusCodes.Good));
            }
        }

        /// <summary>
        /// A reused grant is withdrawn by the first sample after the subscriber's channel no
        /// longer meets the node's restrictions.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task ReusedGrantFollowsChannelDowngradeAsync()
        {
            await using var harness = new Harness();
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            harness.AfterSample(3, () => harness.Endpoint.SecurityMode = MessageSecurityMode.Sign);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(5).ConfigureAwait(false);

            Assert.That(harness.Status(2), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Status(3), Is.EqualTo(StatusCodes.Good));
            Assert.That(StatusCode.IsBad(harness.Status(4)!.Value), Is.True);
            Assert.That(StatusCode.IsBad(harness.Status(5)!.Value), Is.True);
        }

        /// <summary>
        /// A revoked role permission withdraws a cached grant on the next sample.
        /// </summary>
        [Test]
        public async Task ReusedGrantDoesNotSurviveRoleRevocationAsync()
        {
            await using var harness = new Harness();
            harness.AfterSample(3, () => harness.Node.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)PermissionType.Read
                }
            ]);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(5).ConfigureAwait(false);

            Assert.That(harness.Status(3), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Status(4), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Status(5), Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A bad verdict is never reused: every denied sample runs the full check, so the first
        /// sample after the channel is upgraded again is delivered.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task BadVerdictIsNotCachedAsync()
        {
            await using var harness = new Harness();
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            harness.Endpoint.SecurityMode = MessageSecurityMode.Sign;
            harness.AfterSample(4, () => harness.Endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(6).ConfigureAwait(false);

            for (int ii = 0; ii <= 4; ii++)
            {
                Assert.That(harness.Status(ii), Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(harness.ChecksIn(ii), Is.GreaterThan(0), $"denied sample {ii} reused a verdict");
            }
            Assert.That(harness.Status(5), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Status(6), Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// An item whose variable has a registered Value read authorizer runs the full check and
        /// the policy on every sample, and a denial by the policy applies to the next sample.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task AuthorizerItemIsCheckedEverySampleAsync()
        {
            await using var harness = new Harness();
            int authorizations = 0;
            bool deny = false;
            using IDisposable registration = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, (_, _) =>
                {
                    Interlocked.Increment(ref authorizations);
                    return new ValueTask<ServiceResult>(Volatile.Read(ref deny)
                        ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                        : ServiceResult.Good);
                });
            harness.AfterSample(4, () => Volatile.Write(ref deny, true));
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(6).ConfigureAwait(false);

            for (int ii = 0; ii <= 6; ii++)
            {
                Assert.That(harness.ChecksIn(ii), Is.GreaterThan(0), $"sample {ii} skipped the full check");
            }
            Assert.That(Volatile.Read(ref authorizations), Is.GreaterThanOrEqualTo(7));
            Assert.That(harness.Status(4), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Status(5), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Status(6), Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A policy revoked while its callback is suspended cannot publish its former grant.
        /// </summary>
        [Test]
        public async Task SuspendedAuthorizerCannotGrantAfterRevocationAsync()
        {
            await using var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, token) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            await harness.StartAsync().ConfigureAwait(false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            registration.Dispose();
            release.TrySetResult(true);
            await harness.WaitForSampleAsync(2).ConfigureAwait(false);

            Assert.That(harness.Status(0), Is.EqualTo(StatusCodes.BadShutdown));
            Assert.That(harness.Status(1), Is.EqualTo(StatusCodes.BadShutdown));
            Assert.That(harness.Status(2), Is.EqualTo(StatusCodes.BadShutdown));
        }

        /// <summary>
        /// A custom-session role refresh during authorization invalidates the old callback result.
        /// </summary>
        [Test]
        public async Task SuspendedAuthorizationRechecksCustomIdentityGenerationAsync()
        {
            await using var harness = new Harness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int checks = 0;
            using IDisposable registration = harness.Owner.RegisterValueReadAuthorization(
                harness.Node, async (_, token) =>
                {
                    if (Interlocked.Increment(ref checks) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        return ServiceResult.Good;
                    }
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                });
            await harness.StartAsync().ConfigureAwait(false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            harness.Change("IdentityGeneration");
            release.TrySetResult(true);
            await harness.WaitForSampleAsync(1).ConfigureAwait(false);

            Assert.That(harness.Status(0), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Status(1), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(checks, Is.GreaterThanOrEqualTo(3), "the suspended grant must be rechecked");
        }

        /// <summary>
        /// Sessionless delivery reuses its own verdict without adopting a prior session grant.
        /// </summary>
        [Test]
        public async Task SessionlessSamplesReuseTheirOwnVerdictAsync()
        {
            await using var harness = new Harness(sessionless: true);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(4).ConfigureAwait(false);

            Assert.That(harness.ChecksIn(0), Is.GreaterThan(0));
            Assert.That(harness.ChecksIn(3), Is.Zero);
            Assert.That(harness.Status(4), Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A grant cannot be reused for another manager, exact node or attribute.
        /// </summary>
        [Test]
        public async Task ReplacedManagerOrSourceCannotReuseVerdictAsync()
        {
            await using var harness = new Harness();
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(2).ConfigureAwait(false);
            await harness.AssertReplacementMissesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Concurrent readers share immutable grants, and every reuse misses after a channel revocation.
        /// </summary>
        [Test]
        public async Task ConcurrentReuseCannotSurviveRevocationAsync()
        {
            await using var harness = new Harness();
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForSampleAsync(2).ConfigureAwait(false);
            await harness.AssertConcurrentRevocationAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Drives a sampling group of a real asynchronous node manager with one sampled item and
        /// records, per sample, the queued status and the number of exact-source checks so far.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates one real manager and a modeled subscriber.
            /// </summary>
            public Harness(bool sessionless = false)
            {
                m_server = DeterministicServerMock.Create(out m_queues);
                Owner = new CountingNodeManager(
                    m_server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri);
                Node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("SampledValue", Owner.NamespaceIndex),
                    BrowseName = new QualifiedName("SampledValue", Owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = 42
                };
                m_handle = new NodeHandle(Node.NodeId, Node);
                Owner.ItemHandle = m_handle;

                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
                m_identity = identity.Object;
                Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(1));
                session.SetupGet(value => value.EffectiveIdentity).Returns(() => m_identity);
                session.SetupGet(value => value.SecureChannelId).Returns(() => m_secureChannelId);
                session.SetupGet(value => value.EndpointDescription).Returns(() => Endpoint);
                session.SetupGet(value => value.PreferredLocales).Returns([]);
                session.Setup(value => value.CaptureIdentityRefreshSnapshot())
                    .Returns(() => new IdentityRefreshSnapshot(m_identity, Interlocked.Read(ref m_identityGeneration)));
                m_session = sessionless ? null : session.Object;

                m_item.SetupGet(m => m.Id).Returns(1);
                m_item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                m_item.SetupGet(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
                m_item.SetupGet(m => m.SamplingInterval).Returns(kInterval);
                m_item.SetupGet(m => m.Session).Returns(m_session!);
                m_item.SetupGet(m => m.EffectiveIdentity).Returns(() => m_identity);
                m_item.SetupGet(m => m.ManagerHandle).Returns(() => m_handle);
                m_item.SetupGet(m => m.NodeId).Returns(Node.NodeId);
                m_item.SetupGet(m => m.AttributeId).Returns(Attributes.Value);
                m_item.Setup(m => m.GetReadValueId()).Returns(() => new ReadValueId
                {
                    NodeId = Node.NodeId,
                    AttributeId = Attributes.Value
                });
                m_item.Setup(m => m.QueueValue(in It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult?>()))
                    .Callback(new QueueValueCallback(OnQueued));
            }

            /// <summary>
            /// Gets the exact sampled owner.
            /// </summary>
            public CountingNodeManager Owner { get; }

            /// <summary>
            /// Gets the sampled variable.
            /// </summary>
            public BaseDataVariableState Node { get; }

            /// <summary>
            /// The subscriber's current accepted endpoint.
            /// </summary>
            public EndpointDescription Endpoint
            {
                get => Volatile.Read(ref m_endpoint)!;
                set => Volatile.Write(ref m_endpoint, value);
            }

            /// <summary>
            /// Runs an action after the given sample was queued, before the next sample.
            /// </summary>
            public void AfterSample(int sample, Action action)
            {
                m_afterSample[sample] = action;
            }

            /// <summary>
            /// Moves one input the cached verdict is keyed on, without changing the verdict.
            /// </summary>
            public void Change(string key)
            {
                switch (key)
                {
                    case "RolePermissionsVersion":
                        _ = new BaseDataVariableState(null)
                        {
                            RolePermissions =
                            [
                                new RolePermissionType
                                {
                                    RoleId = ObjectIds.WellKnownRole_Operator,
                                    Permissions = (uint)PermissionType.Read
                                }
                            ]
                        };
                        break;
                    case "NamespaceDefaultsGeneration":
                        NamespaceDefaultPermissionReader.Invalidate();
                        break;
                    case "NamespaceMetadata":
                        DeterministicServerMock.SetNamespaceMetadata(
                            m_server, Owner.NamespaceIndex, new NamespaceMetadataState(null));
                        break;
                    case "IdentityGeneration":
                        Interlocked.Increment(ref m_identityGeneration);
                        break;
                    case "Identity":
                        m_identity = new UserIdentity();
                        break;
                    case "SecureChannelId":
                        m_secureChannelId = "replacement-channel";
                        break;
                    case "Handle":
                        m_handle = new NodeHandle(Node.NodeId, Node);
                        Owner.ItemHandle = m_handle;
                        break;
                    case "Endpoint":
                        Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                        break;
                    case "SecurityMode":
                        Endpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                    case "Transport":
                        Endpoint.TransportProfileUri = Profiles.HttpsBinaryTransport;
                        break;
                    case "AccessRestrictions":
                        Node.AccessRestrictions = AccessRestrictionType.SigningRequired;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(key), key, null);
                }
            }

            /// <summary>
            /// Publishes the variable and starts sampling after its initial value.
            /// </summary>
            public async Task StartAsync()
            {
                await Owner.AddNodeAsync(Owner.SystemContext, NodeId.Null, Node).ConfigureAwait(false);
                m_context = m_session == null
                    ? new OperationContext(m_item.Object)
                    : new OperationContext(
                        new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, m_session);
                m_group = new SamplingGroup(
                    m_server.Object,
                    Owner,
                    [new SamplingRateGroup((int)kInterval, (int)kInterval, 4)],
                    m_context,
                    kInterval,
                    savedOwnerIdentity: m_identity);
                m_checksBefore = Owner.Checks;

                // the initial value counts as queued, so every sample is a cycle of the loop.
                Assert.That(m_group.StartMonitoring(
                    m_context, m_item.Object, m_identity, initialValueQueued: true), Is.True);
                Assert.That(m_group.ApplyChanges(), Is.False);
            }

            /// <summary>
            /// Waits until the given sample was queued.
            /// </summary>
            public async Task WaitForSampleAsync(int sample)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (true)
                {
                    lock (m_recordsLock)
                    {
                        if (m_records.Count > sample)
                        {
                            return;
                        }
                    }
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// The number of exact-source (and role) checks the sample ran after its read.
            /// </summary>
            public int ChecksIn(int sample)
            {
                lock (m_recordsLock)
                {
                    int before = sample == 0 ? m_checksBefore : m_records[sample - 1].Checks;
                    return m_records[sample].Checks - before;
                }
            }

            /// <summary>
            /// Gets the status queued for a recorded sample.
            /// </summary>
            public StatusCode? Status(int sample)
            {
                lock (m_recordsLock)
                {
                    return m_records[sample].Status;
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_group?.Dispose();
                m_context?.Dispose();
                Owner.Dispose();
                await Owner.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            /// <summary>
            /// Exercises the exact manager, node and attribute keys directly.
            /// </summary>
            public async Task AssertReplacementMissesAsync()
            {
                OperationContext context = MonitoredReadAuthorization.GetSubscriberContext(m_item.Object);
                MonitoredReadInputs inputs = MonitoredReadInputs.Capture(m_session, Node);
                (_, MonitoredReadVerdict? verdict) = await MonitoredReadVerdict.ValidateAsync(
                    m_server.Object, Owner, Node, context.GetSystemContext(Owner.SystemContext),
                    Attributes.Value, inputs, CancellationToken.None).ConfigureAwait(false);
                Assert.That(verdict, Is.Not.Null);
                await using var replacement = new Harness();
                Assert.That(MonitoredReadVerdict.TryReuse(
                    verdict, m_server.Object, replacement.Owner, Node, Attributes.Value, inputs, out _), Is.False);
                Assert.That(MonitoredReadVerdict.TryReuse(
                    verdict, m_server.Object, Owner, new BaseDataVariableState(null) { NodeId = Node.NodeId },
                    Attributes.Value, inputs, out _), Is.False);
                Assert.That(MonitoredReadVerdict.TryReuse(
                    verdict, m_server.Object, Owner, Node, Attributes.RolePermissions, inputs, out _), Is.False);
            }

            /// <summary>
            /// Measures concurrent immutable reuse before and after revoking channel restrictions.
            /// </summary>
            public async Task AssertConcurrentRevocationAsync()
            {
                OperationContext context = MonitoredReadAuthorization.GetSubscriberContext(m_item.Object);
                MonitoredReadInputs inputs = MonitoredReadInputs.Capture(m_session, Node);
                (_, MonitoredReadVerdict? verdict) = await MonitoredReadVerdict.ValidateAsync(
                    m_server.Object, Owner, Node, context.GetSystemContext(Owner.SystemContext),
                    Attributes.Value, inputs, CancellationToken.None).ConfigureAwait(false);
                Assert.That(verdict, Is.Not.Null);
                int hits = 0;
                Parallel.For(0, 1024, iteration =>
                {
                    if (MonitoredReadVerdict.TryReuse(verdict, m_server.Object, Owner, Node, Attributes.Value,
                        MonitoredReadInputs.Capture(m_session, Node), out ServiceResult? result) &&
                        ServiceResult.IsGood(result))
                    {
                        Interlocked.Increment(ref hits);
                    }
                });
                Assert.That(hits, Is.EqualTo(1024));
                Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                Endpoint.SecurityMode = MessageSecurityMode.Sign;
                int staleGrants = 0;
                Parallel.For(0, 1024, iteration =>
                {
                    if (MonitoredReadVerdict.TryReuse(verdict, m_server.Object, Owner, Node, Attributes.Value,
                        MonitoredReadInputs.Capture(m_session, Node), out _))
                    {
                        Interlocked.Increment(ref staleGrants);
                    }
                });
                Assert.That(staleGrants, Is.Zero);
                TestContext.Out.WriteLine(
                    "Concurrent reuse: 1024 hits before revocation; 0 stale grants after revocation.");
            }

            /// <summary>
            /// Records the sample before applying the next-cycle mutation.
            /// </summary>
            private void OnQueued(in DataValue value, ServiceResult? error)
            {
                int sample;
                lock (m_recordsLock)
                {
                    sample = m_records.Count;
                    m_records.Add((value.StatusCode, Owner.Checks));
                }
                if (m_afterSample.TryGetValue(sample, out Action? action))
                {
                    action();
                }
            }

            /// <summary>
            /// Captures the queue method's by-reference signature.
            /// </summary>
            private delegate void QueueValueCallback(in DataValue value, ServiceResult? error);

            /// <summary>Server dependencies used by the real manager.</summary>
            private readonly Mock<IServerInternal> m_server;

            /// <summary>Queue factory owned by the fixture.</summary>
            private readonly MonitoredItemQueueFactory m_queues;

            /// <summary>Current exact-source handle.</summary>
            private NodeHandle m_handle;

            /// <summary>Session, or null for a sessionless item.</summary>
            private readonly ISession? m_session;

            /// <summary>Current effective identity.</summary>
            private IUserIdentity m_identity;

            /// <summary>Current secure channel identity.</summary>
            private string m_secureChannelId = "sample-channel";

            /// <summary>Sampled item with a recording queue callback.</summary>
            private readonly Mock<ISampledDataChangeMonitoredItem> m_item = new();

            /// <summary>Statuses and cumulative permission-check counts.</summary>
            private readonly List<(StatusCode Status, int Checks)> m_records = [];

            /// <summary>Protects sample records.</summary>
            private readonly Lock m_recordsLock = new();

            /// <summary>Mutations executed after specific samples.</summary>
            private readonly Dictionary<int, Action> m_afterSample = [];

            /// <summary>Current accepted endpoint.</summary>
            private EndpointDescription? m_endpoint;

            /// <summary>Current custom-session identity generation.</summary>
            private long m_identityGeneration;

            /// <summary>Check count before the first sample.</summary>
            private int m_checksBefore;

            /// <summary>Fixture-owned creation context.</summary>
            private OperationContext? m_context;

            /// <summary>Fixture-owned periodic sampling worker.</summary>
            private SamplingGroup? m_group;
        }

        /// <summary>
        /// Counts the permission checks that resolve a handle other than the sampled item's own:
        /// the exact-source check and the role permission check, not the sampled Read.
        /// </summary>
        private sealed class CountingNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the sampled owner using the existing manager constructor.
            /// </summary>
            public CountingNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            /// <summary>
            /// Gets the number of exact-source permission validations.
            /// </summary>
            public int Checks => Volatile.Read(ref m_checks);

            /// <summary>
            /// Gets or sets the item's handle, excluded from authorization counts.
            /// </summary>
            internal NodeHandle? ItemHandle { get; set; }

            /// <inheritdoc/>
            protected override NodeHandle? IsHandleInNamespace(object? managerHandle)
            {
                if (managerHandle is NodeHandle && !ReferenceEquals(managerHandle, ItemHandle))
                {
                    Interlocked.Increment(ref m_checks);
                }
                return base.IsHandleInNamespace(managerHandle);
            }

            /// <summary>Exact-source authorization count.</summary>
            private int m_checks;
        }
    }
}
