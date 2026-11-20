/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes the validator a server checks
    /// peer (application instance) certificates with, so subordinate
    /// components can validate a certificate a client supplies later in the
    /// session without any change to <see cref="IServerInternal"/>;
    /// external/mocked <see cref="IServerInternal"/> implementations remain
    /// unaffected.
    /// </summary>
    /// <remarks>
    /// Consumers should resolve the validator in a null-safe manner, e.g.:
    /// <code>
    /// var validator = (server as ICertificateValidatorProvider)?.CertificateValidator;
    /// </code>
    /// </remarks>
    public interface ICertificateValidatorProvider
    {
        /// <summary>
        /// Gets the validator the server checks peer certificates with, or
        /// <c>null</c> when none is available.
        /// </summary>
        ICertificateValidatorEx? CertificateValidator { get; }
    }
}
