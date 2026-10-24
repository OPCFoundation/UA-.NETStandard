// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Test-only in-memory credentials. No key, certificate or trust-store artifact is written.
    /// </summary>
    internal sealed class TestCredentials : IDisposable
    {
        /// <summary>
        /// Creates a short-lived test CA and two independently signed application identities.
        /// </summary>
        internal TestCredentials()
        {
            m_rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                "CN=QUIC backend test authority", m_rootKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            m_root = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            m_clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            m_serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            m_client = CreateLeaf(m_clientKey, ClientUri, false);
            m_server = CreateLeaf(m_serverKey, ServerUri, true);
            m_clientSigning = Export(m_clientKey);
            m_serverSigning = Export(m_serverKey);
            Client = new NativeQuicCredentials(
                new ByteString(m_client.RawData), default, m_clientSigning,
                new ByteString(m_root.RawData), ClientUri);
            Server = new NativeQuicCredentials(
                new ByteString(m_server.RawData), default, m_serverSigning,
                new ByteString(m_root.RawData), ServerUri);
            TlsClient = NativeQuicCredentials.ForTlsClient(new ByteString(m_root.RawData));
            TlsServer = NativeQuicCredentials.ForTlsServer(new ByteString(m_server.RawData), default, m_serverSigning);
            ClientPeer = new NativeQuicPeer(ClientUri,
                new ByteString(SHA256.HashData(m_clientKey.ExportSubjectPublicKeyInfo())));
            ServerPeer = new NativeQuicPeer(ServerUri,
                new ByteString(SHA256.HashData(m_serverKey.ExportSubjectPublicKeyInfo())));
        }

        /// <summary>
        /// Gets client credentials.
        /// </summary>
        internal NativeQuicCredentials Client { get; }

        /// <summary>
        /// Gets server credentials.
        /// </summary>
        internal NativeQuicCredentials Server { get; }

        /// <summary>
        /// Gets transport-only server trust without any client certificate or private key.
        /// </summary>
        internal NativeQuicCredentials TlsClient { get; }

        /// <summary>
        /// Gets server TLS signing material without client authorization or an application URI.
        /// </summary>
        internal NativeQuicCredentials TlsServer { get; }

        /// <summary>
        /// Gets the expected authenticated client.
        /// </summary>
        internal NativeQuicPeer ClientPeer { get; }

        /// <summary>
        /// Gets the expected authenticated server.
        /// </summary>
        internal NativeQuicPeer ServerPeer { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_clientSigning.Dispose();
            m_serverSigning.Dispose();
            m_client.Dispose();
            m_server.Dispose();
            m_root.Dispose();
            m_clientKey.Dispose();
            m_serverKey.Dispose();
            m_rootKey.Dispose();
        }

        /// <summary>
        /// Creates a leaf with a specific URI SAN and TLS usage.
        /// </summary>
        private X509Certificate2 CreateLeaf(ECDsa key, string uri, bool server)
        {
            var request = new CertificateRequest("CN=QUIC native test application", key, HashAlgorithmName.SHA256);
            var sans = new SubjectAlternativeNameBuilder();
            sans.AddUri(new Uri(uri));
            sans.AddDnsName("localhost");
            request.CertificateExtensions.Add(sans.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            var usages = new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            return request.Create(m_root, DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        }

        /// <summary>
        /// Exports test-only software keys and clears the temporary PKCS8 copy.
        /// </summary>
        private static NativeQuicPrivateKey Export(ECDsa key)
        {
            byte[] pkcs8 = key.ExportPkcs8PrivateKey();
            try
            {
                return new NativeQuicPrivateKey(pkcs8);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
        }

        /// <summary>Client certificate URI SAN.</summary>
        internal const string ClientUri = "urn:opcfoundation:quic-tests:client";

        /// <summary>Server certificate URI SAN.</summary>
        internal const string ServerUri = "urn:opcfoundation:quic-tests:server";

        private readonly ECDsa m_rootKey;
        private readonly ECDsa m_clientKey;
        private readonly ECDsa m_serverKey;
        private readonly X509Certificate2 m_root;
        private readonly X509Certificate2 m_client;
        private readonly X509Certificate2 m_server;
        private readonly NativeQuicPrivateKey m_clientSigning;
        private readonly NativeQuicPrivateKey m_serverSigning;
    }
}
