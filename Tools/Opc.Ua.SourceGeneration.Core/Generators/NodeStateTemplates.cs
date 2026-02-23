/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class NodeStateTemplates
    {
        /// <summary>
        /// Main file template for predefined nodes code generation
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            {{Tokens.ListOfImports}}

            namespace {{Tokens.NamespacePrefix}}
            {
                {{Tokens.ListOfTypes}}

                {{Tokens.ListOfTypeActivators}}
            }
            """);

        /// <summary>
        /// Main file template for predefined nodes code generation
        /// </summary>
        public static readonly TemplateString Extensions_File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            {{Tokens.ListOfImports}}

            namespace {{Tokens.NamespacePrefix}}
            {
                /// <summary>
                /// Extensions that add functionality from the {{Tokens.NamespaceUri}} namespace.
                /// </summary>
                public static partial class {{Tokens.Namespace}}Extensions
                {
                    /// <summary>
                    /// Creates and returns all node states for the {{Tokens.NamespaceUri}} namespace.
                    /// </summary>
                    /// <param name="nodes">The collection to add the node states to.</param>
                    /// <param name="context">The system context to use for initialization.</param>
                    /// <returns>Original collection with node states added.</returns>
                    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                    public static global::Opc.Ua.NodeStateCollection Add{{Tokens.Namespace}}(
                        this global::Opc.Ua.NodeStateCollection nodes,
                        global::Opc.Ua.ISystemContext context)
                    {
                        {{Tokens.ListOfNodeStateInitializers}}
                        return nodes;
                    }

                    /// <summary>
                    /// Adds all nodestate activators of the {{Tokens.NamespaceUri}}
                    /// namespace to a INodeStateFactoryBuilder.
                    /// </summary>
                    /// <param name="builder">The node state factory builder.</param>
                    /// <returns>The builder passed as parameter for chaining.</returns>
                    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                    public static global::Opc.Ua.INodeStateFactoryBuilder Add{{Tokens.Namespace}}(
                        this global::Opc.Ua.INodeStateFactoryBuilder builder)
                    {
                        {{Tokens.ListOfActivatorRegistrations}}
                        return builder;
                    }

                    {{Tokens.ListOfNodeStateInstanceFactories}}

                    {{Tokens.ListOfNodeStateTypeFactories}}
                }
            }
            """);

        /// <summary>
        /// Object Type node state
        /// </summary>
        public static readonly TemplateString ObjectType_Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.ClassName}} ObjectType state.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ClassName}}State :
                {{Tokens.BaseClassName}}State{{Tokens.BaseT}}
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}State"/> class.
                /// </summary>
                public {{Tokens.ClassName}}State(global::Opc.Ua.NodeState? parent)
                    : base(parent)
                {
                }

                {{Tokens.ListOfProperties}}

                {{Tokens.ListOfNonMandatoryChildren}}

                /// <inheritdoc/>
                public override object Clone()
                {
                    {{Tokens.ClassName}}State clone = new {{Tokens.ClassName}}State(null);
                    CopyTo(clone);
                    return clone;
                }

                /// <inheritdoc/>
                public override bool DeepEquals(global::Opc.Ua.NodeState node)
                {
                    if (!(node is {{Tokens.ClassName}}State state) || !base.DeepEquals(state))
                    {
                        return false;
                    }
                    {{Tokens.ListOfEqualityComparers}}
                    return true;
                }

                /// <inheritdoc/>
                public override int DeepGetHashCode()
                {
                    int hashCode = base.DeepGetHashCode();
                    {{Tokens.ListOfChildHashes}}
                    return hashCode;
                }

                {{Tokens.ListOfChildOperations}}

                /// <inheritdoc/>
                protected override void CopyTo(global::Opc.Ua.NodeState target)
                {
                    if (!(target is {{Tokens.ClassName}}State state))
                    {
                        return;
                    }
                    {{Tokens.ListOfChildCopies}}
                    base.CopyTo(target);
                }

                /// <inheritdoc/>
                protected override global::Opc.Ua.NodeId GetDefaultTypeDefinitionId(
                    global::Opc.Ua.NamespaceTable namespaceUris)
                {
                    return global::Opc.Ua.NodeId.Create(
                        {{Tokens.NamespacePrefix}}.ObjectTypes.{{Tokens.TypeName}},
                        {{Tokens.NamespaceUri}},
                        namespaceUris);
                }

                /// <inheritdoc/>
                protected override void Initialize(global::Opc.Ua.ISystemContext context)
                {
                    base.Initialize(context);
                    Initialize(
                        context,
                        context.CreateInstanceOf{{Tokens.SymbolicId}}());
                    InitializeOptionalChildren(context);
                }

                /// <inheritdoc/>
                protected override void Initialize(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState source)
                {
                    InitializeOptionalChildren(context);
                    base.Initialize(context, source);
                }

                /// <inheritdoc/>
                protected override void InitializeOptionalChildren(
                    global::Opc.Ua.ISystemContext context)
                {
                    base.InitializeOptionalChildren(context);
                    {{Tokens.InitializeOptionalChildren}}
                }

                {{Tokens.ListOfFields}}
            }

            """);

        /// <summary>
        /// Method type node state
        /// </summary>
        public static readonly TemplateString MethodType_Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.ClassName}} MethodType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ClassName}} : global::Opc.Ua.MethodState
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}"/> class.
                /// </summary>
                public {{Tokens.ClassName}}(global::Opc.Ua.NodeState? parent)
                    : base(parent)
                {
                }

                /// <inheritdoc/>
                public new static global::Opc.Ua.NodeState Construct(global::Opc.Ua.NodeState parent)
                {
                    return new {{Tokens.ClassName}}(parent);
                }

                /// <inheritdoc/>
                public {{Tokens.ClassName}}MethodCallHandler? OnCall;

                /// <inheritdoc/>
                public {{Tokens.ClassName}}MethodAsyncCallHandler? OnCallAsync;

                {{Tokens.ListOfProperties}}

                {{Tokens.ListOfNonMandatoryChildren}}

                /// <inheritdoc/>
                public override object Clone()
                {
                    {{Tokens.ClassName}} clone = new {{Tokens.ClassName}}(null);
                    CopyTo(clone);
                    return clone;
                }

                /// <inheritdoc/>
                public override bool DeepEquals(global::Opc.Ua.NodeState node)
                {
                    if (!(node is {{Tokens.ClassName}} state) || !base.DeepEquals(state))
                    {
                        return false;
                    }
                    {{Tokens.ListOfEqualityComparers}}
                    return true;
                }

                /// <inheritdoc/>
                public override int DeepGetHashCode()
                {
                    int hashCode = base.DeepGetHashCode();
                    {{Tokens.ListOfChildHashes}}
                    return hashCode;
                }

                {{Tokens.ListOfChildOperations}}

                /// <inheritdoc/>
                protected override void CopyTo(global::Opc.Ua.NodeState target)
                {
                    if (!(target is {{Tokens.ClassName}} state))
                    {
                        return;
                    }
                    {{Tokens.ListOfChildCopies}}
                    base.CopyTo(target);
                }

                /// <inheritdoc/>
                protected override void Initialize(global::Opc.Ua.ISystemContext context)
                {
                    base.Initialize(context);
                    Initialize(
                        context,
                        context.CreateInstanceOf{{Tokens.SymbolicId}}());
                    InitializeOptionalChildren(context);
                }

                /// <inheritdoc/>
                protected override void InitializeOptionalChildren(
                    global::Opc.Ua.ISystemContext context)
                {
                    base.InitializeOptionalChildren(context);
                    {{Tokens.InitializeOptionalChildren}}
                }

                /// <inheritdoc/>
                protected override global::Opc.Ua.ServiceResult? Call(
                    global::Opc.Ua.ISystemContext _context,
                    global::Opc.Ua.NodeId _objectId,
                    global::Opc.Ua.VariantCollection _inputArguments,
                    global::Opc.Ua.VariantCollection _outputArguments)
                {
                    if (OnCall == null)
                    {
                        return base.Call(_context, _objectId, _inputArguments, _outputArguments);
                    }

                    global::Opc.Ua.ServiceResult? _result = null;
                    {{Tokens.ListOfInputArguments}}
                    {{Tokens.ListOfOutputDeclarations}}

                    if (OnCall != null)
                    {
                        {{Tokens.OnCallImplementation}}
                    }
                    {{Tokens.ListOfOutputArguments}}

                    return _result;
                }

                /// <inheritdoc/>
                protected override async global::System.Threading.Tasks.ValueTask<global::Opc.Ua.ServiceResult?> CallAsync(
                    global::Opc.Ua.ISystemContext _context,
                    global::Opc.Ua.NodeId _objectId,
                    global::Opc.Ua.VariantCollection _inputArguments,
                    global::Opc.Ua.VariantCollection _outputArguments,
                    global::System.Threading.CancellationToken cancellationToken = default)
                {
                    if (OnCall == null && OnCallAsync == null)
                    {
                        return await base.CallAsync(
                            _context,
                            _objectId,
                            _inputArguments,
                            _outputArguments,
                            cancellationToken).ConfigureAwait(false);
                    }

                    {{Tokens.ClassName}}Result? _result = null;
                    {{Tokens.ListOfInputArguments}}

                    if (OnCallAsync != null)
                    {
                        {{Tokens.OnCallAsyncImplementation}}
                    }
                    else if (OnCall != null)
                    {
                        return Call(_context, _objectId, _inputArguments, _outputArguments);
                    }
                    {{Tokens.ListOfOutputArgumentsFromResult}}

                    return _result?.ServiceResult;
                }

                {{Tokens.ListOfFields}}
            }

            /// <summary>
            /// Represents the result of a {{Tokens.ClassName}} method call.
            /// </summary>
            public partial class {{Tokens.ClassName}}Result
            {
                public global::Opc.Ua.ServiceResult ServiceResult { get; set; }
                {{Tokens.ListOfResultProperties}}
            }

            /// <summary>
            /// Handles the {{Tokens.ClassName}} method call.
            /// </summary>
            public delegate global::Opc.Ua.ServiceResult {{Tokens.ClassName}}MethodCallHandler(
                {{Tokens.OnCallDeclaration}}

            /// <summary>
            /// Handles the asynchronous {{Tokens.ClassName}} method call.
            /// </summary>
            public delegate global::System.Threading.Tasks.ValueTask<{{Tokens.ClassName}}Result> {{Tokens.ClassName}}MethodAsyncCallHandler(
                {{Tokens.OnCallAsyncDeclaration}}

            """);

        /// <summary>
        /// Variable type node state
        /// </summary>
        public static readonly TemplateString VariableType_Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.ClassName}} VariableType state.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ClassName}}State :
                {{Tokens.BaseClassName}}State{{Tokens.BaseT}}
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}State"/> class.
                /// </summary>
                public {{Tokens.ClassName}}State(global::Opc.Ua.NodeState? parent) : base(parent)
                {
                }

                {{Tokens.ListOfProperties}}

                {{Tokens.ListOfNonMandatoryChildren}}

                /// <inheritdoc/>
                public override object Clone()
                {
                    {{Tokens.ClassName}}State clone = new {{Tokens.ClassName}}State(null);
                    CopyTo(clone);
                    return clone;
                }

                /// <inheritdoc/>
                public override bool DeepEquals(global::Opc.Ua.NodeState node)
                {
                    if (!(node is {{Tokens.ClassName}}State state) || !base.DeepEquals(state))
                    {
                        return false;
                    }
                    {{Tokens.ListOfEqualityComparers}}
                    return true;
                }

                /// <inheritdoc/>
                public override int DeepGetHashCode()
                {
                    int hashCode = base.DeepGetHashCode();
                    {{Tokens.ListOfChildHashes}}
                    return hashCode;
                }

                {{Tokens.ListOfChildOperations}}

                /// <inheritdoc/>
                protected override void CopyTo(global::Opc.Ua.NodeState target)
                {
                    if (!(target is {{Tokens.ClassName}}State state))
                    {
                        return;
                    }
                    {{Tokens.ListOfChildCopies}}
                    base.CopyTo(target);
                }

                /// <inheritdoc/>
                protected override global::Opc.Ua.NodeId GetDefaultTypeDefinitionId(
                    global::Opc.Ua.NamespaceTable namespaceUris)
                {
                    return global::Opc.Ua.NodeId.Create(
                        {{Tokens.NamespacePrefix}}.VariableTypes.{{Tokens.TypeName}},
                        {{Tokens.NamespaceUri}},
                        namespaceUris);
                }

                /// <inheritdoc/>
                protected override global::Opc.Ua.NodeId GetDefaultDataTypeId(
                    global::Opc.Ua.NamespaceTable namespaceUris)
                {
                    return global::Opc.Ua.NodeId.Create(
                        {{Tokens.DataTypeNamespacePrefix}}.DataTypes.{{Tokens.DataType}},
                        {{Tokens.DataTypeNamespaceUri}},
                        namespaceUris);
                }

                /// <inheritdoc/>
                protected override int GetDefaultValueRank()
                {
                    return {{Tokens.ValueRank}};
                }

                /// <inheritdoc/>
                protected override void Initialize(global::Opc.Ua.ISystemContext context)
                {
                    base.Initialize(context);
                    Initialize(
                        context,
                        context.CreateInstanceOf{{Tokens.SymbolicId}}());
                    // InitializeOptionalChildren(context);
                }

                /// <inheritdoc/>
                protected override void Initialize(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState source)
                {
                    InitializeOptionalChildren(context);
                    base.Initialize(context, source);
                }

                /// <inheritdoc/>
                protected override void InitializeOptionalChildren(
                    global::Opc.Ua.ISystemContext context)
                {
                    base.InitializeOptionalChildren(context);
                    {{Tokens.InitializeOptionalChildren}}
                }

                {{Tokens.ListOfFields}}
            }

            {{Tokens.TypedVariableType}}

            {{Tokens.VariableTypeValue}}

            """);

        /// <summary>
        /// Factory methods for variable states with typed values
        /// </summary>
        public static readonly TemplateString FactoriesForVariableTypeWithTypedValue = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates a new instance of the {{Tokens.ClassName}}State class with a built-in value type
            /// and associated TBuilder to extract the value from the Variant and create a new Variant
            /// from it.
            /// </summary>
            public static {{Tokens.ClassName}}State<T> For<T, TBuilder>(global::Opc.Ua.NodeState? parent)
                where TBuilder : struct, global::Opc.Ua.IVariantBuilder<T>
            {
                return new {{Tokens.ClassName}}State<T>.Implementation<TBuilder>(parent);
            }
            """);

        /// <summary>
        /// Typed variable type node state
        /// </summary>
        public static readonly TemplateString VariableTypeWithTypedValue_Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.ClassName}} VariableType state.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public abstract class {{Tokens.ClassName}}State<T> :
                {{Tokens.ClassName}}State
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}State{T}"/> class.
                /// </summary>
                protected {{Tokens.ClassName}}State(global::Opc.Ua.NodeState? parent)
                    : base(parent)
                {
                }

                /// <summary>
                /// Creates a new instance of the <see cref="{{Tokens.ClassName}}State{T}"/>
                /// class with a built-in value type and associated TBuilder to extract the
                /// value from the Variant and create a new Variant from it.
                /// </summary>
                /// <typeparam name="TBuilder">The builder to use for T</typeparam>
                public static {{Tokens.ClassName}}State<T> With<TBuilder>(
                    global::Opc.Ua.NodeState? parent = null)
                    where TBuilder : struct, global::Opc.Ua.IVariantBuilder<T>
                {
                    return new Implementation<TBuilder>(parent);
                }

                /// <inheritdoc/>
                public new abstract T Value { get; set; }

                /// <inheritdoc/>
                protected override void Initialize(global::Opc.Ua.ISystemContext context)
                {
                    base.Initialize(context);
                    base.Initialize<T>(context);
                }

                /// <inheritdoc/>
                protected override void Initialize(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState source)
                {
                    InitializeOptionalChildren(context);
                    base.Initialize(context, source);
                }

                /// <summary>
                /// Adds builder which extracts T from Variant or creates new Variant with type T
                /// This is public so it can be overridden by classes outside of the namespace
                /// </summary>
                /// <typeparam name="TBuilder">The builder to use for T</typeparam>
                public class Implementation<TBuilder> : {{Tokens.ClassName}}State<T>
                    where TBuilder : struct, global::Opc.Ua.IVariantBuilder<T>
                {
                    /// <summary>
                    /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}State{T}"/> class.
                    /// </summary>
                    public Implementation(global::Opc.Ua.NodeState? parent)
                        : base(parent)
                    {
                        m_builder = new TBuilder();
                        Value = default(T);
                    }

                    /// <inheritdoc/>
                    public override T Value
                    {
                        get => m_builder.GetValue(WrappedValue);
                        set => WrappedValue = m_builder.WithValue(value);
                    }

                    /// <inheritdoc/>
                    public override object Clone()
                    {
                        Implementation<TBuilder> clone = new Implementation<TBuilder>(null);
                        CopyTo(clone);
                        return clone;
                    }

                    private readonly TBuilder m_builder;
                }
            }

            """);

        /// <summary>
        /// Variable type value field methods
        /// </summary>
        public static readonly TemplateString VariableType_ValueMethods = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Handles the read of the {{Tokens.ChildName}} variable.
            /// </summary>
            private global::Opc.Ua.ServiceResult OnRead_{{Tokens.ChildName}}(
                global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState node,
                global::Opc.Ua.NumericRange indexRange,
                global::Opc.Ua.QualifiedName dataEncoding,
                ref global::Opc.Ua.Variant value,
                ref global::Opc.Ua.StatusCode statusCode,
                ref global::System.DateTime timestamp)
            {
                lock (Lock)
                {
                    DoBeforeReadProcessing(context, node);

                    var childVariable = m_variable?.{{Tokens.ChildPath}};
                    if (childVariable != null && global::Opc.Ua.StatusCode.IsBad(childVariable.StatusCode))
                    {
                        value = global::Opc.Ua.Variant.Null;
                        statusCode = childVariable.StatusCode;
                        return new global::Opc.Ua.ServiceResult(statusCode);
                    }

                    if (m_value != null)
                    {
                        value = new global::Opc.Ua.Variant(m_value.{{Tokens.ChildPath}});
                    }

                    var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                    if (childVariable != null && global::Opc.Ua.ServiceResult.IsNotBad(result))
                    {
                        timestamp = childVariable.Timestamp;
                        if (statusCode != childVariable.StatusCode)
                        {
                            statusCode = childVariable.StatusCode;
                            result = new global::Opc.Ua.ServiceResult(statusCode);
                        }
                    }

                    return result;
                }
            }

            /// <summary>
            /// Handles the write of the {{Tokens.ChildName}} variable.
            /// </summary>
            private global::Opc.Ua.ServiceResult OnWrite_{{Tokens.ChildName}}(
                global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState node,
                global::Opc.Ua.NumericRange indexRange,
                global::Opc.Ua.QualifiedName dataEncoding,
                ref global::Opc.Ua.Variant value,
                ref global::Opc.Ua.StatusCode statusCode,
                ref global::System.DateTime timestamp)
            {
                lock (Lock)
                {
                    UpdateChildVariableStatus(m_variable.{{Tokens.ChildPath}}, ref statusCode, ref timestamp);
                    m_value.{{Tokens.ChildPath}} = ({{Tokens.ChildDataType}})Write(value);
                    UpdateParent(context, ref statusCode, ref timestamp);
                }

                return global::Opc.Ua.ServiceResult.Good;
            }

            """);

        /// <summary>
        /// Variable type node state
        /// </summary>
        public static readonly TemplateString VariableTypeValue_Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.ClassName}} VariableType value.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public class {{Tokens.ClassName}}Value : global::Opc.Ua.BaseVariableValue
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ClassName}}Value"/> class.
                /// </summary>
                public {{Tokens.ClassName}}Value(
                    {{Tokens.ClassName}}State variable,
                    {{Tokens.DataType}}? value,
                    object dataLock)
                    : base(dataLock)
                {
                    m_value = value;

                    if (m_value == null)
                    {
                        m_value = new {{Tokens.DataType}}();
                    }

                    Initialize(variable);
                }

                /// <summary>
                /// Gets the variable associated with the value.
                /// </summary>
                public {{Tokens.ClassName}}State Variable => m_variable;

                /// <summary>
                /// Gets or sets the value.
                /// </summary>
                public {{Tokens.DataType}} Value
                {
                    get => m_value;
                    set => m_value = value;
                }

                /// <summary>
                /// Initializes the value.
                /// </summary>
                private void Initialize({{Tokens.ClassName}}State variable)
                {
                    lock (Lock)
                    {
                        m_variable = variable;

                        variable.Value = m_value;

                        variable.OnReadValue = OnReadValue;
                        variable.OnWriteValue = OnWriteValue;

                        global::Opc.Ua.BaseVariableState? instance = null;
                        global::System.Collections.Generic.List<global::Opc.Ua.BaseInstanceState> updateList =
                            new global::System.Collections.Generic.List<global::Opc.Ua.BaseInstanceState>();
                        updateList.Add(variable);

                        {{Tokens.ListOfChildInitializers}}

                        SetUpdateList(updateList);
                    }
                }

                /// <inheritdoc/>
                protected global::Opc.Ua.ServiceResult OnReadValue(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState node,
                    global::Opc.Ua.NumericRange indexRange,
                    global::Opc.Ua.QualifiedName dataEncoding,
                    ref global::Opc.Ua.Variant value,
                    ref global::Opc.Ua.StatusCode statusCode,
                    ref global::System.DateTime timestamp)
                {
                    lock (Lock)
                    {
                        DoBeforeReadProcessing(context, node);

                        if (m_value != null)
                        {
                            value = new global::Opc.Ua.Variant(m_value);
                        }

                        return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
                    }
                }

                /// <inheritdoc/>
                private global::Opc.Ua.ServiceResult OnWriteValue(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState node,
                    global::Opc.Ua.NumericRange indexRange,
                    global::Opc.Ua.QualifiedName dataEncoding,
                    ref global::Opc.Ua.Variant value,
                    ref global::Opc.Ua.StatusCode statusCode,
                    ref global::System.DateTime timestamp)
                {
                    lock (Lock)
                    {
                        if (!value.TryGetStructure(out {{Tokens.DataType}} newValue))
                        {
                            newValue = default;
                        }

                        if (!global::Opc.Ua.CoreUtils.IsEqual(m_value, newValue))
                        {
                            UpdateChildrenChangeMasks(context, ref newValue, ref statusCode, ref timestamp);
                            Timestamp = timestamp;
                            m_value = ({{Tokens.DataType}})Write(newValue);
                            m_variable.UpdateChangeMasks(global::Opc.Ua.NodeStateChangeMasks.Value);
                        }
                    }

                    return global::Opc.Ua.ServiceResult.Good;
                }

                /// <summary>
                /// Updates the change masks for the children.
                /// </summary>
                private void UpdateChildrenChangeMasks(
                    global::Opc.Ua.ISystemContext context,
                    ref {{Tokens.DataType}} newValue,
                    ref global::Opc.Ua.StatusCode statusCode,
                    ref global::System.DateTime timestamp)
                {
                    {{Tokens.ListOfUpdateChildrenChangeMasks}}
                }

                /// <summary>
                /// Updates the parent variable status.
                /// </summary>
                private void UpdateParent(
                    global::Opc.Ua.ISystemContext context,
                    ref global::Opc.Ua.StatusCode statusCode,
                    ref global::System.DateTime timestamp)
                {
                    Timestamp = timestamp;
                    m_variable.UpdateChangeMasks(global::Opc.Ua.NodeStateChangeMasks.Value);
                    m_variable.ClearChangeMasks(context, false);
                }

                /// <summary>
                /// Updates the status of a child variable.
                /// </summary>
                private void UpdateChildVariableStatus(
                    global::Opc.Ua.BaseVariableState child,
                    ref global::Opc.Ua.StatusCode statusCode,
                    ref global::System.DateTime timestamp)
                {
                    if (child == null)
                    {
                        return;
                    }
                    child.StatusCode = statusCode;
                    if (timestamp == global::System.DateTime.MinValue)
                    {
                        timestamp = global::System.DateTime.UtcNow;
                    }
                    child.Timestamp = timestamp;
                }

                {{Tokens.ListOfChildMethods}}

                private {{Tokens.DataType}}? m_value;
                private {{Tokens.ClassName}}State? m_variable;
            }

            """);

        /// <summary>
        /// Initialize optional child
        /// </summary>
        public static readonly TemplateString InitializeOptionalChild = TemplateString.Parse(
            $$"""
            if ({{Tokens.ChildName}} != null)
            {
                {{Tokens.ChildName}}.Create(
                    context,
                    context.Create{{Tokens.SymbolicId}}(this, true));
            }

            """);

        /// <summary>
        /// Property template
        /// </summary>
        public static readonly TemplateString Property = TemplateString.Parse(
            $$"""
            /// <summary>
            /// {{Tokens.ChildName}} property
            /// </summary>
            {{Tokens.AccessorSymbol}} {{Tokens.ClassName}}? {{Tokens.ChildName}}
            {
                get => {{Tokens.FieldName}};
                set
                {
                    if (!object.ReferenceEquals({{Tokens.FieldName}}, value))
                    {
                        ChangeMasks |= global::Opc.Ua.NodeStateChangeMasks.Children;
                    }

                    {{Tokens.FieldName}} = value;
                }
            }

            """);

        /// <summary>
        /// Override property template
        /// </summary>
        public static readonly TemplateString PropertyOverride = TemplateString.Parse(
            $$"""
            /// <summary>
            /// {{Tokens.ChildName}} property
            /// </summary>
            {{Tokens.AccessorSymbol}} {{Tokens.ClassName}}? {{Tokens.ChildName}}
            {
                get => ({{Tokens.ClassName}})base.{{Tokens.ChildName}};
                set => base.{{Tokens.ChildName}} = value;
            }

            """);

        /// <summary>
        /// Add Optional component template
        /// </summary>
        public static readonly TemplateString OptionalMethod = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Add an optional {{Tokens.ChildName}} child
            /// </summary>
            {{Tokens.AccessorSymbol}} {{Tokens.ClassName}} Add{{Tokens.ChildName}}(
                global::Opc.Ua.ISystemContext context)
            {
                {{Tokens.ClassName}} state =
                    context.Create{{Tokens.SymbolicId}}(this, true);
                {{Tokens.ChildName}} = state;
                return state;
            }

            """);

        /// <summary>
        /// Place holder method template
        /// </summary>
        public static readonly TemplateString PlaceHolderMethod = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Add a placeholder {{Tokens.ChildName}} as child
            /// </summary>
            {{Tokens.AccessorSymbol}} {{Tokens.ClassName}} Add{{Tokens.ChildName}}(
                global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.QualifiedName browseName)
            {
                {{Tokens.ClassName}} state =
                    context.Create{{Tokens.SymbolicId}}(this, browseName);
                ReplaceChild(context, state);
                return state;
            }

            """);

        /// <summary>
        /// Find child methods
        /// </summary>
        public static readonly TemplateString ChildOperations = TemplateString.Parse(
            $$"""
            /// <inheritdoc/>
            public override void GetChildren(
                global::Opc.Ua.ISystemContext context,
                global::System.Collections.Generic.IList<global::Opc.Ua.BaseInstanceState> children)
            {
                {{Tokens.ListOfFindChildren}}

                base.GetChildren(context, children);
            }

            {{Tokens.ListOfCreateOrReplaceChild}}

            /// <inheritdoc/>
            protected override global::Opc.Ua.BaseInstanceState? FindChild(
                global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.QualifiedName browseName,
                bool createOrReplace,
                global::Opc.Ua.BaseInstanceState replacement)
            {
                if (browseName.IsNull)
                {
                    return null;
                }

                global::Opc.Ua.BaseInstanceState? instance = null;

                switch (browseName.Name)
                {
                    {{Tokens.ListOfFindChildCase}}
                }

                if (instance != null)
                {
                    return instance;
                }

                return base.FindChild(context, browseName, createOrReplace, replacement);
            }

            /// <inheritdoc/>
            protected override void RemoveExplicitlyDefinedChild(global::Opc.Ua.BaseInstanceState child)
            {
                {{Tokens.ListOfRemoveChild}}

                base.RemoveExplicitlyDefinedChild(child);
            }

            """);

        /// <summary>
        /// Find child case template
        /// </summary>
        public static readonly TemplateString FindChildCase = TemplateString.Parse(
            $$"""
            case {{Tokens.BrowseNameNamespacePrefix}}.BrowseNames.{{Tokens.ChildName}}:
            {
                instance = !createOrReplace ?
                    {{Tokens.ChildName}} : CreateOrReplace{{Tokens.ChildName}}(context, replacement);
                break;
            }

            """);

        /// <summary>
        /// Create or replace a child on the class
        /// </summary>
        public static readonly TemplateString CreateOrReplaceChild = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Create or replace the mandatory {{Tokens.ChildName}} child
            /// </summary>
            {{Tokens.AccessorSymbol}} {{Tokens.ClassName}} CreateOrReplace{{Tokens.ChildName}}(
                global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.BaseInstanceState replacement)
            {
                if ({{Tokens.ChildName}} == null)
                {
                    {{Tokens.ClassName}}? child = replacement as {{Tokens.ClassName}};
                    if (child == null)
                    {
                        child = {{Tokens.ClassFactory}}(this);
                        if (replacement != null)
                        {
                            child.Create(context, replacement);
                        }
                    }
                    {{Tokens.ChildName}} = child;
                }
                return {{Tokens.ChildName}};
            }

            """);

        /// <summary>
        /// Find children method template
        /// </summary>
        public static readonly TemplateString FindChildren = TemplateString.Parse(
            $$"""
            if ({{Tokens.FieldName}} != null)
            {
                children.Add({{Tokens.FieldName}});
            }

            """);

        /// <summary>
        /// Remove child template
        /// </summary>
        public static readonly TemplateString RemoveChild = TemplateString.Parse(
            $$"""
            if (object.ReferenceEquals({{Tokens.FieldName}}, child))
            {
                {{Tokens.FieldName}} = null;
                return;
            }

            """);

        /// <summary>
        /// Clone child template
        /// </summary>
        public static readonly TemplateString CloneChild = TemplateString.Parse(
            $$"""
            state.{{Tokens.FieldName}} =
                ({{Tokens.ClassName}}){{Tokens.FieldName}}?.Clone();
            """);

        /// <summary>
        /// Compare child template
        /// </summary>
        public static readonly TemplateString CompareChild = TemplateString.Parse(
            $$"""
            if (!global::System.Collections.Generic.EqualityComparer<{{Tokens.ClassName}}>
                .Default
                .Equals(state.{{Tokens.FieldName}}, {{Tokens.FieldName}}))
            {
                return false;
            }
            """);

        /// <summary>
        /// Hash child template
        /// </summary>
        public static readonly TemplateString HashChild = TemplateString.Parse(
            $$"""
            hash = (hash * 16777619) ^
                global::System.Collections.Generic.EqualityComparer<{{Tokens.ClassName}}>
                .Default
                .GetHashCode(state.{{Tokens.FieldName}}));
            """);

        /// <summary>
        /// Encodeable activator
        /// </summary>
        public static readonly TemplateString ActivatorClass = TemplateString.Parse(
            $$"""
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public sealed class {{Tokens.StateClassName}}Activator : global::Opc.Ua.NodeStateActivator
            {
                /// <summary>
                /// The singleton instance of the activator.
                /// </summary>
                public static readonly {{Tokens.StateClassName}}Activator Instance
                    = new {{Tokens.StateClassName}}Activator();

                /// <inheritdoc/>
                protected override global::Opc.Ua.NodeState CreateInstance(
                    global::Opc.Ua.ISystemContext context,
                    global::Opc.Ua.NodeState parent)
                {
                    return context.CreateInstanceOf{{Tokens.SymbolicId}}(parent);
                }
            }
            """);

        /// <summary>
        /// Nodestate activator registration
        /// </summary>
        public static readonly TemplateString ActivatorRegistration = TemplateString.Parse(
            $$"""
            // Register node state factory for {{Tokens.BrowseName}} {{Tokens.NodeClass}}
            builder = builder.RegisterType(
                {{Tokens.NodeClass}}Ids.{{Tokens.SymbolicId}},
                {{Tokens.StateClassName}}Activator.Instance);
            """);

        /// <summary>
        /// Template for a single node state creation call
        /// </summary>
        public static readonly TemplateString Add = TemplateString.Parse(
            $$"""
            // Add {{Tokens.SymbolicName}} predefined node
            {
                global::Opc.Ua.NodeState state = Create{{Tokens.SymbolicId}}(context);
                state.CreateAsPredefinedNode(context);
                nodes.Add(state);
            }
            """);

        /// <summary>
        /// Template for BaseObjectTypeState creation
        /// </summary>
        public static readonly TemplateString Create_ObjectType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} ObjectType node state.
            /// </summary>
            internal static global::Opc.Ua.BaseObjectTypeState Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = false)
            {
                var state = new global::Opc.Ua.BaseObjectTypeState();
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.SuperTypeId = {{Tokens.SuperTypeId}};
                state.IsAbstract = {{Tokens.IsAbstract}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for BaseDataVariableTypeState creation
        /// </summary>
        public static readonly TemplateString Create_VariableType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} VariableType node state.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}();
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.SuperTypeId = {{Tokens.SuperTypeId}};
                state.IsAbstract = {{Tokens.IsAbstract}};
                state.DataType = {{Tokens.DataTypeIdConstant}};
                state.ValueRank = {{Tokens.ValueRank}};
                {{Tokens.ArrayDimensions}}
                {{Tokens.ValueCode}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for ReferenceTypeState creation
        /// </summary>
        public static readonly TemplateString Create_ReferenceType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} ReferenceType node state.
            /// </summary>
            internal static global::Opc.Ua.ReferenceTypeState Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = false)
            {
                var state = new global::Opc.Ua.ReferenceTypeState();
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.SuperTypeId = {{Tokens.SuperTypeId}};
                state.IsAbstract = {{Tokens.IsAbstract}};
                state.Symmetric = {{Tokens.SymmetricValue}};
                {{Tokens.InverseNameValue}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for DataTypeState creation
        /// </summary>
        public static readonly TemplateString Create_DataType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} DataType node state.
            /// </summary>
            internal static global::Opc.Ua.DataTypeState Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = false)
            {
                var state = new global::Opc.Ua.DataTypeState();
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.SuperTypeId = {{Tokens.SuperTypeId}};
                state.IsAbstract = {{Tokens.IsAbstract}};
                state.DataTypeDefinition = {{Tokens.DataTypeDefinition}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for BaseObjectState creation
        /// </summary>
        public static readonly TemplateString Create_Object = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} Object node state.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = true)
            {
                var state = {{Tokens.StateClassFactory}}(null);
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.NumericId = {{Tokens.NumericIdValue}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                // {{Tokens.ModellingRuleId}}
                state.EventNotifier = {{Tokens.EventNotifier}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for BaseVariableState (Property or DataVariable) creation
        /// </summary>
        public static readonly TemplateString Create_Variable = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} Variable node state.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                bool forInstance = true)
            {
                var state = {{Tokens.StateClassFactory}}(null);
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.NumericId = {{Tokens.NumericIdValue}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText(
                    {{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                // {{Tokens.ModellingRuleId}}
                state.DataType = {{Tokens.DataTypeIdConstant}};
                state.ValueRank = {{Tokens.ValueRank}};
                {{Tokens.ArrayDimensions}}
                state.AccessLevel = {{Tokens.AccessLevelValue}};
                state.UserAccessLevel = {{Tokens.UserAccessLevelValue}};
                state.MinimumSamplingInterval = {{Tokens.MinimumSamplingIntervalValue}};
                state.Historizing = {{Tokens.HistorizingValue}};
                {{Tokens.ValueCode}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for ViewState creation
        /// </summary>
        public static readonly TemplateString Create_View = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} View node state.
            /// </summary>
            internal static global::Opc.Ua.ViewState Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context)
            {
                var state = new global::Opc.Ua.ViewState();
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText({{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.EventNotifier = {{Tokens.EventNotifier}};
                state.ContainsNoLoops = {{Tokens.ContainsNoLoopsValue}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for MethodState creation
        /// </summary>
        public static readonly TemplateString Create_InstanceOfMethodType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates an instance of the {{Tokens.SymbolicName}} Method node state.
            /// </summary>
            public static {{Tokens.StateClassName}} CreateInstanceOf{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent = null,
                global::Opc.Ua.QualifiedName browseName = default)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                }
                else
                {
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }
                {{Tokens.MethodDeclarationId}}
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.Executable = {{Tokens.ExecutableValue}};
                state.UserExecutable = {{Tokens.ExecutableValue}};
                {{Tokens.ListOfInputArguments}}
                {{Tokens.ListOfOutputArguments}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                return state;
            }

            """);

        /// <summary>
        /// Template for object type instance creation (instantiate an object type)
        /// </summary>
        public static readonly TemplateString Create_InstanceOfObjectType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates an instance of the {{Tokens.TypeName}} type.
            /// </summary>
            public static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent = null,
                global::Opc.Ua.QualifiedName browseName = default)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                }
                else
                {
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.EventNotifier = {{Tokens.EventNotifier}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                return state;
            }

            """);

        /// <summary>
        /// Template variable type instance creation (instantiate a variable type)
        /// </summary>
        public static readonly TemplateString Create_InstanceOfVariableType = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates an instance of the {{Tokens.TypeName}} variable type.
            /// </summary>
            public static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent = null,
                global::Opc.Ua.QualifiedName browseName = default)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                }
                else
                {
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }

                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                {{Tokens.ModellingRuleId}}
                state.DataType = {{Tokens.DataTypeIdConstant}};
                state.ValueRank = {{Tokens.ValueRank}};
                {{Tokens.ArrayDimensions}}
                state.AccessLevel = {{Tokens.AccessLevelValue}};
                state.UserAccessLevel = {{Tokens.UserAccessLevelValue}};
                state.MinimumSamplingInterval = {{Tokens.MinimumSamplingIntervalValue}};
                state.Historizing = {{Tokens.HistorizingValue}};
                {{Tokens.ValueCode}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                return state;
            }

            """);

        /// <summary>
        /// Template for child object state creation
        /// </summary>
        public static readonly TemplateString Create_ChildObject = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates an instance of the {{Tokens.SymbolicName}} object node child component.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText(
                    {{Tokens.DisplayName}});
                state.NumericId = {{Tokens.NumericIdValue}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.EventNotifier = {{Tokens.EventNotifier}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for child variable state creation
        /// </summary>
        public static readonly TemplateString Create_ChildVariable = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} child Variable node child component.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.NumericId = {{Tokens.NumericIdValue}};
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText(
                    {{Tokens.DisplayName}});
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.DataType = {{Tokens.DataTypeIdConstant}};
                state.ValueRank = {{Tokens.ValueRank}};
                {{Tokens.ArrayDimensions}}
                state.AccessLevel = {{Tokens.AccessLevelValue}};
                state.UserAccessLevel = {{Tokens.UserAccessLevelValue}};
                state.MinimumSamplingInterval = {{Tokens.MinimumSamplingIntervalValue}};
                state.Historizing = {{Tokens.HistorizingValue}};
                {{Tokens.ValueCode}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                {{Tokens.MethodDeclarationId}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for child method state creation
        /// </summary>
        public static readonly TemplateString Create_ChildMethod = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} child Method node child component.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                state.NodeId = {{Tokens.NodeIdConstant}};
                {{Tokens.MethodDeclarationId}}
                state.BrowseName = new global::Opc.Ua.QualifiedName(
                    {{Tokens.BrowseNameSymbol}},
                    context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                state.DisplayName = new global::Opc.Ua.LocalizedText(
                    {{Tokens.DisplayName}});
                state.NumericId = {{Tokens.NumericIdValue}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.Executable = {{Tokens.ExecutableValue}};
                state.UserExecutable = {{Tokens.ExecutableValue}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for child object state creation
        /// </summary>
        public static readonly TemplateString Create_ChildObject_Placeholder = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates an instance of the {{Tokens.SymbolicName}} object placeholder child.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                global::Opc.Ua.QualifiedName browseName = default,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                    forInstance = true;
                }
                else
                {
                    // Create the state for the type parent
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }
                state.NumericId = {{Tokens.NumericIdValue}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.EventNotifier = {{Tokens.EventNotifier}};
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for child variable state creation
        /// </summary>
        public static readonly TemplateString Create_ChildVariable_Placeholder = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} child Variable object placeholder child.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                global::Opc.Ua.QualifiedName browseName = default,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                    forInstance = true;
                }
                else
                {
                    // Create the state for the type parent
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }
                state.NumericId = {{Tokens.NumericIdValue}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.TypeDefinitionId = {{Tokens.TypeDefinitionId}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                {{Tokens.ModellingRuleId}}
                state.DataType = {{Tokens.DataTypeIdConstant}};
                state.ValueRank = {{Tokens.ValueRank}};
                {{Tokens.ArrayDimensions}}
                state.AccessLevel = {{Tokens.AccessLevelValue}};
                state.UserAccessLevel = {{Tokens.UserAccessLevelValue}};
                state.MinimumSamplingInterval = {{Tokens.MinimumSamplingIntervalValue}};
                state.Historizing = {{Tokens.HistorizingValue}};
                {{Tokens.ValueCode}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for child method state creation
        /// </summary>
        public static readonly TemplateString Create_ChildMethod_Placeholder = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Creates the {{Tokens.SymbolicName}} child Method object placeholder child.
            /// </summary>
            internal static {{Tokens.StateClassName}} Create{{Tokens.SymbolicId}}(
                this global::Opc.Ua.ISystemContext context,
                global::Opc.Ua.NodeState parent,
                global::Opc.Ua.QualifiedName browseName = default,
                bool forInstance = false)
            {
                var state = {{Tokens.StateClassFactory}}(parent);
                state.NodeId = {{Tokens.NodeIdConstant}};
                if (!browseName.IsNull)
                {
                    state.SymbolicName = browseName.Name;
                    state.BrowseName = browseName;
                    state.DisplayName = new global::Opc.Ua.LocalizedText(browseName.Name);
                    forInstance = true;
                }
                else
                {
                    // Create the state for the type parent
                    state.SymbolicName = {{Tokens.SymbolicNameSymbol}};
                    state.BrowseName = new global::Opc.Ua.QualifiedName(
                        {{Tokens.BrowseNameSymbol}},
                        context.NamespaceUris.GetIndexOrAppend({{Tokens.BrowseNameNamespaceUri}}));
                    state.DisplayName = new global::Opc.Ua.LocalizedText(
                        {{Tokens.DisplayName}});
                }
                state.NumericId = {{Tokens.NumericIdValue}};
                {{Tokens.DescriptionValue}}
                state.WriteMask = {{Tokens.WriteMaskValue}};
                state.UserWriteMask = {{Tokens.UserWriteMaskValue}};
                state.ReferenceTypeId = {{Tokens.ReferenceTypeId}};
                state.Executable = {{Tokens.ExecutableValue}};
                state.UserExecutable = {{Tokens.ExecutableValue}};
                {{Tokens.MethodDeclarationId}}
                {{Tokens.ReleaseStatusValue}}
                {{Tokens.CategoriesValue}}
                {{Tokens.SpecificationValue}}
                {{Tokens.AccessRestrictionsValue}}
                {{Tokens.ListOfRolePermissions}}
                {{Tokens.ListOfReferences}}
                {{Tokens.ListOfChildNodeStates}}
                if (!forInstance)
                {
                    {{Tokens.ModellingRuleId}}
                    {{Tokens.ListOfOptionalChildNodeStates}}
                }
                return state;
            }

            """);

        /// <summary>
        /// Template for role permissions collection initialization
        /// </summary>
        public static readonly TemplateString ListOfRolePermissions = TemplateString.Parse(
            $$"""
            state.RolePermissions = new global::Opc.Ua.RolePermissionTypeCollection
            {
                {{Tokens.ListOfRolePermissions}}
            };

            """);

        /// <summary>
        /// Template for role permission
        /// </summary>
        public static readonly TemplateString RolePermission = TemplateString.Parse(
            $$"""
            new global::Opc.Ua.RolePermissionType
            {
                RoleId = {{Tokens.RoleIdConstant}},
                Permissions = {{Tokens.PermissionsValue}}
            },

            """);

        /// <summary>
        /// Template for description assignment
        /// </summary>
        public static readonly TemplateString Description = TemplateString.Parse(
            $$"""
            state.Description = new global::Opc.Ua.LocalizedText({{Tokens.DescriptionValue}});

            """);

        /// <summary>
        /// Template for inverse name assignment
        /// </summary>
        public static readonly TemplateString InverseName = TemplateString.Parse(
            $$"""
            state.InverseName = new global::Opc.Ua.LocalizedText({{Tokens.InverseNameValue}});

            """);

        /// <summary>
        /// Template for release status assignment
        /// </summary>
        public static readonly TemplateString ReleaseStatus = TemplateString.Parse(
            $$"""
            state.ReleaseStatus = {{Tokens.ReleaseStatusValue}};

            """);

        /// <summary>
        /// Template for categories assignment
        /// </summary>
        public static readonly TemplateString Categories = TemplateString.Parse(
            $$"""
            state.Categories = {{Tokens.CategoriesValue}};

            """);

        /// <summary>
        /// Template for specification assignment
        /// </summary>
        public static readonly TemplateString Specification = TemplateString.Parse(
            $$"""
            state.Specification = {{Tokens.SpecificationValue}};

            """);

        /// <summary>
        /// Template for access restrictions assignment
        /// </summary>
        public static readonly TemplateString AccessRestrictions = TemplateString.Parse(
            $$"""
            state.AccessRestrictions = {{Tokens.AccessRestrictionsValue}};

            """);

        /// <summary>
        /// Template for modelling rule assignment
        /// </summary>
        public static readonly TemplateString ModellingRuleId = TemplateString.Parse(
            $$"""
            state.ModellingRuleId = {{Tokens.ModellingRuleId}};

            """);

        /// <summary>
        /// Template for method declaration id assignment
        /// </summary>
        public static readonly TemplateString MethodDeclarationId = TemplateString.Parse(
            $$"""
            state.MethodDeclarationId = {{Tokens.MethodDeclarationId}};

            """);

        /// <summary>
        /// Template for array dimensions assignment
        /// </summary>
        public static readonly TemplateString ArrayDimensions = TemplateString.Parse(
            $$"""
            state.ArrayDimensions = {{Tokens.ArrayDimensions}};

            """);

        /// <summary>
        /// Template for value assignment
        /// </summary>
        public static readonly TemplateString VariantArrayOfValue = TemplateString.Parse(
            $$"""
            state.WrappedValue = global::Opc.Ua.Variant.FromStructure(
                global::Opc.Ua.ArrayOf.ToArrayOf(new {{Tokens.DataType}}[]
                {
                    {{Tokens.ListOfValues}}
                }));
            """);

        /// <summary>
        /// Template for value assignment
        /// </summary>
        public static readonly TemplateString ArgumentValue = TemplateString.Parse(
            $$"""
            new global::Opc.Ua.Argument
            {
                Name = {{Tokens.Name}},
                DataType = {{Tokens.DataType}},
                ValueRank = {{Tokens.ValueRank}},
                ArrayDimensions = {{Tokens.ArrayDimensions}},
                Description = {{Tokens.Description}}
            },
            """);
    }
}
