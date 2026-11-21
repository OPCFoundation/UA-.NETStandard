// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Retains the certificate material and version used to configure a managed client channel.
    /// </summary>
    internal sealed class ClientChannelCertificateSnapshot : IDisposable
    {
        /// <summary>
        /// Acquires independent references to the certificate and chain for the specified version.
        /// </summary>
        public ClientChannelCertificateSnapshot(
            Certificate? certificate,
            CertificateCollection? chain,
            long version)
        {
            Certificate = certificate?.AddRef();
            try
            {
                Chain = chain?.AddRef();
            }
            catch
            {
                Certificate?.Dispose();
                throw;
            }
            Version = version;
        }

        /// <summary>
        /// Gets the retained application certificate, including its private key when available.
        /// </summary>
        public Certificate? Certificate { get; }

        /// <summary>
        /// Gets the retained certificate chain supplied to the transport.
        /// </summary>
        public CertificateCollection? Chain { get; }

        /// <summary>
        /// Gets the certificate configuration version represented by this snapshot.
        /// </summary>
        public long Version { get; }

        /// <summary>
        /// Releases the certificate and chain references owned by this snapshot.
        /// </summary>
        public void Dispose()
        {
            Certificate?.Dispose();
            Chain?.Dispose();
        }

        /// <summary>
        /// Compares certificate bytes, private-key availability, and ordered issuers, ignoring a repeated leaf.
        /// </summary>
        internal static bool HaveSameMaterial(
            Certificate? first,
            CertificateCollection? firstChain,
            Certificate? second,
            CertificateCollection? secondChain)
        {
            if (!SameCertificate(first, second) || first?.HasPrivateKey != second?.HasPrivateKey)
            {
                return false;
            }
            int firstStart = IssuerStart(first, firstChain);
            int secondStart = IssuerStart(second, secondChain);
            int count = (firstChain?.Count ?? 0) - firstStart;
            if (count != (secondChain?.Count ?? 0) - secondStart)
            {
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (!SameCertificate(firstChain![i + firstStart], secondChain![i + secondStart]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether two certificates are both absent or contain identical encoded bytes.
        /// </summary>
        private static bool SameCertificate(Certificate? first, Certificate? second)
        {
            return first is null ? second is null :
                second is not null && first.RawData.AsSpan().SequenceEqual(second.RawData);
        }

        /// <summary>
        /// Finds the first issuer in a chain that may include the application certificate at its start.
        /// </summary>
        private static int IssuerStart(Certificate? certificate, CertificateCollection? chain)
        {
            return chain is { Count: > 0 } && SameCertificate(certificate, chain[0]) ? 1 : 0;
        }
    }
}
