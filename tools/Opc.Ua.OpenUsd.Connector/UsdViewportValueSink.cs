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
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Connector
{
    /// <summary>
    /// Forwards live values to a viewport stage whose structural composition was
    /// prepared before its retained renderer was created.
    /// </summary>
    internal sealed class UsdViewportValueSink : IUsdSink
    {
        public UsdViewportValueSink(IUsdSink inner)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void SetAttribute(string primPath, string propertyName, Variant value)
        {
            m_inner.SetAttribute(primPath, propertyName, value);
        }

        public void SetTimeSample(
            string primPath,
            string propertyName,
            DateTime time,
            Variant value)
        {
            m_inner.SetTimeSample(primPath, propertyName, time, value);
        }

        public void ComposePrim(
            string primPath,
            OpenUsdCompositionArc arc,
            string? assetReference,
            bool active)
        {
            // OpenUSD 0.12 retained renderers do not rebuild reliably after live
            // composition edits. The file sink still persists these changes.
        }

        public IDisposable BeginBatch()
        {
            return m_inner.BeginBatch();
        }

        private readonly IUsdSink m_inner;
    }
}
