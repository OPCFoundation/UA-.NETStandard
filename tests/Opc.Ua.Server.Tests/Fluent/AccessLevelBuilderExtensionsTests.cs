// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="AccessLevelBuilderExtensions"/> — the fluent
    /// <c>Writable()</c> helper.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class AccessLevelBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Variable,
            BaseObjectState NonVariable)
            CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var variable = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root.Value", kNs),
                BrowseName = new QualifiedName("Value", kNs),
                DisplayName = new LocalizedText("Value"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            root.AddChild(variable);

            var nonVar = new BaseObjectState(root)
            {
                NodeId = new NodeId("Root.Folder", kNs),
                BrowseName = new QualifiedName("Folder", kNs),
                DisplayName = new LocalizedText("Folder")
            };
            root.AddChild(nonVar);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [variable.NodeId] = variable,
                [nonVar.NodeId] = nonVar
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: FluentTestNodeManager.Create(kNs),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, variable, nonVar);
        }

        [Test]
        public void WritableGrantsCurrentWriteBit()
        {
            (NodeManagerBuilder b, BaseDataVariableState v, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root.Value", kNs));

            INodeBuilder chain = nb.Writable();

            Assert.That(chain, Is.SameAs(nb));
            Assert.That(
                v.AccessLevel & AccessLevels.CurrentWrite,
                Is.EqualTo(AccessLevels.CurrentWrite));
            Assert.That(
                v.UserAccessLevel & AccessLevels.CurrentWrite,
                Is.EqualTo(AccessLevels.CurrentWrite));
        }

        [Test]
        public void WritableFalseClearsCurrentWriteBit()
        {
            (NodeManagerBuilder b, BaseDataVariableState v, _) = CreateBuilder();
            v.AccessLevel = (byte)(AccessLevels.CurrentRead | AccessLevels.CurrentWrite);
            v.UserAccessLevel = (byte)(AccessLevels.CurrentRead | AccessLevels.CurrentWrite);
            INodeBuilder nb = b.Node(new NodeId("Root.Value", kNs));

            nb.Writable(false);

            Assert.That(v.AccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            Assert.That(v.UserAccessLevel & AccessLevels.CurrentWrite, Is.Zero);
            Assert.That(
                v.AccessLevel & AccessLevels.CurrentRead,
                Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public void WritableOnNonVariableThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root.Folder", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.Writable())!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void WritableTypedBuilderPreservesTypedView()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder<BaseDataVariableState> typed =
                b.Node<BaseDataVariableState>(new NodeId("Root.Value", kNs));

            INodeBuilder<BaseDataVariableState> chain = typed.Writable();

            Assert.That(chain, Is.SameAs(typed));
        }
    }
}
