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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits a typed, intellisense-aware fluent builder facade for the
    /// model's predefined-instance tree. Drops in alongside the existing
    /// <see cref="NodeManagerGenerator"/> output so users can write
    /// <c>b.Boilers.Boiler__1.DrumX001.LIX001.Output.OnRead(...)</c>
    /// inside their <c>Configure</c> partial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One typed wrapper class is emitted per instance design that lives
    /// in the manager's predefined-instance tree. Each wrapper exposes
    /// typed accessor properties for its direct children (resolved via
    /// the design <c>Hierarchy</c>):
    /// </para>
    /// <list type="bullet">
    ///   <item><description>variables → <c>IVariableBuilder&lt;T&gt;</c>;</description></item>
    ///   <item><description>methods → a per-method wrapper class with typed
    ///     <c>OnCall(Func&lt;…&gt;)</c> overloads;</description></item>
    ///   <item><description>objects → the matching child instance wrapper.</description></item>
    /// </list>
    /// <para>
    /// A top-level <c>I{Manager}NodeManagerBuilder</c> interface (extending
    /// <c>Opc.Ua.Server.Fluent.INodeManagerBuilder</c>) carries one
    /// accessor per top-level predefined instance. The
    /// node-manager generator emits both
    /// <c>Configure(INodeManagerBuilder)</c> and
    /// <c>Configure(I{Manager}NodeManagerBuilder)</c> partials so existing
    /// implementations continue to work unchanged.
    /// </para>
    /// </remarks>
    internal sealed class FluentBuilderGenerator : IGenerator
    {
        /// <summary>
        /// Optional override for the manager class name (matches the value
        /// passed to <see cref="NodeManagerGenerator.OverrideClassName"/>).
        /// Defaults to <c>{Prefix}NodeManager</c>.
        /// </summary>
        public string OverrideManagerClassName { get; init; }

        /// <summary>
        /// Optional override for the manager namespace (matches the value
        /// passed to <see cref="NodeManagerGenerator.OverrideNamespace"/>).
        /// Defaults to the model's target-namespace prefix.
        /// </summary>
        public string OverrideManagerNamespace { get; init; }

        /// <summary>
        /// Initializes a new <see cref="FluentBuilderGenerator"/>.
        /// </summary>
        public FluentBuilderGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            string nsPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string typeStem = nsPrefix.Replace(".", string.Empty, StringComparison.Ordinal);
            string outputNamespace = string.IsNullOrEmpty(OverrideManagerNamespace)
                ? nsPrefix
                : OverrideManagerNamespace;
            string managerClassName = string.IsNullOrEmpty(OverrideManagerClassName)
                ? typeStem + "NodeManager"
                : OverrideManagerClassName;
            string interfaceName = "I" + managerClassName + "Builder";
            string typedBuilderClassName = managerClassName + "TypedBuilder";

            // Discover the top-level predefined instances. We always emit
            // the typed manager interface and proxy class — even for
            // models with no predefined instances — because the
            // source-generated NodeManager partial unconditionally
            // references them.
            List<InstanceDesign> roots = GetTopLevelInstances();

            // Walk every instance under each root and assemble the wrapper
            // class metadata. m_wrappers is keyed by SymbolicId path so
            // child accessors can resolve their target wrapper by id.
            m_wrappers = [];
            m_methodWrappers = [];
            foreach (InstanceDesign root in roots)
            {
                CollectInstanceWrappers(root);
            }

            // Detect naming collisions per containing wrapper. Fail with a
            // diagnostic (mirrored as an InvalidOperationException since we
            // are running outside the Roslyn diagnostic pipeline here).
            ValidateNoCollisions();

            string fileStem = string.IsNullOrEmpty(OverrideManagerClassName)
                ? nsPrefix
                : OverrideManagerClassName;
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.FluentBuilders.g.cs", fileStem));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, FluentBuilderTemplates.File);

            template.AddReplacement(Tokens.NamespacePrefix, outputNamespace);

            // Render the typed manager interface, the typed manager
            // implementation, and one wrapper class per instance / method
            // into the body slot. The body is emitted by a single
            // load-template callback (the templating engine treats the
            // returned null TemplateString as "nothing more to emit").
            object[] bodyTargets = ["body"];
            template.AddReplacement(
                Tokens.ListOfTypes,
                bodyTargets,
                onLoad: ctx =>
                {
                    EmitManagerInterface(ctx.Out, interfaceName, roots);
                    EmitTypedManagerImpl(
                        ctx.Out,
                        interfaceName,
                        typedBuilderClassName,
                        managerClassName,
                        roots);

                    foreach (InstanceWrapper wrapper in m_wrappers.Values
                        .OrderBy(w => w.ClassName, StringComparer.Ordinal))
                    {
                        EmitInstanceWrapper(ctx.Out, wrapper);
                    }

                    foreach (MethodWrapper method in m_methodWrappers.Values
                        .OrderBy(w => w.ClassName, StringComparer.Ordinal))
                    {
                        EmitMethodWrapper(ctx.Out, method);
                    }

                    return null;
                });

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        // ============================================================
        // Discovery
        // ============================================================

        /// <summary>
        /// Returns the model's top-level instance designs — those whose
        /// <see cref="NodeDesign.Parent"/> is null and which sit in the
        /// manager's predefined-instance tree (instances, not types).
        /// </summary>
        private List<InstanceDesign> GetTopLevelInstances()
        {
            var result = new List<InstanceDesign>();
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is not InstanceDesign instance)
                {
                    continue;
                }
                if (instance.Parent != null)
                {
                    continue;
                }
                if (m_context.ModelDesign.IsExcluded(instance))
                {
                    continue;
                }
                if (instance.NotInAddressSpace)
                {
                    continue;
                }
                if (instance.IsDeclaration)
                {
                    continue;
                }
                if (instance.Hierarchy == null)
                {
                    continue;
                }
                result.Add(instance);
            }
            result.Sort(static (a, b) => string.CompareOrdinal(
                a.SymbolicId?.Name,
                b.SymbolicId?.Name));
            return result;
        }

        /// <summary>
        /// Walks <paramref name="root"/>'s hierarchy and registers a
        /// wrapper for every non-method instance plus a method wrapper for
        /// every method.
        /// </summary>
        private void CollectInstanceWrappers(InstanceDesign root)
        {
            if (root.Hierarchy == null)
            {
                return;
            }

            // Build a parent-path → list of direct children mapping. The
            // hierarchy keys are constructed by joining segments with
            // <see cref="NodeDesign.PathChar"/>; segment names themselves
            // can contain underscores (e.g. <c>Boiler__1</c> for the
            // browse name <c>Boiler #1</c>) so we cannot naively split on
            // the path char. Instead we derive the parent path by stripping
            // the child node's own <c>SymbolicName.Name</c> (plus the
            // separator) from its <c>RelativePath</c> — the same trick
            // used by <see cref="NodeStateGenerator"/>.
            var directChildren = new Dictionary<string, List<HierarchyNode>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, HierarchyNode> entry in root.Hierarchy.Nodes)
            {
                string path = entry.Key ?? string.Empty;
                if (path.Length == 0)
                {
                    continue;
                }
                HierarchyNode hnode = entry.Value;
                if (hnode?.Instance?.SymbolicName?.Name is not string segment ||
                    segment.Length == 0)
                {
                    continue;
                }
                int trimLen = segment.Length + 1;
                string parent;
                if (path.Length == segment.Length)
                {
                    // Top-level child of the root (no preceding parent path).
                    parent = string.Empty;
                }
                else if (path.Length < trimLen)
                {
                    // Malformed — skip.
                    continue;
                }
                else
                {
                    parent = path[..^trimLen];
                }
                if (!directChildren.TryGetValue(parent, out List<HierarchyNode> bucket))
                {
                    bucket = [];
                    directChildren[parent] = bucket;
                }
                bucket.Add(hnode);
            }

            // Emit one wrapper per non-method instance (root + descendants).
            foreach (KeyValuePair<string, HierarchyNode> entry in root.Hierarchy.Nodes)
            {
                if (entry.Value?.Instance is not NodeDesign nd)
                {
                    continue;
                }
                if (m_context.ModelDesign.IsExcluded(nd))
                {
                    continue;
                }
                // Skip method instances — methods get their own wrapper
                // class, registered separately below.
                if (entry.Value.Instance is MethodDesign method)
                {
                    RegisterMethodWrapper(root, entry.Key, method);
                    continue;
                }
                // Skip variables — they are surfaced as IVariableBuilder<T>
                // accessors on their parent wrapper, not as standalone
                // wrapper classes.
                if (entry.Value.Instance is VariableDesign)
                {
                    continue;
                }
                if (entry.Value.Instance is not ObjectDesign)
                {
                    continue;
                }
                RegisterInstanceWrapper(root, entry.Key, entry.Value, directChildren);
            }
        }

        /// <summary>
        /// Registers an <see cref="InstanceWrapper"/> for the supplied
        /// node. Idempotent — if the wrapper has already been registered
        /// (e.g. via a sibling root) the existing entry is reused.
        /// </summary>
        private void RegisterInstanceWrapper(
            InstanceDesign root,
            string relativePath,
            HierarchyNode hnode,
            Dictionary<string, List<HierarchyNode>> directChildren)
        {
            string key = ComposeKey(root, relativePath);
            if (m_wrappers.ContainsKey(key))
            {
                return;
            }

            string className = ComposeClassName(root, relativePath, suffix: "Builder");
            string nsUri = ResolveNodeBrowseNamespace(hnode.Instance);
            var wrapper = new InstanceWrapper
            {
                Key = key,
                ClassName = className,
                NodeStateType = ResolveStateClrType(hnode.Instance),
                BrowseNamespaceUri = nsUri,
                Children = []
            };

            // Children resolved relative to this node.
            if (directChildren.TryGetValue(relativePath, out List<HierarchyNode> kids))
            {
                foreach (HierarchyNode kid in kids)
                {
                    if (kid?.Instance == null || m_context.ModelDesign.IsExcluded(kid.Instance))
                    {
                        continue;
                    }
                    string childKey = ComposeKey(root, kid.RelativePath);
                    string accessorName = GetAccessorName(kid.Instance);
                    string browseName = GetBrowseName(kid.Instance);
                    string browseNsUri = ResolveNodeBrowseNamespace(kid.Instance);
                    var child = new ChildAccessor
                    {
                        AccessorName = accessorName,
                        BrowseName = browseName,
                        BrowseNamespaceUri = browseNsUri
                    };

                    switch (kid.Instance)
                    {
                        case VariableDesign var:
                            child.Kind = ChildKind.Variable;
                            child.ValueClrType = GetVariableValueClrType(var);
                            break;
                        case MethodDesign method:
                            child.Kind = ChildKind.Method;
                            child.WrapperClassName = ComposeClassName(
                                root, kid.RelativePath, suffix: "MethodBuilder");
                            break;
                        case ObjectDesign:
                            child.Kind = ChildKind.Object;
                            child.WrapperClassName = ComposeClassName(
                                root, kid.RelativePath, suffix: "Builder");
                            child.ChildKey = childKey;
                            child.ChildStateType = ResolveStateClrType(kid.Instance);
                            break;
                        default:
                            // Properties / unknowns: skip for v1.
                            continue;
                    }
                    wrapper.Children.Add(child);
                }
            }

            m_wrappers[key] = wrapper;
        }

        /// <summary>
        /// Registers a <see cref="MethodWrapper"/> for the supplied method
        /// design. Resolves typed argument shapes from
        /// <see cref="MethodDesign.InputArguments"/> /
        /// <see cref="MethodDesign.OutputArguments"/>.
        /// </summary>
        private void RegisterMethodWrapper(
            InstanceDesign root,
            string relativePath,
            MethodDesign method)
        {
            string key = ComposeKey(root, relativePath);
            if (m_methodWrappers.ContainsKey(key))
            {
                return;
            }

            string className = ComposeClassName(root, relativePath, suffix: "MethodBuilder");
            var wrapper = new MethodWrapper
            {
                Key = key,
                ClassName = className,
                Inputs = method.InputArguments ?? [],
                Outputs = method.OutputArguments ?? []
            };
            m_methodWrappers[key] = wrapper;
        }

        // ============================================================
        // Validation
        // ============================================================

        /// <summary>
        /// Verifies that no two children of the same wrapper sanitize to
        /// the same C# accessor identifier.
        /// </summary>
        private void ValidateNoCollisions()
        {
            foreach (InstanceWrapper wrapper in m_wrappers.Values)
            {
                var seen = new Dictionary<string, ChildAccessor>(StringComparer.Ordinal);
                foreach (ChildAccessor child in wrapper.Children)
                {
                    if (seen.TryGetValue(child.AccessorName, out ChildAccessor existing))
                    {
                        throw new InvalidOperationException(CoreUtils.Format(
                            "Fluent builder generation: children '{0}' and '{1}' on '{2}' both sanitize to the same C# accessor '{3}'. Rename one of the children in the design.",
                            existing.BrowseName,
                            child.BrowseName,
                            wrapper.ClassName,
                            child.AccessorName));
                    }
                    seen[child.AccessorName] = child;
                }
            }
        }

        // ============================================================
        // Emission
        // ============================================================

        /// <summary>
        /// Emits the typed manager interface declaration with one accessor
        /// per top-level predefined instance.
        /// </summary>
        private void EmitManagerInterface(
            ITemplateWriter writer,
            string interfaceName,
            IReadOnlyList<InstanceDesign> roots)
        {
            writer.WriteLine();
            writer.WriteLine("/// <summary>");
            writer.WriteLine(
                "/// Source-generated typed sibling of"
                + " <see cref=\"global::Opc.Ua.Server.Fluent.INodeManagerBuilder\"/>");
            writer.WriteLine(
                "/// that surfaces typed accessors for the predefined-instance tree.");
            writer.WriteLine("/// </summary>");
            writer.WriteLine(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{0}\", \"{1}\")]",
                ToolName,
                ToolVersion);
            writer.WriteLine("internal interface {0} : global::Opc.Ua.Server.Fluent.INodeManagerBuilder", interfaceName);
            writer.WriteLine("{");
            foreach (InstanceDesign root in roots)
            {
                string accessor = GetAccessorName(root);
                string wrapperKey = ComposeKey(root, string.Empty);
                if (!m_wrappers.TryGetValue(wrapperKey, out InstanceWrapper wrapper))
                {
                    continue;
                }
                writer.WriteLine("    /// <summary>Resolves the predefined instance <c>{0}</c>.</summary>",
                    GetBrowseName(root));
                writer.WriteLine("    {0} {1} {{ get; }}", wrapper.ClassName, accessor);
            }
            writer.WriteLine("}");
        }

        /// <summary>
        /// Emits the internal sealed implementation that wraps the runtime
        /// fluent <c>NodeManagerBuilder</c> and adds typed top-level
        /// accessors.
        /// </summary>
        private void EmitTypedManagerImpl(
            ITemplateWriter writer,
            string interfaceName,
            string className,
            string managerClassName,
            IReadOnlyList<InstanceDesign> roots)
        {
            writer.WriteLine();
            writer.WriteLine("/// <summary>");
            writer.WriteLine(
                "/// Internal proxy that wraps the runtime fluent"
                + " <c>NodeManagerBuilder</c>");
            writer.WriteLine("/// to surface the typed <see cref=\"{0}\"/> facade.", interfaceName);
            writer.WriteLine("/// </summary>");
            writer.WriteLine(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{0}\", \"{1}\")]",
                ToolName,
                ToolVersion);
            writer.WriteLine("internal sealed class {0} : {1}", className, interfaceName);
            writer.WriteLine("{");
            writer.WriteLine(
                "    private readonly global::Opc.Ua.Server.Fluent.INodeManagerBuilder __inner;");
            writer.WriteLine();
            writer.WriteLine(
                "    internal {0}(global::Opc.Ua.Server.Fluent.INodeManagerBuilder inner)",
                className);
            writer.WriteLine("    {");
            writer.WriteLine(
                "        __inner = inner ?? throw new global::System.ArgumentNullException(nameof(inner));");
            writer.WriteLine("    }");
            writer.WriteLine();

            // Pass-through INodeManagerBuilder members. Each line is broken
            // explicitly to satisfy the repository line-length analyzer
            // (RCS0056, max 120 chars).
            EmitPassThroughProperty(writer, "global::Opc.Ua.ISystemContext", "Context");
            EmitPassThroughProperty(writer, "global::Opc.Ua.Server.IAsyncNodeManager", "NodeManager");
            EmitPassThroughProperty(writer, "global::Opc.Ua.Server.Fluent.IFluentDispatcher", "Dispatcher");

            EmitPassThroughMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder", "Node",
                "string browsePath", "browsePath");
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder<TState>", "Node",
                "string browsePath", "browsePath");
            EmitPassThroughMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder", "Node",
                "global::Opc.Ua.NodeId nodeId", "nodeId");
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder<TState>", "Node",
                "global::Opc.Ua.NodeId nodeId", "nodeId");

            EmitPassThroughMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder", "NodeFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId", "typeDefinitionId");
            EmitPassThroughMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder", "NodeFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId, global::Opc.Ua.QualifiedName browseName",
                "typeDefinitionId, browseName");
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder<TState>", "NodeFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId", "typeDefinitionId");
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.INodeBuilder<TState>", "NodeFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId, global::Opc.Ua.QualifiedName browseName",
                "typeDefinitionId, browseName");

            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.IVariableBuilder<TValue>", "Variable",
                "string browsePath", "browsePath", typeArg: "TValue", noConstraint: true);
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.IVariableBuilder<TValue>", "Variable",
                "global::Opc.Ua.NodeId nodeId", "nodeId", typeArg: "TValue", noConstraint: true);
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.IVariableBuilder<TValue>", "VariableFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId", "typeDefinitionId",
                typeArg: "TValue", noConstraint: true);
            EmitPassThroughGenericMethod(writer,
                "global::Opc.Ua.Server.Fluent.IVariableBuilder<TValue>", "VariableFromTypeId",
                "global::Opc.Ua.NodeId typeDefinitionId, global::Opc.Ua.QualifiedName browseName",
                "typeDefinitionId, browseName",
                typeArg: "TValue", noConstraint: true);

            // Typed top-level accessors.
            foreach (InstanceDesign root in roots)
            {
                string accessor = GetAccessorName(root);
                string wrapperKey = ComposeKey(root, string.Empty);
                if (!m_wrappers.TryGetValue(wrapperKey, out InstanceWrapper wrapper))
                {
                    continue;
                }
                string browseName = GetBrowseName(root);
                string nsUri = ResolveNodeBrowseNamespace(root);
                writer.WriteLine();
                writer.WriteLine("    /// <inheritdoc/>");
                writer.WriteLine("    public {0} {1}", wrapper.ClassName, accessor);
                writer.WriteLine("    {");
                writer.WriteLine("        get");
                writer.WriteLine("        {");
                writer.WriteLine("            ushort __ns = __inner.Context.NamespaceUris.GetIndexOrAppend(\"{0}\");",
                    EscapeStringLiteral(nsUri));
                writer.WriteLine("            return new {0}(__inner.Node<{1}>(new global::Opc.Ua.NodeId({2}, __ns)));",
                    wrapper.ClassName,
                    wrapper.NodeStateType,
                    EmitNodeIdConstructorArg(root));
                writer.WriteLine("        }");
                writer.WriteLine("    }");
            }

            writer.WriteLine("}");
        }

        /// <summary>
        /// Emits a <c>{Type} {Name} =&gt; __inner.{Name};</c> property forwarder
        /// across two output lines so the result fits within RCS0056's 120
        /// character ceiling for any plausible runtime type name.
        /// </summary>
        private static void EmitPassThroughProperty(
            ITemplateWriter writer,
            string returnType,
            string memberName)
        {
            writer.WriteLine();
            writer.WriteLine("    /// <inheritdoc/>");
            writer.WriteLine("    public {0} {1}", returnType, memberName);
            writer.WriteLine("        => __inner.{0};", memberName);
        }

        /// <summary>
        /// Emits a non-generic pass-through method forwarder broken across
        /// multiple lines (signature, opener, body, closer).
        /// </summary>
        private static void EmitPassThroughMethod(
            ITemplateWriter writer,
            string returnType,
            string memberName,
            string parameterList,
            string callArguments)
        {
            writer.WriteLine();
            writer.WriteLine("    /// <inheritdoc/>");
            writer.WriteLine("    public {0} {1}({2})", returnType, memberName, parameterList);
            writer.WriteLine("        => __inner.{0}({1});", memberName, callArguments);
        }

        /// <summary>
        /// Emits a generic pass-through method forwarder broken across
        /// multiple lines. Defaults to <c>where TState : NodeState</c>;
        /// pass <paramref name="noConstraint"/> = <c>true</c> to suppress
        /// the constraint and <paramref name="typeArg"/> to use a custom
        /// type-parameter name (e.g. <c>TValue</c>).
        /// </summary>
        private static void EmitPassThroughGenericMethod(
            ITemplateWriter writer,
            string returnType,
            string memberName,
            string parameterList,
            string callArguments,
            string typeArg = "TState",
            bool noConstraint = false)
        {
            writer.WriteLine();
            writer.WriteLine("    /// <inheritdoc/>");
            writer.WriteLine("    public {0} {1}<{2}>({3})",
                returnType, memberName, typeArg, parameterList);
            if (!noConstraint)
            {
                writer.WriteLine("        where {0} : global::Opc.Ua.NodeState", typeArg);
            }
            writer.WriteLine("        => __inner.{0}<{1}>({2});",
                memberName, typeArg, callArguments);
        }

        /// <summary>
        /// Emits one wrapper class for an <see cref="InstanceDesign"/>
        /// describing a non-method instance.
        /// </summary>
        private void EmitInstanceWrapper(ITemplateWriter writer, InstanceWrapper wrapper)
        {
            writer.WriteLine();
            writer.WriteLine("/// <summary>Typed wrapper for the predefined instance.</summary>");
            writer.WriteLine(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{0}\", \"{1}\")]",
                ToolName,
                ToolVersion);
            writer.WriteLine("internal sealed class {0}", wrapper.ClassName);
            writer.WriteLine("{");
            writer.WriteLine("    private readonly global::Opc.Ua.Server.Fluent.INodeBuilder<{0}> __node;",
                wrapper.NodeStateType);
            writer.WriteLine();
            writer.WriteLine("    internal {0}(global::Opc.Ua.Server.Fluent.INodeBuilder<{1}> node)",
                wrapper.ClassName, wrapper.NodeStateType);
            writer.WriteLine("    {");
            writer.WriteLine("        __node = node ?? throw new global::System.ArgumentNullException(nameof(node));");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine("    /// <summary>Underlying typed node builder.</summary>");
            writer.WriteLine("    public global::Opc.Ua.Server.Fluent.INodeBuilder<{0}> Builder => __node;",
                wrapper.NodeStateType);
            writer.WriteLine();
            writer.WriteLine("    /// <summary>Resolved underlying node.</summary>");
            writer.WriteLine("    public {0} Node => __node.Node;", wrapper.NodeStateType);

            foreach (ChildAccessor child in wrapper.Children)
            {
                EmitChildAccessor(writer, child);
            }

            writer.WriteLine("}");
        }

        /// <summary>
        /// Emits one accessor property on the parent wrapper.
        /// </summary>
        private void EmitChildAccessor(ITemplateWriter writer, ChildAccessor child)
        {
            writer.WriteLine();
            switch (child.Kind)
            {
                case ChildKind.Variable:
                    writer.WriteLine("    /// <summary>Typed accessor for variable child <c>{0}</c>.</summary>",
                        child.BrowseName);
                    writer.WriteLine("    public global::Opc.Ua.Server.Fluent.IVariableBuilder<{0}> {1}",
                        child.ValueClrType, child.AccessorName);
                    writer.WriteLine("    {");
                    writer.WriteLine("        get");
                    writer.WriteLine("        {");
                    writer.WriteLine("            ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{0}\");",
                        EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("            return __node.Variable<{0}>(new global::Opc.Ua.QualifiedName(\"{1}\", __ns));",
                        child.ValueClrType,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    break;
                case ChildKind.Method:
                    writer.WriteLine("    /// <summary>Typed accessor for method child <c>{0}</c>.</summary>",
                        child.BrowseName);
                    writer.WriteLine("    public {0} {1}", child.WrapperClassName, child.AccessorName);
                    writer.WriteLine("    {");
                    writer.WriteLine("        get");
                    writer.WriteLine("        {");
                    writer.WriteLine("            ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{0}\");",
                        EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("            return new {0}(__node.Child<global::Opc.Ua.MethodState>(new global::Opc.Ua.QualifiedName(\"{1}\", __ns)));",
                        child.WrapperClassName,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    break;
                case ChildKind.Object:
                    writer.WriteLine("    /// <summary>Typed accessor for object child <c>{0}</c>.</summary>",
                        child.BrowseName);
                    writer.WriteLine("    public {0} {1}", child.WrapperClassName, child.AccessorName);
                    writer.WriteLine("    {");
                    writer.WriteLine("        get");
                    writer.WriteLine("        {");
                    writer.WriteLine("            ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{0}\");",
                        EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("            return new {0}(__node.Child<{1}>(new global::Opc.Ua.QualifiedName(\"{2}\", __ns)));",
                        child.WrapperClassName,
                        child.ChildStateType,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    break;
            }
        }

        /// <summary>
        /// Emits one wrapper class for a method instance with typed
        /// <c>OnCall</c> overloads.
        /// </summary>
        private void EmitMethodWrapper(ITemplateWriter writer, MethodWrapper method)
        {
            writer.WriteLine();
            writer.WriteLine("/// <summary>Typed method-call wrapper for the predefined method.</summary>");
            writer.WriteLine(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{0}\", \"{1}\")]",
                ToolName,
                ToolVersion);
            writer.WriteLine("internal sealed class {0}", method.ClassName);
            writer.WriteLine("{");
            writer.WriteLine("    private readonly global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> __node;");
            writer.WriteLine();
            writer.WriteLine("    internal {0}(global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> node)",
                method.ClassName);
            writer.WriteLine("    {");
            writer.WriteLine("        __node = node ?? throw new global::System.ArgumentNullException(nameof(node));");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine("    /// <summary>Underlying typed node builder. Use to drop into the non-typed fluent surface.</summary>");
            writer.WriteLine("    public global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> Builder => __node;");
            writer.WriteLine();
            writer.WriteLine("    /// <summary>Resolved underlying method state.</summary>");
            writer.WriteLine("    public global::Opc.Ua.MethodState Node => __node.Node;");

            // Sync typed OnCall.
            EmitMethodOnCall(writer, method, async: false);
            // Async typed OnCall.
            EmitMethodOnCall(writer, method, async: true);

            writer.WriteLine("}");
        }

        /// <summary>
        /// Emits one OnCall overload for a method wrapper. The overload
        /// shape is determined by the method's argument signature plus the
        /// requested sync/async flavor.
        /// </summary>
        private void EmitMethodOnCall(ITemplateWriter writer, MethodWrapper method, bool async)
        {
            string targetNamespace = m_context.ModelDesign.TargetNamespace.Value;
            Namespace[] namespaces = m_context.ModelDesign.Namespaces;
            Parameter[] inputs = method.Inputs;
            Parameter[] outputs = method.Outputs;

            string returnTypeAnnotation = GetReturnTypeAnnotation(outputs, targetNamespace, namespaces);
            string handlerType;
            if (async)
            {
                if (inputs.Length == 0 && outputs.Length == 0)
                {
                    handlerType = "global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask>";
                }
                else if (outputs.Length == 0)
                {
                    handlerType = CoreUtils.Format(
                        "global::System.Func<{0}, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask>",
                        FormatInputTypeList(inputs, targetNamespace, namespaces));
                }
                else
                {
                    handlerType = CoreUtils.Format(
                        "global::System.Func<{0}{1}global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask{2}>",
                        FormatInputTypeList(inputs, targetNamespace, namespaces),
                        inputs.Length == 0 ? string.Empty : ", ",
                        returnTypeAnnotation);
                }
            }
            else
            {
                if (inputs.Length == 0 && outputs.Length == 0)
                {
                    handlerType = "global::System.Action";
                }
                else if (outputs.Length == 0)
                {
                    handlerType = CoreUtils.Format(
                        "global::System.Action<{0}>",
                        FormatInputTypeList(inputs, targetNamespace, namespaces));
                }
                else
                {
                    handlerType = CoreUtils.Format(
                        "global::System.Func<{0}{1}{2}>",
                        FormatInputTypeList(inputs, targetNamespace, namespaces),
                        inputs.Length == 0 ? string.Empty : ", ",
                        StripAngleBrackets(returnTypeAnnotation, defaultIfEmpty: "void"));
                }
            }

            writer.WriteLine();
            writer.WriteLine("    /// <summary>Wires the method-call handler ({0}).</summary>",
                async ? "async" : "sync");
            writer.WriteLine("    public {0} OnCall({1} handler)", method.ClassName, handlerType);
            writer.WriteLine("    {");
            writer.WriteLine("        if (handler == null) throw new global::System.ArgumentNullException(nameof(handler));");
            if (async)
            {
                writer.WriteLine("        __node.OnCall(async (");
                writer.WriteLine("            global::Opc.Ua.ISystemContext __ctx,");
                writer.WriteLine("            global::Opc.Ua.MethodState __m,");
                writer.WriteLine("            global::Opc.Ua.NodeId __oid,");
                writer.WriteLine("            global::Opc.Ua.ArrayOf<global::Opc.Ua.Variant> __inputs,");
                writer.WriteLine("            global::System.Collections.Generic.List<global::Opc.Ua.Variant> __outputs,");
                writer.WriteLine("            global::System.Threading.CancellationToken __ct) =>");
                writer.WriteLine("        {");
            }
            else
            {
                writer.WriteLine("        __node.OnCall((");
                writer.WriteLine("            global::Opc.Ua.ISystemContext __ctx,");
                writer.WriteLine("            global::Opc.Ua.MethodState __m,");
                writer.WriteLine("            global::Opc.Ua.NodeId __oid,");
                writer.WriteLine("            global::Opc.Ua.ArrayOf<global::Opc.Ua.Variant> __inputs,");
                writer.WriteLine("            global::System.Collections.Generic.List<global::Opc.Ua.Variant> __outputs) =>");
                writer.WriteLine("        {");
            }

            // Validate input arg count.
            if (inputs.Length > 0)
            {
                writer.WriteLine("            if (__inputs.Count < {0})", inputs.Length);
                writer.WriteLine("            {");
                writer.WriteLine("                return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadArgumentsMissing);");
                writer.WriteLine("            }");
            }

            // Unpack inputs.
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                EmitInputUnpack(writer, inputs[ii], ii, targetNamespace, namespaces);
            }

            // Invoke user handler.
            if (async)
            {
                if (outputs.Length == 0)
                {
                    writer.Write("            await handler(");
                    EmitInputArgPassThrough(writer, inputs, withCt: true);
                    writer.WriteLine(").ConfigureAwait(false);");
                }
                else if (outputs.Length == 1)
                {
                    writer.Write("            var __r = await handler(");
                    EmitInputArgPassThrough(writer, inputs, withCt: true);
                    writer.WriteLine(").ConfigureAwait(false);");
                }
                else
                {
                    writer.Write("            var __r = await handler(");
                    EmitInputArgPassThrough(writer, inputs, withCt: true);
                    writer.WriteLine(").ConfigureAwait(false);");
                }
            }
            else
            {
                if (outputs.Length == 0)
                {
                    writer.Write("            handler(");
                    EmitInputArgPassThrough(writer, inputs, withCt: false);
                    writer.WriteLine(");");
                }
                else
                {
                    writer.Write("            var __r = handler(");
                    EmitInputArgPassThrough(writer, inputs, withCt: false);
                    writer.WriteLine(");");
                }
            }

            // Marshal outputs.
            for (int ii = 0; ii < outputs.Length; ii++)
            {
                EmitOutputBox(writer, outputs[ii], ii, outputs.Length);
            }

            writer.WriteLine("            return global::Opc.Ua.ServiceResult.Good;");
            if (async)
            {
                writer.WriteLine("        });");
            }
            else
            {
                writer.WriteLine("        });");
            }
            writer.WriteLine("        return this;");
            writer.WriteLine("    }");
        }

        /// <summary>
        /// Emits the typed unpack code for a single input argument. Mirrors
        /// the logic in <see cref="ObjectTypeProxyGenerator"/>.
        /// </summary>
        private static void EmitInputUnpack(
            ITemplateWriter writer,
            Parameter input,
            int index,
            string targetNamespace,
            Namespace[] namespaces)
        {
            string typeName = input.DataTypeNode.GetMethodArgumentTypeAsCode(
                input.ValueRank,
                targetNamespace,
                namespaces,
                input.IsOptional);
            string local = "__a" + index;
            switch (input.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    writer.WriteLine("            if (!__inputs[{0}].TryGetStructure(out {1} {2}))",
                        index, typeName, local);
                    writer.WriteLine("            {");
                    writer.WriteLine("                return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadInvalidArgument);");
                    writer.WriteLine("            }");
                    break;
                case BasicDataType.BaseDataType when input.ValueRank == ValueRank.Scalar:
                    writer.WriteLine("            {0} {1} = __inputs[{2}];", typeName, local, index);
                    break;
                default:
                    writer.WriteLine("            if (!__inputs[{0}].TryGetValue(out {1} {2}))",
                        index, typeName, local);
                    writer.WriteLine("            {");
                    writer.WriteLine("                return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadInvalidArgument);");
                    writer.WriteLine("            }");
                    break;
            }
        }

        /// <summary>
        /// Emits the comma-separated list of unpacked input locals (plus
        /// optional CancellationToken) for the user-handler invocation.
        /// </summary>
        private static void EmitInputArgPassThrough(
            ITemplateWriter writer,
            Parameter[] inputs,
            bool withCt)
        {
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                if (ii > 0)
                {
                    writer.Write(", ");
                }
                writer.Write("__a" + ii);
            }
            if (withCt)
            {
                if (inputs.Length > 0)
                {
                    writer.Write(", ");
                }
                writer.Write("__ct");
            }
        }

        /// <summary>
        /// Emits the boxing code for a single output argument. For multi-
        /// output methods the user returns a <c>ValueTuple</c> and we
        /// destructure by field name (<c>Item1</c>, <c>Item2</c>, …).
        /// </summary>
        private static void EmitOutputBox(
            ITemplateWriter writer,
            Parameter output,
            int index,
            int totalOutputs)
        {
            string source;
            if (totalOutputs == 1)
            {
                source = "__r";
            }
            else
            {
                source = "__r.Item" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            switch (output.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    writer.WriteLine("            __outputs.Add(global::Opc.Ua.Variant.FromStructure({0}));", source);
                    break;
                case BasicDataType.BaseDataType when output.ValueRank == ValueRank.Scalar:
                    writer.WriteLine("            __outputs.Add({0});", source);
                    break;
                default:
                    writer.WriteLine("            __outputs.Add(global::Opc.Ua.Variant.From({0}));", source);
                    break;
            }
        }

        /// <summary>
        /// Returns the <c>ValueTask&lt;…&gt;</c> suffix used in the method's
        /// async return type. Mirrors
        /// <see cref="ObjectTypeProxyGenerator"/> verbatim.
        /// </summary>
        private static string GetReturnTypeAnnotation(
            Parameter[] outputs,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (outputs.Length == 0)
            {
                return string.Empty;
            }
            if (outputs.Length == 1)
            {
                return CoreUtils.Format(
                    "<{0}>",
                    outputs[0].DataTypeNode.GetMethodArgumentTypeAsCode(
                        outputs[0].ValueRank,
                        targetNamespace,
                        namespaces,
                        outputs[0].IsOptional));
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("<(");
            for (int ii = 0; ii < outputs.Length; ii++)
            {
                if (ii > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(outputs[ii].DataTypeNode.GetMethodArgumentTypeAsCode(
                    outputs[ii].ValueRank,
                    targetNamespace,
                    namespaces,
                    outputs[ii].IsOptional));
                sb.Append(" Item" + (ii + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(")>");
            return sb.ToString();
        }

        private static string StripAngleBrackets(string returnTypeAnnotation, string defaultIfEmpty)
        {
            if (string.IsNullOrEmpty(returnTypeAnnotation))
            {
                return defaultIfEmpty;
            }
            // Strip leading '<' and trailing '>'.
            return returnTypeAnnotation[1..^1];
        }

        private static string FormatInputTypeList(
            Parameter[] inputs,
            string targetNamespace,
            Namespace[] namespaces)
        {
            var sb = new System.Text.StringBuilder();
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                if (ii > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(inputs[ii].DataTypeNode.GetMethodArgumentTypeAsCode(
                    inputs[ii].ValueRank,
                    targetNamespace,
                    namespaces,
                    inputs[ii].IsOptional));
            }
            return sb.ToString();
        }

        // ============================================================
        // Helpers
        // ============================================================

        /// <summary>
        /// Returns the wrapper-key string used to deduplicate wrappers and
        /// to compose CLR class names. Combines the root's symbolic id
        /// with the relative path from the root.
        /// </summary>
        private static string ComposeKey(InstanceDesign root, string relativePath)
        {
            string rootId = root?.SymbolicId?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(relativePath))
            {
                return rootId;
            }
            return rootId + "_" + relativePath;
        }

        private static string ComposeClassName(
            InstanceDesign root,
            string relativePath,
            string suffix)
        {
            string key = ComposeKey(root, relativePath);
            return key + suffix;
        }

        private static string GetAccessorName(NodeDesign node)
        {
            string name = node?.SymbolicName?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return "Item";
            }
            return name.ToSafeSymbolName(toLowerCamelCase: false);
        }

        private static string GetBrowseName(NodeDesign node)
        {
            // Per the design schema the BrowseName is identical to the
            // SymbolicName when it isn't explicitly overridden. Use the
            // BrowseName when present so source-generated identifiers and
            // the on-the-wire OPC UA browse name stay aligned.
            if (!string.IsNullOrEmpty(node?.BrowseName))
            {
                return node.BrowseName;
            }
            return node?.SymbolicName?.Name ?? string.Empty;
        }

        private string ResolveNodeBrowseNamespace(NodeDesign node)
        {
            string ns = node?.SymbolicName?.Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                return ns;
            }
            return m_context.ModelDesign.TargetNamespace?.Value ?? string.Empty;
        }

        /// <summary>
        /// Returns the C# type name of the runtime <see cref="NodeState"/>
        /// derivative for the supplied instance.
        /// </summary>
        private string ResolveStateClrType(NodeDesign node)
        {
            // For object instances, use BaseObjectState as the lowest common
            // denominator. The user can call .Builder.As&lt;TConcrete&gt;() to
            // narrow.
            if (node is ObjectDesign)
            {
                return "global::Opc.Ua.BaseObjectState";
            }
            if (node is MethodDesign)
            {
                return "global::Opc.Ua.MethodState";
            }
            if (node is VariableDesign)
            {
                return "global::Opc.Ua.BaseDataVariableState";
            }
            return "global::Opc.Ua.NodeState";
        }

        /// <summary>
        /// Returns the CLR type name for a variable's value attribute,
        /// inferred from the variable's <c>DataType</c> and <c>ValueRank</c>.
        /// </summary>
        private string GetVariableValueClrType(VariableDesign variable)
        {
            string targetNamespace = m_context.ModelDesign.TargetNamespace.Value;
            Namespace[] namespaces = m_context.ModelDesign.Namespaces;
            return variable.DataTypeNode.GetMethodArgumentTypeAsCode(
                variable.ValueRank,
                targetNamespace,
                namespaces,
                isOptional: false);
        }

        /// <summary>
        /// Emits the constructor argument used to materialize the
        /// top-level instance's <see cref="NodeId"/>. Numeric ids preferred
        /// when present; otherwise falls back to the SymbolicId string.
        /// </summary>
        private static string EmitNodeIdConstructorArg(InstanceDesign node)
        {
            if (node.NumericIdSpecified && node.NumericId != 0u)
            {
                return CoreUtils.Format("{0}u",
                    node.NumericId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrEmpty(node.StringId))
            {
                return CoreUtils.Format("\"{0}\"", EscapeStringLiteral(node.StringId));
            }
            // No id assigned — fall back to the SymbolicId.Name as a string id.
            return CoreUtils.Format("\"{0}\"",
                EscapeStringLiteral(node.SymbolicId?.Name ?? string.Empty));
        }

        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        // ============================================================
        // State
        // ============================================================

        private readonly IGeneratorContext m_context;
        private Dictionary<string, InstanceWrapper> m_wrappers = [];
        private Dictionary<string, MethodWrapper> m_methodWrappers = [];

        private static string ToolName
            => System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
        private static string ToolVersion
            => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private enum ChildKind
        {
            Variable,
            Method,
            Object
        }

        private sealed class InstanceWrapper
        {
            public string Key;
            public string ClassName;
            public string NodeStateType;
            public string BrowseNamespaceUri;
            public List<ChildAccessor> Children;
        }

        private sealed class ChildAccessor
        {
            public string AccessorName;
            public string BrowseName;
            public string BrowseNamespaceUri;
            public ChildKind Kind;
            public string ValueClrType;       // Variable
            public string WrapperClassName;   // Method or Object
            public string ChildKey;           // Object — key into m_wrappers
            public string ChildStateType;     // Object — node state type
        }

        private sealed class MethodWrapper
        {
            public string Key;
            public string ClassName;
            public Parameter[] Inputs;
            public Parameter[] Outputs;
        }
    }
}
