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
    /// Templates emitted by the <see cref="NodeManagerGenerator"/>.
    /// </summary>
    /// <remarks>
    /// All emitted code uses fully-qualified type names so the consuming
    /// project only needs to reference <c>Opc.Ua.Server</c> (which itself
    /// transitively brings in <c>Opc.Ua.Core</c>).
    /// </remarks>
    internal static class NodeManagerTemplates
    {
        /// <summary>
        /// Top-level template for the partial <c>NodeManager</c> class.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.NamespacePrefix}}
            {
                /// <summary>
                /// Source-generated node manager for the
                /// <c>{{Tokens.NamespaceUri}}</c> namespace.
                /// </summary>
                /// <remarks>
                /// Implement <c>partial void Configure(INodeManagerBuilder builder)</c>
                /// in a sibling partial to wire per-node callbacks using the
                /// fluent API in <c>Opc.Ua.Server.Fluent</c>.
                /// </remarks>
                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                public partial class {{Tokens.NodeManagerClassName}} : global::Opc.Ua.Server.CustomNodeManager2
                {
                    private global::Opc.Ua.Server.Fluent.NodeManagerBuilder __m_builder;

                    /// <summary>
                    /// Initializes a new <see cref="{{Tokens.NodeManagerClassName}}"/>.
                    /// </summary>
                    public {{Tokens.NodeManagerClassName}}(
                        global::Opc.Ua.Server.IServerInternal server,
                        global::Opc.Ua.ApplicationConfiguration configuration)
                        : base(server, configuration, {{Tokens.NamespaceUri}})
                    {
                        SystemContext.NodeIdFactory = this;
                    }

                    /// <summary>
                    /// User extensibility hook. Implement in a sibling
                    /// <c>partial</c> to wire callbacks via the fluent builder.
                    /// </summary>
                    partial void Configure(global::Opc.Ua.Server.Fluent.INodeManagerBuilder builder);

                    /// <inheritdoc/>
                    protected override global::Opc.Ua.NodeStateCollection LoadPredefinedNodes(
                        global::Opc.Ua.ISystemContext context)
                    {
                        return new global::Opc.Ua.NodeStateCollection().Add{{Tokens.Namespace}}(context);
                    }

                    /// <inheritdoc/>
                    public override void CreateAddressSpace(
                        global::System.Collections.Generic.IDictionary<global::Opc.Ua.NodeId,
                            global::System.Collections.Generic.IList<global::Opc.Ua.IReference>> externalReferences)
                    {
                        lock (Lock)
                        {
                            base.CreateAddressSpace(externalReferences);

                            ushort __nsIndex = Server.NamespaceUris.GetIndexOrAppend({{Tokens.NamespaceUri}});

                            __m_builder = new global::Opc.Ua.Server.Fluent.NodeManagerBuilder(
                                SystemContext,
                                this,
                                __nsIndex,
                                __FindRootByBrowseName,
                                __FindRootByNodeId);

                            Configure(__m_builder);
                            __m_builder.Seal();

                            foreach (global::Opc.Ua.NodeState __node in PredefinedNodes.Values)
                            {
                                __m_builder.Dispatcher.NotifyNodeAdded(SystemContext, __node);
                            }
                        }
                    }

                    /// <inheritdoc/>
                    protected override void AddPredefinedNode(
                        global::Opc.Ua.ISystemContext context,
                        global::Opc.Ua.NodeState node)
                    {
                        base.AddPredefinedNode(context, node);
                        if (__m_builder is { } __b)
                        {
                            __b.Dispatcher.NotifyNodeAdded(context, node);
                        }
                    }

                    /// <inheritdoc/>
                    protected override void RemovePredefinedNode(
                        global::Opc.Ua.ISystemContext context,
                        global::Opc.Ua.NodeState node,
                        global::System.Collections.Generic.List<global::Opc.Ua.Server.LocalReference> referencesToRemove)
                    {
                        if (__m_builder is { } __b)
                        {
                            __b.Dispatcher.NotifyNodeRemoved(context, node);
                        }
                        base.RemovePredefinedNode(context, node, referencesToRemove);
                    }

                    /// <inheritdoc/>
                    protected override void OnMonitoredItemCreated(
                        global::Opc.Ua.Server.ServerSystemContext context,
                        global::Opc.Ua.Server.NodeHandle handle,
                        global::Opc.Ua.Server.ISampledDataChangeMonitoredItem monitoredItem)
                    {
                        base.OnMonitoredItemCreated(context, handle, monitoredItem);
                        if (__m_builder is { } __b && handle?.Node is { } __node)
                        {
                            __b.Dispatcher.NotifyMonitoredItemCreated(context, __node, monitoredItem);
                        }
                    }

                    private global::Opc.Ua.NodeState __FindRootByBrowseName(global::Opc.Ua.QualifiedName browseName)
                    {
                        if (browseName == null)
                        {
                            return null;
                        }
                        foreach (global::Opc.Ua.NodeState __node in PredefinedNodes.Values)
                        {
                            if (__node.BrowseName == browseName)
                            {
                                return __node;
                            }
                        }
                        return null;
                    }

                    private global::Opc.Ua.NodeState __FindRootByNodeId(global::Opc.Ua.NodeId nodeId)
                    {
                        if (nodeId.IsNull)
                        {
                            return null;
                        }
                        return PredefinedNodes.TryGetValue(nodeId, out global::Opc.Ua.NodeState __node) ? __node : null;
                    }
                }
            }
            """);

        /// <summary>
        /// Top-level template for the generated factory class.
        /// </summary>
        public static readonly TemplateString FactoryFile = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.NamespacePrefix}}
            {
                /// <summary>
                /// Source-generated <see cref="global::Opc.Ua.Server.INodeManagerFactory"/>
                /// for the <c>{{Tokens.NamespaceUri}}</c> namespace.
                /// </summary>
                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                public partial class {{Tokens.NodeManagerFactoryClassName}} : global::Opc.Ua.Server.INodeManagerFactory
                {
                    /// <inheritdoc/>
                    public virtual global::Opc.Ua.ArrayOf<string> NamespacesUris
                        => new global::Opc.Ua.ArrayOf<string>(new string[] { {{Tokens.NamespaceUri}} });

                    /// <inheritdoc/>
                    public virtual global::Opc.Ua.Server.INodeManager Create(
                        global::Opc.Ua.Server.IServerInternal server,
                        global::Opc.Ua.ApplicationConfiguration configuration)
                    {
                        return new {{Tokens.NodeManagerClassName}}(server, configuration);
                    }
                }
            }
            """);
    }
}
