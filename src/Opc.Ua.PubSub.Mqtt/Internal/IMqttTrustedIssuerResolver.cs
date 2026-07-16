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
 *
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
