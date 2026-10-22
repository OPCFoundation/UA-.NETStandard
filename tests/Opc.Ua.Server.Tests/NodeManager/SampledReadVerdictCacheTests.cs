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
        }

        /// <summary>
        /// Each input the verdict is keyed on retires it: the next sample runs the full check
        /// again, and the verdict it computes is reused afterwards.
        /// </summary>
        [TestCase("RolePermissionsVersion")]
        [TestCase("NamespaceDefaultsGeneration")]
        [TestCase("NamespaceMetadata")]
        [TestCase("IdentityGeneration")]
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
        /// Drives a sampling group of a real asynchronous node manager with one sampled item and
        /// records, per sample, the queued status and the number of exact-source checks so far.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            public Harness()
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
                Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(1));
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                session.SetupGet(value => value.EndpointDescription).Returns(() => Endpoint);
                session.SetupGet(value => value.PreferredLocales).Returns([]);
                session.Setup(value => value.CaptureIdentityRefreshSnapshot())
                    .Returns(() => new IdentityRefreshSnapshot(identity.Object, Interlocked.Read(ref m_identityGeneration)));
                m_session = session.Object;

                m_item.SetupGet(m => m.Id).Returns(1);
                m_item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                m_item.SetupGet(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
                m_item.SetupGet(m => m.SamplingInterval).Returns(kInterval);
                m_item.SetupGet(m => m.Session).Returns(m_session);
                m_item.SetupGet(m => m.EffectiveIdentity).Returns(identity.Object);
                m_item.SetupGet(m => m.ManagerHandle).Returns(m_handle);
                m_item.SetupGet(m => m.NodeId).Returns(Node.NodeId);
                m_item.SetupGet(m => m.AttributeId).Returns(Attributes.Value);
                m_item.Setup(m => m.GetReadValueId()).Returns(() => new ReadValueId
                {
                    NodeId = Node.NodeId,
                    AttributeId = Attributes.Value
                });
                m_item.Setup(m => m.QueueValue(in It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult?>()))
                    .Callback(new QueueValueCallback((in DataValue value, ServiceResult? _) => OnQueued(value)));
            }

            public CountingNodeManager Owner { get; }

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

            public async Task StartAsync()
            {
                await Owner.AddNodeAsync(Owner.SystemContext, NodeId.Null, Node).ConfigureAwait(false);
                m_context = new OperationContext(
                    new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, m_session);
                m_group = new SamplingGroup(
                    m_server.Object,
                    Owner,
                    [new SamplingRateGroup((int)kInterval, (int)kInterval, 4)],
                    m_context,
                    kInterval);
                m_checksBefore = Owner.Checks;

                // the initial value counts as queued, so every sample is a cycle of the loop.
                Assert.That(m_group.StartMonitoring(m_context, m_item.Object, null, initialValueQueued: true), Is.True);
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

            public StatusCode? Status(int sample)
            {
                lock (m_recordsLock)
                {
                    return m_records[sample].Status;
                }
            }

            public async ValueTask DisposeAsync()
            {
                m_group?.Dispose();
                m_context?.Dispose();
                Owner.Dispose();
                await Owner.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            private void OnQueued(in DataValue value)
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

            private delegate void QueueValueCallback(in DataValue value, ServiceResult? error);

            private readonly Mock<IServerInternal> m_server;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly NodeHandle m_handle;
            private readonly ISession m_session;
            private readonly Mock<ISampledDataChangeMonitoredItem> m_item = new();
            private readonly List<(StatusCode Status, int Checks)> m_records = [];
            private readonly Lock m_recordsLock = new();
            private readonly Dictionary<int, Action> m_afterSample = [];
            private EndpointDescription? m_endpoint;
            private long m_identityGeneration;
            private int m_checksBefore;
            private OperationContext? m_context;
            private SamplingGroup? m_group;
        }

        /// <summary>
        /// Counts the permission checks that resolve a handle other than the sampled item's own:
        /// the exact-source check and the role permission check, not the sampled Read.
        /// </summary>
        private sealed class CountingNodeManager : AsyncCustomNodeManager
        {
            public CountingNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public object? ItemHandle { get; set; }

            public int Checks => Volatile.Read(ref m_checks);

            protected override NodeHandle? IsHandleInNamespace(object managerHandle)
            {
                if (managerHandle is NodeHandle && !ReferenceEquals(managerHandle, ItemHandle))
                {
                    Interlocked.Increment(ref m_checks);
                }
                return base.IsHandleInNamespace(managerHandle);
            }

            private int m_checks;
        }
    }
}
