// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Pins the contract of <see cref="TestCertificateCache"/>: an identical
    /// identity is seeded from the cache and visible to the certificate
    /// manager, anything else creates its own key pairs.
    /// </summary>
    [TestFixture]
    [Category("TestFramework")]
    [Parallelizable(ParallelScope.All)]
    public sealed class TestCertificateCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            if (!TestCertificateCache.Enabled)
            {
                Assert.Ignore("The certificate cache is disabled by OPCUA_TEST_FRESH_CERTIFICATES.");
            }
        }

        [Test]
        public async Task SameIdentityIsSeededAndVisibleToTheCertificateManagerAsync()
        {
            string name = NewName();
            string first = await CheckAsync(name, Uri(name), reuse: true).ConfigureAwait(false);

            using var application = await TestApplication.CreateAsync(name, Uri(name)).ConfigureAwait(false);
            Assert.That(await application.CheckAsync(reuse: true).ConfigureAwait(false), Is.True);

            Assert.That(application.StoredThumbprints(), Is.EqualTo(new[] { first }));
            Assert.That(application.ManagerThumbprint(), Is.EqualTo(first));
        }

        [Test]
        public async Task ChangedIdentityCreatesItsOwnKeysAsync()
        {
            string name = NewName();
            string first = await CheckAsync(name, Uri(name), reuse: true).ConfigureAwait(false);

            string other = await CheckAsync(name, Uri(name) + ":other", reuse: true).ConfigureAwait(false);

            Assert.That(other, Is.Not.EqualTo(first));
        }

        [Test]
        public async Task OptOutCreatesItsOwnKeysAsync()
        {
            string name = NewName();
            string first = await CheckAsync(name, Uri(name), reuse: true).ConfigureAwait(false);

            string optedOut = await CheckAsync(name, Uri(name), reuse: false).ConfigureAwait(false);

            Assert.That(optedOut, Is.Not.EqualTo(first));
        }

        [Test]
        public async Task ExistingCertificatesArePreservedAsync()
        {
            string name = NewName();
            string cached = await CheckAsync(name, Uri(name), reuse: true).ConfigureAwait(false);

            string pkiRoot = NewPkiRoot();
            try
            {
                string existing;
                using (TestApplication own = await TestApplication.CreateAsync(name, Uri(name), pkiRoot).ConfigureAwait(false))
                {
                    Assert.That(await own.CheckAsync(reuse: false).ConfigureAwait(false), Is.True);
                    existing = own.StoredThumbprints().Single();
                }

                using TestApplication reused = await TestApplication.CreateAsync(name, Uri(name), pkiRoot).ConfigureAwait(false);
                Assert.That(await reused.CheckAsync(reuse: true).ConfigureAwait(false), Is.True);

                Assert.That(existing, Is.Not.EqualTo(cached));
                Assert.That(reused.StoredThumbprints(), Is.EqualTo(new[] { existing }));
            }
            finally
            {
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public async Task ParallelFirstUseCreatesTheKeysOnceAsync()
        {
            string name = NewName();

            string[] thumbprints = await Task.WhenAll(
                Enumerable.Range(0, 4).Select(_ => CheckAsync(name, Uri(name), reuse: true))).ConfigureAwait(false);

            Assert.That(thumbprints.Distinct().Count(), Is.EqualTo(1));
        }

#if NET8_0_OR_GREATER
        [Test]
        public async Task SeededPrivateKeyIsReadableOnlyByTheOwnerAsync()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes only.");
                return;
            }

            string name = NewName();
            await CheckAsync(name, Uri(name), reuse: true).ConfigureAwait(false);

            using var application = await TestApplication.CreateAsync(name, Uri(name)).ConfigureAwait(false);
            Assert.That(await application.CheckAsync(reuse: true).ConfigureAwait(false), Is.True);

            string privateDirectory = Path.Combine(application.StorePath, "private");
            Assert.That(
                File.GetUnixFileMode(privateDirectory),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            foreach (string key in Directory.GetFiles(privateDirectory))
            {
                Assert.That(File.GetUnixFileMode(key), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }
#endif

        /// <summary>
        /// Checks a new application on a new PKI folder and returns the
        /// thumbprint of its certificate.
        /// </summary>
        private static async Task<string> CheckAsync(string name, string applicationUri, bool reuse)
        {
            using TestApplication application = await TestApplication.CreateAsync(name, applicationUri).ConfigureAwait(false);
            Assert.That(await application.CheckAsync(reuse).ConfigureAwait(false), Is.True);
            return application.StoredThumbprints().Single();
        }

        private static string NewName()
        {
            return "CacheTest" + Guid.NewGuid().ToString("N")[..12];
        }

        private static string Uri(string name)
        {
            return "urn:localhost:opcfoundation.org:" + name;
        }

        private static string NewPkiRoot()
        {
            return Path.Combine(Path.GetTempPath(), "OPC", "cache-tests", Path.GetRandomFileName());
        }

        private static void DeletePkiRoot(string pkiRoot)
        {
            try
            {
                Directory.Delete(pkiRoot, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// A client application with one RSA application certificate.
        /// </summary>
        private sealed class TestApplication : IDisposable
        {
            private TestApplication(
                ApplicationInstance application,
                ApplicationConfiguration configuration,
                ITelemetryContext telemetry,
                string pkiRoot,
                bool ownsPkiRoot)
            {
                m_application = application;
                m_configuration = configuration;
                m_telemetry = telemetry;
                m_pkiRoot = pkiRoot;
                m_ownsPkiRoot = ownsPkiRoot;
            }

            public string StorePath => Path.GetFullPath(
                m_configuration.SecurityConfiguration.ApplicationCertificates[0].StorePath!);

            public static async Task<TestApplication> CreateAsync(
                string name,
                string applicationUri,
                string? pkiRoot = null)
            {
                bool ownsPkiRoot = pkiRoot == null;
                pkiRoot ??= NewPkiRoot();
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = name,
                    ApplicationType = ApplicationType.Client
                };
                ArrayOf<CertificateIdentifier> certificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        "CN=" + name + ", O=OPC Foundation, DC=localhost",
                        CertificateStoreType.Directory,
                        pkiRoot);
                ApplicationConfiguration configuration = await application
                    .Build(applicationUri, applicationUri + ":product")
                    .AsClient()
                    .AddSecurityConfiguration([certificates[0]], pkiRoot)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .CreateAsync()
                    .ConfigureAwait(false);
                return new TestApplication(application, configuration, telemetry, pkiRoot, ownsPkiRoot);
            }

            public Task<bool> CheckAsync(bool reuse)
            {
                return TestCertificateCache.CheckApplicationInstanceCertificatesAsync(
                    m_application,
                    null,
                    reuse,
                    m_telemetry);
            }

            public List<string> StoredThumbprints()
            {
                return [.. Directory.GetFiles(Path.Combine(StorePath, "certs"), "*.der")
                    .Select(path =>
                    {
                        using var certificate = Certificate.FromRawData(File.ReadAllBytes(path));
                        return certificate.Thumbprint;
                    })];
            }

            public string? ManagerThumbprint()
            {
                using CertificateEntry? entry = ((CertificateManager)m_configuration.CertificateManager!)
                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256);
                return entry?.Certificate?.Thumbprint;
            }

            public void Dispose()
            {
                m_application.DisposeAsync().AsTask().GetAwaiter().GetResult();
                (m_configuration.CertificateManager as IDisposable)?.Dispose();
                if (m_ownsPkiRoot)
                {
                    DeletePkiRoot(m_pkiRoot);
                }
            }

            private readonly ApplicationInstance m_application;
            private readonly ApplicationConfiguration m_configuration;
            private readonly ITelemetryContext m_telemetry;
            private readonly string m_pkiRoot;
            private readonly bool m_ownsPkiRoot;
        }
    }
}
