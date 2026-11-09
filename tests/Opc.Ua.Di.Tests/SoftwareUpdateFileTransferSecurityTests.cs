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
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
 * OR OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Drives real upload handlers over an offline core model and a recording package store.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SoftwareUpdateFileTransferSecurityTests
    {
        /// <summary>
        /// Two authenticated no-session requests cannot allocate state or borrow a real owner's state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR17RemoteSessionlessUploadCannotAllocateOrBorrowHandlesAsync(bool explicitNull)
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            ServerSystemContext owner = harness.Context("owner", true);
            foreach (string caller in new[] { "sessionless-A", "sessionless-B" })
            {
                ServerSystemContext remote = harness.Context(caller, false);
                if (explicitNull)
                {
                    remote.SessionId = NodeId.Null;
                }
                NodeId fileId = NodeId.Null;
                uint deniedHandle = 0;
                Assert.That(harness.Generate(remote, ref fileId, ref deniedHandle).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadSessionIdInvalid));
                Assert.That(fileId.IsNull, Is.True);
                Assert.That(deniedHandle, Is.Zero);
            }
            (FileState file, uint handle) = harness.GenerateAndOpen(owner);
            Assert.That(handle, Is.EqualTo(1u), "Rejected allocation must not consume a slot or identifier.");
            Assert.That(harness.Write(owner, file, handle, [1, 2, 3, 4]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            foreach (string caller in new[] { "sessionless-A", "sessionless-B" })
            {
                AssertDeniedOperations(harness, harness.Context(caller, false), file, handle,
                    StatusCodes.BadSessionIdInvalid);
            }
            Assert.That(harness.Position(owner, file, handle), Is.EqualTo(4UL));
            Assert.That(file.Size!.Value, Is.EqualTo(4UL));
            Assert.That(file.OpenCount!.Value, Is.EqualTo((ushort)1));
            Assert.That(harness.Store.Invocations, Is.Empty);
        }

        /// <summary>
        /// A genuine owner commits exactly its own bytes, metadata and digest and retires its transient node.
        /// </summary>
        [Test]
        public async Task SecurityR17RealSessionOwnerCommitsExactPackageAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            ServerSystemContext owner = harness.Context("owner", true);
            (FileState file, uint handle) = harness.GenerateAndOpen(owner);
            Assert.That(harness.Write(owner, file, handle, [1, 2, 3, 4]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            AssertDeniedOperations(harness, harness.Context("other", true), file, handle,
                StatusCodes.BadUserAccessDenied);
            Assert.That(harness.Position(owner, file, handle), Is.EqualTo(4UL));
            Assert.That(file.Close!.OnCall!(owner, file.Close, file.NodeId, handle).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            NodeId completion = NodeId.Null;
            ServiceResult committed = harness.Transfer.CloseAndCommit!.OnCall!(
                owner, harness.Transfer.CloseAndCommit, harness.Transfer.NodeId, handle, ref completion);

            Assert.That(committed.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(harness.Payload, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(harness.Metadata!.Id, Is.EqualTo("package-A"));
            Assert.That(harness.Metadata.SizeBytes, Is.EqualTo(4L));
            Assert.That(harness.Metadata.Hash, Is.EqualTo(CoreUtils.ToHexString(SHA256.HashData([1, 2, 3, 4]))));
            Assert.That(harness.Store.Invocations, Has.Count.EqualTo(1));
            Assert.That(harness.Manager.Find(file.NodeId), Is.Null);
            Assert.That(completion.IsNull, Is.True);
        }

        /// <summary>
        /// Handle ownership is exact even for trusted direct calls, alternate receivers and closed handles.
        /// </summary>
        [Test]
        public async Task SecurityR17EveryUploadHandleOperationRequiresRecordedOwnerAsync()
        {
            await using Harness harness = await Harness.CreateAsync().ConfigureAwait(false);
            ServerSystemContext owner = harness.Context("owner", true);
            (FileState victim, uint victimHandle) = harness.GenerateAndOpen(owner);
            (FileState neighbor, uint neighborHandle) = harness.GenerateAndOpen(owner);
            Assert.That(harness.Write(owner, victim, victimHandle, [1, 2]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            AssertDeniedOperations(harness, owner, neighbor, victimHandle, StatusCodes.BadUserAccessDenied,
                checkCommit: false, checkOpen: false);
            AssertDeniedOperations(harness, harness.Manager.SystemContext, victim, victimHandle,
                StatusCodes.BadUserAccessDenied);
            Assert.That(harness.Position(owner, victim, victimHandle), Is.EqualTo(2UL));
            Assert.That(harness.Position(owner, neighbor, neighborHandle), Is.Zero);
            Assert.That(victim.Close!.OnCall!(owner, victim.Close, victim.NodeId, victimHandle).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(harness.Write(owner, victim, victimHandle, [9]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(harness.Write(owner, neighbor, uint.MaxValue, [9]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            (FileState trusted, uint trustedHandle) = harness.GenerateAndOpen(harness.Manager.SystemContext);
            Assert.That(harness.Write(harness.Manager.SystemContext, trusted, trustedHandle, [7]).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good), "Explicit in-process invocation remains supported.");
            Assert.That(harness.Store.Invocations, Is.Empty);
        }

        /// <summary>
        /// Asserts all allocating and consuming delegates without permitting any mutation.
        /// </summary>
        private static void AssertDeniedOperations(
            Harness harness, ServerSystemContext caller, FileState file, uint handle, StatusCode expected,
            bool checkCommit = true, bool checkOpen = true)
        {
            if (checkOpen)
            {
                uint open = 0;
                Assert.That(file.Open!.OnCall!(caller, file.Open, file.NodeId, 6, ref open).StatusCode,
                    Is.EqualTo((StatusCode)expected));
                Assert.That(open, Is.Zero);
            }
            Assert.That(harness.Write(caller, file, handle, [9, 9]).StatusCode, Is.EqualTo((StatusCode)expected));
            ulong position = 0;
            Assert.That(file.GetPosition!.OnCall!(
                caller, file.GetPosition, file.NodeId, handle, ref position).StatusCode,
                Is.EqualTo((StatusCode)expected));
            Assert.That(file.SetPosition!.OnCall!(
                caller, file.SetPosition, file.NodeId, handle, 0).StatusCode, Is.EqualTo((StatusCode)expected));
            ByteString data = default;
            Assert.That(file.Read!.OnCall!(caller, file.Read, file.NodeId, handle, 2, ref data).StatusCode,
                Is.EqualTo((StatusCode)expected));
            Assert.That(data.IsNull, Is.True);
            Assert.That(file.Close!.OnCall!(caller, file.Close, file.NodeId, handle).StatusCode,
                Is.EqualTo((StatusCode)expected));
            if (checkCommit)
            {
                NodeId completion = NodeId.Null;
                Assert.That(harness.Transfer.CloseAndCommit!.OnCall!(
                    caller, harness.Transfer.CloseAndCommit, harness.Transfer.NodeId, handle, ref completion).StatusCode,
                    Is.EqualTo((StatusCode)expected));
                Assert.That(completion.IsNull, Is.True);
            }
        }

        /// <summary>
        /// Owns only offline node managers, real bound delegates and an external storage fake.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates an offline standard model and DI manager.
            /// </summary>
            private Harness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_queues = new MonitoredItemQueueFactory(telemetry);
                m_server = new Mock<IServerInternal>();
                var namespaces = new NamespaceTable();
                var master = new Mock<IMasterNodeManager>();
                m_server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                m_server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                m_server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
                m_server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry);
                m_server.SetupGet(value => value.NodeManager).Returns(master.Object);
                m_server.SetupGet(value => value.SessionManager).Returns(Mock.Of<ISessionManager>());
                m_server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(m_queues);
                m_server.SetupGet(value => value.DefaultSystemContext)
                    .Returns(new ServerSystemContext(m_server.Object));
                master.SetupGet(value => value.ConfigurationNodeManager)
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                var configuration = new ApplicationConfiguration
                {
                    ApplicationUri = "urn:security:r17",
                    ServerConfiguration = new ServerConfiguration()
                };
                m_core = new CoreNodeManager(m_server.Object, configuration);
                m_server.SetupGet(value => value.CoreNodeManager).Returns(m_core);
                master.SetupGet(value => value.CoreNodeManager).Returns(m_core);
                Manager = new DiNodeManager(m_server.Object, configuration);
                Store = new Mock<ISoftwarePackageStore>(MockBehavior.Strict);
                Store.Setup(value => value.AddAsync(
                    It.IsAny<SoftwarePackage>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                    .Returns(async (SoftwarePackage metadata, Stream payload, CancellationToken ct) =>
                    {
                        using var copy = new MemoryStream();
                        await payload.CopyToAsync(copy, 81920, ct).ConfigureAwait(false);
                        Payload = copy.ToArray();
                        Metadata = metadata;
                        return metadata;
                    });
            }

            /// <summary>
            /// Gets the real address-space owner.
            /// </summary>
            public DiNodeManager Manager { get; }

            /// <summary>
            /// Gets the recording store.
            /// </summary>
            public Mock<ISoftwarePackageStore> Store { get; }

            /// <summary>
            /// Gets the real transfer object's bound handlers.
            /// </summary>
            public TemporaryFileTransferState Transfer { get; private set; } = null!;

            /// <summary>
            /// Gets the exact payload received by storage.
            /// </summary>
            public byte[] Payload { get; private set; } = [];

            /// <summary>
            /// Gets the metadata received by storage.
            /// </summary>
            public SoftwarePackage? Metadata { get; private set; }

            /// <summary>
            /// Imports real standard and DI nodes without opening a listening endpoint.
            /// </summary>
            public static async ValueTask<Harness> CreateAsync()
            {
                var harness = new Harness();
                try
                {
                    await harness.m_core.ImportNodesAsync(
                        harness.m_core.SystemContext,
                        new NodeStateCollection().AddOpcUa(harness.m_core.SystemContext), true).ConfigureAwait(false);
                    await harness.Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    harness.Transfer = harness.Manager.SystemContext.CreateInstanceOfTemporaryFileTransferType(
                        null!, new QualifiedName("Transfer", harness.Manager.DiNamespaceIndex));
                    harness.Transfer.NodeId = new NodeId("R17Transfer", harness.Manager.DiNamespaceIndex);
                    harness.m_transfer = new SoftwareUpdateFileTransferManager(
                        harness.Transfer, harness.Manager, harness.Store.Object,
                        harness.m_server.Object.Telemetry.CreateLogger<SoftwareUpdateFileTransferSecurityTests>());
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Supplies distinct authenticated request identities and optional genuine protocol session ownership.
            /// </summary>
            public ServerSystemContext Context(string name, bool withSession)
            {
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                identity.SetupGet(value => value.DisplayName).Returns(name);
                var channel = new SecureChannelContext(name, new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, RequestEncoding.Binary);
                OperationContext operation;
                if (withSession)
                {
                    var session = new Mock<ISession>();
                    session.SetupGet(value => value.Id).Returns(new NodeId(name, 1));
                    session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                    session.SetupGet(value => value.PreferredLocales).Returns([]);
                    operation = new OperationContext(
                        new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object);
                }
                else
                {
                    operation = new OperationContext(
                        new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
                }
                m_operations.Add(operation);
                return new ServerSystemContext(m_server.Object, operation);
            }

            /// <summary>
            /// Invokes the real allocation delegate.
            /// </summary>
            public ServiceResult Generate(ServerSystemContext context, ref NodeId fileId, ref uint handle)
            {
                return Transfer.GenerateFileForWrite!.OnCall!(
                    context, Transfer.GenerateFileForWrite, Transfer.NodeId, new Variant("package-A"),
                    ref fileId, ref handle);
            }

            /// <summary>
            /// Allocates and opens a real transient file.
            /// </summary>
            public (FileState File, uint Handle) GenerateAndOpen(ServerSystemContext context)
            {
                NodeId fileId = NodeId.Null;
                uint handle = 0;
                Assert.That(Generate(context, ref fileId, ref handle).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                var file = (FileState)Manager.Find(fileId)!;
                uint opened = 0;
                Assert.That(file.Open!.OnCall!(context, file.Open, file.NodeId, 6, ref opened).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(opened, Is.EqualTo(handle));
                return (file, handle);
            }

            /// <summary>
            /// Invokes the real write delegate.
            /// </summary>
            public ServiceResult Write(ServerSystemContext context, FileState file, uint handle, byte[] bytes)
            {
                return file.Write!.OnCall!(context, file.Write, file.NodeId, handle, ByteString.From(bytes));
            }

            /// <summary>
            /// Obtains the current cursor through the real delegate.
            /// </summary>
            public ulong Position(ServerSystemContext context, FileState file, uint handle)
            {
                ulong position = 0;
                Assert.That(file.GetPosition!.OnCall!(
                    context, file.GetPosition, file.NodeId, handle, ref position).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                return position;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_transfer?.Dispose();
                foreach (OperationContext operation in m_operations)
                {
                    operation.Dispose();
                }
                await Manager.DisposeAsync().ConfigureAwait(false);
                await m_core.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            private readonly Mock<IServerInternal> m_server;
            private readonly CoreNodeManager m_core;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly List<OperationContext> m_operations = [];
            private SoftwareUpdateFileTransferManager? m_transfer;
        }
    }
}
