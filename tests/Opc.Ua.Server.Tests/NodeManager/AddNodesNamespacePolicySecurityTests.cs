/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises caller-effective namespace policy before real AddNodes registration.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class AddNodesNamespacePolicySecurityTests
    {
        /// <summary>
        /// Namespace restriction callbacks are applied in the caller context, not read from permissive backing bits.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR31NamespaceRestrictionCallbacksRequireEncryptedCallerAsync(bool simpleCallback)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.SetUserPermissionCallback(simpleCallback, true);
            BaseVariableState restrictions = harness.Metadata.DefaultAccessRestrictions!;
            if (simpleCallback)
            {
                restrictions.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant value) =>
                {
                    value = new Variant((ushort)AccessRestrictionType.EncryptionRequired);
                    return ServiceResult.Good;
                };
            }
            else
            {
                restrictions.OnReadValue =
                    (ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                        ref Variant value, ref StatusCode _, ref DateTimeUtc _) =>
                    {
                        value = new Variant((ushort)AccessRestrictionType.EncryptionRequired);
                        return ServiceResult.Good;
                    };
            }
            EndpointDescription endpoint = harness.Context.ChannelContext!.EndpointDescription!;
            endpoint.SecurityMode = MessageSecurityMode.Sign;
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("WeakTransport")]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(harness.Owner.Find(new NodeId("WeakTransport", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.Zero);
            endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            (results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("EncryptedTransport")]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].AddedNodeId,
                Is.EqualTo(new NodeId("EncryptedTransport", harness.Owner.NamespaceIndex)));
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Read and decode errors in any namespace default remain service failures rather than empty authorization.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [Category("SecurityRegression")]
        public async Task SecurityR31EveryNamespaceDefaultPreservesReadAndDecodeFailuresAsync(int field, bool wrongType)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Metadata.DefaultUserRolePermissions!.Value =
                Harness.Permissions(PermissionType.AddNode | PermissionType.AddReference);
            BaseVariableState property = field switch
            {
                0 => harness.Metadata.DefaultAccessRestrictions!,
                1 => harness.Metadata.DefaultRolePermissions!,
                _ => harness.Metadata.DefaultUserRolePermissions!
            };
            property.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant value) =>
            {
                value = new Variant("not-a-policy");
                return wrongType ? ServiceResult.Good : StatusCodes.BadNotReadable;
            };
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("MalformedPolicy")]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode,
                Is.EqualTo(wrongType ? StatusCodes.BadTypeMismatch : StatusCodes.BadNotReadable));
            Assert.That(harness.Owner.Find(new NodeId("MalformedPolicy", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.Zero);
            property.OnSimpleReadValue = null;
            (results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("RecoveredPolicy")]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Owner.Find(new NodeId("RecoveredPolicy", harness.Owner.NamespaceIndex)), Is.Not.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Either callback form failing on any default denies mutation without caching the failed policy.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [Category("SecurityRegression")]
        public async Task SecurityR31NamespaceCallbackExceptionsFailClosedAndRecoverAsync(int field, bool simple)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Metadata.DefaultUserRolePermissions!.Value =
                Harness.Permissions(PermissionType.AddNode | PermissionType.AddReference);
            BaseVariableState property = field switch
            {
                0 => harness.Metadata.DefaultAccessRestrictions!,
                1 => harness.Metadata.DefaultRolePermissions!,
                _ => harness.Metadata.DefaultUserRolePermissions!
            };
            IUserIdentity? observedCaller = null;
            if (simple)
            {
                property.OnSimpleReadValue = (ISystemContext context, NodeState _, ref Variant _) =>
                {
                    observedCaller = ((ServerSystemContext)context).OperationContext!.UserIdentity;
                    throw new InvalidOperationException("Namespace callback failed.");
                };
            }
            else
            {
                property.OnReadValue =
                    (ISystemContext context, NodeState _, NumericRange _, QualifiedName _,
                        ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                    {
                        observedCaller = ((ServerSystemContext)context).OperationContext!.UserIdentity;
                        throw new InvalidOperationException("Namespace callback failed.");
                    };
            }
            AddNodesItem denied = harness.CreateItem("ThrowingPolicy");
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(harness.Context, [denied])
                .ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(results[0].AddedNodeId.IsNull, Is.True);
            Assert.That(harness.Owner.Find(new NodeId("ThrowingPolicy", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, denied.BrowseName), Is.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.Zero);
            Assert.That(observedCaller, Is.SameAs(harness.Context.UserIdentity));

            property.OnSimpleReadValue = null;
            property.OnReadValue = null;
            AddNodesItem recovered = harness.CreateItem("RecoveredAfterException");
            (results, _) = await harness.Dispatcher.AddNodesAsync(harness.Context, [recovered]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].AddedNodeId,
                Is.EqualTo(new NodeId("RecoveredAfterException", harness.Owner.NamespaceIndex)));
            NodeState? registered = harness.Owner.Find(results[0].AddedNodeId);
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.BrowseName, Is.EqualTo(recovered.BrowseName));
            Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, recovered.BrowseName),
                Is.SameAs(registered));
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// All defined restriction bits are accepted, while the first unknown bit, later bits and negatives deny.
        /// </summary>
        [TestCase(15, true)]
        [TestCase(16, false)]
        [TestCase(17, false)]
        [TestCase(65535, false)]
        [TestCase(-1, false)]
        [Category("SecurityRegression")]
        public async Task SecurityR31NamespaceRestrictionBitsAreValidatedBeforeMutationAsync(int bits, bool allowed)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Metadata.DefaultUserRolePermissions!.Value =
                Harness.Permissions(PermissionType.AddNode | PermissionType.AddReference);
            harness.Metadata.DefaultAccessRestrictions!.OnSimpleReadValue =
                (ISystemContext _, NodeState _, ref Variant value) =>
                {
                    value = new Variant(bits);
                    return ServiceResult.Good;
                };
            AddNodesItem item = harness.CreateItem("RestrictionBits");
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(harness.Context, [item])
                .ConfigureAwait(false);
            Assert.That(results[0].StatusCode,
                Is.EqualTo(allowed ? StatusCodes.Good : StatusCodes.BadOutOfRange));
            NodeState? registered = harness.Owner.Find(new NodeId("RestrictionBits", harness.Owner.NamespaceIndex));
            if (allowed)
            {
                Assert.That(results[0].AddedNodeId,
                    Is.EqualTo(new NodeId("RestrictionBits", harness.Owner.NamespaceIndex)));
                Assert.That(registered, Is.Not.Null);
                Assert.That(registered!.BrowseName, Is.EqualTo(item.BrowseName));
                Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, item.BrowseName),
                    Is.SameAs(registered));
                Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
            }
            else
            {
                Assert.That(results[0].AddedNodeId.IsNull, Is.True);
                Assert.That(registered, Is.Null);
                Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, item.BrowseName), Is.Null);
                Assert.That(harness.Owner.RegistrationCalls, Is.Zero);
            }
        }

        /// <summary>
        /// Dynamic user permissions override permissive backing bits without borrowing AddReference.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR31AddNodesUsesCurrentCallerNamespacePermissionAsync(bool simpleCallback)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.SetUserPermissionCallback(simpleCallback, false);
            AddNodesItem item = harness.CreateItem("Denied");

            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [item]).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(results[0].AddedNodeId.IsNull, Is.True);
            Assert.That(harness.Owner.Find(new NodeId("Denied", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, item.BrowseName), Is.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.Zero);
            Assert.That(harness.CallbackCaller, Is.SameAs(harness.Context.UserIdentity));
        }

        /// <summary>
        /// Current namespace approval creates exactly the requested node but does not override the parent ACL.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR31AuthorizedNamespaceAndParentPermissionsCreateExactNodeAsync(bool simpleCallback)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.SetUserPermissionCallback(simpleCallback, true);
            AddNodesItem item = harness.CreateItem("Allowed");
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [item]).ConfigureAwait(false);
            var expected = new NodeId("Allowed", harness.Owner.NamespaceIndex);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].AddedNodeId, Is.EqualTo(expected));
            NodeState? added = harness.Owner.Find(expected);
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.BrowseName, Is.EqualTo(item.BrowseName));
            Assert.That(harness.Parent.FindChild(harness.Owner.SystemContext, item.BrowseName), Is.SameAs(added));
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));

            harness.Parent.RolePermissions = Harness.Permissions(PermissionType.Read);
            (results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("ParentDenied")]).ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Owner.Find(new NodeId("ParentDenied", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed namespace read is not empty policy, and a mixed batch mutates only its approved item.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR31NamespaceReadFailuresAndMixedItemsRemainEffectiveAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            int reads = 0;
            harness.Metadata.DefaultUserRolePermissions!.OnSimpleReadValue =
                (ISystemContext _, NodeState _, ref Variant value) =>
                {
                    reads++;
                    value = Variant.FromStructure(
                        Harness.Permissions(PermissionType.AddNode | PermissionType.AddReference));
                    return reads == 1 ? StatusCodes.BadUserAccessDenied : ServiceResult.Good;
                };
            (ArrayOf<AddNodesResult> results, _) = await harness.Dispatcher.AddNodesAsync(
                harness.Context, [harness.CreateItem("FailedRead"), harness.CreateItem("CurrentPolicy")])
                .ConfigureAwait(false);
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Owner.Find(new NodeId("FailedRead", harness.Owner.NamespaceIndex)), Is.Null);
            Assert.That(harness.Owner.Find(new NodeId("CurrentPolicy", harness.Owner.NamespaceIndex)), Is.Not.Null);
            Assert.That(harness.Owner.RegistrationCalls, Is.EqualTo(1));
            Assert.That(reads, Is.GreaterThanOrEqualTo(2));
        }

        /// <summary>
        /// Owns a real dispatcher and opted-in manager backed only by in-memory server services.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates namespace policy, caller and dispatcher without starting a transport listener.
            /// </summary>
            public Harness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
                var types = (TypeTable)server.Object.TypeTree;
                types.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
                types.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
                types.AddSubtype(ReferenceTypeIds.Organizes, ReferenceTypeIds.HierarchicalReferences);
                types.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
                var configuration = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
                Owner = new ManagementManager(server.Object, configuration);
                Parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Parent", Owner.NamespaceIndex),
                    BrowseName = new QualifiedName("Parent", Owner.NamespaceIndex),
                    RolePermissions = Permissions(PermissionType.AddReference)
                };
                Metadata = new NamespaceMetadataState(null);
                Metadata.AddDefaultRolePermissions(Owner.SystemContext)
                    .AddDefaultUserRolePermissions(Owner.SystemContext)
                    .AddDefaultAccessRestrictions(Owner.SystemContext);
                Metadata.DefaultRolePermissions!.Value = Permissions(PermissionType.AddNode);
                Metadata.DefaultUserRolePermissions!.Value = Permissions(PermissionType.AddNode);
                DeterministicServerMock.SetNamespaceMetadata(server, Owner.NamespaceIndex, Metadata);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                Dispatcher = new MasterNodeManager(server.Object, configuration, null, [Owner], null);
                server.SetupGet(value => value.NodeManager).Returns(Dispatcher);
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Observer]);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId("AddNodesCaller", 1));
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                session.SetupGet(value => value.EndpointDescription).Returns(endpoint);
                Context = new OperationContext(new RequestHeader(),
                    new SecureChannelContext("add-nodes", endpoint, RequestEncoding.Binary),
                    RequestType.AddNodes, RequestLifetime.None, session.Object);
            }

            /// <summary>
            /// Gets the real mutation dispatcher.
            /// </summary>
            public MasterNodeManager Dispatcher { get; }

            /// <summary>
            /// Gets the opted-in namespace owner.
            /// </summary>
            public ManagementManager Owner { get; }

            /// <summary>
            /// Gets the existing parent, whose references must not change on denial.
            /// </summary>
            public BaseObjectState Parent { get; }

            /// <summary>
            /// Gets the caller-dependent namespace metadata.
            /// </summary>
            public NamespaceMetadataState Metadata { get; }

            /// <summary>
            /// Gets the authenticated caller context.
            /// </summary>
            public OperationContext Context { get; }

            /// <summary>
            /// Gets the identity observed by the namespace callback.
            /// </summary>
            public IUserIdentity? CallbackCaller { get; private set; }

            /// <summary>
            /// Registers the real parent before dispatching client requests.
            /// </summary>
            public async Task InitializeAsync()
            {
                await Owner.AddNodeAsync(Owner.SystemContext, NodeId.Null, Parent).ConfigureAwait(false);
            }

            /// <summary>
            /// Supplies either supported callback form independently of the permissive backing value.
            /// </summary>
            public void SetUserPermissionCallback(bool simple, bool allowed)
            {
                ArrayOf<RolePermissionType> effective = Permissions(
                    allowed ? PermissionType.AddNode | PermissionType.AddReference : PermissionType.AddReference);
                if (simple)
                {
                    Metadata.DefaultUserRolePermissions!.OnSimpleReadValue =
                        (ISystemContext context, NodeState _, ref Variant value) =>
                        {
                            CallbackCaller = ((ServerSystemContext)context).OperationContext!.UserIdentity;
                            value = Variant.FromStructure(effective);
                            return ServiceResult.Good;
                        };
                }
                else
                {
                    Metadata.DefaultUserRolePermissions!.OnReadValue =
                        (ISystemContext context, NodeState _, NumericRange _, QualifiedName _,
                            ref Variant value, ref StatusCode _, ref DateTimeUtc _) =>
                        {
                            CallbackCaller = ((ServerSystemContext)context).OperationContext!.UserIdentity;
                            value = Variant.FromStructure(effective);
                            return ServiceResult.Good;
                        };
                }
            }

            /// <summary>
            /// Creates an ordinary object request in the protected namespace.
            /// </summary>
            public AddNodesItem CreateItem(string name)
            {
                return new AddNodesItem
                {
                    ParentNodeId = Parent.NodeId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    RequestedNewNodeId = new NodeId(name, Owner.NamespaceIndex),
                    BrowseName = new QualifiedName(name, Owner.NamespaceIndex),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                };
            }

            /// <summary>
            /// Creates an explicit permission set for the caller's sole dynamic role.
            /// </summary>
            public static ArrayOf<RolePermissionType> Permissions(PermissionType permissions)
            {
                return [new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = (uint)permissions
                }];
            }

            /// <summary>
            /// Releases the caller, dispatcher and queue ownership.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                Context.Dispose();
                await Dispatcher.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            /// <summary>The in-memory queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>
        /// Counts actual service registration attempts while retaining the production mutation implementation.
        /// </summary>
        private sealed class ManagementManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the namespace owner.
            /// </summary>
            public ManagementManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <inheritdoc/>
            public override bool AllowNodeManagement => true;

            /// <summary>
            /// Gets the number of client AddNodes calls that passed dispatcher authorization.
            /// </summary>
            public int RegistrationCalls { get; private set; }

            /// <inheritdoc/>
            public override ValueTask<(ServiceResult result, NodeId addedNodeId)> AddNodeAsync(
                OperationContext context, AddNodesItem item, CancellationToken cancellationToken = default)
            {
                RegistrationCalls++;
                return base.AddNodeAsync(context, item, cancellationToken);
            }
        }
    }
}
