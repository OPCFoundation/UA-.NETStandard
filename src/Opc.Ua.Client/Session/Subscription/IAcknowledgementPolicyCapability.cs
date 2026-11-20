/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// Implemented by a subscription engine context that knows whether an
    /// acknowledgement-deferral policy is registered.
    /// </summary>
    /// <remarks>
    /// Contexts that do not implement it are assumed to have a policy, so that it is
    /// always applied.
    /// </remarks>
    internal interface IAcknowledgementPolicyCapability
    {
        /// <summary>
        /// Gets a value indicating whether an acknowledgement-deferral policy is registered.
        /// </summary>
        bool HasAcknowledgementPolicy { get; }

        /// <summary>
        /// Gets the session's acknowledgement configuration, or <c>null</c> when the session
        /// uses the defaults.
        /// </summary>
        SessionAcknowledgements? Acknowledgements { get; }
    }
}
