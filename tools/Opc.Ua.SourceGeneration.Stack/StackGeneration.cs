// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
            StackGenerationOptions options)
        {
            m_context = context;
            m_options = options;
            m_compilationOptions = compilationOptions;
            m_telemetry = SourceGeneratorTelemetry.Create(m_context);
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
                                OptimizationLevel.Debug,
                        // The standard NodeSet "Models" pass runs in the Opc.Ua
                        // (Types-only) assembly which does not reference
                        // ISessionClient/ObjectTypeClient. Proxies are emitted
                        // by the Stack pass (Opc.Ua.Core) instead.
                        OmitObjectTypeProxies =
                            generationType == StackGenerationType.Models,
                        // Neither pass references Opc.Ua.Server.Fluent. The
                        // typed-accessor extensions emitted by
                        // FluentBuilderGenerator delegate into the Server-side
                        // INodeBuilder<T> surface, so they must be omitted
                        // here. Server-side consumers that ship per-namespace
                        // accessors emit them from their own model packages
                        // (the default opt-out is honoured by setting
                        // ModelSourceGeneratorOmitFluentApi=true on the
                        // assembly's csproj when needed).
                        OmitFluentApi = true,
                        UseTypeDefinitionModellingRules = true
                    });
                // Collect all generated cs files and produce them into the compilation
                foreach (string file in fileSystem.CreatedFiles
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                {
                    string content = Encoding.UTF8.GetString(fileSystem.Get(file));
                    m_context.AddSource(file, content);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A cancelled run is not a generator failure: let the driver
                // see the cancellation instead of caching an error diagnostic.
                throw;
            }
            catch (Exception ex)
            {
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.Exception,
                        Location.None,
                        ex.Message,
                        ex.ToString()));
            }
        }

        /// <summary>
        /// Tests the compilation options are valid
        /// </summary>
        /// <returns></returns>
        private bool CheckCompilationOptions(out StackGenerationType type)
        {
            type = StackGenerationType.None;

            // A generator must never fail a compilation it was not meant for.
            // The stack generator is loaded into every compilation that
            // references it transitively - a BenchmarkDotNet host project
            // being the case that surfaced this - so an absent opt-in means
            // "not my compilation", not "misconfigured".
            if (string.IsNullOrEmpty(m_options.Mode))
            {
                return false;
            }

            if (!m_compilationOptions.IsCSharp13OrLater)
            {
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.GenericError,
                        Location.None,
                        "Opc UA stack is too old. Minimum required language version is CSharp 13."));
                return false;
            }

            if (!Enum.TryParse(m_options.Mode, ignoreCase: true, out type) ||
                type == StackGenerationType.None)
            {
                type = StackGenerationType.None;
                m_context.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.GenericError,
                        Location.None,
                        $"'{m_options.Mode}' is not a valid {SourceGenerator.Name}Mode. " +
                            $"Use one of {StackGenerationType.Stack}, " +
                            $"{StackGenerationType.Models} or {StackGenerationType.All}."));
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
