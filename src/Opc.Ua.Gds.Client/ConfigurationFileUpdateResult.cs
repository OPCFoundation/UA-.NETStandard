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

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Describes the outcome of a remote ConfigurationFile update.
    /// </summary>
    public enum ConfigurationFileUpdateOutcome
    {
        /// <summary>
        /// The server applied the update and did not request confirmation.
        /// </summary>
        Applied,

        /// <summary>
        /// The server applied the update and accepted its confirmation.
        /// </summary>
        Confirmed,

        /// <summary>
        /// The server applied the update, but the client did not confirm it.
        /// </summary>
        ConfirmationPending,

        /// <summary>
        /// The client did not receive enough information to determine whether the update or confirmation completed.
        /// </summary>
        OutcomeUnknown,

        /// <summary>
        /// The server rejected the update.
        /// </summary>
        Rejected
    }

    /// <summary>
    /// Reports the remote status, target results and confirmation state of a ConfigurationFile update.
    /// </summary>
    /// <param name="Outcome">The known outcome category.</param>
    /// <param name="NewVersion">The version returned by the server, or zero when unknown.</param>
    /// <param name="UpdateId">The exact update identifier returned by the server, or <see cref="Uuid.Empty"/>.</param>
    /// <param name="UpdateResults">The per-target status results returned by the server.</param>
    /// <param name="StatusCode">The last known service status.</param>
    public readonly record struct ConfigurationFileUpdateResult(
        ConfigurationFileUpdateOutcome Outcome,
        uint NewVersion,
        Uuid UpdateId,
        ArrayOf<StatusCode> UpdateResults,
        StatusCode StatusCode);
}
