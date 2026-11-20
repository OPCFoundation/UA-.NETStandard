// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// An identity that decorates the authenticated identity, for example with Roles,
    /// and exposes it so the user it names can be resolved.
    /// </summary>
    internal interface IDecoratedUserIdentity
    {
        /// <summary>
        /// The decorated identity.
        /// </summary>
        IUserIdentity InnerIdentity { get; }
    }
}
