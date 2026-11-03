/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OnboardingDevice;
using OnboardingRegistrar;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies the connected device's restricted public journal adapter without opening network listeners.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceSecurityCheckpointStoreTests
    {
        /// <summary>
        /// Selects one isolated directory for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            string temporary = Path.GetTempPath();
            m_directory = Path.Combine(
                temporary, nameof(DeviceSecurityCheckpointStoreTests), Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Removes only the current test's directory after all writer leases have been disposed.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// Both exact production identifiers survive restart and each read owns a detached buffer.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExactDeviceCheckpointSurvivesRestartWithDetachedReads(bool defaults)
        {
            byte[] value = [1, 2, 3];
            SecretIdentifier id;
            using (var files = new PushRegistrarCheckpointFiles(m_directory))
            {
                var store = new DeviceSecurityCheckpointStore(files, kScope);
                id = Identifier(store, defaults);
                await store.SetAsync(id, value).ConfigureAwait(false);
                Assert.That(store.TryGet(id), Is.Null);
            }
            using var restarted = new PushRegistrarCheckpointFiles(m_directory);
            var recovered = new DeviceSecurityCheckpointStore(restarted, kScope);
            using (ISecret? first = await recovered.GetAsync(id).ConfigureAwait(false))
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(first!.Bytes.ToArray(), Is.EqualTo(value));
            }
            using (ISecret? second = await recovered.GetAsync(id).ConfigureAwait(false))
            {
                Assert.That(second, Is.Not.Null);
                Assert.That(second!.Bytes.ToArray(), Is.EqualTo(value));
            }
            Assert.That(await recovered.RemoveAsync(id).ConfigureAwait(false), Is.True);
            using ISecret? removed = await recovered.GetAsync(id).ConfigureAwait(false);
            Assert.That(removed, Is.Null);
            Assert.That(await recovered.RemoveAsync(id).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// A device journal alone cannot replace missing original trust; defaults are required explicitly.
        /// </summary>
        [Test]
        public async Task OriginalTrustDefaultsMustExistIndependentlyOfSecurityJournal()
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var store = new DeviceSecurityCheckpointStore(files, kScope);
            await store.SetAsync(Identifier(store, defaults: false), new byte[] { 1 }).ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.RequireDefaultsAsync(CancellationToken.None).ConfigureAwait(false));

            SecretIdentifier defaults = Identifier(store, defaults: true);
            await store.SetAsync(defaults, new byte[] { 2 }).ConfigureAwait(false);
            await store.RequireDefaultsAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(await store.RemoveAsync(defaults).ConfigureAwait(false), Is.True);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.RequireDefaultsAsync(CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// Every adapter operation rejects another scope, store, path or producer before accessing storage.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task AllOperationsRejectIdentifiersOutsideExactDeviceScope(int scenario)
        {
            using var files = new PushRegistrarCheckpointFiles(m_directory);
            var store = new DeviceSecurityCheckpointStore(files, kScope);
            string name = Identifier(store, defaults: false).Name;
            var id = scenario switch
            {
                0 => new SecretIdentifier("DeviceSecurity-invalid", store.StoreType),
                1 => new SecretIdentifier(name, "AnotherStore"),
                2 => new SecretIdentifier(name, store.StoreType, "another-path"),
                3 => new SecretIdentifier("PushEnrollment-" + Digest(kScope), store.StoreType),
                4 => new SecretIdentifier("DeviceSecurity-" + Digest(kScope + ":other"), store.StoreType),
                _ => new SecretIdentifier("ProvisionableDeviceDefaults-" + Digest(kScope + ":other"), store.StoreType)
            };

            Assert.Throws<ArgumentException>(() => store.TryGet(id));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.GetAsync(id).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SetAsync(id, new byte[] { 1 }).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.RemoveAsync(id).ConfigureAwait(false));
            Assert.That(Directory.GetFiles(m_directory, "*.bin"), Is.Empty);
        }

        /// <summary>
        /// Reproduces the exact identifier derivation used by the production device security producers.
        /// </summary>
        private static SecretIdentifier Identifier(DeviceSecurityCheckpointStore store, bool defaults)
        {
            string prefix = defaults ? "ProvisionableDeviceDefaults-" : "DeviceSecurity-";
            return new SecretIdentifier(prefix + Digest(kScope), store.StoreType);
        }

        /// <summary>
        /// Computes the production scope digest rather than a path or prefix-based approximation.
        /// </summary>
        private static string Digest(string scope)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
        }

        /// <summary>The application URI used as the durable device-security scope.</summary>
        private const string kScope = "urn:opcfoundation.org:onboarding:checkpoint-test";

        /// <summary>The exact directory owned by the current test.</summary>
        private string m_directory = null!;
    }
}
#endif
