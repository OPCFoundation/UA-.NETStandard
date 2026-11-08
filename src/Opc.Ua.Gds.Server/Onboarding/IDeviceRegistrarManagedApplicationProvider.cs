/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Supplies the authoritative endpoint metadata and host-selected authentication for a managed server.
    /// </summary>
    public interface IDeviceRegistrarManagedApplicationProvider
    {
        /// <summary>
        /// Resolves the managed application's published discovery URLs, installed certificate, endpoint and user
        /// identity and explicit server capabilities. Connection and discovery URLs may differ.
        /// The certificate is the initial operator pin; rotation recovery requires an exact retained issuance.
        /// </summary>
        /// <param name="applicationUri">The exact application URI from the device's managed-application record.</param>
        /// <param name="cancellationToken">A token that cancels metadata and endpoint resolution.</param>
        /// <returns>The host-authoritative endpoint metadata and authentication target.</returns>
        ValueTask<DeviceRegistrarManagedApplicationTarget> GetTargetAsync(
            string applicationUri,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Host-authoritative endpoint metadata and authentication for one managed application with a server endpoint.
    /// </summary>
    /// <param name="DiscoveryUrls">
    /// Authoritative absolute discovery URLs published for the application.
    /// </param>
    /// <param name="InstalledCertificate">
    /// The public leaf certificate initially installed by the application, retained as the operator pin.
    /// </param>
    /// <param name="Endpoint">
    /// The host-selected encrypted connection endpoint; its URL may differ from the discovery URLs.
    /// </param>
    /// <param name="UserIdentity">
    /// The host-selected identity used to authenticate the fresh session.
    /// </param>
    /// <param name="ServerCapabilities">
    /// Nonempty server capability identifiers selected by the application owner, independent of discovery.
    /// </param>
    public sealed record DeviceRegistrarManagedApplicationTarget(
        ArrayOf<string> DiscoveryUrls,
        ByteString InstalledCertificate,
        ConfiguredEndpoint Endpoint,
        IUserIdentity UserIdentity,
        ArrayOf<string> ServerCapabilities)
    {
        /// <summary>
        /// Gets the operator-selected certificate type for this managed application.
        /// A null identifier uses the registrar connection's configured certificate type.
        /// </summary>
        public ExpandedNodeId CertificateTypeId { get; init; }

        /// <summary>
        /// Gets the exact HTTPS TLS leaf certificate pin, independent of the installed OPC UA application certificate.
        /// An empty value requires OS TLS trust. Hostname checks apply in either case on every TLS connection.
        /// The TLS terminator is part of the trust boundary and can access transport and session data.
        /// </summary>
        public ByteString HttpsServerCertificate { get; init; }
    }

    /// <summary>
    /// Verifies a managed application's installed certificate on a fresh encrypted session.
    /// </summary>
    public interface IDeviceRegistrarManagedApplicationChannelVerifier
    {
        /// <summary>
        /// Opens a fresh session to the selected endpoint, verifies the application certificate and proof
        /// against <paramref name="expectedCertificate"/>, then closes and disposes that session before returning.
        /// HTTPS authenticates its TLS terminator separately, on every connection, before sending credentials.
        /// </summary>
        /// <param name="application">The application identity being verified.</param>
        /// <param name="target">The host-authoritative endpoint, certificate and user identity.</param>
        /// <param name="expectedCertificate">The public leaf certificate expected for the application proof.</param>
        /// <param name="cancellationToken">A token that cancels session creation and verification.</param>
        /// <returns>A task that completes after peer verification and session cleanup.</returns>
        ValueTask VerifyAsync(
            ApplicationDescription application,
            DeviceRegistrarManagedApplicationTarget target,
            ByteString expectedCertificate,
            CancellationToken cancellationToken = default);
    }
}
