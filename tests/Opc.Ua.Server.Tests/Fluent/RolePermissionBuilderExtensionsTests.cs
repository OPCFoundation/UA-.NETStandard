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

using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

// CA2000: the NodeState instances created here are owned by the builder under
// test for the lifetime of the test method.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the fluent wiring of the RolePermissions and
    /// UserRolePermissions attribute read hooks.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class RolePermissionBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static ServiceResult GrantAnonymous(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            value = [new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Anonymous }];
            return ServiceResult.Good;
        }

        private static ServiceResult GrantObserver(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            value = [new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Observer }];
            return ServiceResult.Good;
        }

        private static (NodeManagerBuilder Builder, MethodState Method) CreateBuilder()
        {
            var ctx = new SystemContext(telemetry: null);

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var method = new MethodState(root)
            {
                NodeId = new NodeId("Root.M1", kNs),
                BrowseName = new QualifiedName("M1", kNs),
                DisplayName = new LocalizedText("M1")
            };
            root.AddChild(method);

            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [method.NodeId] = method
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => q == root.BrowseName ? root : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);

            return (builder, method);
        }

        [Test]
        public void OnReadRolePermissionsAssignsHandlerAndReturnsBuilder()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();
            INodeBuilder nb = b.Node(method.NodeId);

            INodeBuilder returned = nb.OnReadRolePermissions(GrantAnonymous);

            Assert.That(returned, Is.SameAs(nb));
            Assert.That(method.OnReadRolePermissions, Is.Not.Null);

            ArrayOf<RolePermissionType> value = default;
            Assert.That(
                method.OnReadRolePermissions!(b.Context, method, ref value),
                Is.EqualTo(ServiceResult.Good));
            Assert.That(value.Count, Is.EqualTo(1));
            Assert.That(
                value[0].RoleId,
                Is.EqualTo((NodeId)ObjectIds.WellKnownRole_Anonymous));
        }

        [Test]
        public void OnReadUserRolePermissionsAssignsHandler()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();

            b.Node(method.NodeId).OnReadUserRolePermissions(GrantObserver);

            Assert.That(method.OnReadUserRolePermissions, Is.Not.Null);
            Assert.That(method.OnReadRolePermissions, Is.Null);
        }

        [Test]
        public void TypedOverloadsPreserveTypedBuilder()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();

            INodeBuilder<MethodState> typed = b
                .Node<MethodState>(method.NodeId)
                .OnReadRolePermissions(GrantAnonymous)
                .OnReadUserRolePermissions(GrantAnonymous);

            Assert.That(typed.Node, Is.SameAs(method));
            Assert.That(method.OnReadRolePermissions, Is.Not.Null);
            Assert.That(method.OnReadUserRolePermissions, Is.Not.Null);
        }

        [Test]
        public void SecondDifferentHandlerThrowsBadConfigurationError()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();
            b.Node(method.NodeId).OnReadRolePermissions(GrantAnonymous);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => b.Node(method.NodeId).OnReadRolePermissions(GrantObserver))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void ReassigningTheSameHandlerIsAllowed()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();

            b.Node(method.NodeId).OnReadRolePermissions(GrantAnonymous);
            Assert.DoesNotThrow(
                () => b.Node(method.NodeId).OnReadRolePermissions(GrantAnonymous));

            b.Node(method.NodeId).OnReadUserRolePermissions(GrantAnonymous);
            Assert.DoesNotThrow(
                () => b.Node(method.NodeId).OnReadUserRolePermissions(GrantAnonymous));
        }

        [Test]
        public void NullArgumentsThrow()
        {
            (NodeManagerBuilder b, MethodState method) = CreateBuilder();
            INodeBuilder nb = b.Node(method.NodeId);

            Assert.Throws<ArgumentNullException>(
                () => nb.OnReadRolePermissions(null!));
            Assert.Throws<ArgumentNullException>(
                () => nb.OnReadUserRolePermissions(null!));
            Assert.Throws<ArgumentNullException>(
                () => ((INodeBuilder)null!).OnReadRolePermissions(GrantAnonymous));
            Assert.Throws<ArgumentNullException>(
                () => ((INodeBuilder)null!).OnReadUserRolePermissions(GrantAnonymous));
        }
    }
}
