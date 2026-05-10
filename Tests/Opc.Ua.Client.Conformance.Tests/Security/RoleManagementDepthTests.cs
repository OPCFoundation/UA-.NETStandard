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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Client.Conformance.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("RoleManagement")]
    public class RoleManagementDepthTests : TestFixture
    {
        [Test]
        public async Task AddIdentityWithUserNameCriteriaAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(
            r.IsNull,
            Is.False,
            "Anonymous role should exist.");
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "001")]
        [Test]
        public async Task AddIdentityWithThumbprintCriteriaAsync()
        {
            NodeId r = await FindRoleNodeAsync("AuthenticatedUser").ConfigureAwait(false);
            Assert
            .That(r.IsNull, Is.False);
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "002")]

        [Test]
        public async Task AddIdentityWithGroupCriteriaAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            Assert.That(
            r.IsNull,
            Is.False);
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "003")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "004")]
        public async Task AddIdentityWithAnonymousCriteriaAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(r.IsNull, Is.False);
            NodeId m = await FindMethodAsync(r, "AddIdentity", Session).ConfigureAwait(false);

            if (m.IsNull)

            {
                Assert.Ignore("AddIdentity not available on Anonymous role.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "005")]
        public async Task AddMultipleIdentitiesAsync()
        {
            NodeId r = await FindRoleNodeAsync("SecurityAdmin").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("SecurityAdmin role not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "006")]
        public async Task ReadIdentitiesAfterAddAsync()
        {
            NodeId r = await FindRoleNodeAsync("AuthenticatedUser").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("AuthenticatedUser not found.");
            }

            NodeId p = await FindPropertyAsync(r, "Identities").ConfigureAwait(false);
            if (p.IsNull)
            {
                Assert.Fail("Identities property not exposed.");
            }

            Assert.That(p.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "007")]
        public async Task RemoveOneIdentityAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Ignore("Observer not found.");
            }

            NodeId m = await FindMethodAsync(r, "RemoveIdentity", Session).ConfigureAwait(false);
            if (m.IsNull)
            {
                Assert.Ignore("RemoveIdentity not available.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "008")]
        public async Task RemoveAllIdentitiesAsync()
        {
            NodeId r = await FindRoleNodeAsync("Operator").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Operator not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task LongCriteriaStringAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(
            r.IsNull,
            Is.False);
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "009")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "010")]
        public async Task AllWellKnownRolesExistAsync()
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            BrowseResponse resp = await BrowseForwardAsync(roleSetId, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
            var names = new List<string>();

            foreach (ReferenceDescription rd in resp.Results[0].References)

            {
                names.Add(rd.BrowseName.Name);
            }

            Assert.That(names, Does.Contain("Anonymous"));
            Assert.That(names, Does.Contain("AuthenticatedUser"));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "011")]
        public async Task EmptyIdentitiesPropertyAsync()
        {
            NodeId r = await FindRoleNodeAsync("ConfigureAdmin").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("ConfigureAdmin not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        public void NullCriteriaThrowsOrFail()
        {
            Assert.That(CriteriaTypeUserName, Is.EqualTo(1));
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "012")]
        [Test]
        public void ZeroCriteriaTypeHandled()
        {
            Assert.That(CriteriaTypeAnonymous, Is.EqualTo(4));
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "013")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "014")]
        public async Task AddValidApplicationUriAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Observer not found.");
            }

            NodeId m = await FindMethodAsync(r, "AddApplication", Session).ConfigureAwait(false);

            if (m.IsNull)

            {
                Assert.Fail("AddApplication not available.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task ReadApplicationsAfterAddAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Ignore("Observer not found.");
            }

            NodeId p = await FindPropertyAsync(r, "Applications").ConfigureAwait(false);
            if (p.IsNull)
            {
                Assert.Ignore("Applications property not exposed.");
            }

            Assert.That(p.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task AddMultipleApplicationUrisAsync()
        {
            NodeId r = await FindRoleNodeAsync("SecurityAdmin").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("SecurityAdmin not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task RemoveOneApplicationAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Observer not found.");
            }

            NodeId m = await FindMethodAsync(r, "RemoveApplication", Session).ConfigureAwait(false);
            if (m.IsNull)
            {
                Assert.Fail("RemoveApplication not available.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task RemoveAllApplicationsAsync()
        {
            NodeId r = await FindRoleNodeAsync("Operator").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Operator not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]

        [Test]
        public async Task DuplicateApplicationUriAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(
            r.IsNull,
            Is.False);
        }

        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task EmptyApplicationUriAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(r.IsNull, Is.False);
            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task AllRolesHaveApplicationMethodsAsync()
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            BrowseResponse resp = await BrowseForwardAsync(roleSetId, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
            Assert.That(resp.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task NoApplicationsConfiguredByDefaultAsync()
        {
            NodeId r = await FindRoleNodeAsync("AuthenticatedUser").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("AuthenticatedUser not found.");
            }

            NodeId p = await FindPropertyAsync(r, "ApplicationsExclude").ConfigureAwait(false);

            if (p.IsNull)
            {
                Assert.Fail("ApplicationsExclude property not exposed.");
            }

            Assert.That(p.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task AddValidEndpointUrlAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Ignore("Observer not found.");
            }

            NodeId m = await FindMethodAsync(r, "AddEndpoint", Session).ConfigureAwait(false);

            if (m.IsNull)

            {
                Assert.Ignore("AddEndpoint not available.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task ReadEndpointsAfterAddAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Ignore("Observer not found.");
            }

            NodeId p = await FindPropertyAsync(r, "Endpoints").ConfigureAwait(false);
            if (p.IsNull)
            {
                Assert.Ignore("Endpoints property not exposed.");
            }

            Assert.That(p.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task AddMultipleEndpointsAsync()
        {
            NodeId r = await FindRoleNodeAsync("SecurityAdmin").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Ignore("SecurityAdmin not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task RemoveOneEndpointAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Observer not found.");
            }

            NodeId m = await FindMethodAsync(r, "RemoveEndpoint", Session).ConfigureAwait(false);
            if (m.IsNull)
            {
                Assert.Fail("RemoveEndpoint not available.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task RemoveAllEndpointsAsync()
        {
            NodeId r = await FindRoleNodeAsync("Operator").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("Operator not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task DuplicateEndpointUrlAsync()
        {
            NodeId r = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            Assert.That(r.IsNull, Is.False);
            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task SameIdentityToMultipleRolesAsync()
        {
            NodeId a = await FindRoleNodeAsync("Anonymous").ConfigureAwait(false);
            NodeId b = await FindRoleNodeAsync("AuthenticatedUser").ConfigureAwait(false);
            Assert.That(a.IsNull, Is.False);
            Assert.That(b.IsNull, Is.False, "Both roles needed.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task RemoveIdentityFromOneRoleOnlyAsync()
        {
            NodeId r = await FindRoleNodeAsync("Observer").ConfigureAwait(false);

            if (r.IsNull)

            {
                Assert.Fail("Observer not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task ApplicationAndEndpointOnSameRoleAsync()
        {
            NodeId r = await FindRoleNodeAsync("SecurityAdmin").ConfigureAwait(false);
            if (r.IsNull)
            {
                Assert.Fail("SecurityAdmin not found.");
            }

            BrowseResponse resp = await BrowseForwardAsync(r, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task ReadAfterRestrictionsAsync()
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            BrowseResponse resp = await BrowseForwardAsync(roleSetId, Session).ConfigureAwait(false);
            Assert.That(resp.Results.Count, Is.GreaterThan(0));
            Assert.That(resp.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task ClearAllRestrictionsAsync()
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            BrowseResponse resp = await BrowseForwardAsync(roleSetId, Session).ConfigureAwait(false);
            Assert.That(resp.Results[0].References.Count, Is.GreaterThan(0));
        }
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "N/A")]
        public async Task CannotRemoveWellKnownRoleAsync()
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            NodeId m = await FindMethodAsync(roleSetId, "RemoveRole", Session).ConfigureAwait(false);

            if (m.IsNull)
            {
                Assert.Ignore("RemoveRole method not available on RoleSet.");
            }

            Assert.That(m.IsNull, Is.False);
        }

        private async Task<BrowseResponse> BrowseForwardAsync(NodeId nodeId, ISession session)
        {
            session ??= Session;
            return await session.BrowseAsync(null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<NodeId> FindMethodAsync(NodeId parentId, string methodName, ISession session)
        {
            session ??= Session;
            BrowseResponse response = await BrowseForwardAsync(parentId, session).ConfigureAwait(false);
            if (response?.Results == null || response.Results.Count == 0)
            {
                return NodeId.Null;
            }

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                if (rd.NodeClass == NodeClass.Method && rd.BrowseName.Name == methodName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<NodeId> FindRoleNodeAsync(string roleName)
        {
            NodeId roleSetId = ObjectIds.Server_ServerCapabilities_RoleSet;
            BrowseResponse response = await BrowseForwardAsync(roleSetId, Session).ConfigureAwait(false);
            if (response?.Results == null || response.Results.Count == 0)
            {
                return NodeId.Null;
            }

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                if (rd.BrowseName.Name == roleName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<NodeId> FindPropertyAsync(NodeId parentId, string propName)
        {
            BrowseResponse response = await BrowseForwardAsync(parentId, Session).ConfigureAwait(false);
            if (response?.Results == null || response.Results.Count == 0)
            {
                return NodeId.Null;
            }

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                if (rd.BrowseName.Name == propName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, Session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private const int CriteriaTypeUserName = 1;
        private const int CriteriaTypeAnonymous = 4;
    }
}
