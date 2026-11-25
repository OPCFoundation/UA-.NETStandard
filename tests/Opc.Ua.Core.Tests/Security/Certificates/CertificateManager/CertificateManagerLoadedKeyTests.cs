// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests that the <see cref="CertificateManager"/> reuses an application
    /// private key loaded from an unchanged directory store and reads it
    /// again whenever the store may have changed.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    public sealed class CertificateManagerLoadedKeyTests
    {
        [Test]
        public async Task LoadReusesKeyFromUnchangedDirectoryStoreAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ReuseKey")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);

            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry first = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry second = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            using Certificate? checkedKey = manager.TryAcquireLoadedApplicationKey(
                id, passwordProvider: null, applicationUri: null);

            Assert.That(second.Certificate.HasPrivateKey, Is.True);
            Assert.That(second.Certificate.X509, Is.SameAs(first.Certificate.X509));
            Assert.That(checkedKey, Is.Not.Null);
            Assert.That(checkedKey!.X509, Is.SameAs(first.Certificate.X509));
        }

        [Test]
        public async Task LoadReadsKeyAgainWhenStoreChangedAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ChangedStore")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry first = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            // Any new, removed or rewritten file in the store invalidates the key.
            using Certificate other = CertificateBuilder.Create("CN=OtherInStore")
                .SetRSAKeySize(2048).CreateForRSA();
            await other.AddToStoreAsync(CertificateStoreType.Directory, id.StorePath!, null, m_telemetry)
                .ConfigureAwait(false);

            Assert.That(
                manager.TryAcquireLoadedApplicationKey(id, null, null),
                Is.Null);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry second = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            Assert.That(second.Certificate.Thumbprint, Is.EqualTo(first.Certificate.Thumbprint));
            Assert.That(second.Certificate.X509, Is.Not.SameAs(first.Certificate.X509));
        }

        [Test]
        public async Task LoadReadsRotatedKeyWithSameFileNameAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=RotatedKey")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry first = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            // Replace the store content by a new key pair with the same subject.
            Directory.Delete(id.StorePath!, recursive: true);
            using Certificate rotated = CertificateBuilder.Create("CN=RotatedKey")
                .SetRSAKeySize(2048).CreateForRSA();
            await rotated.AddToStoreAsync(CertificateStoreType.Directory, id.StorePath!, null, m_telemetry)
                .ConfigureAwait(false);

            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry second = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            Assert.That(first.Certificate.Thumbprint, Is.Not.EqualTo(rotated.Thumbprint));
            Assert.That(second.Certificate.Thumbprint, Is.EqualTo(rotated.Thumbprint));
            Assert.That(second.Certificate.HasPrivateKey, Is.True);
        }

        [Test]
        public async Task ReloadAndUpdateAlwaysReadKeysAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ExplicitReload")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            await manager.ReloadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry reloaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            await manager.UpdateAsync(config).ConfigureAwait(false);
            using CertificateEntry updated = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            Assert.That(reloaded.Certificate.X509, Is.Not.SameAs(loaded.Certificate.X509));
            Assert.That(updated.Certificate.X509, Is.Not.SameAs(reloaded.Certificate.X509));
        }

        [Test]
        public async Task UpdateApplicationCertificateForgetsLoadedKeysAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=PushedKey")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            using Certificate pushed = CertificateBuilder.Create("CN=PushedKey")
                .SetRSAKeySize(2048).CreateForRSA();

            await manager.UpdateApplicationCertificateAsync(id.CertificateType, pushed).ConfigureAwait(false);

            Assert.That(manager.TryAcquireLoadedApplicationKey(id, null, null), Is.Null);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry reloaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            Assert.That(reloaded.Certificate.X509, Is.Not.SameAs(loaded.Certificate.X509));
        }

        [Test]
        public async Task LoadedKeyExpiresAfterMaxAgeAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ExpiredKey")
                .ConfigureAwait(false);
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var manager = new CertificateManager(m_telemetry, null, 5, null, time);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            time.Advance(TimeSpan.FromSeconds(10));
            using (Certificate? young = manager.TryAcquireLoadedApplicationKey(id, null, null))
            {
                Assert.That(young, Is.Not.Null);
            }
            time.Advance(TimeSpan.FromSeconds(25));
            Assert.That(manager.TryAcquireLoadedApplicationKey(id, null, null), Is.Null);

            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry reloaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            Assert.That(reloaded.Certificate.X509, Is.Not.SameAs(loaded.Certificate.X509));
        }

        [Test]
        public async Task LoadedKeyIsBoundToLookupAndPasswordProviderAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=BoundKey")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);

            var otherSubject = new CertificateIdentifier
            {
                StoreType = id.StoreType,
                StorePath = id.StorePath,
                SubjectName = "CN=SomethingElse",
                CertificateType = id.CertificateType
            };
            Assert.That(manager.TryAcquireLoadedApplicationKey(otherSubject, null, null), Is.Null);
            Assert.That(
                manager.TryAcquireLoadedApplicationKey(id, null, "urn:other:application"),
                Is.Null);
            Assert.That(
                manager.TryAcquireLoadedApplicationKey(
                    id,
                    new CertificatePasswordProvider("secret".AsSpan()),
                    null),
                Is.Null);
        }

        [Test]
        public async Task LookupWithThumbprintMatchesKeyWithThatThumbprintAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ThumbprintLookup")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            // The certificate check fills in the thumbprint after the first load.
            id.Thumbprint = loaded.Certificate.Thumbprint.ToLowerInvariant();
            using (Certificate? sameKey = manager.TryAcquireLoadedApplicationKey(id, null, null))
            {
                Assert.That(sameKey, Is.Not.Null);
                Assert.That(sameKey!.X509, Is.SameAs(loaded.Certificate.X509));
            }
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using (CertificateEntry reloaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!)
            {
                Assert.That(reloaded.Certificate.X509, Is.SameAs(loaded.Certificate.X509));
            }

            id.Thumbprint = "0000000000000000000000000000000000000000";
            Assert.That(manager.TryAcquireLoadedApplicationKey(id, null, null), Is.Null);
        }

        [Test]
        public async Task LookupWithoutThumbprintDoesNotMatchKeyLoadedByThumbprintAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ThumbprintFirst")
                .ConfigureAwait(false);
            await using (var probe = new CertificateManager(m_telemetry))
            {
                await probe.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
                using CertificateEntry entry = probe.AcquireApplicationCertificateByType(id.CertificateType)!;
                id.Thumbprint = entry.Certificate.Thumbprint;
            }
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);

            var bySubject = new CertificateIdentifier
            {
                StoreType = id.StoreType,
                StorePath = id.StorePath,
                SubjectName = id.SubjectName,
                CertificateType = id.CertificateType
            };
            Assert.That(manager.TryAcquireLoadedApplicationKey(bySubject, null, null), Is.Null);
            using Certificate? byThumbprint = manager.TryAcquireLoadedApplicationKey(id, null, null);
            Assert.That(byThumbprint, Is.Not.Null);
        }

        [Test]
        public async Task LookupsDifferingOnlyInSeparatorPlacementDoNotShareKeyAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=Separator")
                .ConfigureAwait(false);
            string type = id.CertificateType.ToString();
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config, "urn:a|" + type + "|urn:b")
                .ConfigureAwait(false);

            // Joined with '|', subject + URI of this lookup read the same as
            // those of the load above.
            var shifted = new CertificateIdentifier
            {
                StoreType = id.StoreType,
                StorePath = id.StorePath,
                SubjectName = "CN=Separator|" + type + "|urn:a",
                CertificateType = id.CertificateType
            };
            Assert.That(manager.TryAcquireLoadedApplicationKey(shifted, null, "urn:b"), Is.Null);
            using Certificate? same = manager.TryAcquireLoadedApplicationKey(
                id, null, "urn:a|" + type + "|urn:b");
            Assert.That(same, Is.Not.Null);
        }

        [Test]
        public async Task KeyFromCustomStoreProviderIsNotReusedAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=CustomProviderKey")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(
                m_telemetry,
                [new CountingDirectoryProvider(m_telemetry)]);

            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry first = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry second = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            Assert.That(second.Certificate.X509, Is.Not.SameAs(first.Certificate.X509));
            Assert.That(manager.TryAcquireLoadedApplicationKey(id, null, null), Is.Null);
        }

        [Test]
        public async Task StoreChangeDuringLoadReloadsReusedKeysAsync()
        {
            (CertificateIdentifier rsaId, SecurityConfiguration config) = await CreateStoreAsync("CN=RaceRsa")
                .ConfigureAwait(false);
            using Certificate ecc = CertificateBuilder.Create("CN=RaceEcc")
                .SetECCurve(ECCurve.NamedCurves.nistP256).CreateForECDsa();
            await ecc.AddToStoreAsync(CertificateStoreType.Directory, rsaId.StorePath!, null, m_telemetry)
                .ConfigureAwait(false);
            var eccId = new CertificateIdentifier
            {
                SubjectName = "CN=RaceEcc",
                StoreType = CertificateStoreType.Directory,
                StorePath = rsaId.StorePath,
                CertificateType = ObjectTypeIds.EccNistP256ApplicationCertificateType
            };

            // The RSA key in place of the original, with the same file names.
            using Certificate rotated = CertificateBuilder.Create("CN=RaceRsa")
                .SetRSAKeySize(2048).CreateForRSA();
            string rotatedStore = rsaId.StorePath + "-rotated";
            lock (m_tempDirs)
            {
                m_tempDirs.Add(rotatedStore);
            }
            byte[] rotatedCert;
            byte[] rotatedKey;
            using (var store = new DirectoryCertificateStore(m_telemetry))
            {
                store.Open(rotatedStore, false);
                await store.AddAsync(rotated).ConfigureAwait(false);
                rotatedCert = File.ReadAllBytes(store.GetPublicKeyFilePath(rotated.Thumbprint)!);
                rotatedKey = File.ReadAllBytes(store.GetPrivateKeyFilePath(rotated.Thumbprint)!);
            }

            await using var manager = new CertificateManager(m_telemetry);
            var passwords = new RotatingPasswordProvider();
            config.CertificatePasswordProvider = passwords;
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry original = manager.AcquireApplicationCertificateByType(rsaId.CertificateType)!;
            string certFile;
            string keyFile;
            using (var store = new DirectoryCertificateStore(m_telemetry))
            {
                store.Open(rsaId.StorePath!, false);
                certFile = store.GetPublicKeyFilePath(original.Certificate.Thumbprint)!;
                keyFile = store.GetPrivateKeyFilePath(original.Certificate.Thumbprint)!;
            }

            // The second load reuses the RSA key; loading the ECC key rotates
            // the RSA key in the store while the load is running.
            passwords.OnEcc = () =>
            {
                File.WriteAllBytes(certFile, rotatedCert);
                File.WriteAllBytes(keyFile, rotatedKey);
            };
            config.ApplicationCertificates = [rsaId, eccId];
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);

            using CertificateEntry rsa = manager.AcquireApplicationCertificateByType(rsaId.CertificateType)!;
            using CertificateEntry loadedEcc = manager.AcquireApplicationCertificateByType(eccId.CertificateType)!;
            Assert.That(passwords.Rotated, Is.True);
            Assert.That(rsa.Certificate.Thumbprint, Is.EqualTo(rotated.Thumbprint));
            Assert.That(rsa.Certificate.HasPrivateKey, Is.True);
            Assert.That(loadedEcc.Certificate.Thumbprint, Is.EqualTo(ecc.Thumbprint));
        }


        [Test]
        public async Task KeysLoadedBeforeCertificateUpdateAreNotRememberedAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=UpdateDuringLoad")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            using Certificate pushed = CertificateBuilder.Create("CN=UpdateDuringLoad")
                .SetRSAKeySize(2048).CreateForRSA();
            var passwords = new RotatingPasswordProvider { TriggerType = id.CertificateType };
            config.CertificatePasswordProvider = passwords;

            // The certificate is replaced while the load reads the key.
            passwords.OnEcc = () =>
            {
                using var chain = new CertificateCollection();
                manager.UpdateApplicationCertificateAsync(id.CertificateType, pushed, chain)
                    .GetAwaiter().GetResult();
            };
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);

            Assert.That(passwords.Rotated, Is.True);
            Assert.That(manager.TryAcquireLoadedApplicationKey(id, passwords, null), Is.Null);
        }

        [Test]
        public async Task ConfiguredStoreLookupReusesKeyTheStoreLookupWouldLoadAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ConfiguredStore")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config, "urn:configured:store").ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;

            // Without a thumbprint the store lookup could find another
            // certificate of the subject, so the key is loaded again.
            Assert.That(
                manager.TryAcquireLoadedApplicationKeyForConfiguredStore(id, null, "urn:configured:store"),
                Is.Null);

            id.Thumbprint = loaded.Certificate.Thumbprint;
            using (Certificate? reused = manager.TryAcquireLoadedApplicationKeyForConfiguredStore(
                id, null, "urn:configured:store"))
            {
                Assert.That(reused, Is.Not.Null);
                Assert.That(reused!.X509, Is.SameAs(loaded.Certificate.X509));
            }
            using (Certificate? found = await config.FindApplicationCertificateAsync(
                SecurityPolicies.Basic256Sha256,
                privateKey: true,
                m_telemetry,
                manager,
                "urn:configured:store",
                default).ConfigureAwait(false))
            {
                Assert.That(found, Is.Not.Null);
                Assert.That(found!.X509, Is.SameAs(loaded.Certificate.X509));
            }
            using (Certificate? fromStore = await config.FindApplicationCertificateAsync(
                SecurityPolicies.Basic256Sha256,
                privateKey: true,
                m_telemetry).ConfigureAwait(false))
            {
                Assert.That(fromStore, Is.Not.Null);
                Assert.That(fromStore!.Thumbprint, Is.EqualTo(loaded.Certificate.Thumbprint));
                Assert.That(fromStore.X509, Is.Not.SameAs(loaded.Certificate.X509));
            }
        }

        [Test]
        public async Task ConfiguredStoreLookupDoesNotReuseKeyFoundByApplicationUriAsync()
        {
            // The configured subject no longer matches: the manager finds the
            // certificate through its application URI, the store lookup
            // without an application URI does not find it.
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:renamed:application",
                    "Renamed",
                    "CN=Renamed",
                    ["localhost"])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-cm-key-" + Guid.NewGuid().ToString("N")[..8]);
            lock (m_tempDirs)
            {
                m_tempDirs.Add(storePath);
            }
            await certificate.AddToStoreAsync(CertificateStoreType.Directory, storePath, null, m_telemetry)
                .ConfigureAwait(false);
            var id = new CertificateIdentifier
            {
                SubjectName = "CN=BeforeRename",
                StoreType = CertificateStoreType.Directory,
                StorePath = storePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            var config = new SecurityConfiguration { ApplicationCertificates = [id] };
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config, "urn:renamed:application").ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            Assert.That(loaded.Certificate.Thumbprint, Is.EqualTo(certificate.Thumbprint));

            id.Thumbprint = certificate.Thumbprint;
            using (Certificate? managerKey = manager.TryAcquireLoadedApplicationKey(
                id, null, "urn:renamed:application"))
            {
                Assert.That(managerKey, Is.Not.Null);
            }
            Assert.That(
                manager.TryAcquireLoadedApplicationKeyForConfiguredStore(id, null, "urn:renamed:application"),
                Is.Null);
            Assert.That(
                await config.FindApplicationCertificateAsync(
                    SecurityPolicies.Basic256Sha256,
                    privateKey: true,
                    m_telemetry,
                    manager,
                    "urn:renamed:application",
                    default).ConfigureAwait(false),
                Is.Null);
        }

        [Test]
        public async Task ConfiguredStoreLookupDoesNotReuseKeyOfCustomStoreProviderAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ConfiguredCustom")
                .ConfigureAwait(false);
            await using var manager = new CertificateManager(
                m_telemetry,
                [new CountingDirectoryProvider(m_telemetry)]);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            id.Thumbprint = loaded.Certificate.Thumbprint;

            Assert.That(manager.TryAcquireLoadedApplicationKeyForConfiguredStore(id, null, null), Is.Null);
        }

        [Test]
        public async Task ConfiguredStoreLookupResolvesTheStoreTypeLikeTheBuiltInResolutionAsync()
        {
            (CertificateIdentifier id, SecurityConfiguration config) = await CreateStoreAsync("CN=ConfiguredType")
                .ConfigureAwait(false);
            // Without a store type the path decides, for the manager and for
            // the built-in resolution: a directory path is a directory store.
            id.StoreType = null;
            await using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(config).ConfigureAwait(false);
            using CertificateEntry loaded = manager.AcquireApplicationCertificateByType(id.CertificateType)!;
            id.Thumbprint = loaded.Certificate.Thumbprint;
            using (Certificate? reused = manager.TryAcquireLoadedApplicationKeyForConfiguredStore(id, null, null))
            {
                Assert.That(reused, Is.Not.Null);
                Assert.That(reused!.X509, Is.SameAs(loaded.Certificate.X509));
            }

            // Any other store type is loaded from its store.
            id.StoreType = CertificateStoreType.X509Store;
            Assert.That(manager.TryAcquireLoadedApplicationKeyForConfiguredStore(id, null, null), Is.Null);
        }

        private async Task<(CertificateIdentifier Id, SecurityConfiguration Config)> CreateStoreAsync(string subject)
        {
            using Certificate certificate = CertificateBuilder.Create(subject)
                .SetRSAKeySize(2048).CreateForRSA();
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-cm-key-" + Guid.NewGuid().ToString("N")[..8]);
            lock (m_tempDirs)
            {
                m_tempDirs.Add(storePath);
            }
            await certificate.AddToStoreAsync(CertificateStoreType.Directory, storePath, null, m_telemetry)
                .ConfigureAwait(false);
            var id = new CertificateIdentifier
            {
                SubjectName = subject,
                StoreType = CertificateStoreType.Directory,
                StorePath = storePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            return (id, new SecurityConfiguration { ApplicationCertificates = [id] });
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            foreach (string dir in m_tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
            }
            (m_telemetry as IDisposable)?.Dispose();
        }

        /// <summary>
        /// A custom provider for the directory store type.
        /// </summary>
        private sealed class CountingDirectoryProvider(ITelemetryContext telemetry) : ICertificateStoreProvider
        {
            public string StoreTypeName => CertificateStoreType.Directory;

            public bool SupportsStorePath(string storePath)
            {
                return false;
            }

            public ICertificateStore CreateStore(ITelemetryContext context)
            {
                return new DirectoryCertificateStore(false, telemetry);
            }
        }

        /// <summary>
        /// Runs a callback once, the first time the ECC key's password is read.
        /// </summary>
        private sealed class RotatingPasswordProvider : ICertificatePasswordProvider
        {
            public Action? OnEcc { get; set; }

            public NodeId TriggerType { get; set; } = ObjectTypeIds.EccNistP256ApplicationCertificateType;

            public bool Rotated { get; private set; }

            public char[] GetPassword(CertificateIdentifier certificateIdentifier)
            {
                if (!Rotated &&
                    OnEcc != null &&
                    certificateIdentifier.CertificateType == TriggerType)
                {
                    Rotated = true;
                    OnEcc();
                }
                return [];
            }
        }


        private ITelemetryContext m_telemetry = null!;
        private readonly List<string> m_tempDirs = [];
    }
}
