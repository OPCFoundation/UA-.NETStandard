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

using System.Collections.Generic;
using System.Collections.Immutable;
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
        public static void GenerateCode(
            this DesignFileCollection designFiles,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null,
            bool useAllowSubtypes = false,
            List<string> identifierFiles = null,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null)
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
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                Generate(new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                }, validateSchemas: false);
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
        /// <param name="referencedModels">Models supplied by referenced
        /// assemblies (keyed by model URI). When a target's model URI
        /// is in this map the nodeset is skipped (referenced assembly
        /// already supplies the types). Transitive nodeset dependencies
        /// found in the map are also satisfied without erroring.</param>
        public static void GenerateCode(
            this NodesetFileCollection nodesets,
            IFileSystem fileSystem,
            string outputDir,
            ITelemetryContext telemetry,
            GeneratorOptions options = null,
            bool useAllowSubtypes = false,
            IReadOnlyDictionary<string, ModelDependencyReference> referencedModels = null)
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
                        System.StringComparison.Ordinal))
                {
                    continue;
                }
                // The rest of the input is processed as design files
                IModelDesign modelDesign = fileSystem.OpenModelDesign(
                    new DesignFileCollection
                    {
                        Targets = designFilesForModel
                    },
                    options.Exclusions,
                    telemetry,
                    useAllowSubtypes);

                Generate(new GeneratorContext
                {
                    FileSystem = fileSystem,
                    OutputFolder = outputDir,
                    ModelDesign = modelDesign,
                    Telemetry = telemetry,
                    Options = options,
                    ReferencedModels = referencedModels
                }, validateSchemas: false);
                // TODO {
                // TODO     AvailableNodeSets = nodesets.Files
                // TODO };
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
            }

            if ((generatorType & StackGenerationType.Models) != 0)
            {
                var attributesGenerator = new AttributesGenerator(generatorContext);
                attributesGenerator.Emit();
                var statusCodesGenerator = new StatusCodesGenerator(generatorContext);
                statusCodesGenerator.Emit();

                Generate(generatorContext, !options.OptimizeForCompileSpeed);
            }
        }

        /// <summary>
        /// Generates all files
        /// </summary>
        private static void Generate(
            GeneratorContext context,
            bool validateSchemas = false)
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
            var modelDependencyGenerator = new ModelDependencyGenerator(context);
            modelDependencyGenerator.Emit();
        }
    }
}
