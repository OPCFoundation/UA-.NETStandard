/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
