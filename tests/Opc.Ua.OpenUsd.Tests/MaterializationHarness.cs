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

using System.Collections.Generic;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// A NodeIdFactory that hands each newly materialized node a fresh, unique numeric
    /// identifier in a dedicated instance namespace, mirroring what a real NodeManager does
    /// while a subtree is being wired up. Nodes that already carry an identifier keep it.
    /// </summary>
    internal sealed class SequentialNodeIdFactory : INodeIdFactory
    {
        private readonly ushort m_namespaceIndex;
        private uint m_next;

        public SequentialNodeIdFactory(ushort namespaceIndex)
        {
            m_namespaceIndex = namespaceIndex;
        }

        public NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId.IsNull ? new NodeId(++m_next, m_namespaceIndex) : node.NodeId;
        }
    }

    /// <summary>
    /// The result of materializing a scene in a test: the context it was materialized in, the
    /// companion namespace index, the root the stage hangs under and the materialization
    /// result itself.
    /// </summary>
    internal sealed class MaterializedScene
    {
        public MaterializedScene(
            SystemContext context,
            ushort ns,
            BaseObjectState root,
            UsdMaterializationResult result)
        {
            Context = context;
            Namespace = ns;
            Root = root;
            Result = result;
        }

        public SystemContext Context { get; }

        public ushort Namespace { get; }

        public BaseObjectState Root { get; }

        public UsdMaterializationResult Result { get; }

        public UsdStageState Stage => Result.Stage;
    }

    /// <summary>
    /// Builds an in-process <see cref="ISystemContext"/> with a working NodeIdFactory and a
    /// namespace table that already carries the OpenUSD Scene companion URI, so the tests can
    /// materialize a stage and assert directly on the resulting <see cref="NodeState"/> tree
    /// without standing up a real <c>opc.tcp</c> server.
    /// </summary>
    internal static class MaterializationHarness
    {
        /// <summary>
        /// A private namespace the instance NodeIds are minted in.
        /// </summary>
        public const string InstanceNamespaceUri = "urn:opcfoundation:openusd:tests:instances";

        /// <summary>
        /// Creates a fresh context, the companion namespace index and a root Object to attach
        /// a stage under.
        /// </summary>
        public static (SystemContext Context, ushort Namespace, BaseObjectState Root) NewContext()
        {
            var namespaces = new NamespaceTable();
            var ns = (ushort)namespaces.GetIndexOrAppend(Opc.Ua.OpenUsd.Scene.Namespaces.OpenUSDScene);
            var instanceNs = (ushort)namespaces.GetIndexOrAppend(InstanceNamespaceUri);

            var context = new SystemContext(null!)
            {
                NamespaceUris = namespaces,
                ServerUris = new StringTable(),
                NodeIdFactory = new SequentialNodeIdFactory(instanceNs)
            };

            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("TestStages", instanceNs),
                BrowseName = new QualifiedName("TestStages", instanceNs),
                DisplayName = new LocalizedText("TestStages"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            return (context, ns, root);
        }

        /// <summary>
        /// Materializes <paramref name="stage"/> under a fresh root and returns everything a
        /// test needs to assert on.
        /// </summary>
        public static MaterializedScene Materialize(
            UsdStage stage, UsdMaterializationOptions? options = null)
        {
            (SystemContext context, ushort ns, BaseObjectState root) = NewContext();
            UsdMaterializationResult result = context.MaterializeUsdStage(root, stage, ns, options);
            return new MaterializedScene(context, ns, root, result);
        }

        /// <summary>
        /// Returns the materialized children of <paramref name="parent"/> that are of type
        /// <typeparamref name="T"/>, in address-space order.
        /// </summary>
        public static List<T> ChildrenOfType<T>(ISystemContext context, NodeState parent)
            where T : NodeState
        {
            var all = new List<BaseInstanceState>();
            parent.GetChildren(context, all);
            var typed = new List<T>();
            foreach (BaseInstanceState child in all)
            {
                if (child is T match)
                {
                    typed.Add(match);
                }
            }
            return typed;
        }
    }
}
