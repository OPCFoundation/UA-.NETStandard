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
using Opc.Ua.Export;
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
        /// <summary>
        /// Gets the number of binding channels opened by the factory.
        /// </summary>
        public int OpenCount { get; private set; }

        /// <summary>
        /// Gets the compiled forms supplied when opening binding channels.
        /// </summary>
        public List<WotCompiledForm> OpenedForms { get; } = [];

        /// <summary>
        /// Registers the channel returned when the form is opened.
        /// </summary>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="channel">The WoT binding channel.</param>
        public void SetChannel(WotCompiledForm form, IWotBindingChannel channel)
        {
            m_openers[form] = _ => new ValueTask<IWotBindingChannel>(channel);
        }

        /// <summary>
        /// Registers a parameterless opener that produces the channel when the form is opened.
        /// </summary>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="opener">The delegate that opens the channel.</param>
        public void SetOpener(WotCompiledForm form, Func<ValueTask<IWotBindingChannel>> opener)
        {
            m_openers[form] = _ => opener();
        }

        /// <summary>
        /// Registers a cancellable opener that produces the channel when the form is opened.
        /// </summary>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="opener">The delegate that opens the channel.</param>
        public void SetOpener(
            WotCompiledForm form, Func<CancellationToken, ValueTask<IWotBindingChannel>> opener)
        {
            m_openers[form] = opener;
        }

        /// <inheritdoc/>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            OpenedForms.Add(form);
            if (m_openers.TryGetValue(form, out Func<CancellationToken, ValueTask<IWotBindingChannel>>? opener))
            {
                return opener(cancellationToken);
            }
            throw new InvalidOperationException($"No fake channel configured for form '{form.AffordanceName}'.");
        }

        private readonly Dictionary<WotCompiledForm, Func<CancellationToken, ValueTask<IWotBindingChannel>>> m_openers =
            [];
    }

    /// <summary>
    /// A test <see cref="IWotBindingChannel"/> with configurable read/write/dispose behavior.
    /// </summary>
    internal sealed class FakeWotBindingChannel : IWotBindingChannel
    {
        /// <summary>
        /// Creates a controllable test channel for the supplied compiled form.
        /// </summary>
        /// <param name="form">The compiled form defining the protocol binding.</param>
        public FakeWotBindingChannel(WotCompiledForm form)
        {
            Form = form;
        }

        /// <inheritdoc cref="IWotBindingChannel.Form"/>
        public WotCompiledForm Form { get; }

        /// <summary>
        /// Gets or sets the callback used to provide a property read result.
        /// </summary>
        public Func<CancellationToken, ValueTask<WotReadResult>>? OnRead { get; set; }

        /// <summary>
        /// Gets or sets the callback used to handle a property write.
        /// </summary>
        public Func<DataValue, CancellationToken, ValueTask<WotWriteResult>>? OnWrite { get; set; }

        /// <summary>
        /// Gets or sets the callback used to handle an action invocation.
        /// </summary>
        public Func<IReadOnlyList<Variant>, CancellationToken, ValueTask<WotInvokeResult>>? OnInvoke { get; set; }

        /// <summary>
        /// Gets or sets the callback used to establish an event subscription.
        /// </summary>
        public Func<Action<WotNotification>, CancellationToken, ValueTask<IWotSubscription>>? OnSubscribeEvent { get; set; }

        /// <summary>
        /// Gets or sets the on observe for <c>FakeWotBindingChannel</c>.
        /// </summary>
        public Func<Action<WotNotification>, CancellationToken, ValueTask<IWotSubscription>>? OnObserve { get; set; }

        /// <summary>
        /// Gets or sets the asynchronous callback invoked when the channel is disposed.
        /// </summary>
        public Func<ValueTask>? OnDispose { get; set; }

        /// <summary>
        /// Gets the number of property read requests received by the channel.
        /// </summary>
        public int ReadCount { get; private set; }

        /// <summary>
        /// Gets the number of property write requests received by the channel.
        /// </summary>
        public int WriteCount { get; private set; }

        /// <summary>
        /// Gets the number of action invocations received by the channel.
        /// </summary>
        public int InvokeCount { get; private set; }

        /// <summary>
        /// Gets the number of event subscription requests received by the channel.
        /// </summary>
        public int SubscribeEventCount { get; private set; }

        /// <summary>
        /// Gets or sets the observe count for <c>FakeWotBindingChannel</c>.
        /// </summary>
        public int ObserveCount { get; private set; }

        /// <summary>
        /// Gets the number of channel disposal calls.
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <inheritdoc cref="IWotBindingChannel.ReadAsync"/>
        public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return OnRead?.Invoke(cancellationToken)
                ?? new ValueTask<WotReadResult>(new WotReadResult(StatusCodes.Good, new DataValue(Variant.Null)));
        }

        /// <inheritdoc cref="IWotBindingChannel.WriteAsync"/>
        public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return OnWrite?.Invoke(value, cancellationToken)
                ?? new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
        }

        /// <inheritdoc cref="IWotBindingChannel.InvokeAsync"/>
        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            return OnInvoke?.Invoke(inputs, cancellationToken)
                ?? throw new NotSupportedException();
        }

        /// <inheritdoc/>
        /// <param name="onNotification">The on notification.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            ObserveCount++;
            return OnObserve?.Invoke(onNotification, cancellationToken) ?? throw new NotSupportedException();
        }

        /// <inheritdoc cref="IWotBindingChannel.SubscribeEventAsync"/>
        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            SubscribeEventCount++;
            return OnSubscribeEvent?.Invoke(onEvent, cancellationToken)
                ?? throw new NotSupportedException();
        }

        /// <inheritdoc />
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
        /// <summary>
        /// Gets or sets the integer field of the child test structure.
        /// </summary>
        public int X { get; set; }

        /// <inheritdoc />
        public ExpandedNodeId TypeId => TestChildType.EncodingId;

        /// <inheritdoc />
        public ExpandedNodeId BinaryEncodingId => TestChildType.EncodingId;

        /// <inheritdoc />
        public ExpandedNodeId XmlEncodingId => TestChildType.EncodingId;

        /// <inheritdoc />
        public void Encode(IEncoder encoder)
        {
        }

        /// <inheritdoc />
        public void Decode(IDecoder decoder)
        {
        }

        /// <inheritdoc />
        public bool IsEqual(IEncodeable? encodeable)
        {
            return encodeable is TestChildStructure other && other.X == X;
        }

        /// <inheritdoc />
        public object Clone()
        {
            return new TestChildStructure { X = X };
        }

        /// <inheritdoc />
        public IReadOnlyList<IStructureField> GetFields()
        {
            return [];
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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
        /// <summary>
        /// The numeric identifier used by the child test structure type.
        /// </summary>
        public const uint NumericId = 9101;

        /// <summary>
        /// Gets the portable encoding identifier used for child test structures.
        /// </summary>
        public static ExpandedNodeId EncodingId { get; } = new ExpandedNodeId(NumericId, TestStructureNamespace.Uri);

        /// <inheritdoc />
        public override XmlQualifiedName XmlName => new("TestChildStructure", Ua.Namespaces.OpcUaXsd);

        /// <inheritdoc />
        public override IEncodeable CreateInstance()
        {
            return new TestChildStructure();
        }

        /// <inheritdoc />
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
        /// <summary>
        /// Gets or sets the scalar integer field of the root test structure.
        /// </summary>
        public int A { get; set; }

        /// <summary>
        /// Gets or sets the nested structure value used by field-path mapping tests.
        /// </summary>
        public Variant ChildValue { get; set; } = Variant.Null;

        /// <summary>
        /// Gets or sets the array field value used by field-path mapping tests.
        /// </summary>
        public Variant ArrayValue { get; set; } = Variant.Null;

        /// <summary>
        /// Gets or sets the node ID for <c>TestRootStructure</c>.
        /// </summary>
        public NodeId Target { get; set; }

        /// <inheritdoc />
        public ExpandedNodeId TypeId => TestRootType.EncodingId;

        /// <inheritdoc />
        public ExpandedNodeId BinaryEncodingId => TestRootType.EncodingId;

        /// <inheritdoc />
        public ExpandedNodeId XmlEncodingId => TestRootType.EncodingId;

        /// <inheritdoc />
        public void Encode(IEncoder encoder)
        {
        }

        /// <inheritdoc />
        public void Decode(IDecoder decoder)
        {
        }

        /// <inheritdoc />
        public bool IsEqual(IEncodeable? encodeable)
        {
            return ReferenceEquals(this, encodeable);
        }

        /// <inheritdoc />
        public object Clone()
        {
            return new TestRootStructure { A = A, ChildValue = ChildValue, ArrayValue = ArrayValue, Target = Target };
        }

        /// <inheritdoc />
        public IReadOnlyList<IStructureField> GetFields()
        {
            return [];
        }

        /// <inheritdoc />
        public Variant this[int index]
        {
            get => index switch
            {
                0 => new Variant(A),
                1 => ChildValue,
                2 => ArrayValue,
                3 => new Variant(Target),
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
                    case 3:
                        if (value.TryGetValue(out NodeId target))
                        {
                            Target = target;
                        }
                        break;
                }
            }
        }

        /// <inheritdoc />
        public Variant this[string name]
        {
            get => name switch
            {
                "A" => new Variant(A),
                "Child" => ChildValue,
                "ArrayField" => ArrayValue,
                "Target" => new Variant(Target),
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
                    case "Target":
                        if (value.TryGetValue(out NodeId target))
                        {
                            Target = target;
                        }
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
        /// <summary>
        /// The numeric identifier used by the root test structure type.
        /// </summary>
        public const uint NumericId = 9100;

        /// <summary>
        /// Gets the portable encoding identifier used for root test structures.
        /// </summary>
        public static ExpandedNodeId EncodingId { get; } = new ExpandedNodeId(NumericId, TestStructureNamespace.Uri);

        /// <inheritdoc />
        public override XmlQualifiedName XmlName => new("TestRootStructure", Ua.Namespaces.OpcUaXsd);

        /// <inheritdoc />
        public override IEncodeable CreateInstance()
        {
            return new TestRootStructure();
        }

        /// <inheritdoc/>
        /// <param name="namespaceUris">The namespace URIs.</param>
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
                    },
                    new StructureField
                    {
                        Name = "Target",
                        DataType = Ua.DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar
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
        /// <summary>
        /// The namespace URI identifying the test structure model.
        /// </summary>
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
        /// <summary>
        /// Gets the fluent node-manager builder used to register test bindings.
        /// </summary>
        public NodeManagerBuilder Builder { get; }

        /// <summary>
        /// Gets the namespace index assigned to the test nodes and structure types.
        /// </summary>
        public ushort Ns { get; }

        /// <summary>
        /// Gets the scalar variable targeted by property binding tests.
        /// </summary>
        public BaseDataVariableState ScalarVar { get; }

        /// <summary>
        /// Gets the structured variable targeted by field-path mapping tests.
        /// </summary>
        public BaseDataVariableState StructVar { get; }

        /// <summary>
        /// Gets the root object used to organize test methods and variables.
        /// </summary>
        public BaseObjectState Root { get; }

        /// <summary>
        /// Gets the controllable channel factory used by the binding runtime.
        /// </summary>
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

            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Ua.DataTypeIds.Structure, Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(TestRootType.EncodingId, namespaceUris), Ua.DataTypeIds.Structure);
            typeTable.AddSubtype(
                ExpandedNodeId.ToNodeId(TestChildType.EncodingId, namespaceUris), Ua.DataTypeIds.Structure);
            var ctx = new SystemContext(TelemetryExtensions.InternalOnly__TelemetryHook())
            {
                NamespaceUris = namespaceUris,
                TypeTable = typeTable,
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

        /// <summary>
        /// Gets the textual node identifier of the scalar test variable.
        /// </summary>
        public string ScalarNodeIdText => $"ns={Ns};s=Scalar";

        /// <summary>
        /// Gets the textual node identifier of the structured test variable.
        /// </summary>
        public string StructNodeIdText => $"ns={Ns};s=Struct";

        /// <summary>
        /// Gets the textual node identifier of the root test structure data type.
        /// </summary>
        public string StructTypeNodeIdText => $"ns={Ns};i={TestRootType.NumericId}";

        /// <summary>
        /// Imports the node set into the harness and tracks the imported nodes.
        /// </summary>
        /// <param name="nodeSet">The node set.</param>
        public void Import(UANodeSet nodeSet)
        {
            Builder.Context.EncodeableFactory.Builder.AddOpcUa().Commit();
            var imported = new NodeStateCollection();
            nodeSet.Import(Builder.Context, imported);
            foreach (NodeState node in imported)
            {
                m_nodes.Add(node.NodeId, node);
            }
        }

        /// <summary>
        /// Adds an executable test method with the supplied input and output argument definitions.
        /// </summary>
        /// <param name="name">The name assigned to the node or field used by the operation.</param>
        /// <param name="inputs">The input argument definitions for the test method.</param>
        /// <param name="outputs">The output argument definitions for the test method.</param>
        /// <param name="parent">The optional parent object; the test root is used when omitted.</param>
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

        /// <summary>
        /// Registers a concrete test event type with the supplied supertype.
        /// </summary>
        /// <param name="name">The name assigned to the node or field used by the operation.</param>
        /// <param name="superTypeId">The supertype identifier assigned to the test event type.</param>
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

        /// <summary>
        /// Registers a test object without attaching it to an address-space parent.
        /// </summary>
        /// <param name="name">The name assigned to the node or field used by the operation.</param>
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

        /// <summary>
        /// Creates a compiled form for the operation that targets the given mapping.
        /// </summary>
        /// <param name="operation">The wo t binding capability enum.</param>
        /// <param name="mapping">The WoT target mapping descriptor.</param>
        /// <param name="affordanceName">The affordance name.</param>
        /// <param name="formIndex">The form index.</param>
        /// <param name="executable">The executable flag supplied by the caller.</param>
        public static WotCompiledForm Form(
            WoTBindingCapabilityEnum operation,
            WotTargetMappingDescriptor mapping,
            bool executable = true,
            string affordanceName = "value",
            int formIndex = 0)
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
                "/properties/" + affordanceName + "/forms/" +
                    formIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

        /// <summary>
        /// Creates a test resource binding plan containing the supplied compiled forms.
        /// </summary>
        /// <param name="forms">The compiled forms to include in the test binding plan.</param>
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
