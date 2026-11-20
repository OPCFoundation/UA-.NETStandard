/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Collections.Immutable;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Context for all generators
    /// </summary>
    internal sealed record class GeneratorContext : IGeneratorContext
    {
        /// <summary>
        /// File system to use
        /// </summary>
        public required IFileSystem FileSystem { get; init; }

        /// <summary>
        /// Output folder for generated files
        /// </summary>
        public required string OutputFolder { get; init; }

        /// <summary>
        /// Model design validated
        /// </summary>
        public required IModelDesign ModelDesign { get; init; }

        /// <summary>
        /// Telemetry context for logging
        /// </summary>
        public required ITelemetryContext Telemetry { get; init; }

        /// <summary>
        /// Generator options
        /// </summary>
        public required GeneratorOptions Options { get; init; }

        /// <summary>
        /// Models supplied by referenced assemblies.
        /// </summary>
        public IReadOnlyDictionary<string, ModelDependencyReference> ReferencedModels { get; init; }
            = ImmutableDictionary<string, ModelDependencyReference>.Empty;
    }
}
