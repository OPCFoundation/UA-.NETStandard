// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Covers unlimited, disabled, and bounded rejected-certificate retention in managers and store implementations.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    public sealed class RejectedCertificateLimitRegressionTests
    {
        /// <summary>
        /// Verifies the manager retains unlimited history at zero and clears or prevents history for a negative limit.
        /// </summary>
        [Test]
        public async Task ManagerPreservesUnlimitedAndDisabledLimitsWhenTrimmingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var temporary = TemporaryCertificateManager.Create(telemetry, true);
            CertificateManager manager = temporary.Update();
            manager.MaxRejectedCertificates = 0;
            for (int i = 0; i < 3; i++)
            {
                using Certificate certificate = CertificateBuilder.Create("CN=ManagerRejected" + i).CreateForRSA();
                using var chain = new CertificateCollection { certificate };
                CertificateValidationResult result = await manager.ValidateAsync(chain).ConfigureAwait(false);
                Assert.That(result.IsValid, Is.False);
            }
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using (CertificateCollection retained = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false))
            {
                Assert.That(retained, Has.Count.EqualTo(3));
            }
            manager.MaxRejectedCertificates = -1;
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using (CertificateCollection retained = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false))
            {
                Assert.That(retained, Is.Empty);
            }
            using Certificate disabled = CertificateBuilder.Create("CN=ManagerRejectedDisabled").CreateForRSA();
            using var disabledChain = new CertificateCollection { disabled };
            Assert.That((await manager.ValidateAsync(disabledChain).ConfigureAwait(false)).IsValid, Is.False);
            await manager.FlushRejectedAsync().ConfigureAwait(false);
            using CertificateCollection final = await temporary.RejectedStore.EnumerateAsync().ConfigureAwait(false);
            Assert.That(final, Is.Empty);
        }

        /// <summary>
        /// Verifies directory and shared stores interpret zero, negative, and positive rejection-history limits
        /// consistently.
        /// </summary>
        [Test]
        public async Task RejectedHistoryUsesZeroAsUnlimitedAndNegativeAsDisabledAsync(
            [Values(false, true)] bool shared,
            [Values(-1, 0, 1, 2)] int limit)
        {
            string path = Path.Combine(Path.GetTempPath(), "RejectedLimit-" + Guid.NewGuid().ToString("N"));
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var backend = new InMemorySharedKeyValueStore();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using ICertificateStore store = shared
                ? new SharedKeyValueCertificateStore(backend, null, telemetry)
                : new DirectoryCertificateStore(true, telemetry, time);
            store.Open(shared ? "kv:rejected-limit" : path, noPrivateKeys: true);
            using var certificates = new CertificateCollection();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using Certificate certificate = CertificateBuilder.Create("CN=RejectedLimit" + i).CreateForRSA();
                    certificates.Add(certificate);
                    using var chain = new CertificateCollection { certificate };
                    await store.AddRejectedAsync(chain, limit).ConfigureAwait(false);
                    time.Advance(TimeSpan.FromSeconds(1));
                }
                using CertificateCollection retained = await store.EnumerateAsync().ConfigureAwait(false);
                int expected = limit < 0 ? 0 : limit == 0 ? 3 : limit;
                Assert.That(retained, Has.Count.EqualTo(expected));
                if (limit == 0 || (!shared && limit > 0))
                {
                    Assert.That(retained.Select(certificate => certificate.Thumbprint),
                        Is.EquivalentTo(certificates.Skip(3 - expected).Select(certificate => certificate.Thumbprint)));
                }
                if (!shared)
                {
                    using var empty = new CertificateCollection();
                    await store.AddRejectedAsync(empty, -1).ConfigureAwait(false);
                    using CertificateCollection cleared = await store.EnumerateAsync().ConfigureAwait(false);
                    Assert.That(cleared, Is.Empty);
                }
            }
            finally
            {
                store.Close();
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }
}
