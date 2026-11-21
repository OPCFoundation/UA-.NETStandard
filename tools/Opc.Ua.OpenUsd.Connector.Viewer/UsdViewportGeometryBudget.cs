// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
