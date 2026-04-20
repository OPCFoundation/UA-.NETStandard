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
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Emits a partial <c>{Namespace}NodeManager</c> deriving from
    /// <c>CustomNodeManager2</c> plus a matching
    /// <c>{Namespace}NodeManagerFactory</c> implementing
    /// <c>INodeManagerFactory</c>. Opt-in via
    /// <see cref="DesignFileOptions.GenerateNodeManager"/>.
    /// </summary>
    /// <remarks>
    /// The generated manager wires the predefined nodes from the existing
    /// <c>Add{Namespace}(NodeStateCollection, ISystemContext)</c> extension
    /// (emitted by <see cref="NodeStateGenerator"/>) and exposes a
    /// <c>partial void Configure(INodeManagerBuilder builder)</c> hook for
    /// user code-behind. Lifecycle dispatchers (<c>AddPredefinedNode</c>,
    /// <c>RemovePredefinedNode</c>, <c>OnMonitoredItemCreated</c>) are wired
    /// to the runtime fluent dispatcher so callbacks registered via the
    /// builder are invoked at the right times.
    /// <para>
    /// When an attribute-driven binding supplies a class name and namespace
    /// override (via <see cref="DesignFileOptions.NodeManagerClassName"/> and
    /// <see cref="DesignFileOptions.NodeManagerNamespace"/>), the generator
    /// emits the partial under that user-chosen identity instead of the
    /// design-derived defaults. Factory emission can be suppressed via
    /// <see cref="DesignFileOptions.EmitNodeManagerFactory"/>.
    /// </para>
    /// </remarks>
    internal sealed class NodeManagerGenerator : IGenerator
    {
        /// <summary>
        /// Optional override for the namespace of the generated partial.
        /// Defaults to the design's <c>TargetNamespace.Prefix</c>.
        /// </summary>
        public string OverrideNamespace { get; init; }

        /// <summary>
        /// Optional override for the class name of the generated partial.
        /// Defaults to <c>{Prefix}NodeManager</c>.
        /// </summary>
        public string OverrideClassName { get; init; }

        /// <summary>
        /// When <c>false</c> the matching <c>{ClassName}Factory</c> is
        /// not emitted. Defaults to <c>true</c>.
        /// </summary>
        public bool EmitFactory { get; init; } = true;

        /// <summary>
        /// Create node manager generator.
        /// </summary>
        public NodeManagerGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            string nsPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string typeStem = nsPrefix.Replace(".", string.Empty, StringComparison.Ordinal);
            string nsUriSymbol = m_context.ModelDesign.Namespaces
                .GetConstantSymbolForNamespace(m_context.ModelDesign.TargetNamespace.Value);

            string targetNamespace = string.IsNullOrEmpty(OverrideNamespace)
                ? nsPrefix
                : OverrideNamespace;
            string targetClass = string.IsNullOrEmpty(OverrideClassName)
                ? typeStem + "NodeManager"
                : OverrideClassName;
            string factoryClass = string.IsNullOrEmpty(OverrideClassName)
                ? typeStem + "NodeManagerFactory"
                : OverrideClassName + "Factory";
            string fileStem = string.IsNullOrEmpty(OverrideClassName)
                ? nsPrefix
                : OverrideClassName;

            var resources = new List<Resource>(2)
            {
                EmitNodeManager(targetNamespace, targetClass, typeStem, nsUriSymbol, fileStem)
            };
            if (EmitFactory)
            {
                resources.Add(EmitFactoryFile(targetNamespace, targetClass, factoryClass, nsUriSymbol, fileStem));
            }
            return resources;
        }

        private TextFileResource EmitNodeManager(
            string targetNamespace,
            string targetClass,
            string typeStem,
            string nsUriSymbol,
            string fileStem)
        {
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.NodeManager.g.cs", fileStem));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, NodeManagerTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, targetNamespace);
            template.AddReplacement(Tokens.Namespace, typeStem);
            template.AddReplacement(Tokens.NodeManagerClassName, targetClass);
            template.AddReplacement(Tokens.NamespaceUri, nsUriSymbol);
            template.Render();
            return fileName.AsTextFileResource();
        }

        private TextFileResource EmitFactoryFile(
            string targetNamespace,
            string targetClass,
            string factoryClass,
            string nsUriSymbol,
            string fileStem)
        {
            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.NodeManagerFactory.g.cs", fileStem));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, NodeManagerTemplates.FactoryFile);
            template.AddReplacement(Tokens.NamespacePrefix, targetNamespace);
            template.AddReplacement(Tokens.NodeManagerClassName, targetClass);
            template.AddReplacement(Tokens.NodeManagerFactoryClassName, factoryClass);
            template.AddReplacement(Tokens.NamespaceUri, nsUriSymbol);
            template.Render();
            return fileName.AsTextFileResource();
        }

        private readonly IGeneratorContext m_context;
    }
}
