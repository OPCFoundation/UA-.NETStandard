// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// An <see cref="IServiceLevelProvider"/> that reports a fixed service
    /// level (255 by default). This preserves the historical single-instance
    /// behavior and is the default when no redundancy is configured.
    /// </summary>
    public sealed class ConstantServiceLevelProvider : IServiceLevelProvider
    {
        /// <summary>
        /// Creates a provider reporting a fixed service level.
        /// </summary>
        /// <param name="level">The fixed service level (default 255).</param>
        public ConstantServiceLevelProvider(byte level = ServiceLevels.Maximum)
        {
            m_level = level;
        }

        /// <inheritdoc/>
        public event Action<byte>? ServiceLevelChanged
        {
            // The level never changes, so there is nothing to raise.
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public byte GetServiceLevel()
        {
            return m_level;
        }

        private readonly byte m_level;
    }
}
