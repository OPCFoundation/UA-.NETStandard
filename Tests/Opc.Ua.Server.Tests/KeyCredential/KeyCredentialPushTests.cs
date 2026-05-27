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

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.KeyCredential
{
    [TestFixture]
    [Category("KeyCredential")]
    public class KeyCredentialPushTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        [Test]
        public async Task UpdateCredentialThenDeleteCredentialUpdatesStore()
        {
            using var store = new InMemoryKeyCredentialStore();
            var subject = new KeyCredentialPushSubject(store);
            KeyCredentialConfigurationFolderState folder = CreateFolder();
            ISystemContext context = CreateAdminContext();
            await subject.BindAsync(folder, context).ConfigureAwait(false);

            KeyCredentialConfigurationState credentialNode = await CreateCredentialNodeAsync(folder, context)
                .ConfigureAwait(false);
            byte[] secret = [1, 2, 3, 4];
            KeyCredentialUpdateMethodStateResult updateResult = await credentialNode.UpdateCredential.OnCallAsync(
                    context,
                    credentialNode.UpdateCredential,
                    credentialNode.NodeId,
                    "credential-1",
                    ByteString.From(secret),
                    "thumbprint",
                    SecurityPolicies.Basic256Sha256,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(updateResult.ServiceResult), Is.True);
            Opc.Ua.Server.KeyCredential stored = await store.GetAsync("credential-1", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.Secret, Is.EqualTo(secret));

            ServiceResult deleteResult = await credentialNode.DeleteCredential.OnCallMethod2Async(
                    context,
                    credentialNode.DeleteCredential,
                    credentialNode.NodeId,
                    [],
                    [],
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(deleteResult), Is.True);
            Assert.That(await store.GetAsync("credential-1", CancellationToken.None).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task UpdateCredentialRejectsUnauthorizedCaller()
        {
            using var store = new InMemoryKeyCredentialStore();
            var subject = new KeyCredentialPushSubject(store);
            KeyCredentialConfigurationFolderState folder = CreateFolder();
            ISystemContext adminContext = CreateAdminContext();
            await subject.BindAsync(folder, adminContext).ConfigureAwait(false);
            KeyCredentialConfigurationState credentialNode = await CreateCredentialNodeAsync(folder, adminContext)
                .ConfigureAwait(false);

            KeyCredentialUpdateMethodStateResult result = await credentialNode.UpdateCredential.OnCallAsync(
                    CreateAnonymousContext(),
                    credentialNode.UpdateCredential,
                    credentialNode.NodeId,
                    "credential-1",
                    ByteString.From([1, 2, 3, 4]),
                    string.Empty,
                    SecurityPolicies.Basic256Sha256,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task BindAddsBrowseChildrenForStoredCredentials()
        {
            using var store = new InMemoryKeyCredentialStore();
            await store.UpdateAsync(
                    "browse-credential",
                    new Opc.Ua.Server.KeyCredential([9, 8, 7], DateTime.UtcNow.AddHours(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);
            var subject = new KeyCredentialPushSubject(store);
            KeyCredentialConfigurationFolderState folder = CreateFolder();
            ISystemContext context = CreateAdminContext();

            await subject.BindAsync(folder, context).ConfigureAwait(false);

            IList<BaseInstanceState> children = [];
            folder.GetChildren(context, children);
            Assert.That(children.OfType<KeyCredentialConfigurationState>()
                .Any(child => child.CredentialId.Value == "browse-credential"), Is.True);
        }

        private static async Task<KeyCredentialConfigurationState> CreateCredentialNodeAsync(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context)
        {
            CreateCredentialMethodStateResult createResult = await folder.CreateCredential.OnCallAsync(
                    context,
                    folder.CreateCredential,
                    folder.NodeId,
                    "ServiceA",
                    "urn:test:resource",
                    KeyCredentialBridgeOptions.DefaultProfileUri,
                    [],
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(createResult.ServiceResult), Is.True);

            IList<BaseInstanceState> children = [];
            folder.GetChildren(context, children);
            return children.OfType<KeyCredentialConfigurationState>()
                .Single(child => child.NodeId == createResult.CredentialNodeId);
        }

        private static KeyCredentialConfigurationFolderState CreateFolder()
        {
            return new KeyCredentialConfigurationFolderState(null)
            {
                NodeId = KeyCredentialPushSubject.StandardConfigurationFolderNodeId,
                BrowseName = new QualifiedName("KeyCredentialConfiguration"),
                DisplayName = LocalizedText.From("KeyCredentialConfiguration"),
                TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationFolderType
            };
        }

        private static SessionSystemContext CreateAdminContext()
        {
            return CreateContext(UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);
        }

        private static SessionSystemContext CreateAnonymousContext()
        {
            return CreateContext(UserTokenType.Anonymous);
        }

        private static SessionSystemContext CreateContext(UserTokenType tokenType, params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(tokenType.ToString());
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }
    }
}
