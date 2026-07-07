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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Opc.Ua.SourceGeneration.Dependency;
using ILogger = SGF.Diagnostics.ILogger;
using SourceProductionContext = SGF.SgfSourceProductionContext;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Compiles the model. This honors the options of the compile command
    /// of the tool.  However, it does not generate identifier files and
    /// assumes that if identifiers are required they are loaded from a
    /// csv file that is side by side with the design files.  Also, this
    /// does not produce xsd or bsd nor nodeset files, which cannot be
    /// added to a compilation using roslyn source generator (yet).
    /// </summary>
    internal sealed class ModelCompilation
    {
        /// <summary>
        /// Create compilation
        /// </summary>
        public ModelCompilation(
            SourceProductionContext context,
            ImmutableArray<(AdditionalText, NodesetFileOptions)> inputFiles,
            ImmutableArray<AdditionalText> identifierFiles,
            ModelCompilationOptions options,
            CompilationOptions compilationOptions,
            ImmutableArray<ModelDependencyReference> referencedModels,
            ImmutableArray<NodeManagerAttributeDiscovery> nodeManagerBindings,
            ILogger logger)
        {
            m_context = context;
            m_input = inputFiles;
            m_identifierFiles = identifierFiles;
            m_options = options;
            m_compilationOptions = compilationOptions;
            m_nodeManagerBindings = nodeManagerBindings;
            m_referencedModels = referencedModels;
            m_telemetry = SourceGeneratorTelemetry.Create(logger, m_context);
        }

        /// <summary>
        /// Perform the compilation
        /// </summary>
        public void Emit(CancellationToken cancellationToken)
        {
            if (!CheckCompilationOptions())
            {
                return;
            }
            var sourceFiles = new SourceGeneratorFileSystem(
                m_input.Select(i => i.Item1).Concat(m_identifierFiles));

            using var vfs = new VirtualFileSystem(); // Use a virtual file sytem
            try
            {
                if (m_input.Length == 0)
                {
                    // Nothing to do
                    return;
                }

                string[] exclusions = [.. m_options.Exclude
                    .Append("Draft")
                    .Distinct()];
                var generatorOptions = new GeneratorOptions
                {
                    Cancellation = cancellationToken,
                    Exclusions = exclusions,
                    // csharp10 or below does not support utf8 string literals
                    UseUtf8StringLiterals =
                        m_compilationOptions.LanguageVersion >= LanguageVersion.CSharp11,
                    OptimizeForCompileSpeed =
                        m_compilationOptions.OptimizationLevel == OptimizationLevel.Debug,
                    OmitObjectTypeProxies = m_options.OmitObjectTypeProxies,
                    ObjectTypeProxyNamespace =
                        string.IsNullOrWhiteSpace(m_options.ObjectTypeProxyNamespace)
                            ? null
                            : m_options.ObjectTypeProxyNamespace,
                    UseTypeDefinitionModellingRules =
                        m_options.UseTypeDefinitionModellingRules,
                    EmitDependencyMetadata = ResolveEmitDependencyMetadata(),
                    OmitFluentApi = m_options.OmitFluentApi
                };

                // Load all available nodeset files from the input
                NodesetFileCollection nodesets = m_input.ToNodeSetFileCollection(
                    sourceFiles, // .WithFallback(vfs),
                    m_telemetry);

                // Resolve [NodeManager] bindings: validate partial-ness and
                // build the binding list to pass into both GenerateCode calls
                // (nodeset-derived and design-file-derived).
                var bindings = new List<NodeManagerAttributeBinding>();
                var bindingByPayload =
                    new Dictionary<NodeManagerAttributeBinding, NodeManagerAttributeDiscovery>();
                foreach (NodeManagerAttributeDiscovery discovery in m_nodeManagerBindings)
                {
                    if (discovery == null)
                    {
                        continue;
                    }
                    if (!discovery.IsPartial)
                    {
                        m_context.ReportDiagnostic(
                            Diagnostic.Create(
                                SourceGenerator.NodeManagerNotPartial,
                                discovery.Location,
                                discovery.Binding.TargetNamespace +
                                "." +
                                discovery.Binding.TargetClassName));
                        continue;
                    }
                    bindings.Add(discovery.Binding);
                    bindingByPayload[discovery.Binding] = discovery;
                }

                void reportBinding(NodeManagerAttributeBinding binding, string message)
                {
                    Location loc = bindingByPayload.TryGetValue(binding, out NodeManagerAttributeDiscovery d) && d != null
                        ? d.Location
                        : Location.None;
                    m_context.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.NodeManagerBindingError,
                            loc,
                            message));
                }

                // Reduce referenced model attributes to a single dictionary by
                // model URI (with tie-break on highest version+publication date)
                // so the downstream generators can apply override resolution.
                IReadOnlyDictionary<string, ModelDependencyReference>
                    referencedModels = BuildReferencedModelMap();
                IReadOnlyDictionary<string, ModelDependencyV1>
                    referencedDependencies = BuildReferencedDependencyMap();

                // The design files that are not NodeSet2 inputs form the
                // ModelDesign pass. Compute them up front so the total model
                // count (NodeSet2 models + ModelDesign targets) can be shared
                // with both passes: [NodeManager] bindings are resolved across
                // both passes, so single-model fallback / ambiguity detection
                // must see the global model count, not the per-pass count.
                List<string> designTargets = [.. m_input
                    .Where(f => !nodesets.Files.ContainsValue(f.Item1.Path))
                    .Select(f => f.Item1.Path)];

                var designDependencies = new List<string>(nodesets.DesignFileEntries);
                designDependencies.AddRange(designTargets);

                // A [NodeManager] may bind to a model produced by either pass
                // (a NodeSet2 type model or a ModelDesign instance model). The
                // "used" set is therefore shared across both passes and the
                // unmatched-binding diagnostics are reported once, after both
                // passes — reporting per pass would false-positive a binding
                // that the other pass matched (issue #3937).
                HashSet<NodeManagerAttributeBinding> usedBindings =
                    bindings.Count > 0 ? [] : null;
                int totalModelCount = nodesets.ModelUris.Count() + designTargets.Count;

                nodesets.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes,
                    referencedModels,
                    bindings.Count > 0 ? bindings : null,
                    bindings.Count > 0 ? reportBinding : null,
                    referencedDependencies,
                    usedBindings,
                    totalModelCount);

                // Process the remaining design files. Every NodeSet2 input
                // (encoded with the prefix/name computed by the nodeset
                // pass) and every other ModelDesign input is supplied as a
                // dependency so cross-model references resolve — both
                // ModelDesign -> NodeSet2 (e.g. an instance whose
                // TypeDefinition is a NodeSet2-defined ObjectType) and
                // ModelDesign -> ModelDesign across directories.
                new DesignFileCollection
                {
                    Targets = designTargets,
                    Dependencies = designDependencies,
                    Options = m_options.Options
                }.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes,
                    [.. m_identifierFiles.Select(i => i.Path)],
                    referencedModels,
                    bindings.Count > 0 ? bindings : null,
                    bindings.Count > 0 ? reportBinding : null,
                    usedBindings,
                    totalModelCount);

                // Report any [NodeManager] bindings that neither pass matched,
                // once, against the shared used-set aggregated across passes.
                Generators.ReportUnmatchedNodeManagerBindings(
                    bindings,
                    usedBindings,
                    totalModelCount,
                    reportBinding);

                // Collect all generated cs files and produce them into the compilation
                foreach (string file in vfs.CreatedFiles
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                {
                    string content = Encoding.UTF8.GetString(vfs.Get(file));
                    m_context.AddSource(file, content);
                }
            }
            catch (Exception ex)
            {
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.Exception,
                        Location.None,
                        ex.Message,
                        ex.StackTrace));
            }
        }

        /// <summary>
        /// Tests the compilation options are valid
        /// </summary>
        /// <returns></returns>
        private bool CheckCompilationOptions()
        {
            if (m_compilationOptions.LanguageVersion < LanguageVersion.CSharp8)
            {
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.GenericError,
                        Location.None,
                        "Minimum required language version is CSharp 8."));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Group the referenced-assembly attributes by model URI; when more
        /// than one assembly contributes the same URI, prefer the entry with
        /// the highest <c>(Version, PublicationDate)</c> lexicographic tuple
        /// per the contract on <see cref="ModelDependencyAttribute"/>.
        /// </summary>
        private IReadOnlyDictionary<string, ModelDependencyReference>
            BuildReferencedModelMap()
        {
            if (m_referencedModels.IsDefaultOrEmpty)
            {
                return ImmutableDictionary<string, ModelDependencyReference>.Empty;
            }
            var map = new Dictionary<string, ModelDependencyReference>(
                StringComparer.Ordinal);
            foreach (ModelDependencyReference candidate in m_referencedModels)
            {
                if (!candidate.IsValid)
                {
                    continue;
                }
                if (!map.TryGetValue(candidate.ModelUri, out ModelDependencyReference existing))
                {
                    map[candidate.ModelUri] = candidate;
                    continue;
                }
                int cmp = string.CompareOrdinal(candidate.Version, existing.Version);
                if (cmp == 0)
                {
                    cmp = string.CompareOrdinal(
                        candidate.PublicationDate, existing.PublicationDate);
                }
                if (cmp > 0)
                {
                    m_context.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.ModelDependencyTieBreak,
                            Location.None,
                            candidate.ModelUri,
                            candidate.AssemblyName,
                            existing.AssemblyName));
                    map[candidate.ModelUri] = candidate;
                }
                else if (cmp < 0)
                {
                    m_context.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.ModelDependencyTieBreak,
                            Location.None,
                            existing.ModelUri,
                            existing.AssemblyName,
                            candidate.AssemblyName));
                }
            }
            return map;
        }

        /// <summary>
        /// Build a per-URI dictionary of the deserialised model
        /// dependency payloads scanned from referenced assemblies.
        /// Payloads with unknown versions or malformed encodings are
        /// silently dropped.
        /// </summary>
        private IReadOnlyDictionary<string, ModelDependencyV1>
            BuildReferencedDependencyMap()
        {
            if (m_referencedModels.IsDefaultOrEmpty)
            {
                return ImmutableDictionary<string, ModelDependencyV1>.Empty;
            }
            var map = new Dictionary<string, ModelDependencyV1>(
                StringComparer.Ordinal);
            foreach (ModelDependencyReference candidate in m_referencedModels)
            {
                ModelDependencyV1 decoded = candidate.GetDependency();
                if (decoded == null)
                {
                    continue;
                }
                if (!map.ContainsKey(candidate.ModelUri))
                {
                    map[candidate.ModelUri] = decoded;
                }
            }
            return map;
        }

        /// <summary>
        /// Resolve whether the model-dependency assembly attribute
        /// should be emitted, honouring the compilation's OutputKind
        /// in <c>Auto</c> mode.
        /// </summary>
        private bool ResolveEmitDependencyMetadata()
        {
            return m_options.EmitDependencyMetadata switch
            {
                EmitDependencyMetadataMode.Always => true,
                EmitDependencyMetadataMode.Never => false,
                _ => m_compilationOptions.OutputKind is
                        OutputKind.DynamicallyLinkedLibrary or
                        OutputKind.NetModule
            };
        }

        private readonly SourceProductionContext m_context;
        private readonly ImmutableArray<(AdditionalText, NodesetFileOptions)> m_input;
        private readonly ImmutableArray<AdditionalText> m_identifierFiles;
        private readonly ModelCompilationOptions m_options;
        private readonly CompilationOptions m_compilationOptions;
        private readonly ImmutableArray<ModelDependencyReference> m_referencedModels;
        private readonly ImmutableArray<NodeManagerAttributeDiscovery> m_nodeManagerBindings;
        private readonly SourceGeneratorTelemetry m_telemetry;
    }
}
