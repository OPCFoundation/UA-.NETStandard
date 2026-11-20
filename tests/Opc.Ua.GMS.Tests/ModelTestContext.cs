// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Creates isolated contexts with shifted or reversed namespaces for model seam tests.
    /// </summary>
    internal static class ModelTestContext
    {
        /// <summary>
        /// The independently specified GMS closure, including the selected MachineTool 1.02 job dependencies.
        /// </summary>
        public static ArrayOf<string> ExpectedNamespaceUris =>
        [
            "http://opcfoundation.org/UA/DI/",
            "http://opcfoundation.org/UA/IA/",
            "http://opcfoundation.org/UA/Machinery/",
            "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/",
            "http://opcfoundation.org/UA/Machinery/Jobs/",
            "http://opcfoundation.org/UA/MachineTool/",
            "http://opcfoundation.org/UA/Machinery/Result/",
            "http://opcfoundation.org/UA/GMS/"
        ];

        /// <summary>
        /// Creates a core message context with optional GMS encodeable registration.
        /// </summary>
        public static ServiceMessageContext CreateMessageContext(
            bool registerModel = true,
            bool reverseNamespaces = false)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:gms-tests:application");
            context.NamespaceUris.GetIndexOrAppend("urn:gms-tests:unrelated");
            if (registerModel)
            {
                IEnumerable<string> namespaces = ExpectedNamespaceUris.ToArray()!;
                foreach (string uri in reverseNamespaces ? namespaces.Reverse() : namespaces)
                {
                    context.NamespaceUris.GetIndexOrAppend(uri);
                }
                context.Factory.Builder.AddGmsTypeSystem().Commit();
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
        /// Requires a child with both the expected CLR type and exact namespace-qualified browse name.
        /// </summary>
        public static T RequireChild<T>(
            SystemContext context,
            NodeState parent,
            string browseName,
            string namespaceUri)
            where T : BaseInstanceState
        {
            var name = new QualifiedName(browseName, context.NamespaceUris.GetIndexOrAppend(namespaceUri));
            BaseInstanceState? child = parent.FindChild(context, name);
            Assert.That(child, Is.InstanceOf<T>(), $"{parent.BrowseName}/{name}");
            Assert.That(child!.BrowseName, Is.EqualTo(name));
            return (T)child;
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
    }
}
