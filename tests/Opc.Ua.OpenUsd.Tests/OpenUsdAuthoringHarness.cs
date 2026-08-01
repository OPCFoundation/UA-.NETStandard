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
            stage.ReferenceTypeId = ReferenceTypeIds.HasComponent;
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
