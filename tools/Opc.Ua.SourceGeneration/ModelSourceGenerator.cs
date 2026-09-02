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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates server and client models using the model generator library
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ModelSourceGenerator : IIncrementalGenerator
    {
        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            SourceGenerator.AttachDebuggerIfRequested();

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

            // Snapshot the structural per-file options before conversion so
            // downstream work remains cheaply cached per file. An ignored WoT
            // input leaves the pipeline here, before its contents are read or
            // it can produce diagnostics, dependencies or a virtual path claim.
            IncrementalValuesProvider<(AdditionalText Text, NodesetFileOptions Options)> wotInputFiles =
                textsWithOptions
                    .Where(static pair => pair.Text.IsWotFile(pair.Options))
                    .Select(static (pair, _) => (
                        pair.Text,
                        Options: pair.Options.ToNodeSetOptions()))
                    .Where(static pair => !pair.Options.Ignore);

            // Every active WoT input is converted independently: parse, bounds,
            // missing preservation/native mapping, dependency/resolver and
            // conversion problems are captured as diagnostics on the outcome
            // rather than thrown, so one malformed input can never abort the
            // whole generator run.
            IncrementalValueProvider<ImmutableArray<WotConversionOutcome>> wotOutcomes =
                wotInputFiles
                    .Select(static (input, ct) => WotNodeSetAdditionalText.Convert(
                        input.Text, input.Options, ct))
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
            IncrementalValueProvider<ImmutableArray<AdditionalText>> csvFiles =
                context.AdditionalTextsProvider
                    .Where(f => f.HasFileExtension("csv"))
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
            IncrementalValueProvider<ImmutableArray<ModelFluentAccessorProviderReference>>
                referencedAccessorProviders = context.CompilationProvider
                    .Select((c, _) => ReferencedFluentAccessorProviderScanner.Scan(c));
            IncrementalValueProvider<ImmutableHashSet<string>> stateTypeIndex =
                context.CompilationProvider
                    .Select((c, _) => OpcUaStateTypeIndex.Build(c));

            IncrementalValueProvider<ImmutableArray<NodeManagerAttributeDiscovery>> nodeManagerBindings =
                context.SyntaxProvider.ForAttributeWithMetadataName(
                    "Opc.Ua.Server.Fluent.NodeManagerAttribute",
                    static (node, ct) => NodeManagerAttributeDiscovery.Handles(node, ct),
                    static (ctx, ct) => NodeManagerAttributeDiscovery.Create(
                        ctx,
                        generateNodeSource: false,
                        cancellationToken: ct))
                .Where(static m => m is not null)
                .Collect();
            IncrementalValueProvider<ImmutableArray<NodeManagerAttributeDiscovery>> nodeSourceBindings =
                context.SyntaxProvider.ForAttributeWithMetadataName(
                    "Opc.Ua.Server.Fluent.NodeSourceAttribute",
                    static (node, ct) => NodeManagerAttributeDiscovery.Handles(node, ct),
                    static (ctx, ct) => NodeManagerAttributeDiscovery.Create(
                        ctx,
                        generateNodeSource: true,
                        cancellationToken: ct))
                .Where(static m => m is not null)
                .Collect();
            IncrementalValueProvider<ImmutableArray<NodeManagerAttributeDiscovery>>
                nodeAuthoringBindings = nodeManagerBindings
                    .Combine(nodeSourceBindings)
                    .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            IncrementalValueProvider<
                (
                    ImmutableArray<(AdditionalText Left, NodesetFileOptions)> InputFiles,
                    ImmutableArray<AdditionalText> CsvFiles,
                    ImmutableArray<AdditionalText> IdentifierFiles)> modelFiles = inputFiles
                .Combine(csvFiles)
                .Combine(identifierFiles)
                .Select(static (pair, _) => (
                    InputFiles: pair.Left.Left,
                    CsvFiles: pair.Left.Right,
                    IdentifierFiles: pair.Right));
            IncrementalValueProvider<
                (ModelCompilationOptions Options, CompilationOptions CompilationOptions)>
                modelSettings = options
                .Combine(settings)
                .Select(static (pair, _) => (
                    Options: pair.Left,
                    CompilationOptions: pair.Right));
            IncrementalValueProvider<
                (
                    ImmutableArray<ModelDependencyReference> ReferencedModels,
                    ImmutableArray<ModelFluentAccessorProviderReference> ReferencedAccessorProviders,
                    ImmutableArray<NodeManagerAttributeDiscovery> NodeManagerBindings)>
                modelReferences = referencedModels
                .Combine(referencedAccessorProviders)
                .Select(static (pair, _) => (
                    ReferencedModels: pair.Left,
                    ReferencedAccessorProviders: pair.Right))
                .Combine(nodeAuthoringBindings)
                .Select(static (pair, _) => (
                    ReferencedModels: pair.Left.ReferencedModels,
                    ReferencedAccessorProviders: pair.Left.ReferencedAccessorProviders,
                    NodeManagerBindings: pair.Right));
            IncrementalValueProvider<
                (
                    ImmutableArray<ModelDependencyReference> ReferencedModels,
                    ImmutableArray<ModelFluentAccessorProviderReference> ReferencedAccessorProviders,
                    ImmutableArray<NodeManagerAttributeDiscovery> NodeManagerBindings,
                    ImmutableHashSet<string> AvailableStateTypeNames)> modelDependencies =
                modelReferences
                .Combine(stateTypeIndex)
                .Select(static (pair, _) => (
                    ReferencedModels: pair.Left.ReferencedModels,
                    ReferencedAccessorProviders: pair.Left.ReferencedAccessorProviders,
                    NodeManagerBindings: pair.Left.NodeManagerBindings,
                    AvailableStateTypeNames: pair.Right));
            IncrementalValueProvider<
                (
                    ImmutableArray<(AdditionalText Left, NodesetFileOptions)> InputFiles,
                    ImmutableArray<AdditionalText> CsvFiles,
                    ImmutableArray<AdditionalText> IdentifierFiles,
                    ModelCompilationOptions Options,
                    CompilationOptions CompilationOptions)> configuredModel = modelFiles
                .Combine(modelSettings)
                .Select(static (pair, _) => (pair.Left.InputFiles,
                    pair.Left.CsvFiles,
                    pair.Left.IdentifierFiles,
                    pair.Right.Options,
                    pair.Right.CompilationOptions));
            IncrementalValueProvider<ModelCompilationInput> modelCompilationInput =
                configuredModel
                    .Combine(modelDependencies)
                    .Select(static (pair, _) => new ModelCompilationInput(
                        pair.Left.InputFiles,
                        pair.Left.CsvFiles,
                        pair.Left.IdentifierFiles,
                        pair.Left.Options,
                        pair.Left.CompilationOptions,
                        pair.Right.ReferencedModels,
                        pair.Right.ReferencedAccessorProviders,
                        pair.Right.NodeManagerBindings,
                        pair.Right.AvailableStateTypeNames));

            context.RegisterSourceOutput(
                modelCompilationInput,
                static (context, input) => SourceGenerator.Guard(
                    context,
                    () => new ModelCompilation(
                        context,
                        input.InputFiles,
                        input.CsvFiles,
                        input.IdentifierFiles,
                        input.Options,
                        input.CompilationOptions,
                        input.ReferencedModels,
                        input.ReferencedAccessorProviders,
                        input.NodeManagerBindings,
                        input.AvailableStateTypeNames).Emit(context.CancellationToken)));

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
                static (spc, pair) => SourceGenerator.Guard(
                    spc,
                    () => DataTypeCompilation.EmitBatch(
                        spc, pair.Left, pair.Right)));
        }

        private readonly record struct ModelCompilationInput(
            ImmutableArray<(AdditionalText, NodesetFileOptions)> InputFiles,
            ImmutableArray<AdditionalText> CsvFiles,
            ImmutableArray<AdditionalText> IdentifierFiles,
            ModelCompilationOptions Options,
            CompilationOptions CompilationOptions,
            ImmutableArray<ModelDependencyReference> ReferencedModels,
            ImmutableArray<ModelFluentAccessorProviderReference> ReferencedAccessorProviders,
            ImmutableArray<NodeManagerAttributeDiscovery> NodeManagerBindings,
            ImmutableHashSet<string> AvailableStateTypeNames);
    }
}
