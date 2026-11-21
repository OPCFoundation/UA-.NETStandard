// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Where inference runs for the sample's pipeline. Selected once
    /// at startup and pinned for the lifetime of the process — the
    /// pipeline's advertised inference-location facet is derived from
    /// this and cannot change afterwards, so it "says honestly" which
    /// path is in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value maps onto the specification's <c>InferenceLocation</c>
    /// concept: the on-server ground truth is
    /// <see cref="OnServer"/> and the agent-as-VLM path is
    /// <see cref="EdgeOffServer"/>. The two are mutually exclusive by
    /// construction — the pipeline binds a single inference provider
    /// and (optionally) a single feedback sink; wiring both at once
    /// would let a submitted result and a computed result publish on
    /// the same pipeline out of any known order, which is the
    /// invariant the option prevents.
    /// </para>
    /// </remarks>
    internal enum BinPickingInferenceLocation
    {
        /// <summary>
        /// The Server computes results locally through the
        /// deterministic ground-truth detector. The pipeline advertises
        /// <c>VIS-Inference-OnServer</c>. This is the CI and offline
        /// default; it needs neither a GPU nor a model.
        /// </summary>
        OnServer = 0,

        /// <summary>
        /// Inference runs off-Server: an agent connected over MCP
        /// looks at the frame, decides what it sees, and calls
        /// <c>SubmitDetections</c>. The pipeline advertises
        /// <c>VIS-Inference-OffServer</c> and publishes results the
        /// Server itself did not compute.
        /// </summary>
        EdgeOffServer = 1
    }

    /// <summary>
    /// Startup options for the bin-picking sample. Populated in
    /// <c>Program.cs</c> from the host configuration and registered as
    /// a DI singleton so both the vision configurator and the proof
    /// services pick up the same value.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed class BinPickingCellOptions
    {
        /// <summary>
        /// Selected inference-location mode. See
        /// <see cref="BinPickingInferenceLocation"/>. Defaults to
        /// <see cref="BinPickingInferenceLocation.OnServer"/>.
        /// </summary>
        public BinPickingInferenceLocation InferenceLocation { get; init; }
            = BinPickingInferenceLocation.OnServer;

        /// <summary>
        /// Attempts to parse the CLI/config value into a
        /// <see cref="BinPickingInferenceLocation"/>. Accepts the
        /// exact enum names case-insensitively so both
        /// <c>OnServer</c> and <c>on-server</c> resolve; returns
        /// <see langword="false"/> for anything else, including empty.
        /// </summary>
        public static bool TryParseLocation(
            string? value, out BinPickingInferenceLocation location)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                location = BinPickingInferenceLocation.OnServer;
                return false;
            }
            string normalised = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
            if (string.Equals(normalised, "OnServer", StringComparison.OrdinalIgnoreCase))
            {
                location = BinPickingInferenceLocation.OnServer;
                return true;
            }
            if (string.Equals(normalised, "EdgeOffServer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalised, "OffServer", StringComparison.OrdinalIgnoreCase))
            {
                location = BinPickingInferenceLocation.EdgeOffServer;
                return true;
            }
            location = BinPickingInferenceLocation.OnServer;
            return false;
        }
    }
}
