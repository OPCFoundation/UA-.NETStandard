/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Applies a registered application certificate to the running server endpoints and listeners.
    /// </summary>
    public interface IServerApplicationCertificateActivator
    {
        /// <summary>
        /// Applies the certificate registered for <paramref name="certificateType"/> and confirms that the
        /// certificate manager still exposes <paramref name="expectedThumbprint"/>.
        /// </summary>
        /// <param name="certificateType">The OPC UA certificate type being activated.</param>
        /// <param name="expectedThumbprint">The certificate thumbprint expected by the caller.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Completion after the endpoint descriptions and bound listeners have been refreshed.</returns>
        ValueTask ActivateAsync(
            NodeId certificateType,
            string expectedThumbprint,
            CancellationToken cancellationToken = default);
    }
}
