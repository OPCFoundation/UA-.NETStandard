/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolBuilder<TState>
    {
        /// <summary>
        /// Selects standard errors, warnings and messages without changing mandatory-only observation defaults.
        /// </summary>
        /// <param name="options">The per-instance provider and bounded notification configuration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public MachineToolBuilder<TState> WithNotifications(MachineToolNotificationOptions options)
        {
            EnsureMutable();
            m_notificationOptions = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>The explicit optional notification selection.</summary>
        private MachineToolNotificationOptions? m_notificationOptions;
    }
}
