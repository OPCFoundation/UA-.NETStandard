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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            ILogger logger)
        {
            m_context = context;
            m_input = inputFiles;
            m_identifierFiles = identifierFiles;
            m_options = options;
            m_compilationOptions = compilationOptions;
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
                            : m_options.ObjectTypeProxyNamespace
                };

                // Load all available nodeset files from the input
                NodesetFileCollection nodesets = m_input.ToNodeSetFileCollection(
                    sourceFiles, // .WithFallback(vfs),
                    m_telemetry);

                // Reduce referenced model attributes to a single dictionary by
                // model URI (with tie-break on highest version+publication date)
                // so the downstream generators can apply override resolution.
                System.Collections.Generic.IReadOnlyDictionary<string, ModelDependencyReference>
                    referencedModels = BuildReferencedModelMap();

                nodesets.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes,
                    referencedModels);

                // Process any remaining design files
                new DesignFileCollection
                {
                    Targets = [.. m_input
                        .Where(f => !nodesets.Files.ContainsValue(f.Item1.Path))
                        .Select(f => f.Item1.Path)],
                    Options = m_options.Options
                }.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes,
                    [.. m_identifierFiles.Select(i => i.Path)],
                    referencedModels);

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
        private System.Collections.Generic.IReadOnlyDictionary<string, ModelDependencyReference>
            BuildReferencedModelMap()
        {
            if (m_referencedModels.IsDefaultOrEmpty)
            {
                return ImmutableDictionary<string, ModelDependencyReference>.Empty;
            }
            var map = new System.Collections.Generic.Dictionary<string, ModelDependencyReference>(
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

        private readonly SourceProductionContext m_context;
        private readonly ImmutableArray<(AdditionalText, NodesetFileOptions)> m_input;
        private readonly ImmutableArray<AdditionalText> m_identifierFiles;
        private readonly ModelCompilationOptions m_options;
        private readonly CompilationOptions m_compilationOptions;
        private readonly ImmutableArray<ModelDependencyReference> m_referencedModels;
        private readonly SourceGeneratorTelemetry m_telemetry;
    }
}
