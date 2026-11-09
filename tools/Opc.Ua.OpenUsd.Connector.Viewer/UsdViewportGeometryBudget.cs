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
using System.Collections.Generic;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Accounts native page declarations before the renderer copies or uploads any mesh data.
    /// Updates replace prior costs rather than treating a long-running scene as an ever-growing history.
    /// </summary>
    internal sealed class UsdViewportGeometryBudget(UsdViewportLimits limits)
    {
        /// <summary>
        /// Gets the current conservative retained geometry estimate.
        /// </summary>
        public long Bytes { get; private set; }

        /// <summary>
        /// Applies a bounded declaration only after its complete cost has passed admission.
        /// </summary>
        public void Upsert(ulong hash, int instance, int points, int indices, int attributes, bool deformation)
        {
            if (points < 0 || indices < 0 || attributes is < 0 or > 32 || deformation)
            {
                throw new NotSupportedException("This viewport requires bounded, non-deforming mesh pages.");
            }
            long bytes = checked(((long)points * 512) + ((long)indices * 16) + 4096);
            var key = (hash, instance);
            bool replacing = m_meshes.TryGetValue(key, out long previous);
            long total = checked(Bytes - previous + bytes);
            if ((!replacing && m_meshes.Count >= limits.MaximumMeshes) || total > limits.MaximumGeometryBytes)
            {
                throw new InvalidOperationException("The retained scene exceeds its geometry allocation budget.");
            }
            m_meshes[key] = bytes;
            Bytes = total;
        }

        /// <summary>
        /// Releases one mesh declaration, including its instance identity.
        /// </summary>
        public void Remove(ulong hash, int instance)
        {
            if (m_meshes.Remove((hash, instance), out long previous))
            {
                Bytes -= previous;
            }
        }

        private readonly Dictionary<(ulong Hash, int Instance), long> m_meshes = [];
    }
}
