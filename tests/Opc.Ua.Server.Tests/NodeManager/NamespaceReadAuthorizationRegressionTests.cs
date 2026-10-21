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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises UInt16 namespace restrictions through the actual service dispatcher and synchronous manager adapter.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class NamespaceReadAuthorizationRegressionTests
    {
        /// <summary>
        /// A weak service channel cannot bypass a namespace's UInt16 signing or encryption requirement.
        /// </summary>
        [TestCase(AccessRestrictionType.SigningRequired, MessageSecurityMode.None)]
        [TestCase(AccessRestrictionType.EncryptionRequired, MessageSecurityMode.None)]
        [TestCase(AccessRestrictionType.EncryptionRequired, MessageSecurityMode.Sign)]
        [TestCase(
            AccessRestrictionType.SigningRequired | AccessRestrictionType.EncryptionRequired,
            MessageSecurityMode.Sign)]
        public async Task SecurityR30SyncNamespaceUInt16RestrictionsRejectWeakServiceChannelAsync(
            AccessRestrictionType restriction, MessageSecurityMode mode)
        {
            await using var harness = new Harness();
            harness.Metadata.DefaultAccessRestrictions!.Value = (ushort)restriction;

            DataValue value = await harness.ReadAsync(mode).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(harness.ValueReads, Is.Zero);
        }

        /// <summary>
        /// Adequate channels and explicit local None remain supported, without confusing absent and zero defaults.
        /// </summary>
        [TestCase("sign")]
        [TestCase("encrypt")]
        [TestCase("absent")]
        [TestCase("zero")]
        [TestCase("local-zero")]
        [TestCase("local-sign")]
        [TestCase("combined")]
        public async Task SecurityR30SyncNamespaceRestrictionAllowsAdequateChannelAndExplicitOverrideAsync(
            string policy)
        {
            await using var harness = new Harness();
            harness.Metadata.DefaultAccessRestrictions!.Value = (ushort)AccessRestrictionType.EncryptionRequired;
            MessageSecurityMode mode = MessageSecurityMode.None;
            switch (policy)
            {
                case "sign":
                    harness.Metadata.DefaultAccessRestrictions.Value = (ushort)AccessRestrictionType.SigningRequired;
                    mode = MessageSecurityMode.Sign;
                    break;
                case "encrypt":
                    mode = MessageSecurityMode.SignAndEncrypt;
                    break;
                case "absent":
                    harness.Metadata.DefaultAccessRestrictions = null;
                    break;
                case "zero":
                    harness.Metadata.DefaultAccessRestrictions.Value = 0;
                    break;
                case "local-zero":
                    harness.Manager.Node.AccessRestrictions = AccessRestrictionType.None;
                    break;
                case "local-sign":
                    harness.Manager.Node.AccessRestrictions = AccessRestrictionType.SigningRequired;
                    mode = MessageSecurityMode.Sign;
                    break;
                case "combined":
                    harness.Metadata.DefaultAccessRestrictions.Value = (ushort)(
                        AccessRestrictionType.EncryptionRequired | AccessRestrictionType.SigningRequired |
                        AccessRestrictionType.SessionRequired | AccessRestrictionType.ApplyRestrictionsToBrowse);
                    mode = MessageSecurityMode.SignAndEncrypt;
                    break;
            }

            DataValue value = await harness.ReadAsync(mode).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(42));
            Assert.That(harness.ValueReads, Is.EqualTo(1));
        }

        /// <summary>
        /// Failed, malformed and unsupported policy values are operation errors, never unrestricted fallbacks.
        /// </summary>
        [TestCase("callback")]
        [TestCase("type")]
        [TestCase("bits")]
        [TestCase("exception")]
        public async Task SecurityR30ConfiguredPolicyReadOrDecodeFailureCannotBecomeNoneAsync(string failure)
        {
            await using var harness = new Harness();
            harness.Metadata.DefaultAccessRestrictions!.OnSimpleReadValue =
                (ISystemContext _, NodeState _, ref Variant value) =>
                {
                    switch (failure)
                    {
                        case "callback":
                            return StatusCodes.BadUserAccessDenied;
                        case "type":
                            value = new Variant("not UInt16");
                            break;
                        case "bits":
                            value = new Variant((ushort)16);
                            break;
                        case "exception":
                            throw new InvalidOperationException("deterministic policy callback failure");
                    }
                    return ServiceResult.Good;
                };

            DataValue value = await harness.ReadAsync(MessageSecurityMode.SignAndEncrypt).ConfigureAwait(false);

            StatusCode expected = failure switch
            {
                "callback" => StatusCodes.BadUserAccessDenied,
                "type" => StatusCodes.BadTypeMismatch,
                "bits" => StatusCodes.BadOutOfRange,
                _ => StatusCodes.BadUnexpectedError
            };
            Assert.That(value.StatusCode, Is.EqualTo(expected));
            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(harness.ValueReads, Is.Zero);
        }

        /// <summary>
        /// Caller-effective policy callbacks see the service caller, rather than an elevated manager context.
        /// </summary>
        [Test]
        public async Task SecurityR30NamespacePolicyUsesServiceCallerAndRequiresSessionAsync()
        {
            await using var harness = new Harness();
            ISession? observed = null;
            harness.Metadata.DefaultAccessRestrictions!.OnSimpleReadValue =
                (ISystemContext context, NodeState _, ref Variant value) =>
                {
                    observed = ((ServerSystemContext)context).OperationContext?.Session;
                    value = new Variant((ushort)AccessRestrictionType.SessionRequired);
                    return ServiceResult.Good;
                };
            DataValue allowed = await harness.ReadAsync(MessageSecurityMode.None).ConfigureAwait(false);
            Assert.That(allowed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(observed, Is.SameAs(harness.Session.Object));

            DataValue denied = await harness.ReadAsync(MessageSecurityMode.SignAndEncrypt, sessionless: true)
                .ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(denied.WrappedValue.IsNull, Is.True);
            Assert.That(observed, Is.Null);
            Assert.That(harness.ValueReads, Is.EqualTo(1));
        }

        /// <summary>
        /// Owns an in-memory service dispatcher, synchronous manager and typed namespace snapshot.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Wires the real dispatcher to deterministic core/configuration services without starting a server.
            /// </summary>
            public Harness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
                IConfigurationNodeManager configuration = server.Object.NodeManager.ConfigurationNodeManager!;
                ICoreNodeManager core = server.Object.CoreNodeManager;
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(configuration);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(core);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                Manager = new ReadManager(server.Object);
                Manager.Node.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
                {
                    ValueReads++;
                    return ServiceResult.Good;
                };
                Metadata = new NamespaceMetadataState(null);
                Metadata.AddDefaultAccessRestrictions(Manager.SystemContext);
                DeterministicServerMock.SetNamespaceMetadata(server, Manager.NamespaceIndex, Metadata);
                m_dispatcher = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null, null, [Manager]);
                server.SetupGet(value => value.NodeManager).Returns(m_dispatcher);
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
                Session = new Mock<ISession>();
                Session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                Session.SetupGet(value => value.Id).Returns(new NodeId(1));
            }

            /// <summary>
            /// Gets the manager exercised through its real synchronous adapter.
            /// </summary>
            public ReadManager Manager { get; }

            /// <summary>
            /// Gets the authoritative namespace metadata.
            /// </summary>
            public NamespaceMetadataState Metadata { get; }

            /// <summary>
            /// Gets the service caller.
            /// </summary>
            public Mock<ISession> Session { get; }

            /// <summary>
            /// Gets the number of protected Value reads, excluding permission attributes.
            /// </summary>
            public int ValueReads { get; private set; }

            /// <summary>
            /// Executes the Read service, including dispatcher permission validation before touching the source.
            /// </summary>
            public async Task<DataValue> ReadAsync(MessageSecurityMode mode, bool sessionless = false)
            {
                var endpoint = new EndpointDescription { SecurityMode = mode };
                Session.SetupGet(value => value.EndpointDescription).Returns(endpoint);
                var channel = new SecureChannelContext("namespace-read", endpoint, RequestEncoding.Binary);
                using var context = sessionless
                    ? new OperationContext(
                        new RequestHeader(), channel, RequestType.Read, RequestLifetime.None,
                        Session.Object.EffectiveIdentity)
                    : new OperationContext(
                        new RequestHeader(), channel, RequestType.Read, RequestLifetime.None, Session.Object);
                (ArrayOf<DataValue> values, _) = await m_dispatcher.ReadAsync(
                    context, 0, TimestampsToReturn.Both,
                    [new ReadValueId { NodeId = Manager.Node.NodeId, AttributeId = Attributes.Value }])
                    .ConfigureAwait(false);
                Assert.That(values, Has.Count.EqualTo(1));
                return values[0];
            }

            /// <summary>
            /// Disposes the real dispatcher and the queues it borrows.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await m_dispatcher.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            /// <summary>The real service dispatcher.</summary>
            private readonly MasterNodeManager m_dispatcher;
            /// <summary>The backing queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>
        /// Provides a readable instance without a node-local restriction, so namespace fallback is exercised.
        /// </summary>
        private sealed class ReadManager : CustomNodeManager2
        {
            /// <summary>
            /// Registers the protected instance.
            /// </summary>
            public ReadManager(IServerInternal server)
                : base(server, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
                Node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("NamespaceProtectedValue", NamespaceIndex),
                    BrowseName = new QualifiedName("NamespaceProtectedValue", NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    Value = 42,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead
                };
                AddPredefinedNode(SystemContext, Node);
            }

            /// <summary>
            /// Gets the actual instance read by the service.
            /// </summary>
            public BaseDataVariableState Node { get; }
        }
    }
}
