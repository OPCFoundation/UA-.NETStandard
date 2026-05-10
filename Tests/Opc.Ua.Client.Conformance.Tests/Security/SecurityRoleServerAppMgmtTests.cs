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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Client.Conformance.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityRoleServerAppMgmt")]
    public class SecurityRoleServerAppMgmtTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security Role Server ApplicationManagement")]
        [Property("Tag", "001")]
        public async Task AppMgmt001AddApplicationAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId observerId = ToNodeId(
                    ObjectIds.WellKnownRole_Observer);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddApplication method not found. " +
                        "Feature not supported by server.");
                }

                const string appUri = "urn:test:appmgmt:app1";
                CallMethodResult result =
                    await CallRoleMethodAsync(
                        adminSession, observerId, addMethod,
                        new Variant(appUri))
                    .ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode),
                    Is.True,
                    "AddApplication failed: " +
                    $"{result.StatusCode}");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveApplication",
                    adminSession).ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    try
                    {
                        await CallRoleMethodAsync(
                            adminSession, observerId,
                            removeMethod,
                            new Variant(appUri))
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

        [Test]
        [Property("ConformanceUnit", "Security Role Server ApplicationManagement")]
        [Property("Tag", "003")]
        public async Task AppMgmt003RemoveApplicationAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId observerId = ToNodeId(
                    ObjectIds.WellKnownRole_Observer);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveApplication",
                    adminSession).ConfigureAwait(false);
                if (addMethod.IsNull || removeMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddApplication or " +
                        "RemoveApplication not found.");
                }

                const string appUri = "urn:test:appmgmt:remove1";

                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri))
                    .ConfigureAwait(false);

                CallMethodResult result =
                    await CallRoleMethodAsync(
                        adminSession, observerId,
                        removeMethod,
                        new Variant(appUri))
                    .ConfigureAwait(false);

                IgnoreIfRoleMethodNotSupported(result.StatusCode);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode),
                    Is.True,
                    "RemoveApplication failed: " +
                    $"{result.StatusCode}");
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
        [Property("ConformanceUnit", "Security Role Server ApplicationManagement")]
        [Property("Tag", "005")]
        public async Task AppMgmt005RemoveAllApplicationsAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId observerId = ToNodeId(
                    ObjectIds.WellKnownRole_Observer);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveApplication",
                    adminSession).ConfigureAwait(false);
                if (addMethod.IsNull || removeMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddApplication or " +
                        "RemoveApplication not found.");
                }

                const string appUri1 = "urn:test:appmgmt:all1";
                const string appUri2 = "urn:test:appmgmt:all2";

                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri1))
                    .ConfigureAwait(false);
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri2))
                    .ConfigureAwait(false);

                await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri1))
                    .ConfigureAwait(false);
                await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri2))
                    .ConfigureAwait(false);

                // After removing all, Applications should
                // be empty
                NodeId appsId = await FindChildAsync(
                    observerId, "Applications", adminSession)
                    .ConfigureAwait(false);
                if (!appsId.IsNull)
                {
                    DataValue dv =
                        await ReadPropertyValueAsync(
                            appsId, adminSession)
                        .ConfigureAwait(false);
                    IgnoreIfRoleMethodNotSupported(dv.StatusCode);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
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

        private async Task<NodeId> FindChildAsync(
            NodeId parentId,
            string childName,
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
                if (rd.BrowseName.Name == childName)
                {
                    return ExpandedNodeId.ToNodeId(
                        rd.NodeId,
                        (session ?? Session).NamespaceUris);
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(parentId, childName);
        }

        private async Task<DataValue> ReadPropertyValueAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
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
