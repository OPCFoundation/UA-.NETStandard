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

            // Wire each wrapper to its direct child object/method
            // wrappers so the recursive emitter can walk the tree
            // depth-first and emit nested type declarations.
            LinkChildWrappers();

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

                    // Walk top-level instance wrappers depth-first so each
                    // child object/method wrapper is emitted as a nested
                    // type inside its parent. Top-level wrappers (those
                    // whose parent path is empty) live at namespace scope.
                    foreach (InstanceWrapper top in m_wrappers.Values
                        .Where(w => w.ParentKey == null)
                        .OrderBy(w => w.LeafName, StringComparer.Ordinal))
                    {
                        EmitInstanceWrapper(ctx.Out, top, indent: string.Empty);
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
        /// Registers an <c>InstanceWrapper</c> for the supplied
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

            string leafName = ResolveLeafName(root, relativePath, hnode.Instance);
            string parentKey = ResolveParentKey(root, relativePath, leafName);
            string className = ComposeWrapperClassName(leafName, suffix: "Builder");
            string nsUri = ResolveNodeBrowseNamespace(hnode.Instance);
            var wrapper = new InstanceWrapper
            {
                Key = key,
                ClassName = className,
                LeafName = leafName,
                ParentKey = parentKey,
                NodeStateType = ResolveStateClrType(hnode.Instance),
                BrowseNamespaceUri = nsUri,
                SupportsPublish = QualifiesAsEventNotifier(hnode.Instance),
                Children = [],
                ChildObjectKeys = [],
                ChildMethodKeys = []
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
                    string childLeaf = ResolveLeafName(root, kid.RelativePath, kid.Instance);
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
                        case MethodDesign:
                            child.Kind = ChildKind.Method;
                            // Lexical scope: methods are emitted as nested
                            // classes inside the parent wrapper, so the
                            // simple leaf name resolves correctly here.
                            child.WrapperClassName = ComposeWrapperClassName(
                                childLeaf, suffix: "MethodBuilder");
                            break;
                        case ObjectDesign:
                            child.Kind = ChildKind.Object;
                            // Lexical scope: object child wrappers are
                            // nested classes; the simple leaf name resolves
                            // through the enclosing parent wrapper.
                            child.WrapperClassName = ComposeWrapperClassName(
                                childLeaf, suffix: "Builder");
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
        /// Registers a <c>MethodWrapper</c> for the supplied method
        /// design. Resolves typed argument shapes from the method's
        /// <c>InputArguments</c>/<c>OutputArguments</c>.
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

            string leafName = ResolveLeafName(root, relativePath, method);
            string parentKey = ResolveParentKey(root, relativePath, leafName);
            string className = ComposeWrapperClassName(leafName, suffix: "MethodBuilder");
            var wrapper = new MethodWrapper
            {
                Key = key,
                ClassName = className,
                LeafName = leafName,
                ParentKey = parentKey,
                Inputs = method.InputArguments ?? [],
                Outputs = method.OutputArguments ?? []
            };
            m_methodWrappers[key] = wrapper;
        }

        // ============================================================
        // Validation
        // ============================================================

        /// <summary>
        /// Wires each wrapper to its direct child object/method wrappers
        /// so the recursive emitter can walk the tree depth-first. Sorts
        /// siblings by leaf name (ordinal) so generation is deterministic.
        /// </summary>
        private void LinkChildWrappers()
        {
            foreach (InstanceWrapper child in m_wrappers.Values)
            {
                if (child.ParentKey == null)
                {
                    continue;
                }
                if (m_wrappers.TryGetValue(child.ParentKey, out InstanceWrapper parent))
                {
                    parent.ChildObjectKeys.Add(child.Key);
                }
            }
            foreach (MethodWrapper method in m_methodWrappers.Values)
            {
                if (method.ParentKey == null)
                {
                    continue;
                }
                if (m_wrappers.TryGetValue(method.ParentKey, out InstanceWrapper parent))
                {
                    parent.ChildMethodKeys.Add(method.Key);
                }
            }
            foreach (InstanceWrapper wrapper in m_wrappers.Values)
            {
                wrapper.ChildObjectKeys.Sort((a, b) => string.CompareOrdinal(
                    m_wrappers[a].LeafName,
                    m_wrappers[b].LeafName));
                wrapper.ChildMethodKeys.Sort((a, b) => string.CompareOrdinal(
                    m_methodWrappers[a].LeafName,
                    m_methodWrappers[b].LeafName));
            }
        }

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
                "/// Source-generated typed sibling of" +
                " <see cref=\"global::Opc.Ua.Server.Fluent.INodeManagerBuilder\"/>");
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
                "/// Internal proxy that wraps the runtime fluent" +
                " <c>NodeManagerBuilder</c>");
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
        /// describing a non-method instance. The class is rendered at the
        /// indentation depth supplied by <paramref name="indent"/>; child
        /// object/method wrappers are emitted recursively as nested types
        /// one level deeper.
        /// </summary>
        private void EmitInstanceWrapper(
            ITemplateWriter writer,
            InstanceWrapper wrapper,
            string indent)
        {
            string memberIndent = indent + Indent;

            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Typed wrapper for the predefined instance.</summary>", indent);
            writer.WriteLine(
                "{0}[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{1}\", \"{2}\")]",
                indent,
                ToolName,
                ToolVersion);
            writer.WriteLine("{0}internal sealed class {1}", indent, wrapper.ClassName);
            writer.WriteLine("{0}{{", indent);
            writer.WriteLine("{0}private readonly global::Opc.Ua.Server.Fluent.INodeBuilder<{1}> __node;",
                memberIndent, wrapper.NodeStateType);
            writer.WriteLine();
            writer.WriteLine("{0}internal {1}(global::Opc.Ua.Server.Fluent.INodeBuilder<{2}> node)",
                memberIndent, wrapper.ClassName, wrapper.NodeStateType);
            writer.WriteLine("{0}{{", memberIndent);
            writer.WriteLine("{0}__node = node ?? throw new global::System.ArgumentNullException(nameof(node));",
                memberIndent + Indent);
            writer.WriteLine("{0}}}", memberIndent);
            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Underlying typed node builder.</summary>", memberIndent);
            writer.WriteLine("{0}public global::Opc.Ua.Server.Fluent.INodeBuilder<{1}> Builder => __node;",
                memberIndent, wrapper.NodeStateType);
            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Resolved underlying node.</summary>", memberIndent);
            writer.WriteLine("{0}public {1} Node => __node.Node;", memberIndent, wrapper.NodeStateType);

            foreach (ChildAccessor child in wrapper.Children)
            {
                EmitChildAccessor(writer, child, memberIndent);
            }

            if (wrapper.SupportsPublish)
            {
                EmitPublishOverloads(writer, wrapper, memberIndent);
            }

            // Emit the nested method wrappers, then the nested object
            // wrappers. Sibling order is leaf-name ordinal (set up by
            // LinkChildWrappers) so generation is deterministic.
            foreach (string methodKey in wrapper.ChildMethodKeys)
            {
                if (m_methodWrappers.TryGetValue(methodKey, out MethodWrapper nestedMethod))
                {
                    EmitMethodWrapper(writer, nestedMethod, memberIndent);
                }
            }
            foreach (string childKey in wrapper.ChildObjectKeys)
            {
                if (m_wrappers.TryGetValue(childKey, out InstanceWrapper nested))
                {
                    EmitInstanceWrapper(writer, nested, memberIndent);
                }
            }

            writer.WriteLine("{0}}}", indent);
        }

        /// <summary>
        /// Emits one accessor property on the parent wrapper at the
        /// supplied <paramref name="indent"/> (the parent's member
        /// indent).
        /// </summary>
        private void EmitChildAccessor(
            ITemplateWriter writer,
            ChildAccessor child,
            string indent)
        {
            string bodyIndent = indent + Indent;
            string innerIndent = bodyIndent + Indent;

            writer.WriteLine();
            switch (child.Kind)
            {
                case ChildKind.Variable:
                    writer.WriteLine("{0}/// <summary>Typed accessor for variable child <c>{1}</c>.</summary>",
                        indent, child.BrowseName);
                    writer.WriteLine("{0}public global::Opc.Ua.Server.Fluent.IVariableBuilder<{1}> {2}",
                        indent, child.ValueClrType, child.AccessorName);
                    writer.WriteLine("{0}{{", indent);
                    writer.WriteLine("{0}get", bodyIndent);
                    writer.WriteLine("{0}{{", bodyIndent);
                    writer.WriteLine("{0}ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{1}\");",
                        innerIndent, EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("{0}return __node.Variable<{1}>(new global::Opc.Ua.QualifiedName(\"{2}\", __ns));",
                        innerIndent,
                        child.ValueClrType,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("{0}}}", bodyIndent);
                    writer.WriteLine("{0}}}", indent);
                    break;
                case ChildKind.Method:
                    writer.WriteLine("{0}/// <summary>Typed accessor for method child <c>{1}</c>.</summary>",
                        indent, child.BrowseName);
                    writer.WriteLine("{0}public {1} {2}", indent, child.WrapperClassName, child.AccessorName);
                    writer.WriteLine("{0}{{", indent);
                    writer.WriteLine("{0}get", bodyIndent);
                    writer.WriteLine("{0}{{", bodyIndent);
                    writer.WriteLine("{0}ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{1}\");",
                        innerIndent, EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("{0}return new {1}(__node.Child<global::Opc.Ua.MethodState>(new global::Opc.Ua.QualifiedName(\"{2}\", __ns)));",
                        innerIndent,
                        child.WrapperClassName,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("{0}}}", bodyIndent);
                    writer.WriteLine("{0}}}", indent);
                    break;
                case ChildKind.Object:
                    writer.WriteLine("{0}/// <summary>Typed accessor for object child <c>{1}</c>.</summary>",
                        indent, child.BrowseName);
                    writer.WriteLine("{0}public {1} {2}", indent, child.WrapperClassName, child.AccessorName);
                    writer.WriteLine("{0}{{", indent);
                    writer.WriteLine("{0}get", bodyIndent);
                    writer.WriteLine("{0}{{", bodyIndent);
                    writer.WriteLine("{0}ushort __ns = __node.Builder.Context.NamespaceUris.GetIndexOrAppend(\"{1}\");",
                        innerIndent, EscapeStringLiteral(child.BrowseNamespaceUri));
                    writer.WriteLine("{0}return new {1}(__node.Child<{2}>(new global::Opc.Ua.QualifiedName(\"{3}\", __ns)));",
                        innerIndent,
                        child.WrapperClassName,
                        child.ChildStateType,
                        EscapeStringLiteral(child.BrowseName));
                    writer.WriteLine("{0}}}", bodyIndent);
                    writer.WriteLine("{0}}}", indent);
                    break;
            }
        }

        /// <summary>
        /// Emits the typed <c>Publish&lt;TEvent&gt;</c> overloads on a
        /// notifier-capable wrapper. Both overloads forward to
        /// <c>Opc.Ua.Server.Fluent.EventNotifierBuilderExtensions</c>;
        /// the wrapper's underlying node-state type is bound as the
        /// <c>TNotifier</c> type argument so callers don't need to spell
        /// it out. The shape mirrors the extension's two overloads (direct
        /// stream + factory).
        /// </summary>
        private static void EmitPublishOverloads(
            ITemplateWriter writer,
            InstanceWrapper wrapper,
            string indent)
        {
            writer.WriteLine();
            writer.WriteLine(
                "{0}/// <summary>Registers an event source for this notifier; lazy by default. See <see cref=\"global::Opc.Ua.Server.Fluent.EventPublishOptions\"/> for activation tuning.</summary>",
                indent);
            writer.WriteLine(
                "{0}public global::Opc.Ua.Server.Fluent.INodeBuilder<{1}> Publish<TEvent>(",
                indent, wrapper.NodeStateType);
            writer.WriteLine(
                "{0}global::System.Collections.Generic.IAsyncEnumerable<TEvent> source,",
                indent + Indent);
            writer.WriteLine(
                "{0}global::Opc.Ua.Server.Fluent.EventPublishOptions? options = null)",
                indent + Indent);
            writer.WriteLine(
                "{0}where TEvent : global::Opc.Ua.BaseEventState",
                indent + Indent);
            writer.WriteLine(
                "{0}=> global::Opc.Ua.Server.Fluent.EventNotifierBuilderExtensions.Publish<{1}, TEvent>(__node, source, options);",
                indent + Indent, wrapper.NodeStateType);

            writer.WriteLine();
            writer.WriteLine(
                "{0}/// <summary>Registers a factory-based event source for this notifier; the factory runs on each activation. See <see cref=\"global::Opc.Ua.Server.Fluent.EventPublishOptions\"/> for activation tuning.</summary>",
                indent);
            writer.WriteLine(
                "{0}public global::Opc.Ua.Server.Fluent.INodeBuilder<{1}> Publish<TEvent>(",
                indent, wrapper.NodeStateType);
            writer.WriteLine(
                "{0}global::System.Func<{1}, global::Opc.Ua.ISystemContext, global::System.Threading.CancellationToken, global::System.Collections.Generic.IAsyncEnumerable<TEvent>> factory,",
                indent + Indent, wrapper.NodeStateType);
            writer.WriteLine(
                "{0}global::Opc.Ua.Server.Fluent.EventPublishOptions? options = null)",
                indent + Indent);
            writer.WriteLine(
                "{0}where TEvent : global::Opc.Ua.BaseEventState",
                indent + Indent);
            writer.WriteLine(
                "{0}=> global::Opc.Ua.Server.Fluent.EventNotifierBuilderExtensions.Publish<{1}, TEvent>(__node, factory, options);",
                indent + Indent, wrapper.NodeStateType);
        }

        /// <summary>
        /// Emits one wrapper class for a method instance with typed
        /// <c>OnCall</c> overloads at the supplied <paramref name="indent"/>.
        /// </summary>
        private void EmitMethodWrapper(
            ITemplateWriter writer,
            MethodWrapper method,
            string indent)
        {
            string memberIndent = indent + Indent;

            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Typed method-call wrapper for the predefined method.</summary>", indent);
            writer.WriteLine(
                "{0}[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{1}\", \"{2}\")]",
                indent,
                ToolName,
                ToolVersion);
            writer.WriteLine("{0}internal sealed class {1}", indent, method.ClassName);
            writer.WriteLine("{0}{{", indent);
            writer.WriteLine("{0}private readonly global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> __node;",
                memberIndent);
            writer.WriteLine();
            writer.WriteLine("{0}internal {1}(global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> node)",
                memberIndent, method.ClassName);
            writer.WriteLine("{0}{{", memberIndent);
            writer.WriteLine("{0}__node = node ?? throw new global::System.ArgumentNullException(nameof(node));",
                memberIndent + Indent);
            writer.WriteLine("{0}}}", memberIndent);
            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Underlying typed node builder. Use to drop into the non-typed fluent surface.</summary>",
                memberIndent);
            writer.WriteLine("{0}public global::Opc.Ua.Server.Fluent.INodeBuilder<global::Opc.Ua.MethodState> Builder => __node;",
                memberIndent);
            writer.WriteLine();
            writer.WriteLine("{0}/// <summary>Resolved underlying method state.</summary>", memberIndent);
            writer.WriteLine("{0}public global::Opc.Ua.MethodState Node => __node.Node;", memberIndent);

            // Sync typed OnCall.
            EmitMethodOnCall(writer, method, async: false, indent: memberIndent);
            // Async typed OnCall.
            EmitMethodOnCall(writer, method, async: true, indent: memberIndent);

            writer.WriteLine("{0}}}", indent);
        }

        /// <summary>
        /// Emits one OnCall overload for a method wrapper. The overload
        /// shape is determined by the method's argument signature plus the
        /// requested sync/async flavor. <paramref name="indent"/> is the
        /// member indent of the enclosing method wrapper.
        /// </summary>
        private void EmitMethodOnCall(
            ITemplateWriter writer,
            MethodWrapper method,
            bool async,
            string indent)
        {
            string bodyIndent = indent + Indent;
            string lambdaIndent = bodyIndent + Indent;

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
            writer.WriteLine("{0}/// <summary>Wires the method-call handler ({1}).</summary>",
                indent, async ? "async" : "sync");
            writer.WriteLine("{0}public {1} OnCall({2} handler)", indent, method.ClassName, handlerType);
            writer.WriteLine("{0}{{", indent);
            writer.WriteLine("{0}if (handler == null) throw new global::System.ArgumentNullException(nameof(handler));",
                bodyIndent);
            if (async)
            {
                writer.WriteLine("{0}__node.OnCall(async (", bodyIndent);
                writer.WriteLine("{0}global::Opc.Ua.ISystemContext __ctx,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.MethodState __m,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.NodeId __oid,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.ArrayOf<global::Opc.Ua.Variant> __inputs,", lambdaIndent);
                writer.WriteLine("{0}global::System.Collections.Generic.List<global::Opc.Ua.Variant> __outputs,", lambdaIndent);
                writer.WriteLine("{0}global::System.Threading.CancellationToken __ct) =>", lambdaIndent);
                writer.WriteLine("{0}{{", bodyIndent);
            }
            else
            {
                writer.WriteLine("{0}__node.OnCall((", bodyIndent);
                writer.WriteLine("{0}global::Opc.Ua.ISystemContext __ctx,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.MethodState __m,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.NodeId __oid,", lambdaIndent);
                writer.WriteLine("{0}global::Opc.Ua.ArrayOf<global::Opc.Ua.Variant> __inputs,", lambdaIndent);
                writer.WriteLine("{0}global::System.Collections.Generic.List<global::Opc.Ua.Variant> __outputs) =>", lambdaIndent);
                writer.WriteLine("{0}{{", bodyIndent);
            }

            // Validate input arg count.
            if (inputs.Length > 0)
            {
                writer.WriteLine("{0}if (__inputs.Count < {1})", lambdaIndent, inputs.Length);
                writer.WriteLine("{0}{{", lambdaIndent);
                writer.WriteLine("{0}return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadArgumentsMissing);",
                    lambdaIndent + Indent);
                writer.WriteLine("{0}}}", lambdaIndent);
            }

            // Unpack inputs.
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                EmitInputUnpack(writer, inputs[ii], ii, targetNamespace, namespaces, lambdaIndent);
            }

            // Invoke user handler.
            if (async)
            {
                if (outputs.Length == 0)
                {
                    writer.Write("{0}await handler(", lambdaIndent);
                    EmitInputArgPassThrough(writer, inputs, withCt: true);
                    writer.WriteLine(").ConfigureAwait(false);");
                }
                else
                {
                    writer.Write("{0}var __r = await handler(", lambdaIndent);
                    EmitInputArgPassThrough(writer, inputs, withCt: true);
                    writer.WriteLine(").ConfigureAwait(false);");
                }
            }
            else
            {
                if (outputs.Length == 0)
                {
                    writer.Write("{0}handler(", lambdaIndent);
                    EmitInputArgPassThrough(writer, inputs, withCt: false);
                    writer.WriteLine(");");
                }
                else
                {
                    writer.Write("{0}var __r = handler(", lambdaIndent);
                    EmitInputArgPassThrough(writer, inputs, withCt: false);
                    writer.WriteLine(");");
                }
            }

            // Marshal outputs.
            for (int ii = 0; ii < outputs.Length; ii++)
            {
                EmitOutputBox(writer, outputs[ii], ii, outputs.Length, lambdaIndent);
            }

            writer.WriteLine("{0}return global::Opc.Ua.ServiceResult.Good;", lambdaIndent);
            writer.WriteLine("{0}}});", bodyIndent);
            writer.WriteLine("{0}return this;", bodyIndent);
            writer.WriteLine("{0}}}", indent);
        }

        /// <summary>
        /// Emits the typed unpack code for a single input argument. Mirrors
        /// the logic in <c>ObjectTypeProxyGenerator</c>. <paramref name="indent"/>
        /// is the lambda-body indent of the surrounding OnCall.
        /// </summary>
        private static void EmitInputUnpack(
            ITemplateWriter writer,
            Parameter input,
            int index,
            string targetNamespace,
            Namespace[] namespaces,
            string indent)
        {
            string innerIndent = indent + Indent;
            string typeName = input.DataTypeNode.GetMethodArgumentTypeAsCode(
                input.ValueRank,
                targetNamespace,
                namespaces,
                input.IsOptional);
            string local = "__a" + index;
            switch (input.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    writer.WriteLine("{0}if (!__inputs[{1}].TryGetStructure(out {2} {3}))",
                        indent, index, typeName, local);
                    writer.WriteLine("{0}{{", indent);
                    writer.WriteLine("{0}return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadInvalidArgument);",
                        innerIndent);
                    writer.WriteLine("{0}}}", indent);
                    break;
                case BasicDataType.BaseDataType when input.ValueRank == ValueRank.Scalar:
                    writer.WriteLine("{0}{1} {2} = __inputs[{3}];", indent, typeName, local, index);
                    break;
                default:
                    writer.WriteLine("{0}if (!__inputs[{1}].TryGetValue(out {2} {3}))",
                        indent, index, typeName, local);
                    writer.WriteLine("{0}{{", indent);
                    writer.WriteLine("{0}return new global::Opc.Ua.ServiceResult(global::Opc.Ua.StatusCodes.BadInvalidArgument);",
                        innerIndent);
                    writer.WriteLine("{0}}}", indent);
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
        /// <paramref name="indent"/> is the lambda-body indent of the
        /// surrounding OnCall.
        /// </summary>
        /// <remarks>
        /// The base <see cref="Opc.Ua.MethodState"/> dispatcher
        /// pre-populates the outputs list with one default-valued
        /// <see cref="Opc.Ua.Variant"/> per declared output argument
        /// before invoking the user handler, so the wrapper assigns
        /// boxed values by index rather than appending to avoid
        /// double-counting outputs at the wire.
        /// </remarks>
        private static void EmitOutputBox(
            ITemplateWriter writer,
            Parameter output,
            int index,
            int totalOutputs,
            string indent)
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

            string indexLiteral = index.ToString(System.Globalization.CultureInfo.InvariantCulture);

            switch (output.DataTypeNode.BasicDataType)
            {
                case BasicDataType.UserDefined:
                    writer.WriteLine("{0}__outputs[{1}] = global::Opc.Ua.Variant.FromStructure({2});", indent, indexLiteral, source);
                    break;
                case BasicDataType.BaseDataType when output.ValueRank == ValueRank.Scalar:
                    writer.WriteLine("{0}__outputs[{1}] = {2};", indent, indexLiteral, source);
                    break;
                default:
                    writer.WriteLine("{0}__outputs[{1}] = global::Opc.Ua.Variant.From({2});", indent, indexLiteral, source);
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

        /// <summary>
        /// Returns the simple leaf name (the last segment of the relative
        /// path) used as the C# class name's stem. Honors the convention
        /// that segment names themselves can contain underscores so we
        /// rely on the instance's <c>SymbolicName.Name</c> rather than
        /// splitting on <c>NodeDesign.PathChar</c>. For the root
        /// instance (empty <paramref name="relativePath"/>) the leaf is
        /// the root's own <c>SymbolicId.Name</c>.
        /// </summary>
        private static string ResolveLeafName(
            InstanceDesign root,
            string relativePath,
            NodeDesign instance)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return root?.SymbolicId?.Name ?? string.Empty;
            }
            string symbolicName = instance?.SymbolicName?.Name;
            if (string.IsNullOrEmpty(symbolicName))
            {
                return relativePath;
            }
            return symbolicName;
        }

        /// <summary>
        /// Returns the wrapper key of the lexical parent for the wrapper
        /// at <paramref name="relativePath"/> under <paramref name="root"/>.
        /// Returns <c>null</c> for the root itself (lives at namespace
        /// scope), the root's key for direct children, and the parent
        /// path's key for deeper nesting.
        /// </summary>
        private static string ResolveParentKey(
            InstanceDesign root,
            string relativePath,
            string leafName)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }
            if (relativePath.Length == leafName.Length)
            {
                return ComposeKey(root, string.Empty);
            }
            int trim = leafName.Length + 1;
            if (relativePath.Length <= trim)
            {
                return ComposeKey(root, string.Empty);
            }
            string parentPath = relativePath[..^trim];
            return ComposeKey(root, parentPath);
        }

        /// <summary>
        /// Returns the wrapper's CLR class name. Wrappers are emitted as
        /// nested types so the simple leaf name is sufficient — full
        /// dotted access is composed by the consumer through the chain
        /// of typed accessor properties.
        /// </summary>
        private static string ComposeWrapperClassName(string leafName, string suffix)
        {
            return (leafName ?? string.Empty) + suffix;
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
        /// Returns true when the supplied node should expose typed
        /// <c>Publish&lt;TEvent&gt;</c> overloads on its wrapper. A node
        /// qualifies if it carries the <c>EventNotifier=SubscribeToEvents</c>
        /// attribute (modeled as <see cref="ObjectDesign.SupportsEvents"/>;
        /// the model validator auto-promotes nodes with forward
        /// <c>HasEventSource</c>/<c>HasNotifier</c> references) or has a
        /// forward <c>GeneratesEvent</c>/<c>AlwaysGeneratesEvent</c>
        /// reference. Per the locked decision the typed overload is
        /// emitted only on spec-accurate notifier candidates so call sites
        /// don't drift from the model intent.
        /// </summary>
        private static bool QualifiesAsEventNotifier(NodeDesign node)
        {
            if (node is ObjectDesign od && od.SupportsEvents)
            {
                return true;
            }

            Reference[] references = node?.References;
            if (references == null || references.Length == 0)
            {
                return false;
            }

            foreach (Reference reference in references)
            {
                if (reference == null || reference.IsInverse)
                {
                    continue;
                }
                string refName = reference.ReferenceType?.Name;
                if (refName == "GeneratesEvent" ||
                    refName == "AlwaysGeneratesEvent")
                {
                    return true;
                }
            }

            return false;
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

        // Single nesting step. Wrappers are emitted inside the body of
        // the file template at column 4; each additional nesting level
        // adds one Indent.
        private const string Indent = "    ";

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
            public string LeafName;
            public string ParentKey;
            public string NodeStateType;
            public string BrowseNamespaceUri;
            public bool SupportsPublish;
            public List<ChildAccessor> Children;
            public List<string> ChildObjectKeys = [];
            public List<string> ChildMethodKeys = [];
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
            public string LeafName;
            public string ParentKey;
            public Parameter[] Inputs;
            public Parameter[] Outputs;
        }
    }
}
