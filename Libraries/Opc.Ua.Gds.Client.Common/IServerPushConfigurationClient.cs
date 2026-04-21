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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Public surface of <see cref="ServerPushConfigurationClient"/>. Exposes
    /// the asynchronous operations that the push configuration client offers
    /// against a connected server implementing the ServerConfiguration model.
    /// </summary>
    public interface IServerPushConfigurationClient : IAsyncDisposable
    {
        /// <summary>The application configuration in use.</summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>The message context derived from the configuration.</summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>The administrator credentials used to elevate calls.</summary>
        IUserIdentity AdminCredentials { get; set; }

        /// <summary>The endpoint URL of the connected server, when available.</summary>
        string EndpointUrl { get; }

        /// <summary><c>true</c> when an active session exists.</summary>
        bool IsConnected { get; }

        /// <summary>The active session, when connected.</summary>
        ISession Session { get; }

        /// <summary>The endpoint to connect to. Write-once before connect.</summary>
        ConfiguredEndpoint Endpoint { get; set; }

        /// <summary>Locales preferred by the client.</summary>
        ArrayOf<string> PreferredLocales { get; set; }

        /// <summary>NodeId of the DefaultApplicationGroup.</summary>
        NodeId DefaultApplicationGroup { get; }

        /// <summary>NodeId of the DefaultHttpsGroup.</summary>
        NodeId DefaultHttpsGroup { get; }

        /// <summary>NodeId of the DefaultUserTokenGroup.</summary>
        NodeId DefaultUserTokenGroup { get; }

        /// <summary>NodeId of the application certificate type.</summary>
        NodeId ApplicationCertificateType { get; }

        /// <summary>Raised when administrator credentials are needed.</summary>
        event EventHandler<AdminCredentialsRequiredEventArgs> AdminCredentialsRequired;

        /// <summary>Raised when the connection status changes.</summary>
        event EventHandler ConnectionStatusChanged;

        /// <summary>Raised on every keep-alive callback.</summary>
        event KeepAliveEventHandler KeepAlive;

        /// <summary>Raised when monitored item notifications change server status.</summary>
        event MonitoredItemNotificationEventHandler ServerStatusChanged;

        /// <summary>Clears the cached <see cref="AdminCredentials"/>.</summary>
        void ResetCredentials();

        /// <summary>Connects using the previously assigned endpoint.</summary>
        ValueTask ConnectAsync(CancellationToken ct = default);

        /// <summary>Connects to the supplied endpoint URL.</summary>
        ValueTask ConnectAsync(string endpointUrl, CancellationToken ct = default);

        /// <summary>Connects to the supplied configured endpoint.</summary>
        ValueTask ConnectAsync(ConfiguredEndpoint endpoint, CancellationToken ct = default);

        /// <summary>Disconnects the active session.</summary>
        ValueTask DisconnectAsync(CancellationToken ct = default);

        /// <summary>Returns the supported private-key formats.</summary>
        /// <remarks>Reads the <c>SupportedPrivateKeyFormats</c> property of <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.2).</remarks>
        ValueTask<ArrayOf<string>> GetSupportedKeyFormatsAsync(CancellationToken ct = default);

        /// <summary>Reads the default-application-group trust list.</summary>
        /// <remarks>Reads the trust list via the file transfer methods of <c>TrustListType</c> (OPC 10000-12 §7.8).</remarks>
        ValueTask<TrustListDataType> ReadTrustListAsync(
            TrustListMasks masks = TrustListMasks.All,
            long maxTrustListSize = 0,
            CancellationToken ct = default);

        /// <summary>Updates the default-application-group trust list.</summary>
        /// <remarks>Writes the trust list via the file transfer methods of <c>TrustListType</c> (OPC 10000-12 §7.8).</remarks>
        ValueTask<bool> UpdateTrustListAsync(
            TrustListDataType trustList,
            CancellationToken ct = default);

        /// <summary>Updates the default-application-group trust list.</summary>
        /// <remarks>Writes the trust list via the file transfer methods of <c>TrustListType</c> (OPC 10000-12 §7.8).</remarks>
        ValueTask<bool> UpdateTrustListAsync(
            TrustListDataType trustList,
            long maxTrustListSize,
            CancellationToken ct = default);

        /// <summary>Adds a certificate to the trust list.</summary>
        /// <remarks>Calls the <c>AddCertificate</c> method on <c>TrustListType</c> (OPC 10000-12 §7.8.7).</remarks>
        ValueTask AddCertificateAsync(
            X509Certificate2 certificate,
            bool isTrustedCertificate,
            CancellationToken ct = default);

        /// <summary>Removes a certificate from the trust list.</summary>
        /// <remarks>Calls the <c>RemoveCertificate</c> method on <c>TrustListType</c> (OPC 10000-12 §7.8.8).</remarks>
        ValueTask RemoveCertificateAsync(
            string thumbprint,
            bool isTrustedCertificate,
            CancellationToken ct = default);

        /// <summary>Lists the rejected certificates.</summary>
        /// <remarks>Calls the <c>GetRejectedList</c> method on <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.6).</remarks>
        ValueTask<X509Certificate2Collection> GetRejectedListAsync(CancellationToken ct = default);

        /// <summary>Creates a certificate signing request.</summary>
        /// <remarks>Calls the <c>CreateSigningRequest</c> method on <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.4).</remarks>
        ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            ByteString nonce,
            CancellationToken ct = default);

        /// <summary>Updates the application certificate.</summary>
        /// <remarks>Calls the <c>UpdateCertificate</c> method on <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.3).</remarks>
        ValueTask<bool> UpdateCertificateAsync(
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificate,
            string privateKeyFormat,
            ByteString privateKey,
            ArrayOf<ByteString> issuerCertificates,
            CancellationToken ct = default);

        /// <summary>Applies the configuration changes on the server.</summary>
        /// <remarks>Calls the <c>ApplyChanges</c> method on <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.5).</remarks>
        ValueTask ApplyChangesAsync(CancellationToken ct = default);

        /// <summary>Lists the certificates configured on the server.</summary>
        /// <remarks>Calls the <c>GetCertificates</c> method on <c>ServerConfigurationType</c> (OPC 10000-12 §7.10.7).</remarks>
        ValueTask<(ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates)> GetCertificatesAsync(
            NodeId certificateGroupId,
            CancellationToken ct = default);
    }
}
