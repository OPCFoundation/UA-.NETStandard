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
            ILogger logger)
        {
            m_context = context;
            m_input = inputFiles;
            m_identifierFiles = identifierFiles;
            m_options = options;
            m_compilationOptions = compilationOptions;
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
                        m_compilationOptions.OptimizationLevel == OptimizationLevel.Debug
                };

                // Load all available nodeset files from the input
                NodesetFileCollection nodesets = m_input.ToNodeSetFileCollection(
                    sourceFiles, // .WithFallback(vfs),
                    m_telemetry);
                nodesets.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes);

                // Process any remaining design files
                new DesignFileCollection
                {
                    DesignFiles = [.. m_input
                        .Where(f => !nodesets.Files.ContainsValue(f.Item1.Path))
                        .Select(f => f.Item1.Path)],
                    Options = m_options.Options
                }.GenerateCode(
                    sourceFiles.WithFallback(vfs),
                    string.Empty,
                    m_telemetry,
                    generatorOptions,
                    m_options.UseAllowSubtypes,
                    [.. m_identifierFiles.Select(i => i.Path)]);

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

        private readonly SourceProductionContext m_context;
        private readonly ImmutableArray<(AdditionalText, NodesetFileOptions)> m_input;
        private readonly ImmutableArray<AdditionalText> m_identifierFiles;
        private readonly ModelCompilationOptions m_options;
        private readonly CompilationOptions m_compilationOptions;
        private readonly SourceGeneratorTelemetry m_telemetry;
    }
}
