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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Moq;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.CertificateManager;
using UaLens.Telemetry;

namespace UaLens.Tests.Administration;

/// <summary>
/// Isolated real Directory stores. Only generated public certificates are persisted.
/// The configuration's certificate manager is inert and never initializes host PKI.
/// </summary>
internal sealed class TemporaryCertificateStores : IDisposable
{
    public TemporaryCertificateStores()
    {
        Root = Directory.CreateTempSubdirectory("UaLensAdministration").FullName;
        Peer = new CertStoreNode(CertStoreRole.TrustedPeer, "Trusted Peers", Identifier("peer"));
        Issuer = new CertStoreNode(CertStoreRole.TrustedIssuer, "Trusted Issuers", Identifier("issuer"));
        Rejected = new CertStoreNode(CertStoreRole.Rejected, "Rejected", Identifier("rejected"));
        Application = new CertStoreNode(CertStoreRole.Application, "Application", Identifier("application"));
        var manager = new Mock<ICertificateManager>();
        manager.As<IAsyncDisposable>().Setup(m => m.DisposeAsync()).Returns(ValueTask.CompletedTask);
        Configuration = new ApplicationConfiguration(Telemetry)
        {
            ApplicationName = "Temporary administration fixture",
            ApplicationUri = "urn:unit:test:administration",
            CertificateManager = manager.Object,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Application.Identifier.StorePath
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Peer.Identifier.StorePath
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Issuer.Identifier.StorePath
                },
                RejectedCertificateStore = Rejected.Identifier,
                AutoAcceptUntrustedCertificates = false,
                UseValidatedCertificates = false
            }
        };
    }

    public string Root { get; }
    public AppTelemetryContext Telemetry { get; } = new(new LogRingBuffer(32));
    public ApplicationConfiguration Configuration { get; }
    public CertStoreNode Application { get; }
    public CertStoreNode Peer { get; }
    public CertStoreNode Issuer { get; }
    public CertStoreNode Rejected { get; }

    public CertificateStoreIdentifier Identifier(string name)
    {
        return new CertificateStoreIdentifier(
            Path.Combine(Root, name), CertificateStoreType.Directory, noPrivateKeys: true);
    }

    public static Certificate CreateCertificate(string subject, int validity = 0)
    {
        DateTime start = validity switch
        {
            -1 => new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            1 => new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        DateTime end = validity switch
        {
            -1 => new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            1 => new DateTime(2091, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2080, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        using Certificate generated = CertificateBuilder.Create(subject)
            .SetNotBefore(start).SetNotAfter(end).SetRSAKeySize(2048).CreateForRSA();
        return Certificate.FromRawData(generated.RawData);
    }

    public static byte[] CreateSigningRequest()
    {
        using Certificate certificate = CertificateBuilder.Create("CN=CSR fixture")
            .SetRSAKeySize(2048).CreateForRSA();
        return DefaultCertificateFactory.Instance.CreateSigningRequest(certificate, ["csr.example.test"]);
    }

    public async Task AddAsync(CertStoreNode node, Certificate certificate)
    {
        using ICertificateStore store = node.Identifier.OpenStore(Telemetry);
        try
        {
            await store.AddAsync(certificate).ConfigureAwait(false);
        }
        finally
        {
            store.Close();
        }
    }

    public async Task<IReadOnlyList<byte[]>> ReadAsync(CertStoreNode node)
    {
        using ICertificateStore store = node.Identifier.OpenStore(Telemetry);
        try
        {
            using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
            var result = new List<byte[]>(certificates.Count);
            foreach (Certificate certificate in certificates)
            {
                result.Add(certificate.RawData);
            }
            return result;
        }
        finally
        {
            store.Close();
        }
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
