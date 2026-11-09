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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.LADS.Tests.Model
{
    /// <summary>
    /// Creates isolated contexts and reads the vendored LADS model files.
    /// </summary>
    internal static class LadsTestContext
    {
        /// <summary>
        /// The independently specified LADS closure in load order.
        /// </summary>
        public static string[] ExpectedNamespaceUris =>
        [
            "http://opcfoundation.org/UA/DI/",
            "http://opcfoundation.org/UA/IA/",
            "http://opcfoundation.org/UA/Machinery/",
            "http://opcfoundation.org/UA/AMB/",
            "http://opcfoundation.org/UA/LADS/"
        ];

        /// <summary>
        /// Creates a node-state context with the LADS encodeable types registered.
        /// </summary>
        public static SystemContext CreateSystemContext(bool registerModel = true, bool reverseNamespaces = false)
        {
            var message = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            message.NamespaceUris.GetIndexOrAppend("urn:lads-tests:application");
            if (registerModel)
            {
                IEnumerable<string> namespaces = ExpectedNamespaceUris;
                foreach (string uri in reverseNamespaces ? namespaces.Reverse() : namespaces)
                {
                    message.NamespaceUris.GetIndexOrAppend(uri);
                }
                message.Factory.Builder.AddLadsTypeSystem().Commit();
            }
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
        }

        /// <summary>
        /// Indexes all roots and nested declarations.
        /// </summary>
        public static Dictionary<NodeId, NodeState> IndexNodes(ISystemContext context, NodeStateCollection roots)
        {
            var nodes = new Dictionary<NodeId, NodeState>();
            foreach (NodeState root in roots)
            {
                AddNode(context, root, nodes);
            }
            return nodes;
        }

        /// <summary>
        /// Returns the path of a vendored model file copied next to the test assembly.
        /// </summary>
        public static string ModelPath(string file)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Model", file);
        }

        /// <summary>
        /// Loads the root element of the vendored NodeSet.
        /// </summary>
        public static XElement ReadNodeSet()
        {
            return XDocument.Load(ModelPath("Opc.Ua.LADS.NodeSet2.xml")).Root!;
        }

        /// <summary>
        /// Returns the NodeSet namespace index of a model URI, or -1 when the NodeSet does not use it.
        /// </summary>
        public static int NodeSetIndexOf(string namespaceUri)
        {
            XElement table = ReadNodeSet().Element(NodeSetNamespace + "NamespaceUris")!;
            int index = 1;
            foreach (XElement uri in table.Elements())
            {
                if (uri.Value == namespaceUri)
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        /// <summary>
        /// Returns every numeric LADS declaration of the NodeSet with its node class.
        /// </summary>
        public static Dictionary<uint, string> ReadDeclarations()
        {
            string prefix = "ns=" +
                NodeSetIndexOf(LadsModel.NamespaceUri).ToString(CultureInfo.InvariantCulture) + ";i=";
            return ReadNodeSet().Elements()
                .Where(node => ((string?)node.Attribute("NodeId"))?
                    .StartsWith(prefix, System.StringComparison.Ordinal) == true)
                .ToDictionary(
                    node => uint.Parse(
                        ((string)node.Attribute("NodeId")!)[prefix.Length..],
                        CultureInfo.InvariantCulture),
                    node => node.Name.LocalName[2..]);
        }

        /// <summary>
        /// Returns the NodeSet element of a numeric LADS declaration.
        /// </summary>
        public static XElement ReadDeclaration(uint identifier)
        {
            string nodeId = "ns=" +
                NodeSetIndexOf(LadsModel.NamespaceUri).ToString(CultureInfo.InvariantCulture) + ";i=" +
                identifier.ToString(CultureInfo.InvariantCulture);
            return ReadNodeSet().Elements().Single(node => (string?)node.Attribute("NodeId") == nodeId);
        }

        private static void AddNode(ISystemContext context, NodeState node, Dictionary<NodeId, NodeState> nodes)
        {
            if (!nodes.TryAdd(node.NodeId, node))
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                AddNode(context, child, nodes);
            }
        }

        /// <summary>
        /// The UANodeSet XML namespace.
        /// </summary>
        public static readonly XNamespace NodeSetNamespace = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
    }
}
