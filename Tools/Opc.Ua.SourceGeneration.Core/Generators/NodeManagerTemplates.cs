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
                public partial class {{Tokens.NodeManagerClassName}} : global::Opc.Ua.Server.AsyncCustomNodeManager
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
                    protected override global::System.Threading.Tasks.ValueTask<global::Opc.Ua.NodeStateCollection> LoadPredefinedNodesAsync(
                        global::Opc.Ua.ISystemContext context,
                        global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        return new global::System.Threading.Tasks.ValueTask<global::Opc.Ua.NodeStateCollection>(
                            new global::Opc.Ua.NodeStateCollection().Add{{Tokens.Namespace}}(context));
                    }

                    /// <inheritdoc/>
                    public override async global::System.Threading.Tasks.ValueTask CreateAddressSpaceAsync(
                        global::System.Collections.Generic.IDictionary<global::Opc.Ua.NodeId,
                            global::System.Collections.Generic.IList<global::Opc.Ua.IReference>> externalReferences,
                        global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

                        ushort __nsIndex = Server.NamespaceUris.GetIndexOrAppend({{Tokens.NamespaceUri}});

                        __m_builder = new global::Opc.Ua.Server.Fluent.NodeManagerBuilder(
                            SystemContext,
                            this,
                            __nsIndex,
                            __FindRootByBrowseName,
                            __FindRootByNodeId,
                            __FindByTypeDefinitionId);

                        Configure(__m_builder);
                        __m_builder.Seal();

                        foreach (global::Opc.Ua.NodeState __node in PredefinedNodes.Values)
                        {
                            __m_builder.Dispatcher.NotifyNodeAdded(SystemContext, __node);
                        }
                    }

                    /// <inheritdoc/>
                    protected override async global::System.Threading.Tasks.ValueTask AddPredefinedNodeAsync(
                        global::Opc.Ua.ISystemContext context,
                        global::Opc.Ua.NodeState node,
                        global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        await base.AddPredefinedNodeAsync(context, node, cancellationToken).ConfigureAwait(false);
                        if (__m_builder is { } __b)
                        {
                            __b.Dispatcher.NotifyNodeAdded(context, node);
                        }
                    }

                    /// <inheritdoc/>
                    protected override async global::System.Threading.Tasks.ValueTask RemovePredefinedNodeAsync(
                        global::Opc.Ua.ISystemContext context,
                        global::Opc.Ua.NodeState node,
                        global::System.Collections.Generic.List<global::Opc.Ua.Server.LocalReference> referencesToRemove,
                        global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        if (__m_builder is { } __b)
                        {
                            __b.Dispatcher.NotifyNodeRemoved(context, node);
                        }
                        await base.RemovePredefinedNodeAsync(context, node, referencesToRemove, cancellationToken).ConfigureAwait(false);
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

                    private global::System.Collections.Generic.IReadOnlyList<global::Opc.Ua.NodeState> __FindByTypeDefinitionId(
                        global::Opc.Ua.NodeId typeDefinitionId)
                    {
                        if (typeDefinitionId == null || typeDefinitionId.IsNull)
                        {
                            return global::System.Array.Empty<global::Opc.Ua.NodeState>();
                        }

                        var __matches = new global::System.Collections.Generic.List<global::Opc.Ua.NodeState>();
                        var __queue = new global::System.Collections.Generic.Queue<global::Opc.Ua.NodeState>();
                        var __seen = new global::System.Collections.Generic.HashSet<global::Opc.Ua.NodeState>();
                        var __scratch = new global::System.Collections.Generic.List<global::Opc.Ua.BaseInstanceState>();
                        foreach (global::Opc.Ua.NodeState __root in PredefinedNodes.Values)
                        {
                            if (__root != null && __seen.Add(__root))
                            {
                                __queue.Enqueue(__root);
                            }
                        }
                        while (__queue.Count > 0)
                        {
                            global::Opc.Ua.NodeState __current = __queue.Dequeue();
                            if (__current is global::Opc.Ua.BaseInstanceState __instance &&
                                __instance.TypeDefinitionId == typeDefinitionId)
                            {
                                __matches.Add(__current);
                            }
                            __scratch.Clear();
                            __current.GetChildren(SystemContext, __scratch);
                            for (int __i = 0; __i < __scratch.Count; __i++)
                            {
                                global::Opc.Ua.BaseInstanceState __child = __scratch[__i];
                                if (__child != null && __seen.Add(__child))
                                {
                                    __queue.Enqueue(__child);
                                }
                            }
                        }
                        return __matches;
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
                public partial class {{Tokens.NodeManagerFactoryClassName}} : global::Opc.Ua.Server.IAsyncNodeManagerFactory
                {
                    /// <inheritdoc/>
                    public virtual global::Opc.Ua.ArrayOf<string> NamespacesUris
                        => new global::Opc.Ua.ArrayOf<string>(new string[] { {{Tokens.NamespaceUri}} });

                    /// <inheritdoc/>
                    public virtual global::System.Threading.Tasks.ValueTask<global::Opc.Ua.Server.IAsyncNodeManager> CreateAsync(
                        global::Opc.Ua.Server.IServerInternal server,
                        global::Opc.Ua.ApplicationConfiguration configuration,
                        global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        return new global::System.Threading.Tasks.ValueTask<global::Opc.Ua.Server.IAsyncNodeManager>(
                            new {{Tokens.NodeManagerClassName}}(server, configuration));
                    }
                }
            }
            """);
    }
}
