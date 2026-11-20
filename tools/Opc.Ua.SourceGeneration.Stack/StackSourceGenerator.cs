/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.CodeAnalysis;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates server and client models using the model generator library
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class StackSourceGenerator : IIncrementalGenerator
    {
        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            SourceGenerator.AttachDebuggerIfRequested();

            // Collect options
            IncrementalValueProvider<StackGenerationOptions> options =
                context.AnalyzerConfigOptionsProvider
                    .Select((p, _) => StackGenerationOptions.FromProvider(p));
            IncrementalValueProvider<CompilationOptions> settings =
                context.CompilationProvider
                    .Select((c, _) => CompilationOptions.From(c));

            context.RegisterSourceOutput(
                settings.Combine(options),
                static (context, combination) => SourceGenerator.Guard(
                    context,
                    () => new StackGeneration(
                        context,
                        combination.Left,
                        combination.Right).Emit(context.CancellationToken)));
        }
    }
}
