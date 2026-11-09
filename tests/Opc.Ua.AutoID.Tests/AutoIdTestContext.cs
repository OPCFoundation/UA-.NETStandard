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
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Creates isolated contexts with shifted or reversed namespaces and reads the vendored model files.
    /// </summary>
    internal static class AutoIdTestContext
    {
        /// <summary>
        /// The independently specified AutoID closure: Device Integration followed by AutoID.
        /// </summary>
        public static ArrayOf<string> ExpectedNamespaceUris =>
        [
            "http://opcfoundation.org/UA/DI/",
            "http://opcfoundation.org/UA/AutoID/"
        ];

        /// <summary>
        /// Creates a core message context with optional AutoID encodeable registration.
        /// </summary>
        public static ServiceMessageContext CreateMessageContext(
            bool registerModel = true,
            bool reverseNamespaces = false)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:autoid-tests:application");
            context.NamespaceUris.GetIndexOrAppend("urn:autoid-tests:unrelated");
            if (registerModel)
            {
                IEnumerable<string> namespaces = ExpectedNamespaceUris.ToArray()!;
                foreach (string uri in reverseNamespaces ? namespaces.Reverse() : namespaces)
                {
                    context.NamespaceUris.GetIndexOrAppend(uri);
                }
                context.Factory.Builder.AddAutoIdTypeSystem().Commit();
            }
            return context;
        }

        /// <summary>
        /// Creates a node-state context sharing the message namespace table and encodeable factory.
        /// </summary>
        public static SystemContext CreateSystemContext(
            bool registerModel = true,
            bool reverseNamespaces = false)
        {
            ServiceMessageContext message = CreateMessageContext(registerModel, reverseNamespaces);
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = message.NamespaceUris,
                EncodeableFactory = message.Factory
            };
        }

        /// <summary>
        /// Indexes all roots and nested declarations through the public node-state traversal API.
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
            return XDocument.Load(ModelPath("Opc.Ua.AutoID.NodeSet2.xml")).Root!;
        }

        /// <summary>
        /// Returns every numeric AutoID declaration of the NodeSet with its node class.
        /// </summary>
        public static Dictionary<uint, string> ReadDeclarations()
        {
            return ReadNodeSet().Elements()
                .Where(node => node.Attribute("NodeId") != null)
                .ToDictionary(
                    node => uint.Parse(
                        ((string)node.Attribute("NodeId")!)["ns=1;i=".Length..],
                        System.Globalization.CultureInfo.InvariantCulture),
                    node => node.Name.LocalName[2..]);
        }

        private static void AddNode(
            ISystemContext context,
            NodeState node,
            Dictionary<NodeId, NodeState> nodes)
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
