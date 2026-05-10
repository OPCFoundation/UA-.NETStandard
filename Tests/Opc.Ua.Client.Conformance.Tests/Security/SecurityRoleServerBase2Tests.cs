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
    [Category("SecurityRoleServerBase2")]
    public class SecurityRoleServerBase2Tests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "001")]
        public async Task Base2VerifyNamespacesObject001Async()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool foundNamespaceMetadata = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                if (rd.TypeDefinition.ToString().Contains("11616") ||
                    rd.BrowseName.Name.Contains("Namespace"))
                {
                    foundNamespaceMetadata = true;
                    break;
                }
            }

            Assert.That(foundNamespaceMetadata, Is.True,
                "Namespaces folder should contain " +
                "NamespaceMetadataType instances.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "002")]
        public async Task Base2VerifyNamespaceMetadataInstance002Async()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));
            Assert.That(
                response.Results[0].References.Count,
                Is.GreaterThan(0),
                "At least one NamespaceMetadata instance " +
                "should exist.");

            ReferenceDescription firstNs =
                response.Results[0].References[0];
            var nsNodeId = ExpandedNodeId.ToNodeId(
                firstNs.NodeId, Session.NamespaceUris);

            BrowseResponse nsChildren =
                await BrowseForwardAsync(nsNodeId)
                    .ConfigureAwait(false);

            Assert.That(nsChildren.Results, Is.Not.Null);
            Assert.That(nsChildren.Results.Count,
                Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "004")]
        public async Task Base2DefaultRolePermissions004Async()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

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
                    DataValue dv = await ReadPropertyValueAsync(
                        propId).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                    return;
                }
            }

            Assert.Fail(
                "No namespace exposes " +
                "DefaultRolePermissions property.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "005")]
        public async Task Base2DefaultUserRolePermissions005Async()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var nsNodeId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                NodeId propId = await FindChildAsync(
                    nsNodeId, "DefaultUserRolePermissions")
                    .ConfigureAwait(false);
                if (!propId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        propId).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                    return;
                }
            }

            Assert.Fail(
                "No namespace exposes " +
                "DefaultUserRolePermissions property.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "006")]
        public async Task Base2DefaultAccessRestrictions006Async()
        {
            NodeId namespacesId = ToNodeId(
                ObjectIds.Server_Namespaces);

            BrowseResponse response =
                await BrowseForwardAsync(namespacesId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var nsNodeId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                NodeId propId = await FindChildAsync(
                    nsNodeId, "DefaultAccessRestrictions")
                    .ConfigureAwait(false);
                if (!propId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        propId).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                    return;
                }
            }

            Assert.Fail(
                "No namespace exposes " +
                "DefaultAccessRestrictions property.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "007")]
        public async Task Base2FindRolePermissions007Async()
        {
            NodeId objectsId = ToNodeId(ObjectIds.ObjectsFolder);

            BrowseResponse response =
                await BrowseForwardAsync(objectsId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool found = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var childId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                DataValue dv = await ReadAttributeAsync(
                    childId, Attributes.RolePermissions)
                    .ConfigureAwait(false);
                if (StatusCode.IsGood(dv.StatusCode) &&
                    dv.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> _))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Assert.Ignore(
                    "No nodes with RolePermissions " +
                    "attribute found in Objects folder.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "008")]
        public async Task Base2FindUserRolePermissions008Async()
        {
            NodeId objectsId = ToNodeId(ObjectIds.ObjectsFolder);

            BrowseResponse response =
                await BrowseForwardAsync(objectsId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool found = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var childId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                DataValue dv = await ReadAttributeAsync(
                    childId,
                    Attributes.UserRolePermissions)
                    .ConfigureAwait(false);
                if (StatusCode.IsGood(dv.StatusCode) &&
                    dv.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> _))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Assert.Ignore(
                    "No nodes with UserRolePermissions " +
                    "attribute found in Objects folder.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Base 2")]
        [Property("Tag", "009")]
        public async Task Base2FindAccessRestrictions009Async()
        {
            NodeId objectsId = ToNodeId(ObjectIds.ObjectsFolder);

            BrowseResponse response =
                await BrowseForwardAsync(objectsId)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool found = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References.ToArray())
            {
                var childId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                DataValue dv = await ReadAttributeAsync(
                    childId,
                    Attributes.AccessRestrictions)
                    .ConfigureAwait(false);
                if (StatusCode.IsGood(dv.StatusCode) &&
                    dv.WrappedValue.TryGetValue(out ushort _))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Assert.Ignore(
                    "No nodes with AccessRestrictions " +
                    "attribute found in Objects folder.");
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
    }
}
