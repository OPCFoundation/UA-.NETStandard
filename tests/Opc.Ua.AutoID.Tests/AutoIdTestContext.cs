// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
