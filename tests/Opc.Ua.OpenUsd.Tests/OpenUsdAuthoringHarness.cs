/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.OpenUsd;

namespace Opc.Ua.OpenUsd.Server.Tests
{
    /// <summary>
    /// A NodeIdFactory that hands each newly authored node a fresh numeric identifier in a
    /// dedicated instance namespace, mirroring what a real NodeManager does while a subtree
    /// is wired up. Nodes that already carry an identifier keep it.
    /// </summary>
    internal sealed class SequentialOpenUsdNodeIdFactory : INodeIdFactory
    {
        private readonly ushort m_namespaceIndex;
        private uint m_next;

        public SequentialOpenUsdNodeIdFactory(ushort namespaceIndex)
        {
            m_namespaceIndex = namespaceIndex;
        }

        public NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId.IsNull ? new NodeId(++m_next, m_namespaceIndex) : node.NodeId;
        }
    }

    /// <summary>
    /// Builds an in-process <see cref="ISystemContext"/> carrying the draft OpenUSD Bindings
    /// companion namespace plus the well-known <c>OpenUSD</c> facility (stages and
    /// representations registries), so the server-side authoring helpers can be exercised
    /// against a real <see cref="NodeState"/> tree without standing up an <c>opc.tcp</c>
    /// server.
    /// </summary>
    internal static class OpenUsdAuthoringHarness
    {
        /// <summary>
        /// A private namespace the instance NodeIds are minted in.
        /// </summary>
        public const string InstanceNamespaceUri = "urn:opcfoundation:openusd:tests:bindings";

        /// <summary>
        /// Creates a fresh context and the OpenUSD companion namespace index.
        /// </summary>
        public static (SystemContext Context, ushort Namespace) NewContext()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend(Opc.Ua.XRegistry.Namespaces.xRegistry);
            var ns = (ushort)namespaces.GetIndexOrAppend(Namespaces.OpenUSD);
            var instanceNs = (ushort)namespaces.GetIndexOrAppend(InstanceNamespaceUri);

            var context = new SystemContext(null!)
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable(),
                NodeIdFactory = new SequentialOpenUsdNodeIdFactory(instanceNs)
            };
            return (context, ns);
        }

        /// <summary>
        /// Materialises the well-known <c>OpenUSD</c> facility with its <c>Stages</c> and
        /// <c>Representations</c> registries.
        /// </summary>
        public static OpenUsdRootState NewFacility(SystemContext context, ushort ns)
        {
            OpenUsdRootState root = context.CreateInstanceOfOpenUsdRootType(
                null!, new QualifiedName("OpenUSD", ns));
            root.NodeId = new NodeId("OpenUSD", ns);
            _ = root.Stages ?? root.CreateOrReplaceStages(context, null!);
            _ = root.Representations ?? root.CreateOrReplaceRepresentations(context, null!);
            return root;
        }

        /// <summary>
        /// Adds an <c>OpenUsdStageType</c> instance to the facility's stages folder.
        /// </summary>
        public static OpenUsdStageState NewStage(
            SystemContext context, OpenUsdRootState root, ushort ns, string name)
        {
            FolderState stages = root.Stages!;
            OpenUsdStageState stage = context.CreateInstanceOfOpenUsdStageType(
                stages, new QualifiedName(name, ns));
            stage.ReferenceTypeId = ReferenceTypeIds.Organizes;
            stages.AddChild(stage);
            stage.NodeId = context.NodeIdFactory.New(context, stage);
            return stage;
        }

        /// <summary>
        /// Creates a plain Object a representation AddIn can be attached to.
        /// </summary>
        public static BaseObjectState NewOwner(SystemContext context, ushort ns, string name)
        {
            var owner = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            owner.NodeId = context.NodeIdFactory.New(context, owner);
            return owner;
        }
    }
}
