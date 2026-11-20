// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// USD-side sink the <see cref="OpenUsdConnector"/> writes into. Values cross the
    /// boundary as <see cref="Variant"/> (never <see cref="object"/>): a scalar
    /// attribute is a <c>double</c>, a structured Translation/Rotation attribute is a
    /// three-element <c>double</c> array, a colour is a three-element <c>float</c>
    /// array (<see cref="ArrayOf{T}"/>), and a token/visibility value is a
    /// <c>string</c>.
    /// </summary>
    public interface IUsdSink
    {
        /// <summary>
        /// Writes (or overwrites) the current value of the named USD attribute on the
        /// prim at <paramref name="primPath"/>.
        /// </summary>
        void SetAttribute(string primPath, string propertyName, Variant value);

        /// <summary>
        /// Authors a USD time sample (for <c>UaHistoryToUsd</c> playback/scrubbing).
        /// The <paramref name="time"/> is mapped to a USD frame (seconds since the Unix
        /// epoch).
        /// </summary>
        void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value);

        /// <summary>
        /// Composes a component prim (§5.12): a <see cref="OpenUsdCompositionArc.Child"/>
        /// is an inline over/def prim; a Reference/Payload/Instance authors the
        /// references/payload (and <c>instanceable</c> for Instance) to the component
        /// asset. <paramref name="active"/> = <c>false</c> deactivates a removed
        /// component prim (dynamic composition, §5.13).
        /// </summary>
        void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active);

        /// <summary>
        /// Begins a batch. While the returned scope is open the sink may defer
        /// expensive flushes (e.g. rewriting a backing file) until the scope is
        /// disposed; sinks that do not buffer return a no-op scope. History replay uses
        /// this to author many time samples with a single flush.
        /// </summary>
        IDisposable BeginBatch();
    }
}
