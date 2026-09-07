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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// A test <see cref="IWotBindingChannelFactory"/> that opens pre-configured
    /// fake channels (or a configurable opener delegate) per compiled form, and
    /// records how many times each form was opened.
    /// </summary>
    internal sealed class FakeWotBindingChannelFactory : IWotBindingChannelFactory
    {
        public int OpenCount { get; private set; }

        public List<WotCompiledForm> OpenedForms { get; } = [];

        public void SetChannel(WotCompiledForm form, IWotBindingChannel channel)
        {
            m_openers[form] = () => new ValueTask<IWotBindingChannel>(channel);
        }

        public void SetOpener(WotCompiledForm form, Func<ValueTask<IWotBindingChannel>> opener)
        {
            m_openers[form] = opener;
        }

        public ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            OpenedForms.Add(form);
            if (m_openers.TryGetValue(form, out Func<ValueTask<IWotBindingChannel>>? opener))
            {
                return opener();
            }
            throw new InvalidOperationException($"No fake channel configured for form '{form.AffordanceName}'.");
        }

        private readonly Dictionary<WotCompiledForm, Func<ValueTask<IWotBindingChannel>>> m_openers = [];
    }

    /// <summary>
    /// A test <see cref="IWotBindingChannel"/> with configurable read/write/dispose behavior.
    /// </summary>
    internal sealed class FakeWotBindingChannel : IWotBindingChannel
    {
        public FakeWotBindingChannel(WotCompiledForm form)
        {
            Form = form;
        }

        public WotCompiledForm Form { get; }

        public Func<CancellationToken, ValueTask<WotReadResult>>? OnRead { get; set; }

        public Func<DataValue, CancellationToken, ValueTask<WotWriteResult>>? OnWrite { get; set; }

        public Func<IReadOnlyList<Variant>, CancellationToken, ValueTask<WotInvokeResult>>? OnInvoke { get; set; }

        public Func<Action<WotNotification>, CancellationToken, ValueTask<IWotSubscription>>? OnSubscribeEvent { get; set; }

        public Func<ValueTask>? OnDispose { get; set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public int InvokeCount { get; private set; }

        public int SubscribeEventCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return OnRead?.Invoke(cancellationToken)
                ?? new ValueTask<WotReadResult>(new WotReadResult(StatusCodes.Good, new DataValue(Variant.Null)));
        }

        public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return OnWrite?.Invoke(value, cancellationToken)
                ?? new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            return OnInvoke?.Invoke(inputs, cancellationToken)
                ?? throw new NotSupportedException();
        }

        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            SubscribeEventCount++;
            return OnSubscribeEvent?.Invoke(onEvent, cancellationToken)
                ?? throw new NotSupportedException();
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (OnDispose is not null)
            {
                await OnDispose().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// A hand-crafted nested structure used to exercise <c>uav:mapByFieldPath</c> nesting.
    /// </summary>
    internal sealed class TestChildStructure : IEncodeable, IStructure
    {
        public int X { get; set; }

        public ExpandedNodeId TypeId => TestChildType.EncodingId;

        public ExpandedNodeId BinaryEncodingId => TestChildType.EncodingId;

        public ExpandedNodeId XmlEncodingId => TestChildType.EncodingId;

        public void Encode(IEncoder encoder)
        {
        }

        public void Decode(IDecoder decoder)
        {
        }

        public bool IsEqual(IEncodeable? encodeable)
        {
            return encodeable is TestChildStructure other && other.X == X;
        }

        public object Clone()
        {
            return new TestChildStructure { X = X };
        }

        public IReadOnlyList<IStructureField> GetFields()
        {
            return [];
        }

        public Variant this[int index]
        {
            get => index == 0 ? new Variant(X) : throw new ArgumentOutOfRangeException(nameof(index));
            set
            {
                if (index == 0 && value.TryGetValue(out int x))
                {
                    X = x;
                }
            }
        }

        public Variant this[string name]
        {
            get => name == "X" ? new Variant(X) : throw new ArgumentOutOfRangeException(nameof(name));
            set
            {
                if (name == "X" && value.TryGetValue(out int x))
                {
                    X = x;
                }
            }
        }
    }

    /// <summary>
    /// The <see cref="IEncodeableType"/> activator for <see cref="TestChildStructure"/>.
    /// </summary>
    internal sealed class TestChildType : EncodeableType<TestChildStructure>
    {
        public const uint NumericId = 9101;

        public static ExpandedNodeId EncodingId { get; } = new ExpandedNodeId(NumericId, TestStructureNamespace.Uri);

        public override XmlQualifiedName XmlName => new("TestChildStructure", Ua.Namespaces.OpcUaXsd);

        public override IEncodeable CreateInstance()
        {
            return new TestChildStructure();
        }

        public override DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            return new StructureDefinition
            {
                BaseDataType = Ua.DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "X",
                        DataType = Ua.DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }
    }

    /// <summary>
    /// A hand-crafted root structure with a scalar field, an array field and a
    /// nested <see cref="TestChildStructure"/> field, used to exercise one-level
    /// and nested <c>uav:mapByFieldPath</c> composition.
    /// </summary>
    internal sealed class TestRootStructure : IEncodeable, IStructure
    {
        public int A { get; set; }

        public Variant ChildValue { get; set; } = Variant.Null;

        public Variant ArrayValue { get; set; } = Variant.Null;

        public ExpandedNodeId TypeId => TestRootType.EncodingId;

        public ExpandedNodeId BinaryEncodingId => TestRootType.EncodingId;

        public ExpandedNodeId XmlEncodingId => TestRootType.EncodingId;

        public void Encode(IEncoder encoder)
        {
        }

        public void Decode(IDecoder decoder)
        {
        }

        public bool IsEqual(IEncodeable? encodeable)
        {
            return ReferenceEquals(this, encodeable);
        }

        public object Clone()
        {
            return new TestRootStructure { A = A, ChildValue = ChildValue, ArrayValue = ArrayValue };
        }

        public IReadOnlyList<IStructureField> GetFields()
        {
            return [];
        }

        public Variant this[int index]
        {
            get => index switch
            {
                0 => new Variant(A),
                1 => ChildValue,
                2 => ArrayValue,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            set
            {
                switch (index)
                {
                    case 0:
                        if (value.TryGetValue(out int a))
                        {
                            A = a;
                        }
                        break;
                    case 1:
                        ChildValue = value;
                        break;
                    case 2:
                        ArrayValue = value;
                        break;
                }
            }
        }

        public Variant this[string name]
        {
            get => name switch
            {
                "A" => new Variant(A),
                "Child" => ChildValue,
                "ArrayField" => ArrayValue,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            set
            {
                switch (name)
                {
                    case "A":
                        if (value.TryGetValue(out int a))
                        {
                            A = a;
                        }
                        break;
                    case "Child":
                        ChildValue = value;
                        break;
                    case "ArrayField":
                        ArrayValue = value;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// The <see cref="IEncodeableType"/> activator for <see cref="TestRootStructure"/>.
    /// </summary>
    internal sealed class TestRootType : EncodeableType<TestRootStructure>
    {
        public const uint NumericId = 9100;

        public static ExpandedNodeId EncodingId { get; } = new ExpandedNodeId(NumericId, TestStructureNamespace.Uri);

        public override XmlQualifiedName XmlName => new("TestRootStructure", Ua.Namespaces.OpcUaXsd);

        public override IEncodeable CreateInstance()
        {
            return new TestRootStructure();
        }

        public override DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            return new StructureDefinition
            {
                BaseDataType = Ua.DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "A",
                        DataType = Ua.DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "Child",
                        DataType = ExpandedNodeId.ToNodeId(TestChildType.EncodingId, namespaceUris),
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "ArrayField",
                        DataType = Ua.DataTypeIds.Int32,
                        ValueRank = ValueRanks.OneDimension
                    }
                ]
            };
        }
    }

    /// <summary>
    /// The shared namespace URI the hand-crafted test structure types are registered under.
    /// </summary>
    internal static class TestStructureNamespace
    {
        public const string Uri = "http://test.org/UA/WotProjectionBindingRuntimeTests/";
    }

    /// <summary>
    /// Builds a minimal <see cref="NodeManagerBuilder"/> graph (no running
    /// server) with a scalar Int32 variable and a <see cref="TestRootStructure"/>
    /// typed variable, for exercising <see cref="WotProjectionBindingRuntime"/>
    /// directly.
    /// </summary>
    internal sealed class WotProjectionBindingRuntimeTestHarness
    {
        public NodeManagerBuilder Builder { get; }

        public ushort Ns { get; }

        public BaseDataVariableState ScalarVar { get; }

        public BaseDataVariableState StructVar { get; }

        public BaseObjectState Root { get; }

        public FakeWotBindingChannelFactory ChannelFactory { get; } = new();

        /// <summary>
        /// Initializes a new harness.
        /// </summary>
        /// <param name="registerStructureTypes">
        /// When <c>true</c> (the default), <see cref="TestChildType"/> and
        /// <see cref="TestRootType"/> are registered into the harness's
        /// <see cref="IEncodeableFactory"/> up front, matching a NodeManager
        /// activated after <c>NodeManagerLifecycle.RefreshComplexTypesAsync</c>
        /// already populated it. When <c>false</c>, neither type is
        /// registered, matching activation before that refresh runs; the test
        /// can register them later against the same
        /// <see cref="ISystemContext.EncodeableFactory"/> instance (via
        /// <c>Builder.Context.EncodeableFactory</c>) to simulate the refresh
        /// completing after the runtime was wired.
        /// </param>
        public WotProjectionBindingRuntimeTestHarness(bool registerStructureTypes = true)
        {
            var namespaceUris = new NamespaceTable();
            Ns = (ushort)namespaceUris.Append(TestStructureNamespace.Uri);

            IEncodeableFactory factory = ServiceMessageContext.CreateEmpty(null!).Factory;
            if (registerStructureTypes)
            {
                factory.Builder
                    .AddEncodeableType(TestChildType.EncodingId, new TestChildType())
                    .AddEncodeableType(TestRootType.EncodingId, new TestRootType())
                    .Commit();
            }

            var ctx = new SystemContext(TelemetryExtensions.InternalOnly__TelemetryHook())
            {
                NamespaceUris = namespaceUris,
                TypeTable = new TypeTable(namespaceUris),
                NodeIdFactory = new TestNodeIdFactory(Ns),
                EncodeableFactory = factory
            };

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", Ns),
                BrowseName = new QualifiedName("Root", Ns),
                DisplayName = new LocalizedText("Root")
            };
            Root = root;

            ScalarVar = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Scalar", Ns),
                BrowseName = new QualifiedName("Scalar", Ns),
                DisplayName = new LocalizedText("Scalar"),
                DataType = Ua.DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            root.AddChild(ScalarVar);

            StructVar = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Struct", Ns),
                BrowseName = new QualifiedName("Struct", Ns),
                DisplayName = new LocalizedText("Struct"),
                DataType = new NodeId(TestRootType.NumericId, Ns),
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
            root.AddChild(StructVar);

            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [ScalarVar.NodeId] = ScalarVar,
                [StructVar.NodeId] = StructVar
            };
            m_nodes = byId;

            Builder = new NodeManagerBuilder(
                ctx,
                Mock.Of<IAsyncNodeManager>(),
                Ns,
                rootResolver: q => q == root.BrowseName ? root : null!,
                nodeIdResolver: FindNode,
                typeIdResolver: _ => [],
                dataTypeIdResolver: dataTypeId =>
                {
                    var matches = new List<NodeState>();
                    foreach (NodeState node in byId.Values)
                    {
                        if (node is BaseVariableState v && v.DataType == dataTypeId)
                        {
                            matches.Add(node);
                        }
                    }
                    return matches.ToArrayOf();
                });
        }

        public string ScalarNodeIdText => $"ns={Ns};s=Scalar";

        public string StructNodeIdText => $"ns={Ns};s=Struct";

        public string StructTypeNodeIdText => $"ns={Ns};i={TestRootType.NumericId}";

        public MethodState AddMethod(
            string name, ArrayOf<Argument> inputs, ArrayOf<Argument> outputs, BaseObjectState? parent = null)
        {
            parent ??= Root;
            var method = new MethodState(parent)
            {
                NodeId = new NodeId(name, Ns),
                BrowseName = new QualifiedName(name, Ns),
                DisplayName = new LocalizedText(name),
                Executable = true,
                UserExecutable = true
            };
            method.CreateOrReplaceInputArguments(Builder.Context, null).Value = inputs;
            method.CreateOrReplaceOutputArguments(Builder.Context, null).Value = outputs;
            parent.AddChild(method);
            m_nodes.Add(method.NodeId, method);
            return method;
        }

        public BaseObjectTypeState AddEventType(string name, NodeId superTypeId)
        {
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(name, Ns),
                BrowseName = new QualifiedName(name, Ns),
                SuperTypeId = superTypeId,
                IsAbstract = false
            };
            m_nodes.Add(type.NodeId, type);
            return type;
        }

        public BaseObjectState AddDetachedObject(string name)
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, Ns),
                BrowseName = new QualifiedName(name, Ns)
            };
            m_nodes.Add(node.NodeId, node);
            return node;
        }

        private NodeState FindNode(NodeId id)
        {
            if (m_nodes.TryGetValue(id, out NodeState? found))
            {
                return found;
            }
            foreach (NodeState root in m_nodes.Values)
            {
                NodeState node = FindNode(root, id);
                if (node is not null)
                {
                    return node;
                }
            }
            return null!;
        }

        private NodeState FindNode(NodeState root, NodeId id)
        {
            if (root.NodeId == id)
            {
                return root;
            }
            var children = new List<BaseInstanceState>();
            root.GetChildren(Builder.Context, children);
            foreach (BaseInstanceState child in children)
            {
                NodeState node = FindNode(child, id);
                if (node is not null)
                {
                    return node;
                }
            }
            return null!;
        }

        private sealed class TestNodeIdFactory(ushort namespaceIndex) : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId((uint)Interlocked.Increment(ref m_nextId), namespaceIndex);
            }

            private int m_nextId = 10000;
        }

        public static WotCompiledForm Form(
            WoTBindingCapabilityEnum operation,
            WotTargetMappingDescriptor mapping,
            bool executable = true,
            string affordanceName = "value")
        {
            string opToken = operation switch
            {
                WoTBindingCapabilityEnum.ReadProperty => "readproperty",
                WoTBindingCapabilityEnum.WriteProperty => "writeproperty",
                WoTBindingCapabilityEnum.ObserveProperty => "observeproperty",
                WoTBindingCapabilityEnum.InvokeAction => "invokeaction",
                _ => "unknown"
            };
            return new WotCompiledForm(
                new WotBindingIdentity("test", "1.0", "urn:test"),
                WotAffordanceKind.Property,
                affordanceName,
                "/properties/" + affordanceName + "/forms/0",
                operation,
                opToken,
                new WotEndpointDescriptor("test", null, -1, "test://x"),
                new WotAddressingDescriptor(affordanceName),
                new WotOperationDescriptor(operation, opToken, "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                [],
                isExecutable: executable,
                targetMapping: mapping);
        }

        public static WotBindingPlan Plan(params WotCompiledForm[] forms)
        {
            return new(
                        "res",
                        [],
                        [.. forms],
                        [],
                        []);
        }

        private readonly Dictionary<NodeId, NodeState> m_nodes;
    }
}
