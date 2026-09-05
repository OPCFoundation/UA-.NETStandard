/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the channel-security policy: a registry mutation always needs a
    /// <c>SignAndEncrypt</c> channel because a document and its content lookup are
    /// integrity-critical, while reads follow
    /// <see cref="XRegistryServerOptions.RequireEncryptionForReads"/>.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistrySecurityTests
    {
        [Test]
        public async Task CreateGroupIsRejectedOnAChannelThatIsNotEncryptedAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);

            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task CreateGroupIsAcceptedOnAnEncryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);

            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
        }

        [Test]
        public async Task GetOrCreateGroupIsRejectedOnAnUnencryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);

            GetOrCreateGroupMethodStateResult result = await nm.OnGetOrCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.None), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task CreateResourceIsRejectedOnAnUnencryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);

            CreateResourceMethodStateResult result = await nm.OnCreateResourceAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, group.GroupNodeId, "r1", "1", false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task GetOrCreateResourceIsRejectedOnAnUnencryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);

            GetOrCreateResourceMethodStateResult result = await nm.OnGetOrCreateResourceAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, group.GroupNodeId, "r1", "1", false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void AnInProcessCallCarriesNoChannelAndIsAllowed()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);

            Assert.That(XRegistryRegistrationNodeManager.IsWriteChannelSecure(nm.SystemContext), Is.True,
                "The server's own bootstrap has no secure channel and must not be blocked.");
        }

        [Test]
        public void ReadsAreAllowedOnAnyChannelByDefault()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);

            Assert.Multiple(() =>
            {
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.None)), Is.True);
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.Sign)), Is.True);
            });
        }

        [Test]
        public void ReadsRequireEncryptionWhenTheOptionIsSet()
        {
            using XRegistryRegistrationNodeManager nm =
                CreateAddressSpace(out Mock<IServerInternal> server, o => o.RequireEncryptionForReads = true);

            Assert.Multiple(() =>
            {
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.Sign)), Is.False);
                Assert.That(
                    nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.SignAndEncrypt)), Is.True);
            });
        }

        [Test]
        public void WritesRequireEncryptionRegardlessOfTheReadOption()
        {
            using XRegistryRegistrationNodeManager nm =
                CreateAddressSpace(out Mock<IServerInternal> server, o => o.RequireEncryptionForReads = false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    XRegistryRegistrationNodeManager.IsWriteChannelSecure(
                        ContextWith(server, MessageSecurityMode.Sign)),
                    Is.False,
                    "The read option cannot relax the write requirement.");
                Assert.That(
                    XRegistryRegistrationNodeManager.IsWriteChannelSecure(
                        ContextWith(server, MessageSecurityMode.SignAndEncrypt)),
                    Is.True);
            });
        }

        [Test]
        public async Task FileMethodsAreRejectedOnAnUnencryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            // One session, two channels. Holding the session constant isolates the security policy
            // from the separate rule that a handle belongs to the session that opened it.
            var sessionId = new NodeId(7001u);
            ServerSystemContext encrypted =
                ContextWith(server, MessageSecurityMode.SignAndEncrypt, sessionId);
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign, sessionId);

            OpenMethodStateResult refused = await resource.Open!.OnCallAsync!(
                signedOnly, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);

            // Open and write over the encrypted channel so the Close below is a real commit.
            OpenMethodStateResult opened = await resource.Open.OnCallAsync!(
                encrypted, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                signedOnly, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            await resource.Write.OnCallAsync!(
                encrypted, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                signedOnly, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            DeleteMethodStateResult deleted = await resource.Delete!.OnCallAsync!(
                signedOnly, resource.Delete, resource.NodeId, resource.Epoch!.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient), "Open for writing.");
                Assert.That(written.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(closed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient),
                    "A Close that commits a document is a mutation.");
                Assert.That(deleted.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task ClosingAHandleThatWroteNothingIsAllowedOnAnyChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            var sessionId = new NodeId(7002u);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt, sessionId), resource.Open,
                resource.NodeId, kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);

            // Releasing a handle changes nothing, so it must not be gated on the write policy —
            // otherwise a handle opened on a permitted channel could never be closed and would
            // hold the upload budget forever.
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.Sign, sessionId), resource.Close,
                resource.NodeId, opened.FileHandle, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
        }

        [Test]
        public async Task AttributeMethodsAreRejectedOnAnUnencryptedChannelAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            var groupState = (GroupState)nm.Find(group.GroupNodeId)!;
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign);

            AddAttributeMethodStateResult added = await groupState.Labels!.AddAttribute!.OnCallAsync!(
                signedOnly, groupState.Labels.AddAttribute, groupState.NodeId, "k", "v",
                groupState.Epoch!.Value, CancellationToken.None).ConfigureAwait(false);
            RemoveAttributeMethodStateResult removed =
                await groupState.Labels.RemoveAttribute!.OnCallAsync!(
                    signedOnly, groupState.Labels.RemoveAttribute, groupState.NodeId, "k",
                    groupState.Epoch!.Value, CancellationToken.None).ConfigureAwait(false);
            DeleteMethodStateResult deleted = await groupState.Delete!.OnCallAsync!(
                signedOnly, groupState.Delete, groupState.NodeId, groupState.Epoch!.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(added.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(removed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(deleted.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task ReadingIsRejectedWhenEncryptionIsRequiredForReadsAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out Mock<IServerInternal> server, o => o.RequireEncryptionForReads = true);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                signedOnly, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                signedOnly, resource.Read, resource.NodeId, 1u, 16, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(opened.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient), "Open for reading.");
                Assert.That(read.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task AHandleCannotBeUsedFromAnotherSessionAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(out Mock<IServerInternal> server);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            ServerSystemContext sessionA = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            ServerSystemContext sessionB = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                sessionA, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);

            // Session B tries to drive session A's handle.
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                sessionB, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                sessionB, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
                Assert.That(written.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidState),
                    "A handle belongs to the session that opened it.");
                Assert.That(closed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task ClosingASessionReleasesItsHandlesAsync()
        {
            using XRegistryRegistrationNodeManager nm = CreateAddressSpace(
                out Mock<IServerInternal> server, o => o.MaxConcurrentUploads = 1);
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            var sessionId = new NodeId(4242u);
            ServerSystemContext session = ContextWith(
                server, MessageSecurityMode.SignAndEncrypt, sessionId);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                session, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True, "Precondition.");

            // The session goes away without closing the handle; the budget must come back.
            nm.SessionClosing(session.OperationContext!, sessionId, deleteSubscriptions: true);

            OpenMethodStateResult next = await resource.Open.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt), resource.Open,
                resource.NodeId, kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(next.ServiceResult), Is.True,
                "An abandoned session must not hold the upload budget forever.");
        }

        /// <summary>
        /// Builds a system context that looks like a call arriving over a channel with the given
        /// security mode.
        /// </summary>
        private static ServerSystemContext ContextWith(
            Mock<IServerInternal> server,
            MessageSecurityMode mode,
            NodeId? sessionId = null)
        {
            var channel = new SecureChannelContext(
                "test",
                new EndpointDescription { SecurityMode = mode },
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);

            // OperationContext derives the SessionId from its session, so a mocked session is what
            // makes two contexts look like two distinct clients.
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(
                sessionId ?? new NodeId(Utils.IncrementIdentifier(ref s_sessionCounter)));
            session.SetupGet(s => s.EffectiveIdentity).Returns(new UserIdentity());
            session.SetupGet(s => s.PreferredLocales).Returns([]);

            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object);
            return new ServerSystemContext(server.Object, operation);
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace(
            out Mock<IServerInternal> server,
            System.Action<XRegistryServerOptions>? configure = null)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            configure?.Invoke(options);

            server = XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            nm.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());
            return nm;
        }

        private const byte kWriteMode = 2;
        private const byte kReadMode = 1;
        private const byte kEraseExistingMode = 4;
        private static uint s_sessionCounter;
    }
}
