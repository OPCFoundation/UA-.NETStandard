// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Binds a stock factory to the exact operation's prior registration before candidate construction.
    /// </summary>
    internal interface IRegistrationBoundNodeManagerFactory
    {
        /// <summary>
        /// Gets the prior owner, or null for an initial addition.
        /// </summary>
        NodeManagerRegistration? ExpectedRegistration { get; }
    }
}
