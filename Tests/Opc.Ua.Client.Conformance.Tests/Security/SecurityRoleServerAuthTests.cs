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
    [Category("SecurityRoleServerAuth")]
    public class SecurityRoleServerAuthTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security Role Server Authorization")]
        [Property("Tag", "001")]
        public async Task Auth001RestrictAccessByRoleAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                // Read RolePermissions attribute on a well-known
                // node to confirm role-based access is configured
                NodeId serverNode = ToNodeId(
                    ObjectIds.Server);
                DataValue dv = await ReadAttributeAsync(
                    serverNode, Attributes.RolePermissions,
                    adminSession).ConfigureAwait(false);

                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore(
                        "Server does not expose " +
                        "RolePermissions on Server node.");
                }

                Assert.That(
                    dv.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> _), Is.True,
                    "RolePermissions should have a value.");
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
        [Property("ConformanceUnit", "Security Role Server Authorization")]
        [Property("Tag", "002")]
        public async Task Auth002UnmappedUserCannotLoginAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync()
                .ConfigureAwait(false);
            string policy =
                FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Fail(
                    "No endpoint supports UserName token.");
            }

            ServiceResultException ex =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () =>
                    {
                        using ISession session =
                            await OpenAuxSessionAsync(
                                securityProfile: policy,
                                userIdentity: new UserIdentity(
                                    "unmapped_user_xyz",
                                    "badpassword"u8))
                            .ConfigureAwait(false);
                    });

            Assert.That(ex.StatusCode,
                Is.AnyOf(
                    StatusCodes.BadUserAccessDenied,
                    StatusCodes.BadIdentityTokenRejected));
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId,
            uint attributeId,
            ISession session = null)
        {
            session ??= Session;
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
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
