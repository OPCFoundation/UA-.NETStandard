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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// Default BCL-backed DTLS context factory.
    /// </summary>
    public sealed class DefaultDtlsContextFactory : IDtlsContextFactory
    {
        /// <summary>
        /// Initializes a new <see cref="DefaultDtlsContextFactory"/>.
        /// </summary>
        public DefaultDtlsContextFactory(
            IOptions<DtlsTransportOptions> options,
            DtlsProfileRegistry profileRegistry,
            ICertificateValidatorEx? certificateValidator = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (profileRegistry is null)
            {
                throw new ArgumentNullException(nameof(profileRegistry));
            }

            Options = options.Value ?? new DtlsTransportOptions();
            ProfileRegistry = profileRegistry;
            CertificateValidator = certificateValidator;
        }

        /// <summary>
        /// Direct-construct fallback options.
        /// </summary>
        public DtlsTransportOptions Options { get; }

        /// <summary>
        /// Runtime DTLS profile registry.
        /// </summary>
        public DtlsProfileRegistry ProfileRegistry { get; }

        /// <summary>
        /// Injected stack certificate validator used for DTLS peer authentication.
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; }

        /// <inheritdoc/>
        public ValueTask<IDtlsContext> CreateAsync(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            DtlsProfile profile,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            CancellationToken cancellationToken = default)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (!endpoint.IsValid)
            {
                throw new ArgumentException("DTLS endpoint is not valid.", nameof(endpoint));
            }

            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            cancellationToken.ThrowIfCancellationRequested();
            ILogger logger = telemetry.CreateLogger<DefaultDtlsContextFactory>();
            logger.LogInformation(
                "Creating OPC UA PubSub DTLS context: connection='{Connection}' endpoint={Endpoint} profile={Profile}.",
                connection.Name,
                endpoint,
                profile.Name);
            // CA2000: ownership is transferred to DtlsDatagramTransport, which disposes the context on close.
            // TODO(CA2000): introduce an owned-context result type if this factory gains additional disposable contexts.
#pragma warning disable CA2000
            IDtlsContext context = new DtlsHandshakeContext(
                profile,
                Options,
                CertificateValidator ?? Options.PeerCertificateValidator,
                DetermineRole(connection),
                endpoint,
                timeProvider);
#pragma warning restore CA2000
            return new ValueTask<IDtlsContext>(context);
        }

        private static DtlsEndpointRole DetermineRole(PubSubConnectionDataType connection)
        {
            bool hasWriters = !connection.WriterGroups.IsNull && connection.WriterGroups.Count > 0;
            bool hasReaders = !connection.ReaderGroups.IsNull && connection.ReaderGroups.Count > 0;
            return hasWriters && !hasReaders ? DtlsEndpointRole.Client : DtlsEndpointRole.Server;
        }
    }

    internal enum DtlsEndpointRole
    {
        Client,
        Server
    }
}
