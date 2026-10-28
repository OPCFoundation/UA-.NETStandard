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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Tools.Tests.McpAuthentication
{
    /// <summary>
    /// Exercises JWT tool connections over real local OPC UA channels with ephemeral credentials.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Integration")]
    public sealed class McpJwtLiveTests
    {
        /// <summary>
        /// Starts a JWT-enabled server and provisions explicit client trust.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_root = Path.Combine(Path.GetTempPath(), "mcp-jwt-live-" + Guid.NewGuid().ToString("N"));
            m_signer = RSA.Create(2048);
            var publicKey = RSA.Create();
            publicKey.ImportParameters(m_signer.ExportParameters(false));
            m_verificationKey = new IssuerVerificationKey("live", publicKey, "RS256");
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_fixture.LoadConfigurationAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration!.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.Anonymous),
                new UserTokenPolicy
                {
                    PolicyId = "jwt",
                    TokenType = UserTokenType.IssuedToken,
                    IssuedTokenType = Profiles.JwtUserToken,
                    IssuerEndpointUrl = "{\"authorityUri\":\"" + kIssuer + "\"}",
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            ];
            ReferenceServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            server.CurrentInstance!.IdentityRegistry.Register(
                new JwtAuthenticator(new LiveKeyResolver(m_verificationKey), kAudience));
            m_client = new ClientFixture(NUnitTelemetryContext.Create());
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "client"), "McpJwtLive")
                .ConfigureAwait(false);
            m_client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            using CertificateEntry serverCertificate = m_fixture.Config.CertificateManager!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256)
                ?? throw new AssertionException("The server certificate is required.");
            using var trusted = Certificate.FromRawData(serverCertificate.Certificate.RawData);
            await using (ITrustListTransaction transaction = await m_client.Config.CertificateManager!
                .BeginUpdateAsync(TrustListIdentifier.Peers).ConfigureAwait(false))
            {
                await transaction.AddTrustedCertificateAsync(trusted).ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }

            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(new OpcUaClientOptions { Configuration = m_client.Config });
            m_services = services.BuildServiceProvider();
            m_sessions = m_services.GetRequiredService<OpcUaSessionManager>();
        }

        /// <summary>
        /// Releases channels, application certificates and ephemeral signing material.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            m_sessions?.Dispose();
            if (m_services != null)
            {
                await m_services.DisposeAsync().ConfigureAwait(false);
            }

            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }

            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            m_verificationKey?.Dispose();
            m_signer?.Dispose();
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// Keeps independent invalid-token cases from accumulating the server's brute-force lockout.
        /// </summary>
        [SetUp]
        public void ClearAuthenticationLockouts()
        {
            m_fixture.Server.CurrentInstance!.SessionManager.ClearAuthenticationLockouts();
        }

        /// <summary>
        /// Server-side JWT validation succeeds through the actual tool and session-manager path.
        /// </summary>
        [TestCase("SignAndEncrypt", "Basic256Sha256")]
        [TestCase("Sign", "Basic256Sha256")]
        [TestCase("None", "None")]
        public async Task LiveJwtAuthenticatesOverEncryptedTokenPolicy(string mode, string policy)
        {
            string jwt = CreateToken(m_signer, kAudience, 3600);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string response = await ConnectionTools.ConnectJwtAsync(
                m_sessions, $"opc.tcp://localhost:{m_fixture.Port}", jwt, mode, policy, "jwt-live", timeout.Token)
                .ConfigureAwait(false);
            Assert.That(response, Does.StartWith("Connected to "), response);
            OpcUaSessionManager.SessionInfo session = m_sessions.GetSessionInfoOrThrow("jwt-live");
            Assert.That(session.AuthType, Is.EqualTo("Jwt"));
            Assert.That(session.Session.Identity.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
            Assert.That(session.Session.Identity.DisplayName, Does.Not.Contain(jwt));
            Assert.That(session.Endpoint.EndpointUrl, Does.StartWith("opc.tcp://localhost:"));
            _ = await m_sessions.DisconnectAsync("jwt-live", timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Cryptographic and claim failures are rejected by the server, not simulated by a connector.
        /// </summary>
        [TestCase("expired")]
        [TestCase("audience")]
        [TestCase("signature")]
        public async Task LiveJwtRejectsInvalidSignatureAudienceAndExpiry(string failure)
        {
            using var otherSigner = RSA.Create(2048);
            string jwt = CreateToken(
                failure == "signature" ? otherSigner : m_signer,
                failure == "audience" ? "urn:wrong-audience" : kAudience,
                failure == "expired" ? -3600 : 3600);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string response = await ConnectionTools.ConnectJwtAsync(
                m_sessions, $"opc.tcp://localhost:{m_fixture.Port}", jwt,
                "SignAndEncrypt", "Basic256Sha256", "jwt-rejected", timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadIdentityTokenRejected"));
            Assert.That(response, Does.Not.Contain(jwt));
            Assert.That(m_sessions.GetSessionInfo("jwt-rejected"), Is.Null);
        }

        /// <summary>
        /// Global automatic acceptance cannot authorize a JWT connection to an untrusted server.
        /// </summary>
        [Test]
        public async Task LiveJwtRejectsUntrustedServerDespiteGlobalAutoAcceptance()
        {
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(
                Path.Combine(m_root, "untrusted"),
                "McpJwtUntrusted").ConfigureAwait(false);
            client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(new OpcUaClientOptions { Configuration = client.Config });
            await using ServiceProvider provider = services.BuildServiceProvider();
            OpcUaSessionManager sessions = provider.GetRequiredService<OpcUaSessionManager>();
            string jwt = CreateToken(m_signer, kAudience, 3600);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string response = await ConnectionTools.ConnectJwtAsync(
                sessions, $"opc.tcp://localhost:{m_fixture.Port}", jwt,
                "SignAndEncrypt", "Basic256Sha256", "untrusted", timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadCertificateUntrusted"));
            Assert.That(response, Does.Not.Contain(jwt));
            Assert.That(sessions.GetSessionInfo("untrusted"), Is.Null);
        }

        /// <summary>
        /// A trusted server certificate for another host cannot receive the JWT.
        /// </summary>
        [Test]
        public async Task LiveJwtRejectsTrustedServerCertificateWithMismatchedHost()
        {
            string jwt = CreateToken(m_signer, kAudience, 3600);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string response = await ConnectionTools.ConnectJwtAsync(
                m_sessions, $"opc.tcp://127.0.0.2:{m_fixture.Port}", jwt,
                "SignAndEncrypt", "Basic256Sha256", "jwt-host-mismatch", timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response);

            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadCertificateHostNameInvalid"));
            Assert.That(response, Does.Not.Contain(jwt));
            Assert.That(m_sessions.GetSessionInfo("jwt-host-mismatch"), Is.Null);
        }

        /// <summary>
        /// Signs a compact JWT using only per-fixture generated key material.
        /// </summary>
        private static string CreateToken(RSA signer, string audience, int expiresInSeconds)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                                     /*lang=json,strict*/
                                     "{\"alg\":\"RS256\",\"kid\":\"live\",\"typ\":\"JWT\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string payload = JsonSerializer.Serialize(new
            {
                iss = kIssuer,
                sub = "jwt-live-user",
                aud = audience,
                exp = now + expiresInSeconds,
                nbf = now - 7200,
                iat = now - 7200
            });
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string input = header + "." + encoded;
            byte[] signature = signer.SignData(
                Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return input + "." + Convert.ToBase64String(signature)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Resolves only the fixture's ephemeral verification key without external network access.
        /// </summary>
        private sealed class LiveKeyResolver(IssuerVerificationKey key) : IIssuerKeyResolver
        {
            /// <inheritdoc/>
            public string IssuerUri => kIssuer;

            /// <inheritdoc/>
            public ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
                string? keyId, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<IIssuerVerificationKey>>([key]);
            }
        }

        private const string kIssuer = "https://jwt-live.example.test";
        private const string kAudience = "urn:mcp-jwt-live";
        private string m_root = null!;
        private RSA m_signer = null!;
        private IssuerVerificationKey m_verificationKey = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ClientFixture m_client = null!;
        private ServiceProvider m_services = null!;
        private OpcUaSessionManager m_sessions = null!;
    }
}
#endif
