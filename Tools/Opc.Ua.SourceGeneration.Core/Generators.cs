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
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings = null,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic = null)
        {
            if (designFiles.Targets == null || designFiles.Targets.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = nodeManagerBindings is { Count: > 0 }
                ? new HashSet<NodeManagerAttributeBinding>()
                : null;

            int totalDesigns = designFiles.Targets.Count;

            foreach (DesignFileCollection model in designFiles.Group(identifierFiles))
            {
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    model,
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes);

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
                    Options = options
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
                            "[NodeManager] on '" + binding.TargetNamespace + "." +
                            binding.TargetClassName +
                            "' did not match any model design (" + selector + ").");
                    }
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
            if (match == null && totalDesigns == 1 && bindings.Count == 1 &&
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
                    "[NodeManager] on '" + match.TargetNamespace + "." +
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
        public static void GenerateCode(
            this NodesetFileCollection nodesets,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null,
            bool useAllowSubtypes = false,
            IReadOnlyList<NodeManagerAttributeBinding> nodeManagerBindings = null,
            Action<NodeManagerAttributeBinding, string> reportBindingDiagnostic = null)
        {
            if (nodesets.Files.Count == 0)
            {
                return;
            }
            options ??= new GeneratorOptions();

            // Combine with embedded resources in this assembly.
            fileSystem = typeof(Generators).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(fileSystem);

            HashSet<NodeManagerAttributeBinding> usedBindings = nodeManagerBindings is { Count: > 0 }
                ? new HashSet<NodeManagerAttributeBinding>()
                : null;

            int totalDesigns = nodesets.ModelUris.Count();

            foreach (string modelUri in nodesets.ModelUris)
            {
                List<string> designFilesForModel =
                    nodesets.GetDesignFileListForModel(
                        modelUri,
                        out NodesetFile nodeset);
                if (designFilesForModel == null || nodeset.Info.Ignore)
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
                    useAllowSubtypes);

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
                    Options = options
                },
                validateSchemas: false,
                designOptions: effectiveOptions);
                // TODO {
                // TODO     AvailableNodeSets = nodesets.Files
                // TODO };
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
                            "[NodeManager] on '" + binding.TargetNamespace + "." +
                            binding.TargetClassName +
                            "' did not match any model design (" + selector + ").");
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

            if (context.Options?.OmitObjectTypeProxies != true)
            {
                var objectTypeProxyGenerator = new ObjectTypeProxyGenerator(context);
                objectTypeProxyGenerator.Emit();
            }
        }
    }
}
