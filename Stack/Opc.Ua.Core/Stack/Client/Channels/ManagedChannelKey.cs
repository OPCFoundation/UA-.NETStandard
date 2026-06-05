/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Composite identity used by <see cref="IClientChannelManager"/> to
    /// determine which <see cref="IReconnectParticipant"/> instances may
    /// share a single underlying <see cref="ITransportChannel"/>.
    /// </summary>
    /// <remarks>
    /// Two participants share a channel only when their
    /// <see cref="ManagedChannelKey"/> values are equal. Forward and
    /// reverse-connect channels are never shared because the
    /// reverse-connection identity differs (forward is <c>null</c>).
    /// </remarks>
    public readonly record struct ManagedChannelKey
    {
        /// <summary>
        /// Creates a channel key from the supplied components.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="securityMode">The message security mode.</param>
        /// <param name="serverCertificateThumbprint">Thumbprint of the
        /// expected server certificate (may be empty for None
        /// security).</param>
        /// <param name="endpointConfigurationHash">Stable hash of the
        /// endpoint configuration values.</param>
        /// <param name="clientCertificateThumbprint">Thumbprint of the
        /// client instance certificate (may be empty for None
        /// security).</param>
        /// <param name="reverseConnectionIdentity">Opaque identity of
        /// the reverse-connect wait handle. Use <c>null</c> for
        /// forward connections.</param>
        public ManagedChannelKey(
            string endpointUrl,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            ByteString serverCertificateThumbprint,
            int endpointConfigurationHash,
            ByteString clientCertificateThumbprint,
            object? reverseConnectionIdentity)
        {
            EndpointUrl = endpointUrl ?? throw new ArgumentNullException(nameof(endpointUrl));
            SecurityPolicyUri = securityPolicyUri
                ?? throw new ArgumentNullException(nameof(securityPolicyUri));
            SecurityMode = securityMode;
            ServerCertificateThumbprint = serverCertificateThumbprint;
            EndpointConfigurationHash = endpointConfigurationHash;
            ClientCertificateThumbprint = clientCertificateThumbprint;
            ReverseConnectionIdentity = reverseConnectionIdentity;
        }

        /// <summary>
        /// The endpoint URL.
        /// </summary>
        public string EndpointUrl { get; }

        /// <summary>
        /// The security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// The message security mode.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; }

        /// <summary>
        /// Thumbprint of the expected server certificate.
        /// </summary>
        public ByteString ServerCertificateThumbprint { get; }

        /// <summary>
        /// Stable hash of the endpoint configuration values
        /// (timeouts, message size limits, encoding mode, etc.).
        /// </summary>
        public int EndpointConfigurationHash { get; }

        /// <summary>
        /// Thumbprint of the client instance certificate.
        /// </summary>
        public ByteString ClientCertificateThumbprint { get; }

        /// <summary>
        /// Opaque identity of the reverse-connect wait handle. Two
        /// participants supplying the same waiting connection instance
        /// can share; participants supplying different waiting
        /// connections never share. <c>null</c> for forward connections.
        /// </summary>
        public object? ReverseConnectionIdentity { get; }

        /// <summary>
        /// Computes a key for the supplied configured endpoint and
        /// (optional) client certificate / reverse connection identity.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <param name="clientCertificate">The client instance
        /// certificate (may be <c>null</c>).</param>
        /// <param name="reverseConnectionIdentity">Opaque identity of
        /// the reverse-connect waiting connection or <c>null</c> for
        /// forward.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="endpoint"/> is <c>null</c>.</exception>
        public static ManagedChannelKey FromEndpoint(
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate = null,
            object? reverseConnectionIdentity = null)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            EndpointDescription description = endpoint.Description;
            EndpointConfiguration? configuration = endpoint.Configuration;

            ByteString serverThumbprint = ComputeServerCertificateThumbprint(
                description.ServerCertificate);

            ByteString clientThumbprint = clientCertificate is { RawData: { } cd } && cd.Length > 0
                ? new ByteString(ComputeSha1Thumbprint(cd))
                : ByteString.Empty;

            return new ManagedChannelKey(
                description.EndpointUrl ?? string.Empty,
                description.SecurityPolicyUri ?? SecurityPolicies.None,
                description.SecurityMode,
                serverThumbprint,
                ComputeEndpointConfigurationHash(configuration),
                clientThumbprint,
                reverseConnectionIdentity);
        }

        private static ByteString ComputeServerCertificateThumbprint(ByteString rawCertificate)
        {
            if (rawCertificate.IsNull || rawCertificate.Length == 0)
            {
                return ByteString.Empty;
            }
            return new ByteString(ComputeSha1Thumbprint(rawCertificate.ToArray()!));
        }

        [SuppressMessage(
            "Security",
            "CA5350:Do Not Use Weak Cryptographic Algorithms",
            Justification = "SHA1 thumbprint used only as a stable channel sharing key, not for security.")]
        private static byte[] ComputeSha1Thumbprint(byte[] data)
        {
            // SHA1 is used here only as a stable identity hash for
            // channel sharing, NOT for security. It matches X.509
            // thumbprint conventions.
#if NET6_0_OR_GREATER
            return System.Security.Cryptography.SHA1.HashData(data);
#else
            using System.Security.Cryptography.SHA1 sha1 = System.Security.Cryptography.SHA1.Create();

            return sha1.ComputeHash(data);
#endif
        }

        private static int ComputeEndpointConfigurationHash(EndpointConfiguration? configuration)
        {
            if (configuration == null)
            {
                return 0;
            }

            var hash = new HashCode();
            hash.Add(configuration.OperationTimeout);
            hash.Add(configuration.UseBinaryEncoding);
            hash.Add(configuration.MaxArrayLength);
            hash.Add(configuration.MaxStringLength);
            hash.Add(configuration.MaxByteStringLength);
            hash.Add(configuration.MaxMessageSize);
            hash.Add(configuration.MaxBufferSize);
            hash.Add(configuration.ChannelLifetime);
            hash.Add(configuration.SecurityTokenLifetime);
            hash.Add(configuration.MaxEncodingNestingLevels);
            return hash.ToHashCode();
        }
    }
}
