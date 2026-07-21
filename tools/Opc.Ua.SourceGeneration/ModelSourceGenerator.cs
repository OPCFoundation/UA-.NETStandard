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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using IIncrementalGenerator = SGF.IncrementalGenerator;
using IncrementalGeneratorAttribute = SGF.IncrementalGeneratorAttribute;
using IncrementalGeneratorInitializationContext = SGF.SgfInitializationContext;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates server and client models using the model generator library
    /// </summary>
    [IncrementalGenerator]
    public class ModelSourceGenerator : IIncrementalGenerator
    {
        /// <inheritdoc/>
        public ModelSourceGenerator()
            : base(SourceGenerator.Name)
        {
        }

        /// <inheritdoc/>
        public override void OnInitialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUGX
            AttachDebugger();
#endif
            // Pair every AdditionalFile with its own per-file analyzer config
            // options once, up front, so both the design/NodeSet2 filter and
            // the WoT filter (which needs the per-file
            // ModelSourceGeneratorWot opt-in metadata to recognize a plain
            // .jsonld input) can be evaluated without recomputing options.
            IncrementalValuesProvider<(AdditionalText Text, AnalyzerConfigOptions Options)> textsWithOptions =
                context.AdditionalTextsProvider
                    .Combine(context.AnalyzerConfigOptionsProvider)
                    .Select(static (pair, _) => (pair.Left, pair.Right.GetOptions(pair.Left)));

            IncrementalValueProvider<ImmutableArray<(AdditionalText Left, NodesetFileOptions)>> xmlInputFiles =
                textsWithOptions
                    .Where(static pair => pair.Text.IsDesignOrNodeset2File())
                    .Select(static (pair, _) => (pair.Text, pair.Options.ToNodeSetOptions()))
                    .Collect();

            // Every WoT input is converted independently (and cheaply cached
            // per file): parse, bounds, missing preservation/native mapping,
            // dependency/resolver and conversion problems are captured as
            // diagnostics on the outcome rather than thrown, so one malformed
            // input can never abort the whole generator run.
            IncrementalValueProvider<ImmutableArray<WotConversionOutcome>> wotOutcomes =
                textsWithOptions
                    .Where(static pair => pair.Text.IsWotFile(pair.Options))
                    .Select(static (pair, ct) => WotNodeSetAdditionalText.Convert(
                        pair.Text, pair.Options.ToNodeSetOptions(), ct))
                    .Collect();

            // Resolve WoT outcomes against the explicit NodeSet2/ModelDesign
            // inputs and each other: forwards every conversion diagnostic and
            // drops (with a diagnostic) any WoT input whose synthesized
            // virtual NodeSet2 path collides with another input, so a
            // collision can never silently overwrite another model.
            IncrementalValueProvider<(
                ImmutableArray<(AdditionalText Text, NodesetFileOptions Options)> Accepted,
                ImmutableArray<Diagnostic> Diagnostics)> resolvedWotInputs =
                xmlInputFiles
                    .Combine(wotOutcomes)
                    .Select(static (pair, _) => pair.Left.ResolveWotInputs(pair.Right));

            context.RegisterSourceOutput(resolvedWotInputs, static (spc, resolved) =>
            {
                foreach (Diagnostic diagnostic in resolved.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }
            });

            IncrementalValueProvider<ImmutableArray<(AdditionalText Left, NodesetFileOptions)>> inputFiles =
                xmlInputFiles
                    .Combine(resolvedWotInputs)
                    .Select(static (pair, _) => pair.Left.AddRange(pair.Right.Accepted));
            IncrementalValueProvider<ImmutableArray<AdditionalText>> identifierFiles =
                context.AdditionalTextsProvider
                    .Where(f => f.IsIdentifierFile())
                    .Collect();
            IncrementalValueProvider<ModelCompilationOptions> options =
                context.AnalyzerConfigOptionsProvider
                    .Select((p, _) => ModelCompilationOptions.From(p));
            IncrementalValueProvider<CompilationOptions> settings =
                context.CompilationProvider
                    .Select((c, _) => CompilationOptions.From(c));
            IncrementalValueProvider<ImmutableArray<ModelDependencyReference>> referencedModels =
                context.CompilationProvider
                    .Select((c, _) => ReferencedModelDependencyScanner.Scan(c));
            IncrementalValueProvider<ImmutableHashSet<string>> stateTypeIndex =
                context.CompilationProvider
                    .Select((c, _) => OpcUaStateTypeIndex.Build(c));

            IncrementalValueProvider<ImmutableArray<NodeManagerAttributeDiscovery>> nodeManagerBindings =
                context.SyntaxProvider.ForAttributeWithMetadataName(
                    "Opc.Ua.Server.Fluent.NodeManagerAttribute",
                    static (node, ct) => NodeManagerAttributeDiscovery.Handles(node, ct),
                    static (ctx, ct) => NodeManagerAttributeDiscovery.Create(ctx, ct))
                .Where(static m => m is not null)
                .Collect();

            var modelFiles = inputFiles
                .Combine(identifierFiles)
                .Select(static (pair, _) => (
                    InputFiles: pair.Left,
                    IdentifierFiles: pair.Right));
            var modelSettings = options
                .Combine(settings)
                .Select(static (pair, _) => (
                    Options: pair.Left,
                    CompilationOptions: pair.Right));
            var modelReferences = referencedModels
                .Combine(nodeManagerBindings)
                .Select(static (pair, _) => (
                    ReferencedModels: pair.Left,
                    NodeManagerBindings: pair.Right));
            var modelDependencies = modelReferences
                .Combine(stateTypeIndex)
                .Select(static (pair, _) => (
                    ReferencedModels: pair.Left.ReferencedModels,
                    NodeManagerBindings: pair.Left.NodeManagerBindings,
                    AvailableStateTypeNames: pair.Right));
            var configuredModel = modelFiles
                .Combine(modelSettings)
                .Select(static (pair, _) => (
                    InputFiles: pair.Left.InputFiles,
                    IdentifierFiles: pair.Left.IdentifierFiles,
                    Options: pair.Right.Options,
                    CompilationOptions: pair.Right.CompilationOptions));
            IncrementalValueProvider<ModelCompilationInput> modelCompilationInput =
                configuredModel
                    .Combine(modelDependencies)
                    .Select(static (pair, _) => new ModelCompilationInput(
                        pair.Left.InputFiles,
                        pair.Left.IdentifierFiles,
                        pair.Left.Options,
                        pair.Left.CompilationOptions,
                        pair.Right.ReferencedModels,
                        pair.Right.NodeManagerBindings,
                        pair.Right.AvailableStateTypeNames));

            context.RegisterSourceOutput(
                modelCompilationInput,
                (context, input) => new ModelCompilation(
                    context,
                    input.InputFiles,
                    input.IdentifierFiles,
                    input.Options,
                    input.CompilationOptions,
                    input.ReferencedModels,
                    input.NodeManagerBindings,
                    input.AvailableStateTypeNames,
                    Logger).Emit(context.CancellationToken));

            IncrementalValueProvider<bool> publicDataTypeExtensions =
                context.AnalyzerConfigOptionsProvider
                    .Select((p, _) => p.GlobalOptions.GetBool(
                        "PublicDataTypeExtensions"));

            context.RegisterSourceOutput(context.SyntaxProvider.ForAttributeWithMetadataName(
                    "Opc.Ua.DataTypeAttribute",
                    static (node, ct) => DataTypeCompilation.Handles(node, ct),
                    static (context, ct) => new DataTypeCompilation(context, ct))
                .Where(static m => m is not null)
                .Collect()
                .Combine(publicDataTypeExtensions),
                static (spc, pair) => DataTypeCompilation.EmitBatch(
                    spc, pair.Left, pair.Right));
        }

        private readonly record struct ModelCompilationInput(
            ImmutableArray<(AdditionalText, NodesetFileOptions)> InputFiles,
            ImmutableArray<AdditionalText> IdentifierFiles,
            ModelCompilationOptions Options,
            CompilationOptions CompilationOptions,
            ImmutableArray<ModelDependencyReference> ReferencedModels,
            ImmutableArray<NodeManagerAttributeDiscovery> NodeManagerBindings,
            ImmutableHashSet<string> AvailableStateTypeNames);
    }
}
