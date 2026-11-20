/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
