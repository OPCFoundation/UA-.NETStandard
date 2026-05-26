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
using System.Reflection;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Fluent builder for composing the predefined-node tree of a
    /// node manager from multiple information-model sources. Used at
    /// the <c>LoadPredefinedNodesAsync</c> phase, BEFORE
    /// <see cref="INodeManagerBuilder"/> wires callbacks onto loaded
    /// nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fluent surface accepts two source kinds:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="AddModel(Action{NodeStateCollection, ISystemContext})"/>
    ///     — a callback that contributes nodes via a generated
    ///     <c>AddOpcUaXxx</c> extension method.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="ImportNodeSet(Stream)"/> and
    ///     <see cref="ImportEmbeddedNodeSet(Assembly, string)"/>
    ///     — read a NodeSet2 XML at runtime via
    ///     <see cref="Opc.Ua.Export.UANodeSet.Read(Stream)"/> and
    ///     <c>UANodeSet.Import(ISystemContext, NodeStateCollection)</c>.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Sources are applied in the order they were added; later sources
    /// can layer on top of (or extend) earlier ones.
    /// </para>
    /// <para>
    /// AOT/trim safety: model-callback sources are reflection-free.
    /// NodeSet2 XML loading uses
    /// <see cref="Opc.Ua.Export.UANodeSet.Read(Stream)"/>, which the
    /// stack annotates with <c>RequiresDynamicCode</c> /
    /// <c>RequiresUnreferencedCode</c> internally; the runtime stack
    /// preserves the necessary types.
    /// </para>
    /// </remarks>
    public interface IModelLoaderBuilder
    {
        /// <summary>
        /// Adds a generated model callback. Typical use:
        /// <code>
        /// loader.AddModel((nodes, ctx) => nodes.AddOpcUaDi(ctx));
        /// </code>
        /// </summary>
        IModelLoaderBuilder AddModel(Action<NodeStateCollection, ISystemContext> populate);

        /// <summary>
        /// Imports a NodeSet2 XML from the supplied stream. The stream
        /// is read synchronously; the caller is responsible for
        /// disposing it.
        /// </summary>
        IModelLoaderBuilder ImportNodeSet(Stream nodeSetXml);

        /// <summary>
        /// Imports a NodeSet2 XML from an embedded resource on the
        /// supplied assembly. Convenience overload that handles
        /// stream lookup + disposal.
        /// </summary>
        IModelLoaderBuilder ImportEmbeddedNodeSet(
            Assembly assembly,
            string resourceName);

        /// <summary>
        /// Runs every registered source against the supplied
        /// <see cref="NodeStateCollection"/>. Called by the owning
        /// node manager from its <c>LoadPredefinedNodesAsync</c>
        /// override; returns the same collection for chaining.
        /// </summary>
        NodeStateCollection Build(NodeStateCollection target, ISystemContext context);
    }

    /// <summary>
    /// Default <see cref="IModelLoaderBuilder"/> implementation. Hand-
    /// written node managers can instantiate this directly; the
    /// <see cref="FluentNodeManagerBase"/> base class will expose a
    /// builder in a future iteration.
    /// </summary>
    public sealed class ModelLoaderBuilder : IModelLoaderBuilder
    {
        /// <inheritdoc/>
        public IModelLoaderBuilder AddModel(Action<NodeStateCollection, ISystemContext> populate)
        {
            if (populate == null) { throw new ArgumentNullException(nameof(populate)); }
            m_sources.Add(populate);
            return this;
        }

        /// <inheritdoc/>
        public IModelLoaderBuilder ImportNodeSet(Stream nodeSetXml)
        {
            if (nodeSetXml == null) { throw new ArgumentNullException(nameof(nodeSetXml)); }

            // Read eagerly so the caller can dispose the stream immediately;
            // Build replays the parsed nodeset into the target collection.
            Opc.Ua.Export.UANodeSet nodeSet = Opc.Ua.Export.UANodeSet.Read(nodeSetXml)
                ?? throw new InvalidOperationException(
                    "Failed to read NodeSet2 XML from the supplied stream.");
            m_sources.Add((nodes, ctx) => nodeSet.Import(ctx, nodes));
            return this;
        }

        /// <inheritdoc/>
        public IModelLoaderBuilder ImportEmbeddedNodeSet(
            Assembly assembly,
            string resourceName)
        {
            if (assembly == null) { throw new ArgumentNullException(nameof(assembly)); }
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new ArgumentNullException(nameof(resourceName));
            }

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Embedded resource '{0}' not found in assembly '{1}'.",
                        resourceName,
                        assembly.FullName));
            return ImportNodeSet(stream);
        }

        /// <inheritdoc/>
        public NodeStateCollection Build(
            NodeStateCollection target,
            ISystemContext context)
        {
            if (target == null) { throw new ArgumentNullException(nameof(target)); }
            if (context == null) { throw new ArgumentNullException(nameof(context)); }
            foreach (Action<NodeStateCollection, ISystemContext> source in m_sources)
            {
                source(target, context);
            }
            return target;
        }

        private readonly List<Action<NodeStateCollection, ISystemContext>> m_sources = [];
    }
}
