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
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Source Generation API
    /// </summary>
    public static class Generators
    {
        /// <summary>
        /// Generate code from design files
        /// </summary>
        /// <param name="designFiles">Design files to process</param>
        /// <param name="fileSystem">File system abstraction to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">Telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        /// <param name="useAllowSubtypes">allow subtypes</param>
        /// <param name="identifierFiles">Any additional csv files</param>
        /// <param name="referencedModels">Models supplied by referenced assemblies.</param>
        /// <param name="nodeManagerBindings">Optional node manager bindings.</param>
        /// <param name="reportBindingDiagnostic">Optional binding diagnostic callback.</param>
        /// <param name="sharedUsedBindings">Optional set that accumulates matched bindings.</param>
        /// <param name="bindingModelCount">Total number of generatable models across all passes.</param>
        public static void GenerateCode(
            this DesignFileCollection designFiles,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null,
            bool useAllowSubtypes = false,
            List<string> identifierFiles = null,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null,
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings = null,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic = null,
            HashSet<NodeManagerAttributeBinding> sharedUsedBindings = null,
            int bindingModelCount = 0)
        {
            GenerateCode(
                designFiles,
                fileSystem,
                outputDir,
                telemetry,
                options,
                useAllowSubtypes,
                identifierFiles,
                referencedModels,
                nodeManagerBindings,
                reportBindingDiagnostic,
                sharedUsedBindings,
                bindingModelCount,
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// Generate code from design files with fluent-accessor provider metadata.
        /// </summary>
        /// <param name="designFiles">Design files to process</param>
        /// <param name="fileSystem">File system abstraction to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">Telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        /// <param name="useAllowSubtypes">allow subtypes</param>
        /// <param name="identifierFiles">Any additional csv files</param>
        /// <param name="referencedModels">Models supplied by referenced
        /// assemblies (keyed by model URI). Used to seed the assembly
        /// dependency closure and may be empty. Targets supplied under the
        /// same C# prefix are normally skipped; when
        /// <see cref="GeneratorOptions.FluentAccessorsOnly"/> is enabled they are
        /// instead loaded and used to emit typed fluent accessors only.</param>
        /// <param name="nodeManagerBindings">
        /// Optional <c>[NodeManager]</c> attribute bindings discovered in
        /// the consuming compilation. When supplied, each binding is
        /// matched to a design by <see cref="NodeManagerAttributeBinding.NamespaceUri"/>
        /// (preferred) or, if no URI is given, by single-design fallback.
        /// Matched bindings force <c>GenerateNodeManager = true</c> for the
        /// design and override the manager class name and namespace.
        /// </param>
        /// <param name="reportBindingDiagnostic">
        /// Optional callback invoked for each binding-related warning or
        /// error (e.g. unmatched URI, ambiguous fallback). Implementations
        /// typically convert these into Roslyn diagnostics.
        /// </param>
        /// <param name="sharedUsedBindings">
        /// Optional caller-owned set that accumulates the bindings matched
        /// across multiple generation passes (NodeSet2 and ModelDesign).
        /// When supplied, this pass records its matches in the set and does
        /// <b>not</b> report unmatched bindings itself; the caller reports
        /// them once, after all passes, via
        /// <see cref="ReportUnmatchedNodeManagerBindings"/>. When <c>null</c>
        /// the pass keeps a private set and reports directly (single pass).
        /// </param>
        /// <param name="bindingModelCount">
        /// Total number of generatable models across all passes, used for
        /// single-model binding fallback and ambiguity detection. When
        /// <c>0</c> the per-pass model count is used (single-pass callers).
        /// </param>
        /// <param name="reportFluentAccessorsOnlyDiagnostic">
        /// Optional callback invoked when a model cannot participate in
        /// fluent-accessors-only generation. Arguments are model URI,
        /// requested prefix, input path, and failure reason.
        /// </param>
        /// <param name="referencedModelProviders">
        /// All model dependency declarations from referenced assemblies,
        /// including payload-bearing producers and payloadless re-exports.
        /// </param>
        /// <param name="referencedAccessorProviders">
        /// All referenced assemblies that declare generated fluent accessors.
        /// </param>
        /// <param name="referencedDependencies">
        /// Per-URI model dependency payloads recovered from referenced
        /// assemblies via <c>ReferencedModelDependencyScanner</c>. When
        /// present, the validator pre-imports these dependency payloads
        /// so a design file can resolve upstream types (e.g. subtype a
        /// structure) without an explicit <c>AdditionalFiles</c> entry
        /// for the upstream model.
        /// </param>
        public static void GenerateCode(
            this DesignFileCollection designFiles,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options,
            bool useAllowSubtypes,
            List<string> identifierFiles,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels,
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic,
            HashSet<NodeManagerAttributeBinding> sharedUsedBindings,
            int bindingModelCount,
            Action<string, string, string, string> reportFluentAccessorsOnlyDiagnostic,
            IReadOnlyList<ModelDependencyReference> referencedModelProviders,
            IReadOnlyList<ModelFluentAccessorProviderReference> referencedAccessorProviders,
            IReadOnlyDictionary<string, Dependency.ModelDependencyV1> referencedDependencies = null)
        {
            if (designFiles.Targets == null || designFiles.Targets.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();
            referencedModels ??= ImmutableDictionary<string, ModelDependencyReference>.Empty;
            referencedModelProviders ??= [.. referencedModels.Values];
            referencedAccessorProviders ??= [];

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = sharedUsedBindings
                ?? (nodeManagerBindings is { Count: > 0 } ? [] : null);
            bool deferBindingDiagnostics = sharedUsedBindings != null;

            int totalDesigns = bindingModelCount > 0
                ? bindingModelCount
                : designFiles.Targets.Count;

            foreach (DesignFileCollection model in designFiles.Group(identifierFiles))
            {
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    model,
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes,
                    referencedDependencies);

                // Override resolution: if a referenced assembly already
                // provides this model under the same C# prefix, silently
                // skip local generation to avoid duplicate type emission.
                Namespace target = modelDesign.TargetNamespace;
                bool targetProvidedByReference = target != null &&
                    !string.IsNullOrEmpty(target.Value) &&
                    referencedModels.TryGetValue(target.Value,
                        out ModelDependencyReference referenced) &&
                    string.Equals(referenced.Prefix, target.Prefix,
                        StringComparison.Ordinal);
                if (targetProvidedByReference && !options.FluentAccessorsOnly)
                {
                    continue;
                }

                // Cross-namespace prefix override: when a referenced
                // assembly publishes a model under a specific C# prefix,
                // rewrite any matching dependency namespace in the loaded
                // ModelDesign to use that prefix. Without this, NodeSet2
                // inputs (which auto-generate prefixes via
                // NodeSetToModelDesign) emit references like
                // `global::Opc.Ua.DI.X` instead of `global::Opc.Ua.Di.X`.
                OverrideDependencyPrefixes(modelDesign, referencedModels);
                EnsureUniqueTargetNamespaceName(modelDesign);

                DesignFileOptions effectiveOptions = ApplyNodeManagerBinding(
                    model,
                    modelDesign,
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns);
                string modelPath = model.Targets.Count > 0
                    ? model.Targets[0]
                    : string.Empty;

                if (options.FluentAccessorsOnly &&
                    (!ValidateFluentAccessorsOnlyTarget(
                        target?.Value,
                        target?.Prefix,
                        modelPath,
                        referencedModelProviders,
                        referencedAccessorProviders,
                        reportFluentAccessorsOnlyDiagnostic) ||
                    !ValidateFluentAccessorsOnlyOptions(
                        target?.Value,
                        target?.Prefix,
                        modelPath,
                        options,
                        effectiveOptions,
                        reportFluentAccessorsOnlyDiagnostic)))
                {
                    continue;
                }

                var context = new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                };
                Generate(
                context,
                validateSchemas: false,
                designOptions: effectiveOptions);
                // In fluent-accessors-only mode the model itself is already
                // supplied by a referenced assembly, which carries its event
                // records too. Emitting them again here would duplicate every
                // record type the reference already exports (CS0436).
                if (!options.OmitEventRecords && !options.FluentAccessorsOnly)
                {
                    new EventRecordGenerator(context).Emit();
                }
            }

            if (!deferBindingDiagnostics)
            {
                ReportUnmatchedNodeManagerBindings(
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns,
                    reportBindingDiagnostic);
            }
        }

        /// <summary>
        /// Override the C# <see cref="Namespace.Prefix"/> of dependency
        /// namespaces in <paramref name="modelDesign"/> with the prefix
        /// published by the referenced assembly's
        /// <c>[assembly: ModelDependencyAttribute]</c>. This guarantees
        /// that cross-namespace type references emitted by the generator
        /// resolve to the actual C# namespace the referenced assembly
        /// uses (e.g. <c>Opc.Ua.Di</c>), not the auto-generated default
        /// (e.g. <c>Opc.Ua.DI</c>) produced by
        /// <see cref="NodeSetToModelDesign"/> when no ModelDesign XML is
        /// available for the dependency.
        /// </summary>
        /// <remarks>
        /// The target namespace (the one currently being generated) is
        /// intentionally left untouched; otherwise the generator would
        /// emit references into someone else's assembly.
        /// </remarks>
        internal static void OverrideDependencyPrefixes(
            IModelDesign modelDesign,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels)
        {
            if (modelDesign?.Namespaces == null ||
                referencedModels == null ||
                referencedModels.Count == 0)
            {
                return;
            }

            string targetUri = modelDesign.TargetNamespace?.Value;
            foreach (Namespace ns in modelDesign.Namespaces)
            {
                if (ns == null || string.IsNullOrEmpty(ns.Value))
                {
                    continue;
                }
                if (string.Equals(ns.Value, targetUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!referencedModels.TryGetValue(ns.Value, out ModelDependencyReference dep) ||
                    !dep.IsValid)
                {
                    continue;
                }
                if (!string.Equals(ns.Prefix, dep.Prefix, StringComparison.Ordinal))
                {
                    ns.Prefix = dep.Prefix;
                }
                // Also align the namespace Name with the referenced assembly's
                // Namespaces class identifier so that cross-namespace constant
                // references like `global::{Prefix}.Namespaces.{Name}` resolve.
                if (!string.IsNullOrEmpty(dep.Name) &&
                    !string.Equals(ns.Name, dep.Name, StringComparison.Ordinal))
                {
                    ns.Name = dep.Name;
                }
            }
        }

        internal static void EnsureUniqueTargetNamespaceName(IModelDesign modelDesign)
        {
            Namespace target = modelDesign?.TargetNamespace;
            if (target == null ||
                string.IsNullOrEmpty(target.Value) ||
                string.IsNullOrEmpty(target.Name) ||
                modelDesign.Namespaces == null)
            {
                return;
            }

            bool duplicate = modelDesign.Namespaces.Any(ns =>
                ns != null &&
                !string.Equals(ns.Value, target.Value, StringComparison.Ordinal) &&
                string.Equals(ns.Name, target.Name, StringComparison.Ordinal));
            if (!duplicate)
            {
                return;
            }

            string candidate = CreateNamespaceConstantName(target.Prefix);
            if (string.IsNullOrEmpty(candidate) ||
                string.Equals(candidate, target.Name, StringComparison.Ordinal))
            {
                candidate = target.Name + "Namespace";
            }

            HashSet<string> usedNames = [.. modelDesign.Namespaces
                .Where(ns => ns != null &&
                    !string.Equals(ns.Value, target.Value, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(ns.Name))
                .Select(ns => ns.Name)];
            string uniqueName = candidate;
            int suffix = 2;
            while (usedNames.Contains(uniqueName))
            {
                uniqueName = candidate + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                suffix++;
            }
            target.Name = uniqueName;
            foreach (Namespace ns in modelDesign.Namespaces.Where(ns =>
                ns != null &&
                string.Equals(ns.Value, target.Value, StringComparison.Ordinal)))
            {
                ns.Name = uniqueName;
            }
        }

        private static string CreateNamespaceConstantName(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            string[] parts = prefix.Split(['.', '-', '_', '/', ':'], StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(part => part.ToSafeSymbolName().ToUpperCamelCase()));
        }

        /// <summary>
        /// Returns whether a fluent-accessors-only build has a target it can
        /// legitimately extend, reporting why through
        /// <paramref name="reportDiagnostic"/> when it does not.
        /// </summary>
        /// <remarks>
        /// Internal rather than private so the outcomes can be asserted
        /// directly; running the whole generator to observe a single
        /// validation branch would say much less about which branch fired.
        /// </remarks>
        /// <param name="modelUri">The target model URI.</param>
        /// <param name="prefix">The target C# prefix.</param>
        /// <param name="path">The design file path, for diagnostics.</param>
        /// <param name="referencedModelProviders">
        /// Referenced assemblies that produce models.
        /// </param>
        /// <param name="referencedAccessorProviders">
        /// Referenced assemblies that already produce fluent accessors.
        /// </param>
        /// <param name="reportDiagnostic">
        /// Receives model URI, prefix, path and the reason on rejection.
        /// </param>
        /// <returns>True when the target may be extended.</returns>
        internal static bool ValidateFluentAccessorsOnlyTarget(
            string modelUri,
            string prefix,
            string path,
            IReadOnlyList<ModelDependencyReference> referencedModelProviders,
            IReadOnlyList<ModelFluentAccessorProviderReference> referencedAccessorProviders,
            Action<string, string, string, string> reportDiagnostic)
        {
            if (string.IsNullOrEmpty(modelUri))
            {
                reportDiagnostic?.Invoke(
                    modelUri ?? string.Empty,
                    prefix ?? string.Empty,
                    path ?? string.Empty,
                    "no referenced assembly supplies the target model URI.");
                return false;
            }

            ModelFluentAccessorProviderReference accessorProvider =
                referencedAccessorProviders.FirstOrDefault(provider =>
                    provider.IsValid &&
                    string.Equals(provider.ModelUri, modelUri, StringComparison.Ordinal) &&
                    string.Equals(provider.Prefix, prefix, StringComparison.Ordinal));
            if (accessorProvider.IsValid)
            {
                reportDiagnostic?.Invoke(
                    modelUri,
                    prefix ?? string.Empty,
                    path ?? string.Empty,
                    "referenced assembly '" + accessorProvider.AssemblyName +
                    "' already provides generated fluent accessors.");
                return false;
            }

            var producers =
                new List<(ModelDependencyReference Reference,
                    Dependency.ModelDependencyV1 Dependency)>();
            foreach (ModelDependencyReference reference in referencedModelProviders.Where(
                reference =>
                    reference.IsValid &&
                    string.Equals(reference.ModelUri, modelUri, StringComparison.Ordinal)))
            {
                Dependency.ModelDependencyV1 dependency = reference.GetDependency();
                if (string.Equals(reference.Prefix, prefix, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(reference.Payload) &&
                    (dependency == null ||
                        !string.Equals(
                            dependency.ModelUri,
                            modelUri,
                            StringComparison.Ordinal)))
                {
                    reportDiagnostic?.Invoke(
                        modelUri,
                        prefix ?? string.Empty,
                        path ?? string.Empty,
                        "model producer '" + reference.AssemblyName +
                        "' has a malformed model dependency payload.");
                    return false;
                }
                if (dependency != null &&
                    string.Equals(dependency.ModelUri, modelUri, StringComparison.Ordinal))
                {
                    producers.Add((reference, dependency));
                }
            }
            List<(ModelDependencyReference Reference, Dependency.ModelDependencyV1 Dependency)>
                matchingProducers = [.. producers.Where(entry =>
                    string.Equals(entry.Reference.Prefix, prefix, StringComparison.Ordinal))];
            if (matchingProducers.Count == 0)
            {
                string reason = producers.Count > 0
                    ? "model producer '" + producers[0].Reference.AssemblyName +
                        "' supplies prefix '" + producers[0].Reference.Prefix + "'."
                    : "no payload-bearing referenced model producer supplies the target model.";
                reportDiagnostic?.Invoke(
                    modelUri,
                    prefix ?? string.Empty,
                    path ?? string.Empty,
                    reason);
                return false;
            }

            foreach ((ModelDependencyReference reference,
                Dependency.ModelDependencyV1 dependency) in matchingProducers)
            {
                if (!dependency.FluentAccessorsEmitted.HasValue)
                {
                    reportDiagnostic?.Invoke(
                        modelUri,
                        prefix ?? string.Empty,
                        path ?? string.Empty,
                        "model producer '" + reference.AssemblyName +
                        "' has unknown legacy fluent-accessor capability.");
                    return false;
                }
                if (dependency.FluentAccessorsEmitted.Value)
                {
                    reportDiagnostic?.Invoke(
                        modelUri,
                        prefix ?? string.Empty,
                        path ?? string.Empty,
                        "model producer '" + reference.AssemblyName +
                        "' already contains fluent accessors.");
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateFluentAccessorsOnlyOptions(
            string modelUri,
            string prefix,
            string path,
            GeneratorOptions options,
            DesignFileOptions designOptions,
            Action<string, string, string, string> reportDiagnostic)
        {
            string reason = options.OmitFluentApi
                ? "OmitFluentApi is also enabled."
                : designOptions?.GenerateNodeManager == true
                    ? "NodeManager generation is also enabled."
                    : null;
            if (reason == null)
            {
                return true;
            }
            reportDiagnostic?.Invoke(
                modelUri ?? string.Empty,
                prefix ?? string.Empty,
                path ?? string.Empty,
                reason);
            return false;
        }

        /// <summary>
        /// Resolve the effective per-design options by overlaying any
        /// matching <c>[NodeManager]</c> attribute binding on top of the
        /// existing <see cref="DesignFileCollection.Options"/>.
        /// </summary>
        private static DesignFileOptions ApplyNodeManagerBinding(
            DesignFileCollection model,
            IModelDesign modelDesign,
            IReadOnlyList<NodeManagerAttributeBinding> bindings,
            HashSet<NodeManagerAttributeBinding> usedBindings,
            int totalDesigns)
        {
            DesignFileOptions effective = model.Options;
            if (bindings == null || bindings.Count == 0)
            {
                return effective;
            }
            string uri = modelDesign?.TargetNamespace?.Value;
            string designName = model.Targets.Count == 1
                ? System.IO.Path.GetFileNameWithoutExtension(model.Targets[0])
                : null;

            NodeManagerAttributeBinding match = null;
            // 1) exact URI match
            if (!string.IsNullOrEmpty(uri))
            {
                match = bindings.FirstOrDefault(b =>
                    string.Equals(b.NamespaceUri, uri, StringComparison.Ordinal));
            }
            // 2) design file name match
            if (match == null && !string.IsNullOrEmpty(designName))
            {
                match = bindings.FirstOrDefault(b =>
                    !string.IsNullOrEmpty(b.Design) &&
                    string.Equals(b.Design, designName, StringComparison.OrdinalIgnoreCase));
            }
            // 3) single-design / single-binding fallback
            if (match == null &&
                totalDesigns == 1 &&
                bindings.Count == 1 &&
                string.IsNullOrEmpty(bindings[0].NamespaceUri) &&
                string.IsNullOrEmpty(bindings[0].Design))
            {
                match = bindings[0];
            }

            if (match == null)
            {
                return effective;
            }

            usedBindings?.Add(match);

            return (effective ?? new DesignFileOptions()) with
            {
                GenerateNodeManager = true,
                NodeManagerNamespace = match.TargetNamespace,
                NodeManagerClassName = match.TargetClassName,
                EmitNodeManagerFactory = match.GenerateFactory
            };
        }

        /// <summary>
        /// Report a diagnostic for every <c>[NodeManager]</c> binding that
        /// no generation pass matched. Binding resolution runs in two
        /// independent passes (NodeSet2 and ModelDesign); a binding matched
        /// by either pass is recorded in the shared
        /// <paramref name="usedBindings"/> set, so the report must be made
        /// once — after both passes — against that aggregated set. Reporting
        /// per pass would false-positive a binding matched by the other pass
        /// (issue #3937). A selector-less binding in a multi-model project is
        /// reported as an ambiguity (the user must add a <c>NamespaceUri</c>);
        /// everything else is reported as an unmatched selector.
        /// </summary>
        /// <param name="bindings">
        /// The full set of discovered <c>[NodeManager]</c> bindings.
        /// </param>
        /// <param name="usedBindings">
        /// The bindings that were matched to a model across all passes.
        /// </param>
        /// <param name="totalModelCount">
        /// Total number of generatable models across all passes.
        /// </param>
        /// <param name="reportBindingDiagnostic">
        /// Callback invoked for each unmatched or ambiguous binding.
        /// </param>
        public static void ReportUnmatchedNodeManagerBindings(
            IReadOnlyList<NodeManagerAttributeBinding> bindings,
            ICollection<NodeManagerAttributeBinding> usedBindings,
            int totalModelCount,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic)
        {
            if (bindings == null || bindings.Count == 0 || reportBindingDiagnostic == null)
            {
                return;
            }
            foreach (NodeManagerAttributeBinding binding in bindings)
            {
                if (usedBindings != null && usedBindings.Contains(binding))
                {
                    continue;
                }
                bool hasSelector = !string.IsNullOrEmpty(binding.NamespaceUri) ||
                    !string.IsNullOrEmpty(binding.Design);
                if (!hasSelector && totalModelCount > 1)
                {
                    reportBindingDiagnostic(
                        binding,
                        "[NodeManager] on '" +
                        binding.TargetNamespace +
                        "." +
                        binding.TargetClassName +
                        "' has no NamespaceUri/Design selector but the " +
                        "project contains multiple models. Specify " +
                        "NamespaceUri to disambiguate.");
                    continue;
                }
                string selector = !string.IsNullOrEmpty(binding.NamespaceUri)
                    ? "NamespaceUri='" + binding.NamespaceUri + "'"
                    : !string.IsNullOrEmpty(binding.Design)
                        ? "Design='" + binding.Design + "'"
                        : "(no selector)";
                reportBindingDiagnostic(
                    binding,
                    "[NodeManager] on '" +
                    binding.TargetNamespace +
                    "." +
                    binding.TargetClassName +
                    "' did not match any model (" +
                    selector +
                    ").");
            }
        }

        /// <summary>
        /// Generate from nodesets
        /// </summary>
        /// <param name="nodesets">Nodesets to process</param>
        /// <param name="fileSystem">File system abstraction to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">Telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        /// <param name="useAllowSubtypes">allow subtypes</param>
        /// <param name="referencedModels">Models supplied by referenced assemblies.</param>
        /// <param name="nodeManagerBindings">Optional node manager bindings.</param>
        /// <param name="reportBindingDiagnostic">Optional binding diagnostic callback.</param>
        /// <param name="referencedDependencies">Per-URI model dependency payloads.</param>
        /// <param name="sharedUsedBindings">Optional set that accumulates matched bindings.</param>
        /// <param name="bindingModelCount">Total number of generatable models across all passes.</param>
        public static void GenerateCode(
            this NodesetFileCollection nodesets,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null,
            bool useAllowSubtypes = false,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null,
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings = null,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic = null,
            IReadOnlyDictionary<string, Dependency.ModelDependencyV1> referencedDependencies = null,
            HashSet<NodeManagerAttributeBinding> sharedUsedBindings = null,
            int bindingModelCount = 0)
        {
            GenerateCode(
                nodesets,
                fileSystem,
                outputDir,
                telemetry,
                options,
                useAllowSubtypes,
                referencedModels,
                nodeManagerBindings,
                reportBindingDiagnostic,
                referencedDependencies,
                sharedUsedBindings,
                bindingModelCount,
                null,
                null,
                null);
        }

        /// <summary>
        /// Generate from nodesets with fluent-accessor provider metadata.
        /// </summary>
        /// <param name="nodesets">Nodesets to process</param>
        /// <param name="fileSystem">File system abstraction to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">Telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        /// <param name="useAllowSubtypes">allow subtypes</param>
        /// <param name="referencedModels">Models supplied by referenced
        /// assemblies (keyed by model URI). When a target's model URI
        /// is in this map under the same C# prefix the nodeset is normally
        /// skipped because the referenced assembly already supplies the
        /// types. When <see cref="GeneratorOptions.FluentAccessorsOnly"/> is
        /// enabled the target is instead loaded and used to emit typed
        /// fluent accessors only. Transitive nodeset dependencies found in
        /// the map are also satisfied without erroring.</param>
        /// <param name="nodeManagerBindings">
        /// Optional <c>[NodeManager]</c> attribute bindings discovered in
        /// the consuming compilation. When supplied, each binding is
        /// matched to a nodeset model by
        /// <see cref="NodeManagerAttributeBinding.NamespaceUri"/> (preferred)
        /// or, if no URI is given, by single-design fallback. Matched
        /// bindings force <c>GenerateNodeManager = true</c> for the model
        /// and override the manager class name and namespace.
        /// </param>
        /// <param name="reportBindingDiagnostic">
        /// Optional callback invoked for each binding-related warning or
        /// error (e.g. unmatched URI, ambiguous fallback).
        /// </param>
        /// <param name="referencedDependencies">
        /// Per-URI model dependency payloads recovered from referenced
        /// assemblies via <c>ReferencedModelDependencyScanner</c>. When
        /// present, the validator pre-imports these dependency payloads
        /// so downstream models can resolve upstream types without an
        /// explicit <c>AdditionalFiles</c> entry for them.
        /// </param>
        /// <param name="sharedUsedBindings">
        /// Optional caller-owned set that accumulates the bindings matched
        /// across multiple generation passes (NodeSet2 and ModelDesign).
        /// When supplied, this pass records its matches in the set and does
        /// <b>not</b> report unmatched bindings itself; the caller reports
        /// them once, after all passes, via
        /// <see cref="ReportUnmatchedNodeManagerBindings"/>. When <c>null</c>
        /// the pass keeps a private set and reports directly (single pass).
        /// </param>
        /// <param name="bindingModelCount">
        /// Total number of generatable models across all passes, used for
        /// single-model binding fallback and ambiguity detection. When
        /// <c>0</c> the per-pass model count is used (single-pass callers).
        /// </param>
        /// <param name="reportFluentAccessorsOnlyDiagnostic">
        /// Optional callback invoked when a model cannot participate in
        /// fluent-accessors-only generation. Arguments are model URI,
        /// requested prefix, input path, and failure reason.
        /// </param>
        /// <param name="referencedModelProviders">
        /// All model dependency declarations from referenced assemblies,
        /// including payload-bearing producers and payloadless re-exports.
        /// </param>
        /// <param name="referencedAccessorProviders">
        /// All referenced assemblies that declare generated fluent accessors.
        /// </param>
        public static void GenerateCode(
            this NodesetFileCollection nodesets,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options,
            bool useAllowSubtypes,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels,
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic,
            IReadOnlyDictionary<string, Dependency.ModelDependencyV1> referencedDependencies,
            HashSet<NodeManagerAttributeBinding> sharedUsedBindings,
            int bindingModelCount,
            Action<string, string, string, string> reportFluentAccessorsOnlyDiagnostic,
            IReadOnlyList<ModelDependencyReference> referencedModelProviders,
            IReadOnlyList<ModelFluentAccessorProviderReference> referencedAccessorProviders)
        {
            if (nodesets.Files.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();
            referencedModels ??= ImmutableDictionary<string, ModelDependencyReference>.Empty;
            referencedModelProviders ??= [.. referencedModels.Values];
            referencedAccessorProviders ??= [];

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = sharedUsedBindings
                ?? (nodeManagerBindings is { Count: > 0 } ? [] : null);
            bool deferBindingDiagnostics = sharedUsedBindings != null;

            int totalDesigns = bindingModelCount > 0
                ? bindingModelCount
                : nodesets.ModelUris.Count();

            foreach (string modelUri in nodesets.ModelUris)
            {
                List<string> designFilesForModel =
                    nodesets.GetDesignFileListForModel(
                        modelUri,
                        out NodesetFile nodeset,
                        referencedModels);
                if (designFilesForModel == null || nodeset.Info.Ignore)
                {
                    continue;
                }
                // Override resolution: if a referenced assembly already
                // provides this model under the same C# prefix, silently
                // skip local generation to avoid duplicate type emission.
                bool targetProvidedByReference = referencedModels.TryGetValue(modelUri,
                        out ModelDependencyReference referenced) &&
                    string.Equals(referenced.Prefix, nodeset.Info.Prefix,
                        StringComparison.Ordinal);
                if (targetProvidedByReference && !options.FluentAccessorsOnly)
                {
                    continue;
                }
                // The rest of the input is processed as design files
                var model = new DesignFileCollection
                {
                    Targets = designFilesForModel
                };
                IReadOnlyDictionary<string, Dependency.ModelDependencyV1>
                    validationDependencies = referencedDependencies;
                if (options.FluentAccessorsOnly &&
                    referencedDependencies != null &&
                    referencedDependencies.ContainsKey(modelUri))
                {
                    Dictionary<string, Dependency.ModelDependencyV1> dependenciesWithoutTarget =
                        referencedDependencies.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value,
                            StringComparer.Ordinal);
                    dependenciesWithoutTarget.Remove(modelUri);
                    validationDependencies = dependenciesWithoutTarget;
                }
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    model,
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes,
                    validationDependencies);

                // Cross-namespace prefix override: when a referenced
                // assembly publishes a model under a specific C# prefix,
                // rewrite any matching dependency namespace so that
                // generated type references resolve against the referenced
                // assembly's actual prefix (not the auto-generated one).
                OverrideDependencyPrefixes(modelDesign, referencedModels);
                EnsureUniqueTargetNamespaceName(modelDesign);

                DesignFileOptions effectiveOptions = ApplyNodeManagerBinding(
                    model,
                    modelDesign,
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns);

                if (options.FluentAccessorsOnly &&
                    (!ValidateFluentAccessorsOnlyTarget(
                        modelUri,
                        nodeset.Info.Prefix,
                        nodeset.FileName,
                        referencedModelProviders,
                        referencedAccessorProviders,
                        reportFluentAccessorsOnlyDiagnostic) ||
                    !ValidateFluentAccessorsOnlyOptions(
                        modelUri,
                        nodeset.Info.Prefix,
                        nodeset.FileName,
                        options,
                        effectiveOptions,
                        reportFluentAccessorsOnlyDiagnostic)))
                {
                    continue;
                }

                var context = new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                };
                Generate(
                context,
                validateSchemas: false,
                designOptions: effectiveOptions);
                // In fluent-accessors-only mode the model itself is already
                // supplied by a referenced assembly, which carries its event
                // records too. Emitting them again here would duplicate every
                // record type the reference already exports (CS0436).
                if (!options.OmitEventRecords && !options.FluentAccessorsOnly)
                {
                    new EventRecordGenerator(context).Emit();
                }
            }

            if (!deferBindingDiagnostics)
            {
                ReportUnmatchedNodeManagerBindings(
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns,
                    reportBindingDiagnostic);
            }
        }

        /// <summary>
        /// Generate the .net stack code
        /// </summary>
        /// <param name="generatorType">Generator type</param>
        /// <param name="fileSystem">The root file system to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">A telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        public static void GenerateStack(
            StackGenerationType generatorType,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null)
        {
            options ??= new GeneratorOptions();
            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            IModelDesign modelDesign = fileSystem.OpenModelDesign(
                new DesignFileCollection
                {
                    Targets =
                    [
                        BuiltInDesignFiles.StandardTypesXml,
                        BuiltInDesignFiles.UACoreServicesXml
                    ],
                    IdentifierFilePath = BuiltInDesignFiles.StandardTypesCsv,
                    Options = new DesignFileOptions
                    {
                        StartId = 0,
                        ModelVersion = "1.05.06",
                        ModelPublicationDate = "2025-11-08",
                        ReleaseCandidate = true
                    }
                },
                options.Exclusions,
                telemetry,
                false);

            var generatorContext = new GeneratorContext
            {
                FileSystem = fileSystem,
                OutputFolder = outputDir,
                ModelDesign = modelDesign,
                Telemetry = telemetry,
                Options = options
            };
            if ((generatorType & StackGenerationType.Stack) != 0)
            {
                var clientApiGenerator = new ClientApiGenerator(generatorContext);
                clientApiGenerator.Emit();
                var serverApiGenerator = new ServerApiGenerator(generatorContext);
                serverApiGenerator.Emit();
                var endpointsGenerator = new EndpointsGenerator(generatorContext);
                endpointsGenerator.Emit();
                // Emit ObjectType client proxies for every standard UA
                // ObjectType so downstream model proxies (e.g. GDS) can
                // derive from them. Proxies are emitted into the model's
                // own namespace (Opc.Ua for the standard NodeSet) — no
                // namespace override. Suppressed when the consumer opts
                // out via OmitObjectTypeProxies.
                if (!options.OmitObjectTypeProxies)
                {
                    var stackProxyContext = new GeneratorContext
                    {
                        FileSystem = generatorContext.FileSystem,
                        OutputFolder = generatorContext.OutputFolder,
                        ModelDesign = generatorContext.ModelDesign,
                        Telemetry = generatorContext.Telemetry,
                        Options = new GeneratorOptions
                        {
                            OptimizeForCompileSpeed = options.OptimizeForCompileSpeed,
                            Exclusions = options.Exclusions,
                            Cancellation = options.Cancellation,
                            UseUtf8StringLiterals = options.UseUtf8StringLiterals
                        }
                    };
                    var stackProxyGenerator = new ObjectTypeProxyGenerator(stackProxyContext);
                    stackProxyGenerator.Emit();
                }

                // Event records depend on EventRecord and decoder runtime
                // types in Opc.Ua.Core. Emit them only for the Stack target;
                // the Models target builds Opc.Ua.Core.Types and cannot
                // reference Opc.Ua.Core without creating a cycle.
                var stackRecordContext = new GeneratorContext
                {
                    FileSystem = generatorContext.FileSystem,
                    OutputFolder = generatorContext.OutputFolder,
                    ModelDesign = generatorContext.ModelDesign,
                    Telemetry = generatorContext.Telemetry,
                    Options = new GeneratorOptions
                    {
                        OptimizeForCompileSpeed = options.OptimizeForCompileSpeed,
                        Exclusions = options.Exclusions,
                        Cancellation = options.Cancellation,
                        UseUtf8StringLiterals = options.UseUtf8StringLiterals
                    }
                };
                if (!options.OmitEventRecords)
                {
                    new EventRecordGenerator(stackRecordContext).Emit();
                }
            }

            if ((generatorType & StackGenerationType.Models) != 0)
            {
                var attributesGenerator = new AttributesGenerator(generatorContext);
                attributesGenerator.Emit();
                var statusCodesGenerator = new StatusCodesGenerator(generatorContext);
                statusCodesGenerator.Emit();
                var serverCapabilitiesGenerator = new ServerCapabilitiesGenerator(generatorContext);
                serverCapabilitiesGenerator.Emit();

                Generate(generatorContext, !options.OptimizeForCompileSpeed);
            }
        }

        /// <summary>
        /// Generates all files
        /// </summary>
        private static void Generate(
            GeneratorContext context,
            bool validateSchemas = false,
            DesignFileOptions designOptions = null)
        {
            if (context.Options?.FluentAccessorsOnly == true)
            {
                new FluentBuilderGenerator(context)
                {
                    GenerateManagerWrappers = false,
                    EmitFluentAccessors = true
                }.Emit();
                return;
            }

            // Generate schemas
            var xmlSchemaGenerator = new XmlSchemaGenerator(context)
            {
                ValidateOutput = validateSchemas
            };
            IEnumerable<Resource> xmlSchemaResource = xmlSchemaGenerator.Emit();
            var binarySchemaGenerator = new BinarySchemaGenerator(context)
            {
                ValidateOutput = validateSchemas
            };
            IEnumerable<Resource> binarySchemaResource = binarySchemaGenerator.Emit();
            var schemaResources = new ResourceGenerator(context);
            schemaResources.Embed(
                context.ModelDesign.TargetNamespace.Prefix,
                "XmlSchemas",
                false,
                [.. binarySchemaResource, .. xmlSchemaResource]);

            // Must run after schema generation to initilize the dictionaries.
            var constantsGenerator = new ConstantsGenerator(context);
            constantsGenerator.Emit();
            var nodeIdGenerator = new NodeIdGenerator(context);
            nodeIdGenerator.Emit();
            var nodeStateCodeGenerator = new NodeStateGenerator(context);
            nodeStateCodeGenerator.Emit();
            var dataTypesGenerator = new DataTypeGenerator(context);
            dataTypesGenerator.Emit();

            if (designOptions?.GenerateNodeManager == true)
            {
                new NodeManagerGenerator(context)
                {
                    OverrideNamespace = designOptions.NodeManagerNamespace,
                    OverrideClassName = designOptions.NodeManagerClassName,
                    EmitFactory = designOptions.EmitNodeManagerFactory
                }.Emit();
            }

            // FluentBuilderGenerator emits per-ObjectType typed-accessor
            // extension classes by default. Model-only assemblies that
            // don't reference Opc.Ua.Server can opt out via
            // GeneratorOptions.OmitFluentApi (or the MSBuild property
            // ModelSourceGeneratorOmitFluentApi=true). When
            // GenerateNodeManager=true we ALWAYS emit (any consumer
            // that wires a node manager already references
            // Opc.Ua.Server, so suppression is unnecessary).
            bool emitTypedAccessors = designOptions?.GenerateNodeManager == true ||
                context.Options?.OmitFluentApi != true;
            if (emitTypedAccessors)
            {
                new FluentBuilderGenerator(context)
                {
                    OverrideManagerNamespace = designOptions?.NodeManagerNamespace,
                    OverrideManagerClassName = designOptions?.NodeManagerClassName,
                    GenerateManagerWrappers = designOptions?.GenerateNodeManager == true,
                    EmitFluentAccessors = emitTypedAccessors
                }.Emit();
            }

            if (context.Options?.OmitObjectTypeProxies != true)
            {
                var objectTypeProxyGenerator = new ObjectTypeProxyGenerator(context);
                objectTypeProxyGenerator.Emit();
            }
            if (context.Options?.OmitStateMachineIds != true)
            {
                var stateMachineIdsGenerator = new StateMachineIdsGenerator(context);
                stateMachineIdsGenerator.Emit();
            }
            if (context.Options?.EmitDependencyMetadata != false)
            {
                var modelDependencyGenerator = new ModelDependencyGenerator(
                    context,
                    emitTypedAccessors);
                modelDependencyGenerator.Emit();
            }
        }
    }
}
