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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Exercises actual local PKI tools with a supplied configuration and isolated in-memory store seams.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF46PkiAuthorizationTests
    {
        /// <summary>
        /// Host enablement, an exact current caller grant and authentication are all required before store access.
        /// </summary>
        [TestCase(false, true, "owner")]
        [TestCase(true, false, "owner")]
        [TestCase(true, true, "other")]
        [TestCase(true, true, "unauthenticated")]
        [Category("SecurityRegression")]
        public async Task SecurityF46PkiMutationRequiresCallerCapability(bool enabled, bool granted, string identity)
        {
            await using var fixture = new PkiContext(enabled, granted);
            McpCallerIdentity? selected = identity switch
            {
                "owner" => PkiContext.Owner,
                "other" => PkiContext.Other,
                _ => null
            };
            using IDisposable caller = fixture.Callers.EnterRequest(selected);
            string trust = await PkiTools.TrustCertificateAsync(fixture.Manager, fixture.Certificate.Thumbprint)
                .ConfigureAwait(false);
            AssertDenied(trust);
            foreach (string store in new[] { "Trusted", "Peer", "Issuer", "Rejected" })
            {
                AssertDenied(await PkiTools.RemoveCertificateAsync(
                    fixture.Manager, fixture.Certificate.Thumbprint, store).ConfigureAwait(false));
            }
            Assert.That(fixture.Manager.Configuration, Is.Null, "Policy refusal precedes configuration loading.");
            foreach (StoreState store in fixture.Stores)
            {
                Assert.That(store.Opens, Is.Zero);
                Assert.That(store.Adds, Is.Zero);
                Assert.That(store.Deletes, Is.Zero);
            }
            Assert.That(fixture.Rejected.Certificates.Keys, Is.EquivalentTo([fixture.Certificate.Thumbprint]));
            Assert.That(fixture.Trusted.Certificates, Is.Empty);
            Assert.That(fixture.Issuer.Certificates, Is.Empty);
        }

        /// <summary>
        /// A granted administrator promotes exactly the selected certificate, then can explicitly untrust it.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF46GrantedCallerMovesExactRejectedCertificateAndCanUntrustPeer()
        {
            await using var fixture = new PkiContext(enabled: true, granted: true);
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(PkiContext.Owner);
            string trust = await PkiTools.TrustCertificateAsync(fixture.Manager, fixture.Certificate.Thumbprint)
                .ConfigureAwait(false);
            using (var document = JsonDocument.Parse(trust))
            {
                Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.True);
                Assert.That(document.RootElement.GetProperty("certificate").GetProperty("thumbprint").GetString(),
                    Is.EqualTo(fixture.Certificate.Thumbprint));
            }
            Assert.That(fixture.Rejected.Certificates, Is.Empty);
            Assert.That(fixture.Trusted.Certificates.Keys, Is.EquivalentTo([fixture.Certificate.Thumbprint]));
            Assert.That(fixture.Trusted.Adds, Is.EqualTo(1));
            Assert.That(fixture.Rejected.Deletes, Is.EqualTo(1));
            string removed = await PkiTools.RemoveCertificateAsync(
                fixture.Manager, fixture.Certificate.Thumbprint, "Peer").ConfigureAwait(false);
            using var removal = JsonDocument.Parse(removed);
            Assert.That(removal.RootElement.GetProperty("success").GetBoolean(), Is.True);
            Assert.That(fixture.Trusted.Certificates, Is.Empty);
            Assert.That(fixture.Trusted.Deletes, Is.EqualTo(1));
            Assert.That(fixture.Issuer.Opens, Is.Zero);
            Assert.That(fixture.Trusted.Disposals, Is.EqualTo(fixture.Trusted.Opens));
            Assert.That(fixture.Rejected.Disposals, Is.EqualTo(fixture.Rejected.Opens));
        }

        /// <summary>
        /// Issuer deletion uses the same authority boundary; an authorized missing selection does not add trust.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF46IssuerAndMissingThumbprintRemainExplicitAuthorizedOperations()
        {
            await using var fixture = new PkiContext(enabled: true, granted: true);
            fixture.Issuer.Certificates.Add(fixture.Certificate.Thumbprint, fixture.Certificate.AddRef());
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(PkiContext.Owner);
            string issuer = await PkiTools.RemoveCertificateAsync(
                fixture.Manager, fixture.Certificate.Thumbprint, "Issuer").ConfigureAwait(false);
            using (var result = JsonDocument.Parse(issuer))
            {
                Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
            }
            string missing = await PkiTools.TrustCertificateAsync(fixture.Manager, "missing-test-thumbprint")
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(missing);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("message").GetString(), Does.Contain("not found"));
            Assert.That(fixture.Issuer.Certificates, Is.Empty);
            Assert.That(fixture.Issuer.Deletes, Is.EqualTo(1));
            Assert.That(fixture.Rejected.Deletes, Is.Zero);
            Assert.That(fixture.Rejected.Certificates.Keys, Is.EquivalentTo([fixture.Certificate.Thumbprint]));
            Assert.That(fixture.Trusted.Opens, Is.Zero);
            Assert.That(fixture.Trusted.Adds, Is.Zero);
        }

        /// <summary>
        /// Checks a concrete authorization denial rather than interpreting a missing certificate as protection.
        /// </summary>
        private static void AssertDenied(string json)
        {
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("BadUserAccessDenied"));
        }

        /// <summary>
        /// Supplies application configuration without loading, creating or writing host PKI.
        /// </summary>
        private sealed class PkiContext : IAsyncDisposable
        {
            /// <summary>
            /// Creates store spies and independent host/caller authorization settings.
            /// </summary>
            internal PkiContext(bool enabled, bool granted)
            {
                Certificate = CertificateBuilder.Create("CN=SecurityF46 Test Only").SetRSAKeySize(2048).CreateForRSA();
                Rejected.Certificates.Add(Certificate.Thumbprint, Certificate.AddRef());
                var rejected = new Mock<CertificateStoreIdentifier>(MockBehavior.Strict);
                rejected.Setup(store => store.OpenStore(It.IsAny<ITelemetryContext>()))
                    .Returns(() => Rejected.Open());
                var trusted = new Mock<CertificateTrustList>(MockBehavior.Strict);
                trusted.Setup(store => store.OpenStore(It.IsAny<ITelemetryContext>()))
                    .Returns(() => Trusted.Open());
                var issuer = new Mock<CertificateTrustList>(MockBehavior.Strict);
                issuer.Setup(store => store.OpenStore(It.IsAny<ITelemetryContext>()))
                    .Returns(() => Issuer.Open());
                var configuration = new ApplicationConfiguration
                {
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        TrustedPeerCertificates = trusted.Object,
                        TrustedIssuerCertificates = issuer.Object,
                        RejectedCertificateStore = rejected.Object
                    }
                };
                var services = new ServiceCollection();
                services.AddSingleton(Callers);
                services.AddOpcUaMcpCore(new OpcUaMcpOptions
                {
                    Execution = new McpExecutionOptions
                    {
                        EnabledCapabilities = enabled ? [McpCapability.CertificateAdministration] : [],
                        CallerGrants =
                        [
                            new McpCallerGrant
                            {
                                Caller = Owner,
                                Capabilities = granted ? [McpCapability.CertificateAdministration] : []
                            }
                        ]
                    }
                });
                services.AddSingleton(new OpcUaClientOptions { Configuration = configuration });
                Services = services.BuildServiceProvider();
                Manager = Services.GetRequiredService<OpcUaSessionManager>();
            }

            /// <summary>
            /// Gets the explicitly granted operator.
            /// </summary>
            internal static McpCallerIdentity Owner { get; } = new("pki-owner", "urn:security-test");

            /// <summary>
            /// Gets an independently authenticated unprivileged caller.
            /// </summary>
            internal static McpCallerIdentity Other { get; } = new("pki-other", "urn:security-test");

            /// <summary>
            /// Gets the trusted caller scope provider.
            /// </summary>
            internal McpCallerContext Callers { get; } = new();

            /// <summary>
            /// Gets the test-owned certificate.
            /// </summary>
            internal Certificate Certificate { get; }

            /// <summary>
            /// Gets the isolated rejected store.
            /// </summary>
            internal StoreState Rejected { get; } = new();

            /// <summary>
            /// Gets the isolated trusted-peer store.
            /// </summary>
            internal StoreState Trusted { get; } = new();

            /// <summary>
            /// Gets the isolated issuer store.
            /// </summary>
            internal StoreState Issuer { get; } = new();

            /// <summary>
            /// Gets all store boundaries for zero-I/O assertions.
            /// </summary>
            internal StoreState[] Stores => [Rejected, Trusted, Issuer];

            /// <summary>
            /// Gets the DI lifetime.
            /// </summary>
            internal ServiceProvider Services { get; }

            /// <summary>
            /// Gets the actual tool session/configuration manager.
            /// </summary>
            internal OpcUaSessionManager Manager { get; }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Services.DisposeAsync().ConfigureAwait(false);
                foreach (StoreState store in Stores)
                {
                    store.Dispose();
                }
                Certificate.Dispose();
            }
        }

        /// <summary>
        /// An owning in-memory certificate store boundary whose mutations and handle lifetimes are observable.
        /// </summary>
        private sealed class StoreState : IDisposable
        {
            /// <summary>
            /// Gets the exact stored certificates.
            /// </summary>
            internal Dictionary<string, Certificate> Certificates { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// Gets the number of opened stores.
            /// </summary>
            internal int Opens { get; private set; }

            /// <summary>
            /// Gets the number of additions.
            /// </summary>
            internal int Adds { get; private set; }

            /// <summary>
            /// Gets the number of deletion attempts.
            /// </summary>
            internal int Deletes { get; private set; }

            /// <summary>
            /// Gets the number of disposed store handles.
            /// </summary>
            internal int Disposals { get; private set; }

            /// <summary>
            /// Opens one disposable service boundary, never a filesystem or operating-system trust store.
            /// </summary>
            internal ICertificateStore Open()
            {
                Opens++;
                var store = new Mock<ICertificateStore>(MockBehavior.Strict);
                store.Setup(value => value.Dispose()).Callback(() => Disposals++);
                store.Setup(value => value.FindByThumbprintAsync(
                        It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string thumbprint, CancellationToken _) =>
                        Task.FromResult<CertificateCollection>(Certificates.TryGetValue(
                            thumbprint, out Certificate? certificate)
                            ? [certificate] : []));
                store.Setup(value => value.AddAsync(
                        It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                    .Returns((Certificate certificate, char[]? _, CancellationToken _) =>
                    {
                        Adds++;
                        Certificates.Add(certificate.Thumbprint, certificate.AddRef());
                        return Task.CompletedTask;
                    });
                store.Setup(value => value.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string thumbprint, CancellationToken _) =>
                    {
                        Deletes++;
                        bool removed = Certificates.Remove(thumbprint, out Certificate? certificate);
                        certificate?.Dispose();
                        return Task.FromResult(removed);
                    });
                return store.Object;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                foreach (Certificate certificate in Certificates.Values)
                {
                    certificate.Dispose();
                }
                Certificates.Clear();
            }
        }
    }
}
#endif
