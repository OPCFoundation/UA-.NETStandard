// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Forwards live values to a viewport stage whose structural composition was
    /// prepared before its retained renderer was created.
    /// </summary>
    /// <remarks>
    /// Remote component metadata is not an asset-resolution authority. Composition updates,
    /// including activation and deactivation, are suppressed regardless of their destination.
    /// Trusted initial-stage composition must be prepared separately by the host.
    /// </remarks>
    public sealed class UsdViewportValueSink : IUsdSink
    {
        /// <summary>
        /// Creates a value-only boundary over a host-owned viewport sink.
        /// </summary>
        public UsdViewportValueSink(IUsdSink inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc/>
        public void SetAttribute(string primPath, string propertyName, Variant value)
        {
            m_inner.SetAttribute(primPath, propertyName, value);
        }

        /// <inheritdoc/>
        public void SetTimeSample(
            string primPath,
            string propertyName,
            DateTime time,
            Variant value)
        {
            m_inner.SetTimeSample(primPath, propertyName, time, value);
        }

        /// <inheritdoc/>
        public void ComposePrim(
            string primPath,
            OpenUsdCompositionArc arc,
            string? assetReference,
            bool active)
        {
            // Do not resolve server-supplied assets with the viewer's ambient credentials.
        }

        /// <inheritdoc/>
        public IDisposable BeginBatch()
        {
            return m_inner.BeginBatch();
        }

        /// <summary>The host-owned sink receiving only values and time samples.</summary>
        private readonly IUsdSink m_inner;
    }
}
