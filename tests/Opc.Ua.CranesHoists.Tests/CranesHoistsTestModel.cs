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
using System.IO;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Robotics;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Supplies isolated contexts and pinned model assets for generation tests.
    /// </summary>
    internal static class CranesHoistsTestModel
    {
        /// <summary>
        /// Returns the copied model asset path.
        /// </summary>
        internal static string AssetPath(string file)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", file);
        }

        /// <summary>
        /// Reads the unchanged official NodeSet.
        /// </summary>
        internal static XDocument ReadPublishedModel()
        {
            return XDocument.Load(AssetPath("Opc.Ua.CranesHoists.NodeSet2.xml"));
        }

        /// <summary>
        /// Reads a numeric identifier from a published declaration without assuming a CLR identifier representation.
        /// </summary>
        internal static uint PublishedNumericId(XElement declaration)
        {
            var nodeId = NodeId.Parse(declaration.Attribute("NodeId")!.Value);
            Assert.That(nodeId.TryGetValue(out uint identifier), Is.True);
            return identifier;
        }

        /// <summary>
        /// Creates an independent context with a vendor namespace and reordered model namespaces.
        /// </summary>
        internal static SystemContext CreateContext(bool reverseNamespaces = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
            context.NamespaceUris.GetIndexOrAppend("urn:craneshoists:tests:vendor");
            string[] namespaces =
            [
                Di.Namespaces.OpcUaDi,
                IA.Namespaces.IA,
                Machinery.Namespaces.Machinery,
                Robotics.Namespaces.Robotics,
                CranesHoistsModel.NamespaceUri
            ];
            if (reverseNamespaces)
            {
                Array.Reverse(namespaces);
            }
            foreach (string uri in namespaces)
            {
                context.NamespaceUris.GetIndexOrAppend(uri);
            }
            return context;
        }

        /// <summary>
        /// Loads only the runtime dependency closure of the crane model.
        /// </summary>
        internal static NodeStateCollection LoadDependencies(ISystemContext context)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaRobotics(context);
            return nodes;
        }

        /// <summary>
        /// Indexes declared nodes while retaining children from every generated projection of a shared identifier.
        /// </summary>
        internal static Dictionary<NodeId, NodeState> IndexNodes(
            ISystemContext context,
            NodeStateCollection roots)
        {
            var indexed = new Dictionary<NodeId, NodeState>();
            var visited = new HashSet<NodeState>();
            var remaining = new Queue<NodeState>(roots);
            while (remaining.Count != 0)
            {
                NodeState node = remaining.Dequeue();
                if (!visited.Add(node))
                {
                    continue;
                }
                indexed.TryAdd(node.NodeId, node);
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    remaining.Enqueue(child);
                }
            }
            return indexed;
        }

        /// <summary>
        /// Resolves a crane numeric identifier against the context, never the XML namespace index.
        /// </summary>
        internal static NodeId Resolve(uint identifier, ISystemContext context)
        {
            return NodeId.Create(identifier, CranesHoistsModel.NamespaceUri, context.NamespaceUris);
        }

        /// <summary>
        /// The NodeSet schema namespace.
        /// </summary>
        internal static readonly XNamespace Schema = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
