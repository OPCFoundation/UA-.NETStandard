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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Default <see cref="ILoadWeightProvider"/> that reports a fixed load weight (0 by default). With the constant
    /// provider, load direction degenerates to random selection among peers tied at the highest health
    /// <c>ServiceLevel</c>, preserving simple behaviour when no load metric is wired.
    /// </summary>
    public sealed class ConstantLoadWeightProvider : ILoadWeightProvider
    {
        /// <summary>
        /// Creates the provider.
        /// </summary>
        /// <param name="loadWeight">The fixed load weight to report (default 0 = idle).</param>
        public ConstantLoadWeightProvider(byte loadWeight = 0)
        {
            m_loadWeight = loadWeight;
        }

        /// <inheritdoc/>
        public byte GetLoadWeight()
        {
            return m_loadWeight;
        }

        /// <inheritdoc/>
        public event Action<byte>? LoadWeightChanged
        {
            add { }
            remove { }
        }

        private readonly byte m_loadWeight;
    }
}
