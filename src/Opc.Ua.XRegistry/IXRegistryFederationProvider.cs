/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Optional federation verification at the registry's configured provider boundary.
    /// Verification must not read file content or substitute a content lookup for a logical Resource.
    /// </summary>
    public interface IXRegistryFederationProvider
    {
        /// <summary>
        /// Verifies an authorized locator, authenticated application, origin registry, logical
        /// Resource ownership/Xid, FileType capability and Versions hierarchy against the trusted binding.
        /// </summary>
        /// <param name="target">The immutable, independently configured trusted binding.</param>
        /// <param name="endpointUrl">A locator to authorize before connecting, never identity authority.</param>
        /// <param name="cancellationToken">Cancels verification before publication.</param>
        /// <exception cref="ServiceResultException">
        /// Verification fails. A provider without logical Resource verification reports <c>BadNotSupported</c>
        /// before any connection or content operation.
        /// </exception>
        ValueTask VerifyLogicalResourceAsync(
            XRegistryFederationTarget target,
            string endpointUrl,
            CancellationToken cancellationToken = default);
    }
}
