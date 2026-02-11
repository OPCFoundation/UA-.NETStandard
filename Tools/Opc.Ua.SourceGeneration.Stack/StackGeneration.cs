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
    /// Generates the stack core
    /// </summary>
    internal sealed class StackGeneration
    {
        /// <summary>
        /// Generate stack
        /// </summary>
        public StackGeneration(
            SourceProductionContext context,
            CompilationOptions compilationOptions,
            StackGenerationOptions options,
            ILogger logger)
        {
            m_context = context;
            m_options = options;
            m_compilationOptions = compilationOptions;
            m_telemetry = SourceGeneratorTelemetry.Create(logger, m_context);
        }

        /// <summary>
        /// Generate all stack files
        /// </summary>
        public void Emit(CancellationToken cancellationToken)
        {
            if (!CheckCompilationOptions(out StackGenerationType generationType))
            {
                return;
            }
            using var fileSystem = new VirtualFileSystem();
            try
            {
                // Generate files into the virtual file system
                Generators.GenerateStack(
                    generationType,
                    fileSystem,
                    string.Empty,
                    m_telemetry,
                    new GeneratorOptions
                    {
                        Cancellation = cancellationToken,
                        Exclusions = m_options.Exclude,
                        OptimizeForCompileSpeed =
                            m_compilationOptions.OptimizationLevel ==
                                OptimizationLevel.Debug
                    });
                // Collect all generated cs files and produce them into the compilation
                foreach (string file in fileSystem.CreatedFiles
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                {
                    string content = Encoding.UTF8.GetString(fileSystem.Get(file));
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
        private bool CheckCompilationOptions(out StackGenerationType type)
        {
            if (m_compilationOptions.LanguageVersion < LanguageVersion.CSharp13) // TODO: Change to latest CA when available
            {
                type = StackGenerationType.None;
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.GenericError,
                        Location.None,
                        "Opc UA stack is too old. Minimum required language version is CSharp 14."));
                return false;
            }

            // Check that we are running with opc ua core production only
            switch (m_compilationOptions.AssemblyName)
            {
                case "Opc.Ua":
                    type = StackGenerationType.Models;
                    break;
                case "Opc.Ua.Core":
                    type = StackGenerationType.Stack;
                    break;
                case "Opc.Ua.Test":
                    type = StackGenerationType.All;
                    break;
                default:
                    type = StackGenerationType.None;
                    m_context.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.GenericError,
                            Location.None,
                            $"Stack generation not supported for {m_compilationOptions.AssemblyName} assembly."));
                    return false;
            }
            return true;
        }

        private readonly SourceProductionContext m_context;
        private readonly StackGenerationOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly CompilationOptions m_compilationOptions;
    }
}
