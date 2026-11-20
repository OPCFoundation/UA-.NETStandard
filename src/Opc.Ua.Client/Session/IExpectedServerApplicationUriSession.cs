/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// Allows a caller to require a specific server ApplicationUri before session credentials are activated.
    /// </summary>
    public interface IExpectedServerApplicationUriSession
    {
        /// <summary>
        /// Requires the CreateSession endpoint description to report the specified ApplicationUri.
        /// </summary>
        /// <param name="expectedApplicationUri">The expected absolute server ApplicationUri.</param>
        /// <remarks>
        /// Configure this requirement before opening the session. A mismatch rejects the session before
        /// ActivateSession sends user credentials. Generic sessions do not enable this check by default.
        /// </remarks>
        void RequireServerApplicationUri(string expectedApplicationUri);
    }
}
