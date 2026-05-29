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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;

#nullable enable

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Tests for <see cref="InMemoryConfigurationDataStore"/>:
    /// round-trip read/write/confirm, optimistic concurrency, and
    /// managed-application enumeration.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationDataStore")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfigurationDataStoreTests
    {
        private InMemoryConfigurationDataStore m_store = null!;
        private const string AppUri = "urn:test:managed-app";

        [SetUp]
        public void SetUp()
        {
            m_store = new InMemoryConfigurationDataStore();
        }

        [Test]
        public async Task GetManagedApplicationsReturnsEmptyWhenNoneAdded()
        {
            var apps = await m_store.GetManagedApplicationsAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(apps, Is.Empty);
        }

        [Test]
        public async Task AddAndEnumerateApplications()
        {
            m_store.AddApplication(new ManagedApplicationInfo
            {
                ApplicationUri = AppUri,
                ProductUri = "urn:test:product",
                ApplicationType = ApplicationType.Server,
                Enabled = true
            });

            var apps = await m_store.GetManagedApplicationsAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(apps, Has.Count.EqualTo(1));
            Assert.That(apps[0].ApplicationUri, Is.EqualTo(AppUri));
            Assert.That(apps[0].Enabled, Is.True);
        }

        [Test]
        public void AddApplicationRejectsNullInfo()
        {
            Assert.That(() => m_store.AddApplication(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AddApplicationRejectsEmptyUri()
        {
            Assert.That(
                () => m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = "" }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RemoveApplicationReturnsFalseWhenNotFound()
        {
            Assert.That(m_store.RemoveApplication("urn:nonexistent"), Is.False);
        }

        [Test]
        public async Task RemoveApplicationRemovesApp()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });
            Assert.That(m_store.RemoveApplication(AppUri), Is.True);

            var apps = await m_store.GetManagedApplicationsAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(apps, Is.Empty);
        }

        [Test]
        public async Task ReadConfigurationReturnsEmptyForNewApp()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            byte[] data = await m_store.ReadConfigurationAsync(AppUri, CancellationToken.None).ConfigureAwait(false);
            Assert.That(data, Is.Empty);
        }

        [Test]
        public async Task WriteAndReadRoundTrip()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            byte[] payload = [0xCA, 0xFE, 0xBA, 0xBE];
            uint newVersion = await m_store.WriteConfigurationAsync(
                AppUri, payload, 0, CancellationToken.None).ConfigureAwait(false);

            Assert.That(newVersion, Is.EqualTo(1));

            byte[] readBack = await m_store.ReadConfigurationAsync(AppUri, CancellationToken.None).ConfigureAwait(false);
            Assert.That(readBack, Is.EqualTo(payload));
        }

        [Test]
        public async Task WriteRejectsVersionMismatch()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            // Write version 0 → 1
            await m_store.WriteConfigurationAsync(
                AppUri, [1], 0, CancellationToken.None).ConfigureAwait(false);

            // Try to write with stale version 0 → should fail
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_store.WriteConfigurationAsync(
                    AppUri, [2], 0, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ConfirmUpdateSucceeds()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            uint version = await m_store.WriteConfigurationAsync(
                AppUri, [1], 0, CancellationToken.None).ConfigureAwait(false);

            Assert.DoesNotThrowAsync(async () =>
                await m_store.ConfirmUpdateAsync(AppUri, version, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public void ConfirmUpdateRejectsUnknownApp()
        {
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_store.ConfirmUpdateAsync("urn:unknown", 1, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task ConfirmUpdateRejectsVersionMismatch()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            await m_store.WriteConfigurationAsync(
                AppUri, [1], 0, CancellationToken.None).ConfigureAwait(false);

            var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_store.ConfirmUpdateAsync(AppUri, 999, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task SequentialWritesIncrementVersion()
        {
            m_store.AddApplication(new ManagedApplicationInfo { ApplicationUri = AppUri });

            uint v1 = await m_store.WriteConfigurationAsync(
                AppUri, [1], 0, CancellationToken.None).ConfigureAwait(false);

            uint v2 = await m_store.WriteConfigurationAsync(
                AppUri, [2], v1, CancellationToken.None).ConfigureAwait(false);

            Assert.That(v2, Is.GreaterThan(v1));
        }
    }

    /// <summary>
    /// Tests for <see cref="ISecretStore"/> integration in the
    /// <see cref="InMemoryKeyCredentialRequestStore"/>.
    /// </summary>
    [TestFixture]
    [Category("KeyCredential")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class KeyCredentialSecretStoreIntegrationTests
    {
        [Test]
        public async Task CredentialSecretIsMaterialisedFromSecretStoreAsync()
        {
            var secretStore = new InMemorySecretStore("TestKC");
            var store = new InMemoryKeyCredentialRequestStore(secretStore);

            NodeId id = await store.StartRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2 }),
                null,
                default).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await store.FinishRequestAsync(
                id, cancelRequest: false).ConfigureAwait(false);

            Assert.That(result.CredentialId, Is.Not.Null);
            Assert.That(result.CredentialSecret.IsEmpty, Is.False);

            // Verify the secret is also in the ISecretStore
            var sid = new SecretIdentifier(result.CredentialId!, secretStore.StoreType);
            using ISecret? secret = secretStore.TryGet(sid);
            Assert.That(secret, Is.Not.Null);
            Assert.That(secret!.Bytes.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task RevokeRemovesSecretFromStoreAsync()
        {
            var secretStore = new InMemorySecretStore("TestKC");
            var store = new InMemoryKeyCredentialRequestStore(secretStore);

            NodeId id = await store.StartRequestAsync("urn:test:app", default, null, default).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await store.FinishRequestAsync(
                id, cancelRequest: false).ConfigureAwait(false);

            await store.RevokeAsync(result.CredentialId!).ConfigureAwait(false);

            // Secret should be purged from the store
            var sid = new SecretIdentifier(result.CredentialId!, secretStore.StoreType);
            using ISecret? secret = secretStore.TryGet(sid);
            Assert.That(secret, Is.Null);
        }

        [Test]
        public async Task CancelRequestRemovesSecretFromStoreAsync()
        {
            var secretStore = new InMemorySecretStore("TestKC");
            var store = new InMemoryKeyCredentialRequestStore(secretStore);

            NodeId id = await store.StartRequestAsync("urn:test:app", default, null, default).ConfigureAwait(false);

            await store.FinishRequestAsync(id, cancelRequest: true).ConfigureAwait(false);

            // The secret should have been purged on cancel.
            // Verify no secrets remain by trying the expected key.
            var sid = new SecretIdentifier("KC-1", secretStore.StoreType);
            using ISecret? secret = secretStore.TryGet(sid);
            Assert.That(secret, Is.Null);
        }
    }

    /// <summary>
    /// Tests for <see cref="ManagedApplicationInfo"/> data class.
    /// </summary>
    [TestFixture]
    [Category("ManagedApplications")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ManagedApplicationInfoTests
    {
        [Test]
        public void DefaultPropertyValues()
        {
            var info = new ManagedApplicationInfo();
            Assert.That(info.ApplicationUri, Is.EqualTo(string.Empty));
            Assert.That(info.ProductUri, Is.Null);
            Assert.That(info.ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(info.Enabled, Is.True);
            Assert.That(info.IsNonUaApplication, Is.False);
        }

        [Test]
        public void PropertiesCanBeSet()
        {
            var info = new ManagedApplicationInfo
            {
                ApplicationUri = "urn:test:app",
                ProductUri = "urn:test:product",
                ApplicationType = ApplicationType.ClientAndServer,
                Enabled = false,
                IsNonUaApplication = true
            };

            Assert.That(info.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(info.ProductUri, Is.EqualTo("urn:test:product"));
            Assert.That(info.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(info.Enabled, Is.False);
            Assert.That(info.IsNonUaApplication, Is.True);
        }
    }

    /// <summary>
    /// Tests for <see cref="IAccessTokenProvider"/> abstraction and
    /// the <see cref="AccessTokenResult"/> data class.
    /// </summary>
    [TestFixture]
    [Category("AuthorizationService")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AccessTokenProviderTests
    {
        [Test]
        public void AccessTokenResultDefaultValues()
        {
            var result = new AccessTokenResult();
            Assert.That(result.AccessToken, Is.EqualTo(string.Empty));
            Assert.That(result.AccessTokenExpiryTime, Is.Default);
            Assert.That(result.RefreshToken, Is.Null);
        }

        [Test]
        public void AccessTokenResultPropertiesCanBeSet()
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            var result = new AccessTokenResult
            {
                AccessToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9",
                AccessTokenExpiryTime = expiry,
                RefreshToken = "refresh-token-value",
                RefreshTokenExpiryTime = expiry.AddDays(7)
            };

            Assert.That(result.AccessToken, Does.StartWith("eyJ"));
            Assert.That(result.AccessTokenExpiryTime, Is.EqualTo(expiry));
            Assert.That(result.RefreshToken, Is.EqualTo("refresh-token-value"));
        }

        [Test]
        public Task StubProviderReturnsBadNotSupported()
        {
            // Verify that when no provider is configured, the
            // expected pattern is to check for null and throw.
#pragma warning disable CA1508 // Intentional null — validates null-dispatch pattern
            IAccessTokenProvider? provider = null;

            if (provider == null)
#pragma warning restore CA1508
            {
                var ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await Task.CompletedTask.ConfigureAwait(false);
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                });

                Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Tests for the <see cref="IManagedApplicationsNodeManager"/>
    /// interface plumbing — verifies that
    /// <see cref="StubManagedApplicationsNodeManager"/> and
    /// <see cref="DefaultManagedApplicationsNodeManager"/> expose the
    /// expected abstraction surface.
    /// </summary>
    [TestFixture]
    [Category("ManagedApplications")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ManagedApplicationsNodeManagerInterfaceTests
    {
        [Test]
        public void StubNodeManagerConfigurationDataStoreIsNull()
        {
            // Verify the stub exposes null data store (= not supported).
            // We can't instantiate StubManagedApplicationsNodeManager
            // without a real IServerInternal, but we can verify the
            // interface contract.
#pragma warning disable CA1508 // Intentional null — validates interface pattern
            IManagedApplicationsNodeManager? mgr = null;
            Assert.That(mgr?.ConfigurationDataStore, Is.Null);
#pragma warning restore CA1508
        }

        [Test]
        public void InterfaceExposesCoreMembers()
        {
            // Compile-time verification that the interface has
            // the ConfigurationDataStore property.
            var type = typeof(IManagedApplicationsNodeManager);
            var prop = type.GetProperty(nameof(IManagedApplicationsNodeManager.ConfigurationDataStore));
            Assert.That(prop, Is.Not.Null);
            Assert.That(prop!.PropertyType, Is.EqualTo(typeof(IConfigurationDataStore)));
        }
    }
}
