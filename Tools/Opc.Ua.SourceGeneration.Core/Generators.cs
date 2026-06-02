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
        /// <param name="referencedModels">Models supplied by referenced
        /// assemblies (keyed by model URI). Used to seed the assembly
        /// dependency closure and may be empty.</param>
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
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic = null)
        {
            if (designFiles.Targets == null || designFiles.Targets.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();
            referencedModels ??= ImmutableDictionary<string, ModelDependencyReference>.Empty;

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = nodeManagerBindings is { Count: > 0 }
                ? []
                : null;

            int totalDesigns = designFiles.Targets.Count;

            foreach (DesignFileCollection model in designFiles.Group(identifierFiles))
            {
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    model,
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes);

                // Override resolution: if a referenced assembly already
                // provides this model under the same C# prefix, silently
                // skip local generation to avoid duplicate type emission.
                Namespace target = modelDesign.TargetNamespace;
                if (target != null &&
                    !string.IsNullOrEmpty(target.Value) &&
                    referencedModels.TryGetValue(target.Value,
                        out ModelDependencyReference referenced) &&
                    string.Equals(referenced.Prefix, target.Prefix,
                        StringComparison.Ordinal))
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

                DesignFileOptions effectiveOptions = ApplyNodeManagerBinding(
                    model,
                    modelDesign,
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns,
                    reportBindingDiagnostic);

                Generate(new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                },
                validateSchemas: false,
                designOptions: effectiveOptions);
            }

            if (usedBindings != null && nodeManagerBindings != null && reportBindingDiagnostic != null)
            {
                foreach (NodeManagerAttributeBinding binding in nodeManagerBindings)
                {
                    if (!usedBindings.Contains(binding))
                    {
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
                            "' did not match any model design (" +
                            selector +
                            ").");
                    }
                }
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
            int totalDesigns,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic)
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

            // Detect ambiguity: multiple designs but binding has no selector.
            if (totalDesigns > 1 &&
                string.IsNullOrEmpty(match.NamespaceUri) &&
                string.IsNullOrEmpty(match.Design) &&
                reportBindingDiagnostic != null)
            {
                reportBindingDiagnostic(
                    match,
                    "[NodeManager] on '" +
                    match.TargetNamespace +
                    "." +
                    match.TargetClassName +
                    "' has no NamespaceUri/Design selector but the project " +
                    "contains multiple designs. Specify NamespaceUri to " +
                    "disambiguate.");
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
        /// Generate from nodesets
        /// </summary>
        /// <param name="nodesets">Nodesets to process</param>
        /// <param name="fileSystem">File system abstraction to use</param>
        /// <param name="outputDir">Output folder or null</param>
        /// <param name="telemetry">Telemetry context for logging</param>
        /// <param name="options">Generator options</param>
        /// <param name="useAllowSubtypes">allow subtypes</param>
        /// <param name="referencedModels">Models supplied by referenced
        /// assemblies (keyed by model URI). When a target's model URI
        /// is in this map the nodeset is skipped (referenced assembly
        /// already supplies the types). Transitive nodeset dependencies
        /// found in the map are also satisfied without erroring.</param>
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
            IReadOnlyDictionary<string, Opc.Ua.SourceGeneration.Dependency.ModelDependencyV1> referencedDependencies = null)
        {
            if (nodesets.Files.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();
            referencedModels ??= ImmutableDictionary<string, ModelDependencyReference>.Empty;

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = nodeManagerBindings is { Count: > 0 }
                ? []
                : null;

            int totalDesigns = nodesets.ModelUris.Count();

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
                if (referencedModels.TryGetValue(modelUri,
                        out ModelDependencyReference referenced) &&
                    string.Equals(referenced.Prefix, nodeset.Info.Prefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                // The rest of the input is processed as design files
                var model = new DesignFileCollection
                {
                    Targets = designFilesForModel
                };
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    model,
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes,
                    referencedDependencies);

                // Cross-namespace prefix override: when a referenced
                // assembly publishes a model under a specific C# prefix,
                // rewrite any matching dependency namespace so that
                // generated type references resolve against the referenced
                // assembly's actual prefix (not the auto-generated one).
                OverrideDependencyPrefixes(modelDesign, referencedModels);

                DesignFileOptions effectiveOptions = ApplyNodeManagerBinding(
                    model,
                    modelDesign,
                    nodeManagerBindings,
                    usedBindings,
                    totalDesigns,
                    reportBindingDiagnostic);

                Generate(new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                },
                validateSchemas: false,
                designOptions: effectiveOptions);
            }

            if (usedBindings != null && nodeManagerBindings != null && reportBindingDiagnostic != null)
            {
                foreach (NodeManagerAttributeBinding binding in nodeManagerBindings)
                {
                    if (!usedBindings.Contains(binding))
                    {
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
                            "' did not match any model design (" +
                            selector +
                            ").");
                    }
                }
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

                // Emit event-record records for every standard UA
                // event type. Records reference EventRecord (in
                // Opc.Ua.Core) so they are only emitted in the Stack
                // path (which runs against Opc.Ua.Core), not in the
                // Models path (which runs against Opc.Ua.Core.Types).
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
                var stackRecordGenerator = new EventRecordGenerator(stackRecordContext);
                stackRecordGenerator.Emit();
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
            // extension classes whenever the consumer opted in via
            // GeneratorOptions.EmitFluentAccessors OR when
            // GenerateNodeManager=true (any server-side consumer that
            // wires a node manager always references Opc.Ua.Server, so
            // emitting the typed accessors there is safe and provides
            // the typed builder pipeline alongside the manager +
            // instance wrappers).
            bool emitTypedAccessors = context.Options?.EmitFluentAccessors == true
                || designOptions?.GenerateNodeManager == true;
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
                var modelDependencyGenerator = new ModelDependencyGenerator(context);
                modelDependencyGenerator.Emit();
            }
        }
    }
}
