// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// Resolves the certificate authority (CA) certificates referenced by
    /// <see cref="MqttTlsOptions.TrustedIssuerCertificateSubjects"/> into a concrete
    /// trust chain used to validate the MQTT broker certificate.
    /// </summary>
    /// <remarks>
    /// Implementations look the subjects up in the application's trusted issuer
    /// certificate store. The resolver is injected optionally; when it is not available
    /// (no certificate configuration) the MQTT transport falls back to the platform
    /// default trust store.
    /// </remarks>
    internal interface IMqttTrustedIssuerResolver
    {
        /// <summary>
        /// Resolves the supplied CA subject (or thumbprint) references into an owned
        /// <see cref="CertificateCollection"/> of trusted issuer certificates.
        /// </summary>
        /// <param name="subjects">
        /// The configured CA subject distinguished names or thumbprints.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to open the certificate store and emit logs.
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the asynchronous store access.
        /// </param>
        /// <returns>
        /// An owned <see cref="CertificateCollection"/> (the caller disposes it); empty
        /// when nothing is configured or no matching certificate is found.
        /// </returns>
        ValueTask<CertificateCollection> ResolveAsync(
            IReadOnlyList<string> subjects,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken);
    }
}
