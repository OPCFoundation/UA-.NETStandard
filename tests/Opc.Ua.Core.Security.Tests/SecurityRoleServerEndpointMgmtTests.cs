// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityRoleServerEndpointMgmt")]
    public class SecurityRoleServerEndpointMgmtTests : TestFixture
    {
        [Test]
        public async Task EndpointMgmt001AddEndpointAsync()
        {
            ISession? adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId observerId = ToNodeId(
                    ObjectIds.WellKnownRole_Observer);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);
                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddEndpoint method not found. " +
                        "Feature not supported by server.");
                }

                const string url = "opc.tcp://endpointmgmt:4840";
                CallMethodResult result =
                    await CallRoleMethodAsync(
                        adminSession, observerId, addMethod,
                        new Variant(CreateEndpoint(url)))
                    .ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode),
                    Is.True,
                    "AddEndpoint failed: " +
                    $"{result.StatusCode}");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveEndpoint",
                    adminSession).ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    try
                    {
                        await CallRoleMethodAsync(
                            adminSession, observerId,
                            removeMethod,
                            new Variant(CreateEndpoint(url)))
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
            ISession? session = null)
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
            ISession? session = null)
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

        private static ExtensionObject CreateEndpoint(string url,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            string policyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
            string transportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary")
        {
            return new ExtensionObject(new EndpointType
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = policyUri,
                TransportProfileUri = transportProfileUri
            });
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

                    if (ep.UserIdentityTokens == default!)
                    {
                        continue;
                    }

                    foreach (UserTokenPolicy t in
                        ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return ep.SecurityPolicyUri!;
                        }
                    }
                }
            }

            return null!;
        }

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
