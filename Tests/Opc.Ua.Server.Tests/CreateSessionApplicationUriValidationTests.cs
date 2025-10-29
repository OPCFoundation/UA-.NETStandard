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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for CreateSession client certificate ApplicationUri validation.
    /// Validates that StandardServer.CreateSession correctly verifies that the ApplicationUri
    /// in the ApplicationDescription parameter matches the URIs in the client certificate.
    /// Tests include single URI, multiple URIs, matching and non-matching scenarios.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CreateSessionApplicationUriValidationTests
    {
        private const string ClientApplicationUri = "urn:localhost:opcfoundation.org:TestClient";
        private const string ClientSubjectName = "CN=TestClient, O=OPC Foundation";
        private ServerFixture<StandardServer> m_serverFixture;
        private string m_pkiRoot;

        /// <summary>
        /// Certificate types to test.
        /// Note: Currently only testing RSA certificates. ECC certificates require ECC-compatible
        /// security policies which add complexity beyond the scope of ApplicationUri validation testing.
        /// </summary>
        private static readonly NodeId[] CertificateTypes = new[]
        {
            ObjectTypeIds.RsaMinApplicationCertificateType
        };

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;

            // Start a server for testing CreateSession
            m_serverFixture = new ServerFixture<StandardServer>
            {
                AutoAccept = true,
                AllNodeManagers = true,
                SecurityNone = false // Require encryption to enable certificate validation
            };

            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            await m_serverFixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            try
            {
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Test that CreateSession succeeds when client certificate has matching ApplicationUri.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task CreateSessionWithMatchingApplicationUriSucceedsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            // Create client certificate with matching ApplicationUri
            X509Certificate2 clientCert = CreateCertificateWithMultipleUris(
                [ClientApplicationUri],
                ClientSubjectName,
                [Utils.GetHostName()],
                certificateType);

            // Attempt to create session - should succeed
            Client.ISession session = await CreateSessionWithCustomCertificateAsync(clientCert, ClientApplicationUri).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.IsTrue(session.Connected, "Session should be connected");

            // Verify session is functional by reading server state
            DataValue result = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.NotNull(result, "Should be able to read server state");
            Assert.AreEqual(StatusCodes.Good, result.StatusCode, "Read operation should succeed");

            await session.CloseAsync(5_000, true).ConfigureAwait(false);
            session.Dispose();
        }

        /// <summary>
        /// Test that CreateSession throws BadCertificateUriInvalid when client certificate ApplicationUri doesn't match.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public void CreateSessionWithMismatchedApplicationUriThrows(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            // Create client certificate with different ApplicationUri
            const string certUri = "urn:localhost:opcfoundation.org:WrongClient";
            X509Certificate2 clientCert = CreateCertificateWithMultipleUris(
                [certUri],
                ClientSubjectName,
                [Utils.GetHostName()],
                certificateType);

            // Attempt to create session - should throw BadCertificateUriInvalid
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateSessionWithCustomCertificateAsync(clientCert, ClientApplicationUri).ConfigureAwait(false));
            Assert.AreEqual((StatusCode)StatusCodes.BadCertificateUriInvalid, (StatusCode)ex.StatusCode);
        }

        /// <summary>
        /// Test that CreateSession succeeds when client certificate has multiple URIs and one matches.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public async Task CreateSessionWithMultipleUrisOneMatchesSucceedsAsync(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            // Create client certificate with multiple URIs, including the matching one
            const string uri1 = "urn:localhost:opcfoundation.org:App1";
            const string uri2 = ClientApplicationUri; // This matches
            const string uri3 = "https://localhost:8080/OpcUaApp";

            X509Certificate2 clientCert = CreateCertificateWithMultipleUris(
                [uri1, uri2, uri3],
                ClientSubjectName,
                [Utils.GetHostName()],
                certificateType);

            // Verify certificate has multiple URIs
            IReadOnlyList<string> uris = X509Utils.GetApplicationUrisFromCertificate(clientCert);
            Assert.AreEqual(3, uris.Count);
            Assert.Contains(uri1, uris.ToList());
            Assert.Contains(uri2, uris.ToList());
            Assert.Contains(uri3, uris.ToList());

            // Attempt to create session - should succeed because one URI matches
            Client.ISession session = await CreateSessionWithCustomCertificateAsync(clientCert, ClientApplicationUri).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.IsTrue(session.Connected, "Session should be connected");

            // Verify session is functional by reading server state
            DataValue result = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.NotNull(result, "Should be able to read server state");
            Assert.AreEqual(StatusCodes.Good, result.StatusCode, "Read operation should succeed");

            await session.CloseAsync(5_000, true).ConfigureAwait(false);
            session.Dispose();
        }

        /// <summary>
        /// Test that CreateSession throws BadCertificateUriInvalid when client certificate has multiple URIs but none match.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(CertificateTypes))]
        public void CreateSessionWithMultipleUrisNoneMatchThrows(NodeId certificateType)
        {
            // Skip test if certificate type is not supported on this platform
            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore($"Certificate type {certificateType} is not supported on this platform.");
            }

            // Create client certificate with multiple URIs, none matching
            const string uri1 = "urn:localhost:opcfoundation.org:App1";
            const string uri2 = "urn:localhost:opcfoundation.org:App2";
            const string uri3 = "https://localhost:8080/OpcUaApp";

            X509Certificate2 clientCert = CreateCertificateWithMultipleUris(
                [uri1, uri2, uri3],
                ClientSubjectName,
                [Utils.GetHostName()],
                certificateType);

            // Verify certificate has multiple URIs
            IReadOnlyList<string> uris = X509Utils.GetApplicationUrisFromCertificate(clientCert);
            Assert.AreEqual(3, uris.Count);
            Assert.Contains(uri1, uris.ToList());
            Assert.Contains(uri2, uris.ToList());
            Assert.Contains(uri3, uris.ToList());

            // Attempt to create session - should throw BadCertificateUriInvalid
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateSessionWithCustomCertificateAsync(clientCert, ClientApplicationUri).ConfigureAwait(false));
            Assert.AreEqual((StatusCode)StatusCodes.BadCertificateUriInvalid, (StatusCode)ex.StatusCode);
        }

        #region Helper Methods

        /// <summary>
        /// Helper method to create a session with a custom client certificate.
        /// </summary>
        private async Task<Client.ISession> CreateSessionWithCustomCertificateAsync(
            X509Certificate2 clientCertificate,
            string clientApplicationUri)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var logger = telemetry.CreateLogger<CreateSessionApplicationUriValidationTests>();

            // Create temporary PKI directory
            string clientPkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(clientPkiRoot);

            try
            {
                // Save certificate to a temporary store
                string certStorePath = Path.Combine(clientPkiRoot, "own");
                Directory.CreateDirectory(certStorePath);

                using (ICertificateStore store = CertificateStoreIdentifier.CreateStore(CertificateStoreType.Directory, telemetry))
                {
                    store.Open(certStorePath, false);
                    await store.AddAsync(clientCertificate).ConfigureAwait(false);
                }

                // Create certificate identifier pointing to the stored certificate
                // Setting the Certificate property will automatically set the CertificateType
                var certIdentifier = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = certStorePath,
                    SubjectName = clientCertificate.SubjectName.Name,
                    Thumbprint = clientCertificate.Thumbprint,
                    Certificate = clientCertificate
                };

                // Create client application configuration
                var clientApp = new ApplicationInstance(telemetry)
                {
                    ApplicationName = "TestClient",
                    ApplicationType = ApplicationType.Client
                };

                ApplicationConfiguration clientConfig = await clientApp
                    .Build(clientApplicationUri, "uri:opcfoundation.org:TestClient")
                    .AsClient()
                    .AddSecurityConfiguration([certIdentifier], clientPkiRoot)
                    .SetMinimumCertificateKeySize(256)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .CreateAsync()
                    .ConfigureAwait(false);

                // Get server endpoint with RSA-compatible security policy
                var endpoint = m_serverFixture.Server.GetEndpoints()
                    .FirstOrDefault(e => e.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                                         e.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);

                Assert.NotNull(endpoint, "No suitable endpoint found");

                var endpointConfiguration = EndpointConfiguration.Create(clientConfig);
                endpointConfiguration.OperationTimeout = 10000;
                var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

                // Create and open session with retry logic for transient errors
                var sessionFactory = new DefaultSessionFactory(telemetry);
                const int maxAttempts = 5;
                const int delayMs = 1000;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await sessionFactory.CreateAsync(
                            clientConfig,
                            configuredEndpoint,
                            false, // updateBeforeConnect
                            false, // checkDomain
                            "TestSession",
                            60000, // sessionTimeout
                            null, // userIdentity
                            null) // preferredLocales
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException e) when ((e.StatusCode is
                        StatusCodes.BadServerHalted or
                        StatusCodes.BadSecureChannelClosed or
                        StatusCodes.BadNoCommunication or
                        StatusCodes.BadNotConnected) &&
                        attempt < maxAttempts)
                    {
                        // Retry for transient connection errors (can happen on busy CI environments)
                        logger.LogWarning(e, "Failed to create session (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms... Error: {StatusCode}",
                            attempt + 1, maxAttempts, delayMs, e.StatusCode);
                        await Task.Delay(delayMs).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // Clean up temp PKI
                try
                {
                    if (Directory.Exists(clientPkiRoot))
                    {
                        Directory.Delete(clientPkiRoot, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        /// <summary>
        /// Creates a certificate with multiple application URIs in the SAN extension.
        /// </summary>
        private static X509Certificate2 CreateCertificateWithMultipleUris(
            IList<string> applicationUris,
            string subjectName,
            IList<string> domainNames,
            NodeId certificateType = null)
        {
            DateTime notBefore = DateTime.Today.AddDays(-1);
            DateTime notAfter = DateTime.Today.AddYears(1);

            // Default to RSA if not specified
            certificateType ??= ObjectTypeIds.RsaSha256ApplicationCertificateType;

            // Create the SAN extension with multiple URIs
            var subjectAltName = new X509SubjectAltNameExtension(applicationUris, domainNames);

            // Build the certificate with the custom SAN extension
            var builder = CertificateBuilder
                .Create(subjectName)
                .SetNotBefore(notBefore)
                .SetNotAfter(notAfter)
                .AddExtension(subjectAltName);

            // Create certificate based on type
            if (certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                certificateType == ObjectTypeIds.RsaMinApplicationCertificateType)
            {
                return builder.SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            }
            else if (certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccNistP384ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.nistP384).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.brainpoolP256r1).CreateForECDsa();
            }
            else if (certificateType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                return builder.SetECCurve(ECCurve.NamedCurves.brainpoolP384r1).CreateForECDsa();
            }
            else
            {
                // Default to RSA for unknown types
                return builder.SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            }
        }

        #endregion Helper Methods
    }
}
