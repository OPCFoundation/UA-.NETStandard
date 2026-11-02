/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Tests;

namespace Opc.Ua.IA.Tests
{
    /// <summary>
    /// Creates generated IA nodes without sockets or a background server.
    /// </summary>
    internal sealed class IaTestContext : INodeIdFactory
    {
        /// <summary>
        /// Creates a namespace-qualified context with an independent instance namespace.
        /// </summary>
        public IaTestContext()
        {
            var namespaces = new NamespaceTable();
            var telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messages = ServiceMessageContext.Create(telemetry);
            InstanceNamespace = namespaces.GetIndexOrAppend("urn:tests:ia:instances");
            namespaces.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            namespaces.GetIndexOrAppend(Namespaces.IA);
            Context = new SystemContext(telemetry)
            {
                NamespaceUris = namespaces,
                NodeIdFactory = this,
                TypeTable = new TypeTable(namespaces),
                EncodeableFactory = messages.Factory
            };
            Context.EncodeableFactory.Builder.AddOpcUaIA().Commit();
        }

        /// <summary>
        /// Gets the generated-instance context.
        /// </summary>
        public SystemContext Context { get; }

        /// <summary>
        /// Gets the non-model namespace for instances.
        /// </summary>
        public ushort InstanceNamespace { get; }

        /// <summary>
        /// Gets a fixed acquisition time, independent of the host clock.
        /// </summary>
        public static DateTimeUtc Timestamp => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        /// <summary>
        /// Allocates unique identities even for repeated browse names below different parents.
        /// </summary>
        public NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId.IsNull
                ? new NodeId((uint)Interlocked.Increment(ref m_nextId), InstanceNamespace)
                : node.NodeId;
        }

        /// <summary>
        /// Creates a test-local qualified name.
        /// </summary>
        public QualifiedName Name(string name)
        {
            return new QualifiedName(name, InstanceNamespace);
        }

        /// <summary>
        /// Creates an application-defined statistics owner.
        /// </summary>
        public BaseObjectState Owner(string name = "Statistics")
        {
            var state = new BaseObjectState(null);
            state.Create(Context, NodeId.Null, Name(name), new LocalizedText(name), true);
            return state;
        }

        /// <summary>
        /// Creates one application-defined numeric quantity.
        /// </summary>
        public BaseDataVariableState Quantity(BaseObjectState owner, string name = "Count")
        {
            var value = new BaseDataVariableState(owner);
            value.Create(Context, NodeId.Null, Name(name), new LocalizedText(name), true);
            value.DataType = Opc.Ua.DataTypeIds.UInt32;
            value.ValueRank = ValueRanks.Scalar;
            owner.AddChild(value);
            return value;
        }

        /// <summary>
        /// Reads all actual children for topology assertions.
        /// </summary>
        public List<BaseInstanceState> Children(NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            return children;
        }

        /// <summary>The next instance identity.</summary>
        private int m_nextId;
    }
}
