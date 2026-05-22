/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;
namespace Opc.Ua.Conformance.Tests.Security
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityRoleServerEventing")]
    public class SecurityRoleServerEventingTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security Role Server Base Eventing")]
        [Property("Tag", "001")]
        public async Task Eventing001RoleMappingRuleChangedAuditEventAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId roleSetId = ToNodeId(
                    ObjectIds.Server_ServerCapabilities_RoleSet);

                NodeId addRoleMethod = await FindMethodAsync(
                    roleSetId, "AddRole", adminSession)
                    .ConfigureAwait(false);
                if (addRoleMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddRole method not found. " +
                        "Feature not supported by server.");
                }

                // Attempt to add a test role to trigger audit event
                try
                {
                    CallMethodResult result =
                        await CallRoleMethodAsync(
                            adminSession, roleSetId,
                            addRoleMethod,
                            new Variant("TestAuditRole"),
                            new Variant(
                                "http://test.org/audit"))
                        .ConfigureAwait(false);

                    IgnoreIfRoleMethodNotSupported(result.StatusCode);

                    Assert.That(
                        StatusCode.IsGood(result.StatusCode),
                        Is.True,
                        "AddRole should succeed to " +
                        "trigger audit event.");

                    // Cleanup: remove the test role
                    NodeId removeRoleMethod =
                        await FindMethodAsync(
                            roleSetId, "RemoveRole",
                            adminSession)
                        .ConfigureAwait(false);
                    if (!removeRoleMethod.IsNull &&
                        result.OutputArguments.Count > 0)
                    {
                        try
                        {
                            await CallRoleMethodAsync(
                                adminSession, roleSetId,
                                removeRoleMethod,
                                result.OutputArguments[0])
                                .ConfigureAwait(false);
                        }
                        catch (ServiceResultException)
                        {
                            // best-effort cleanup
                        }
                    }
                }
                catch (ServiceResultException ex)
                    when (ex.StatusCode ==
                        StatusCodes.BadNotSupported)
                {
                    Assert.Ignore(
                        "AddRole not supported by server.");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base Eventing")]
        [Property("Tag", "002")]
        public async Task Eventing002IdentityChangeAuditEventAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId observerId = ToNodeId(
                    ObjectIds.WellKnownRole_Observer);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddIdentity method not found.");
                }

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "testAuditUser");

                CallMethodResult result =
                    await CallRoleMethodAsync(
                        adminSession, observerId, addMethod,
                        new Variant(rule))
                    .ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode),
                    Is.True,
                    "AddIdentity should succeed to " +
                    "trigger audit event.");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    try
                    {
                        await CallRoleMethodAsync(
                            adminSession, observerId,
                            removeMethod,
                            new Variant(rule))
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // best-effort cleanup
                    }
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        private async Task<BrowseResponse> BrowseForwardAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            return await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<NodeId> FindMethodAsync(
            NodeId parentId,
            string methodName,
            ISession session = null)
        {
            BrowseResponse response =
                await BrowseForwardAsync(parentId, session)
                    .ConfigureAwait(false);
            if (response?.Results == null ||
                response.Results.Count == 0)
            {
                return NodeId.Null;
            }

            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                if (rd.NodeClass == NodeClass.Method &&
                    rd.BrowseName.Name == methodName)
                {
                    return ExpandedNodeId.ToNodeId(
                        rd.NodeId,
                        (session ?? Session).NamespaceUris);
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(parentId, methodName);
        }

        private async Task<CallMethodResult> CallRoleMethodAsync(
            ISession session,
            NodeId roleId,
            NodeId methodId,
            params Variant[] args)
        {
            CallResponse callResponse = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = roleId,
                        MethodId = methodId,
                        InputArguments = args.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(callResponse.Results, Is.Not.Null);
            Assert.That(callResponse.Results.Count, Is.EqualTo(1));
            return callResponse.Results[0];
        }

        private ExtensionObject CreateIdentityRule(
            int criteriaType,
            string criteria)
        {
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(
                stream,
                ServiceMessageContext.CreateEmpty(Telemetry),
                true);
            encoder.WriteInt32("CriteriaType", criteriaType);
            encoder.WriteString("Criteria", criteria);
            encoder.Close();
            return new ExtensionObject(
                new NodeId(15634),
                ByteString.From(stream.ToArray()));
        }

        private async Task<ISession> ConnectAsAdminAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync()
                .ConfigureAwait(false);
            string policy = FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Ignore(
                    "No endpoint supports UserName token.");
            }

            return await ClientFixture
                .ConnectAsync(ServerUrl, policy,
                    userIdentity: new UserIdentity(
                        "sysadmin", "demo"u8))
                .ConfigureAwait(false);
        }

        private static string FindPolicyWithUsernameToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            // Prefer SignAndEncrypt, then Sign, then None (admin reads need encryption for AccessRestrictions=3)
            foreach (MessageSecurityMode mode in new[]
            {
                MessageSecurityMode.SignAndEncrypt,
                MessageSecurityMode.Sign,
                MessageSecurityMode.None
            })
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode != mode)
                    {
                        continue;
                    }

                    if (ep.UserIdentityTokens == default)
                    {
                        continue;
                    }

                    foreach (UserTokenPolicy t in
                        ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return ep.SecurityPolicyUri;
                        }
                    }
                }
            }

            return null;
        }

        private const int CriteriaTypeUserName = 1;

        private async Task<ArrayOf<EndpointDescription>>
            GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
