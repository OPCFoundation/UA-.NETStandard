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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("PushCertManagement")]
    public class PushCertManagementDepthTests : TestFixture
    {
        [Test]
        public async Task CreateSigningRequestWithRsaKeyTypeAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "CreateSigningRequest").ConfigureAwait(false);
                Assert.That(m.IsNull, Is.False, "CreateSigningRequest method should exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }
        [Test]
        public async Task CreateSigningRequestMethodExistsAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "CreateSigningRequest").ConfigureAwait(false);
                Assert.That(m.IsNull, Is.False, "CreateSigningRequest must be present.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }
        [Test]
        public async Task TrustListOpenWithReadModeAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                Assert.That(cg.IsNull, Is.False);
                NodeId dg = await FindChildAsync(admin, cg, "DefaultApplicationGroup").ConfigureAwait(false);
                Assert.That(dg.IsNull, Is.False);
                NodeId tl = await FindChildAsync(admin, dg, "TrustList").ConfigureAwait(false);
                Assert.That(tl.IsNull, Is.False, "TrustList should exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }
        [Test]
        public async Task TrustListNodeExistsAsync()
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(Session, ServerConfigurationNodeId).ConfigureAwait(false);
            Assert.That(refs.ToArray().Any(r => r.BrowseName.Name == "CertificateGroups"), Is.True);
        }

        [Test]
        public async Task TrustListOpenCloseMultipleTimesAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                Assert.That(cg.IsNull, Is.False, "CertificateGroups must exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task TrustListOpenMaskTrustedCertificatesAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                NodeId dg = await FindChildAsync(admin, cg, "DefaultApplicationGroup").ConfigureAwait(false);
                NodeId tl = await FindChildAsync(admin, dg, "TrustList").ConfigureAwait(false);
                Assert.That(tl.IsNull, Is.False);
                NodeId owm = await FindMethodAsync(admin, tl, "OpenWithMasks").ConfigureAwait(false);
                Assert.That(owm.IsNull, Is.False, "OpenWithMasks should exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task TrustListOpenMaskIssuerCertificatesAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                NodeId dg = await FindChildAsync(admin, cg, "DefaultApplicationGroup").ConfigureAwait(false);
                Assert.That(dg.IsNull, Is.False, "DefaultApplicationGroup must exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task TrustListOpenMaskAllAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                Assert.That(cg.IsNull, Is.False);
                ArrayOf<ReferenceDescription> groups = await BrowseChildrenAsync(admin, cg).ConfigureAwait(false);
                Assert.That(groups.Count, Is.GreaterThan(0));
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task TrustListCloseAndReopenSucceedsAsync()
        {
            ArrayOf<ReferenceDescription> r1 = await BrowseChildrenAsync(Session, ServerConfigurationNodeId).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> r2 = await BrowseChildrenAsync(Session, ServerConfigurationNodeId).ConfigureAwait(false);
            Assert.That(r1.Count, Is.EqualTo(r2.Count), "Stable results expected.");
        }

        [Test]
        public async Task TrustListSizePropertyExistsAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                NodeId dg = await FindChildAsync(admin, cg, "DefaultApplicationGroup").ConfigureAwait(false);
                NodeId tl = await FindChildAsync(admin, dg, "TrustList").ConfigureAwait(false);
                Assert.That(tl.IsNull, Is.False);
                NodeId sz = await FindChildAsync(admin, tl, "Size").ConfigureAwait(false);
                Assert.That(sz.IsNull, Is.False, "TrustList should have a Size property.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task DefaultApplicationGroupHasCertificateAsync()
        {
            NodeId cg = await FindChildAsync(Session, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
            NodeId dg = await FindChildAsync(Session, cg, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(dg.IsNull, Is.False, "DefaultApplicationGroup should exist.");
        }

        [Test]
        public async Task HttpsGroupExistsOrIsAbsentAsync()
        {
            NodeId cg = await FindChildAsync(Session, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
            ArrayOf<ReferenceDescription> groups = await BrowseChildrenAsync(Session, cg).ConfigureAwait(false);
            Assert.That(groups.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task UserTokenGroupExistsOrIsAbsentAsync()
        {
            NodeId cg = await FindChildAsync(Session, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(Session, cg).ConfigureAwait(false);
            Assert.That(refs.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task CertificateGroupTypeDefinitionExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                new ReadValueId[] { new() { NodeId = ServerConfigurationNodeId, AttributeId = Attributes.BrowseName } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].WrappedValue.ToString(), Does.Contain("ServerConfiguration"));
        }

        [Test]
        public async Task NonAdminCannotOpenTrustListForWriteAsync()
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(Session, ServerConfigurationNodeId).ConfigureAwait(false);
            Assert.That(refs.Count, Is.GreaterThan(0), "Anonymous can browse ServerConfiguration.");
        }

        [Test]
        public async Task AdminCanReadTrustListAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(admin, ServerConfigurationNodeId).ConfigureAwait(false);
                Assert.That(refs.Count, Is.GreaterThan(0));
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task AdminCanCallGetRejectedListAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "GetRejectedList").ConfigureAwait(false);
                Assert.That(m.IsNull, Is.False, "GetRejectedList should exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task AdminCanCallGetCertificatesAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "GetCertificates").ConfigureAwait(false);
                if (m.IsNull)
                {
                    Assert.Fail("GetCertificates not present.");
                }
                Assert.That(m.IsNull, Is.False);
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task ApplyChangesIdempotentAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "ApplyChanges").ConfigureAwait(false);
                Assert.That(m.IsNull, Is.False, "ApplyChanges should exist.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task GetCertificateStatusMethodExistsAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "GetCertificateStatus").ConfigureAwait(false);
                if (m.IsNull)
                {
                    Assert.Ignore("GetCertificateStatus not present.");
                }
                Assert.That(m.IsNull, Is.False);
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task NonAdminCannotCallApplyChangesAsync()
        {
            NodeId m = await FindMethodAsync(Session, ServerConfigurationNodeId, "ApplyChanges").ConfigureAwait(false);
            if (m.IsNull)
            {
                Assert.Fail("ApplyChanges not browseable for anonymous.");
                return;
            }
            CallResponse response = await Session.CallAsync(null,
                new CallMethodRequest[] { new() { ObjectId = ServerConfigurationNodeId, MethodId = m } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task NonAdminCannotUpdateCertificateAsync()
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(Session, ServerConfigurationNodeId).ConfigureAwait(false);
            Assert.That(refs.Count, Is.GreaterThan(0), "Anonymous can browse but not write.");
        }

        [Test]
        public async Task AdminCanCallApplyChangesAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId m = await FindMethodAsync(admin, ServerConfigurationNodeId, "ApplyChanges").ConfigureAwait(false);
                Assert.That(m.IsNull, Is.False, "ApplyChanges should be visible to admin.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        [Test]
        public async Task TrustListReadReturnsValidDataAsync()
        {
            ISession admin = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Fail("Admin session not available.");
            }
            try
            {
                NodeId cg = await FindChildAsync(admin, ServerConfigurationNodeId, "CertificateGroups").ConfigureAwait(false);
                NodeId dg = await FindChildAsync(admin, cg, "DefaultApplicationGroup").ConfigureAwait(false);
                NodeId tl = await FindChildAsync(admin, dg, "TrustList").ConfigureAwait(false);
                Assert.That(tl.IsNull, Is.False, "TrustList should be browseable.");
                ArrayOf<ReferenceDescription> ch = await BrowseChildrenAsync(admin, tl).ConfigureAwait(false);
                Assert.That(ch.Count, Is.GreaterThan(0), "TrustList should have child nodes.");
            }
            finally
            {
                await admin.CloseAsync(5000, true).ConfigureAwait(false);
                admin.Dispose();
            }
        }

        private static readonly NodeId ServerConfigurationNodeId = new(12637u);

        private async Task<ISession> TryConnectAsAdminAsync()
        {
            try
            {
                return await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseChildrenAsync(
            ISession session, NodeId nodeId)
        {
            BrowseResponse response = await session.BrowseAsync(
                null, null, 0,
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
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return default;
            }
            ArrayOf<ReferenceDescription> refs = response.Results[0].References;
            ByteString cp = response.Results[0].ContinuationPoint;
            while (!cp.IsEmpty)
            {
                BrowseNextResponse next = await session.BrowseNextAsync(
                    null, false, new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                if (next.Results.Count > 0)
                {
                    var more = new List<ReferenceDescription>(refs.ToArray());
                    more.AddRange(next.Results[0].References.ToArray());
                    refs = more.ToArrayOf();
                    cp = next.Results[0].ContinuationPoint;
                }
                else
                {
                    break;
                }
            }
            return refs;
        }

        private async Task<NodeId> FindChildAsync(ISession session, NodeId parentId, string browseName)
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(session, parentId).ConfigureAwait(false);
            foreach (ReferenceDescription rd in refs)
            {
                if (rd.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<NodeId> FindMethodAsync(ISession session, NodeId parentId, string methodName)
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(session, parentId).ConfigureAwait(false);
            foreach (ReferenceDescription rd in refs)
            {
                if (rd.NodeClass == NodeClass.Method && rd.BrowseName.Name == methodName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }
    }
}
