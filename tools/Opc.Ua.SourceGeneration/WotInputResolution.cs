/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// WoT-specific input-resolution helpers. Kept in a dedicated partial so
    /// the shared <c>Extensions</c> members (compiled into both the model and
    /// the stack source generators) stay free of the <see cref="Opc.Ua.Wot"/>
    /// / <see cref="Opc.Ua.Export"/> converter dependency: only the model
    /// generator (which globs this file) needs WoT NodeSet conversion, so the
    /// stack generator does not compile it.
    /// </summary>
    internal static partial class Extensions
    {
        /// <summary>
        /// Resolves the per-file WoT conversion outcomes against the explicit
        /// NodeSet2/ModelDesign inputs and each other: every diagnostic
        /// produced while converting a WoT input is forwarded, and any WoT
        /// input whose synthesized in-memory NodeSet2 virtual path collides
        /// with an explicitly supplied input or with another WoT input's
        /// virtual path is dropped and reported via
        /// <see cref="SourceGenerator.WotVirtualPathCollision"/> so a
        /// collision can never silently overwrite another model or crash the
        /// underlying virtual file system. Resolution is a pure function of
        /// its inputs and only compares paths, so it stays cheap and
        /// deterministic across incremental re-runs.
        /// </summary>
        public static (
            ImmutableArray<(AdditionalText Text, NodesetFileOptions Options)> Accepted,
            ImmutableArray<Diagnostic> Diagnostics) ResolveWotInputs(
            this ImmutableArray<(AdditionalText Text, NodesetFileOptions Options)> xmlInputFiles,
            ImmutableArray<WotConversionOutcome> wotOutcomes)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            var accepted = ImmutableArray.CreateBuilder<(AdditionalText, NodesetFileOptions)>();
            // Explicit inputs are never displaced by a WoT-synthesized path;
            // claim their own virtual path (== their real path) first.
            // Ordinal, like the virtual file system the paths are stored in.
            // The generated sources' hint names come from the model prefix,
            // not from these paths, so paths differing only in case do not
            // collide and must not drop a valid model.
            var claimedBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((AdditionalText text, NodesetFileOptions _) in xmlInputFiles)
            {
                claimedBy[text.Path] = text.Path;
            }

            foreach (WotConversionOutcome outcome in wotOutcomes)
            {
                diagnostics.AddRange(outcome.Diagnostics);
                if (outcome.NodeSetText is null)
                {
                    // Parse/bounds/conversion failure already reported above.
                    continue;
                }
                string virtualPath = outcome.NodeSetText.Path;
                if (claimedBy.TryGetValue(virtualPath, out string owner))
                {
                    diagnostics.Add(Diagnostic.Create(
                        SourceGenerator.WotVirtualPathCollision,
                        WotNodeSetAdditionalText.CreateFileLocation(outcome.SourcePath),
                        outcome.SourcePath,
                        virtualPath,
                        owner));
                    continue;
                }
                claimedBy[virtualPath] = outcome.SourcePath;
                accepted.Add((outcome.NodeSetText, outcome.Options));
            }

            return (accepted.ToImmutable(), diagnostics.ToImmutable());
        }
    }
}
