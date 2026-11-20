/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Interface for generator context to enable testing and dependency injection.
    /// </summary>
    public interface IGeneratorContext
    {
        /// <summary>
        /// File system to use for file operations.
        /// </summary>
        IFileSystem FileSystem { get; }

        /// <summary>
        /// Output folder for generated files.
        /// </summary>
        string OutputFolder { get; }

        /// <summary>
        /// Model design validator.
        /// </summary>
        IModelDesign ModelDesign { get; }

        /// <summary>
        /// Telemetry context for logging.
        /// </summary>
        ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Generator options.
        /// </summary>
        GeneratorOptions Options { get; }

        /// <summary>
        /// Models that referenced assemblies have already generated, keyed
        /// by model URI. Used to skip locally generating models a referenced
        /// assembly already provides and to satisfy transitive nodeset
        /// dependencies without requiring the consumer to re-add every
        /// upstream <c>&lt;AdditionalFiles&gt;</c> entry.
        /// </summary>
        IReadOnlyDictionary<string, ModelDependencyReference> ReferencedModels { get; }
    }
}
