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

#if NET10_0
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PkiToolsTests
    {
        private const string kMissingThumbprint =
            "0123456789ABCDEF0123456789ABCDEF01234567";

        [Test]
        public async Task GetPkiStorePathsAsyncReturnsConfiguredStoresAsync()
        {
            string json = await PkiTools.GetPkiStorePathsAsync(
                McpTestEnvironment.SessionManager).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(
                GetRequiredProperty(root, "trustedPeerStore").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                GetRequiredProperty(root, "trustedIssuerStore").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                GetRequiredProperty(root, "rejectedStore").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(
                GetRequiredProperty(root, "applicationCertificates").ValueKind,
                Is.EqualTo(JsonValueKind.Array));
        }

        [TestCase("Trusted")]
        [TestCase("Peer")]
        [TestCase("Issuer")]
        [TestCase("Rejected")]
        [TestCase("Own")]
        [TestCase("Application")]
        public async Task ListCertificatesAsyncSupportsStoreAliasesAsync(string store)
        {
            string json = await PkiTools.ListCertificatesAsync(
                McpTestEnvironment.SessionManager,
                store).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement certificates = GetRequiredProperty(root, "certificates");

            Assert.That(root.TryGetProperty("error", out _), Is.False);
            Assert.That(GetRequiredProperty(root, "store").GetString(), Is.EqualTo(store));
            Assert.That(
                GetRequiredProperty(root, "storePath").GetString(),
                Is.Not.Null.And.Not.Empty);
            Assert.That(certificates.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(
                certificates.GetArrayLength(),
                Is.EqualTo(GetRequiredProperty(root, "count").GetInt32()));
        }

        [Test]
        public void ListCertificatesAsyncRejectsUnknownStore()
        {
            Assert.That(
                () => PkiTools.ListCertificatesAsync(
                    McpTestEnvironment.SessionManager,
                    "unknown"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task MissingCertificateOperationsReturnExpectedResultsAsync()
        {
            string trustJson = await PkiTools.TrustCertificateAsync(
                McpTestEnvironment.SessionManager,
                kMissingThumbprint).ConfigureAwait(false);
            string removeJson = await PkiTools.RemoveCertificateAsync(
                McpTestEnvironment.SessionManager,
                kMissingThumbprint).ConfigureAwait(false);

            using JsonDocument trustDocument = JsonDocument.Parse(trustJson);
            using JsonDocument removeDocument = JsonDocument.Parse(removeJson);

            Assert.That(
                GetRequiredProperty(trustDocument.RootElement, "error").GetBoolean(),
                Is.True);
            Assert.That(
                GetRequiredProperty(trustDocument.RootElement, "message").GetString(),
                Does.Contain("not found in Rejected store"));
            Assert.That(
                GetRequiredProperty(removeDocument.RootElement, "success").GetBoolean(),
                Is.False);
        }

        [Test]
        public async Task TrustCertificateAsyncMovesCertificateBetweenStoresAsync()
        {
            ApplicationConfiguration configuration = await McpTestEnvironment.SessionManager
                .EnsureConfigurationAsync()
                .ConfigureAwait(false);
            SecurityConfiguration security = configuration.SecurityConfiguration;
            CertificateStoreIdentifier rejected = security.RejectedCertificateStore!;
            CertificateStoreIdentifier trusted = security.TrustedPeerCertificates!;
            using Certificate certificate = CertificateBuilder
                .Create("CN=McpRejected-" + Guid.NewGuid().ToString("N"))
                .CreateForRSA();

            try
            {
                using (ICertificateStore store =
                    rejected.OpenStore(McpTestEnvironment.SessionManager.Telemetry))
                {
                    await store.AddAsync(certificate, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                }

                string trustJson = await PkiTools.TrustCertificateAsync(
                    McpTestEnvironment.SessionManager,
                    certificate.Thumbprint).ConfigureAwait(false);

                using JsonDocument trustDocument = JsonDocument.Parse(trustJson);
                JsonElement trustRoot = trustDocument.RootElement;

                Assert.That(
                    GetRequiredProperty(trustRoot, "success").GetBoolean(),
                    Is.True);
                Assert.That(
                    GetRequiredProperty(trustRoot, "certificate")
                        .GetProperty("thumbprint")
                        .GetString(),
                    Is.EqualTo(certificate.Thumbprint));

                string removeJson = await PkiTools.RemoveCertificateAsync(
                    McpTestEnvironment.SessionManager,
                    certificate.Thumbprint).ConfigureAwait(false);

                using JsonDocument removeDocument = JsonDocument.Parse(removeJson);
                Assert.That(
                    GetRequiredProperty(removeDocument.RootElement, "success")
                        .GetBoolean(),
                    Is.True);
            }
            finally
            {
                await DeleteIfPresentAsync(rejected, certificate.Thumbprint)
                    .ConfigureAwait(false);
                await DeleteIfPresentAsync(trusted, certificate.Thumbprint)
                    .ConfigureAwait(false);
            }
        }

        private static async Task DeleteIfPresentAsync(
            CertificateStoreIdentifier identifier,
            string thumbprint)
        {
            using ICertificateStore store = identifier.OpenStore(
                McpTestEnvironment.SessionManager.Telemetry);
            _ = await store.DeleteAsync(thumbprint, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static JsonElement GetRequiredProperty(
            JsonElement element,
            string propertyName)
        {
            Assert.That(
                element.TryGetProperty(propertyName, out JsonElement property),
                Is.True);
            return property;
        }
    }
}
#endif
