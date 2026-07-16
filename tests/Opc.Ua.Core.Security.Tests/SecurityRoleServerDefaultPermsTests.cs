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
using Opc.Ua.Client.TestFramework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityRoleServerDefaultPerms")]
    public class SecurityRoleServerDefaultPermsTests : TestFixture
    {
        [Test]
        public async Task DefaultPerms001CheckConfiguration()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count,
                Is.GreaterThan(0));

            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var nsNodeId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                NodeId propId = await FindChildAsync(
                    nsNodeId, "DefaultRolePermissions")
                    .ConfigureAwait(false);
                if (!propId.IsNull)
                {
                    DataValue dv =
                        await ReadPropertyValueAsync(propId)
                        .ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                    Assert.That(
                        dv.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> _), Is.True,
                        "DefaultRolePermissions should " +
                        "have a configured value.");
                    return;
                }
            }

            Assert.Fail(
                "No namespace exposes " +
                "DefaultRolePermissions property.");
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
    }
}
