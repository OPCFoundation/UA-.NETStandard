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
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies restart and publication behavior of the device's discovery-candidate store without physical files.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class FileSystemProvisionableDeviceRegistrarStoreTests
    {
        /// <summary>
        /// Missing paths and telemetry are rejected before accessing storage or changing DI registrations.
        /// </summary>
        [Test]
        public void ConstructorAndRegistrationRejectMissingConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.That(() => new FileSystemProvisionableDeviceRegistrarStore(string.Empty, telemetry),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("path"));
            Assert.That(() => new FileSystemProvisionableDeviceRegistrarStore(kPath, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
            var services = new ServiceCollection();
            Assert.That(() => services.AddFileSystemProvisionableDeviceRegistrarStore(" "),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("path"));
            Assert.That(services, Is.Empty);
        }

        /// <summary>
        /// Restart preserves complete application descriptions and callers cannot mutate the persisted snapshot.
        /// </summary>
        [Test]
        public async Task RestartPreservesDetachedRegistrarDescriptionsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var files = new VirtualFileSystem();
            ApplicationDescription candidate = Registrar();
            using (var first = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files))
            {
                Assert.That((await first.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
                await first.ReplaceAsync([candidate]).ConfigureAwait(false);
                candidate.ApplicationUri = "urn:modified-input";
                candidate.DiscoveryUrls = ["opc.tcp://modified-input:4840"];
            }
            using var restarted = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files);
            ArrayOf<ApplicationDescription> restored = await restarted.ReadAsync().ConfigureAwait(false);
            Assert.That(restored.Count, Is.EqualTo(1));
            Assert.That(restored[0].ApplicationUri, Is.EqualTo("urn:registrar:one"));
            Assert.That(restored[0].ProductUri, Is.EqualTo("urn:registrar:product"));
            Assert.That(restored[0].ApplicationName.Text, Is.EqualTo("Registrar"));
            Assert.That(restored[0].ApplicationName.Locale, Is.EqualTo("en"));
            Assert.That(restored[0].ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(restored[0].GatewayServerUri, Is.EqualTo("urn:gateway"));
            Assert.That(restored[0].DiscoveryProfileUri, Is.EqualTo("urn:discovery"));
            string[] expectedUrls = ["opc.tcp://registrar:4840", "opc.tcp://registrar:4841"];
            Assert.That(restored[0].DiscoveryUrls.ToArray(), Is.EqualTo(expectedUrls));
            restored[0].ApplicationUri = "urn:modified-read";
            Assert.That((await restarted.ReadAsync().ConfigureAwait(false))[0].ApplicationUri,
                Is.EqualTo("urn:registrar:one"));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// An explicit empty replacement persists across restart instead of resurrecting previous candidates.
        /// </summary>
        [Test]
        public async Task EmptyReplacementSurvivesRestartAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var files = new VirtualFileSystem();
            using (var first = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files))
            {
                await first.ReplaceAsync([Registrar()]).ConfigureAwait(false);
                await first.ReplaceAsync([]).ConfigureAwait(false);
            }
            using var restarted = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files);
            Assert.That((await restarted.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// Corrupt, incompatible, invalid, oversized, and appended data cannot silently become an empty configuration.
        /// </summary>
        [TestCase("truncated")]
        [TestCase("version")]
        [TestCase("trailing")]
        [TestCase("invalid-candidate")]
        [TestCase("oversized")]
        public async Task CorruptCheckpointRejectsReadsAndReplacementWithoutOverwritingAsync(string corruption)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] checkpoint;
            using (var buffer = new MemoryStream())
            {
                using (var encoder = new BinaryEncoder(buffer, ServiceMessageContext.Create(telemetry), true))
                {
                    encoder.WriteUInt32(null, corruption == "version" ? 2u : 1u);
                    ApplicationDescription candidate = Registrar();
                    if (corruption == "invalid-candidate")
                    {
                        candidate.DiscoveryUrls = [];
                    }
                    encoder.WriteEncodeableArray<ApplicationDescription>(null, [candidate]);
                }
                checkpoint = buffer.ToArray();
            }
            checkpoint = corruption switch
            {
                "truncated" => checkpoint.Take(5).ToArray(),
                "trailing" => checkpoint.Concat(new byte[] { 1 }).ToArray(),
                "oversized" => new byte[16 * 1024 * 1024 + 1],
                _ => checkpoint
            };
            using var files = new VirtualFileSystem();
            files.Add(kPath, checkpoint);
            using var store = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ReadAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ReplaceAsync([]).ConfigureAwait(false));

            Assert.That(files.Get(kPath), Is.EqualTo(checkpoint));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// Failed publication preserves previous state and removes the uncommitted staging file.
        /// </summary>
        [Test]
        public async Task ReplaceFailurePreservesPreviousCheckpointAndCleansStagingAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var files = new VirtualFileSystem();
            using (var initial = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files))
            {
                await initial.ReplaceAsync([Registrar()]).ConfigureAwait(false);
            }
            byte[] before = files.Get(kPath).ToArray();
            Mock<IFileSystem> provider = Forward(files);
            var failure = new IOException("Publication unavailable.");
            provider.Setup(f => f.Replace(It.IsAny<string>(), kPath)).Throws(failure);
            using var store = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, provider.Object);

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await store.ReplaceAsync([]).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(files.Get(kPath), Is.EqualTo(before));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
            Assert.That((await store.ReadAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A staging-cleanup failure reports both errors and does not replace the original checkpoint.
        /// </summary>
        [Test]
        public async Task PublicationAndCleanupFailuresAreBothReportedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var files = new VirtualFileSystem();
            using (var initial = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, files))
            {
                await initial.ReplaceAsync([Registrar()]).ConfigureAwait(false);
            }
            byte[] before = files.Get(kPath).ToArray();
            Mock<IFileSystem> provider = Forward(files);
            var publication = new IOException("Publication unavailable.");
            var cleanup = new IOException("Staging deletion unavailable.");
            provider.Setup(f => f.Replace(It.IsAny<string>(), kPath)).Throws(publication);
            provider.Setup(f => f.Delete(It.IsAny<string>(), false)).Throws(cleanup);
            using var store = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, provider.Object);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await store.ReplaceAsync([]).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Is.EqualTo(new Exception[] { publication, cleanup }));
            Assert.That(files.Get(kPath), Is.EqualTo(before));
            Assert.That(files.Files.Count(), Is.EqualTo(2));
            string staging = files.Files.Single(path => path != kPath);
            Assert.That(staging, Does.EndWith(".pending"));
        }

        /// <summary>
        /// Cancellation before publication preserves previous candidates; cancellation after publication is success.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationHonorsThePublicationBoundaryAsync(bool cancelAfterPublication)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var files = new VirtualFileSystem();
            using var cancellation = new CancellationTokenSource();
            Mock<IFileSystem> provider = Forward(files);
            provider.Setup(f => f.Replace(It.IsAny<string>(), kPath))
                .Callback((string source, string destination) =>
                {
                    files.Replace(source, destination);
                    cancellation.Cancel();
                });
            using var store = new FileSystemProvisionableDeviceRegistrarStore(kPath, telemetry, provider.Object);
            if (cancelAfterPublication)
            {
                await store.ReplaceAsync([Registrar()], cancellation.Token).ConfigureAwait(false);
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That((await store.ReadAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
            }
            else
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await store.ReplaceAsync([Registrar()], cancellation.Token).ConfigureAwait(false));
                await Assert.CatchAsync<OperationCanceledException>(async () =>
                    await store.ReadAsync(cancellation.Token).ConfigureAwait(false));
                Assert.That(files.Files, Is.Empty);
                provider.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// Invalid runtime inputs are rejected before creating any checkpoint or staging files.
        /// </summary>
        [Test]
        public async Task InvalidCandidateCannotReplaceExistingStateAsync()
        {
            using var files = new VirtualFileSystem();
            using var store = new FileSystemProvisionableDeviceRegistrarStore(
                kPath, NUnitTelemetryContext.Create(), files);
            ApplicationDescription invalid = Registrar();
            invalid.ApplicationType = ApplicationType.Client;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.ReplaceAsync([invalid]).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(files.Files, Is.Empty);
        }

        /// <summary>
        /// Fluent registration shares one container-owned concrete instance and rejects competing providers.
        /// </summary>
        [Test]
        public async Task FluentRegistrationUsesInjectedFilesAndRejectsDuplicateSelectionAsync()
        {
            using var files = new VirtualFileSystem();
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton<IFileSystem>(files);
            Assert.That(services.AddFileSystemProvisionableDeviceRegistrarStore(kPath), Is.SameAs(services));
            ServiceDescriptor[] before = services.ToArray();
            Assert.That(() => services.AddFileSystemProvisionableDeviceRegistrarStore(kPath),
                Throws.InvalidOperationException);
            Assert.That(services.ToArray(), Is.EqualTo(before));
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                IProvisionableDeviceRegistrarStore contract =
                    provider.GetRequiredService<IProvisionableDeviceRegistrarStore>();
                Assert.That(contract,
                    Is.SameAs(provider.GetRequiredService<FileSystemProvisionableDeviceRegistrarStore>()));
                await contract.ReplaceAsync([Registrar()]).ConfigureAwait(false);
                Assert.That((await contract.ReadAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            }
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
            using var restarted = new FileSystemProvisionableDeviceRegistrarStore(
                kPath, NUnitTelemetryContext.Create(), files);
            Assert.That((await restarted.ReadAsync().ConfigureAwait(false))[0].ApplicationUri,
                Is.EqualTo("urn:registrar:one"));
        }

        /// <summary>
        /// Choosing the filesystem adapter cannot silently replace a previously registered custom candidate store.
        /// </summary>
        [Test]
        public void FluentRegistrationPreservesCustomProviderOnConflict()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IProvisionableDeviceRegistrarStore>());
            ServiceDescriptor[] before = services.ToArray();

            Assert.That(() => services.AddFileSystemProvisionableDeviceRegistrarStore(kPath),
                Throws.InvalidOperationException);

            Assert.That(services.ToArray(), Is.EqualTo(before));
        }

        /// <summary>
        /// Disposed instances reject operations without accessing their borrowed file system.
        /// </summary>
        [Test]
        public async Task DisposedStoreRejectsReadAndReplaceAsync()
        {
            var files = new Mock<IFileSystem>(MockBehavior.Strict);
            using var store = new FileSystemProvisionableDeviceRegistrarStore(
                kPath, NUnitTelemetryContext.Create(), files.Object);
            store.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.ReadAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.ReplaceAsync([]).ConfigureAwait(false));

            files.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Forwards file operations while leaving publication injectable for fault and cancellation tests.
        /// </summary>
        private static Mock<IFileSystem> Forward(VirtualFileSystem files)
        {
            var provider = new Mock<IFileSystem>(MockBehavior.Strict);
            provider.Setup(f => f.Exists(It.IsAny<string>(), false))
                .Returns((string path, bool _) => files.Exists(path));
            provider.Setup(f => f.OpenRead(kPath)).Returns(() => files.OpenRead(kPath));
            provider.Setup(f => f.OpenWrite(It.IsAny<string>())).Returns((string path) => files.OpenWrite(path));
            provider.Setup(f => f.Delete(It.IsAny<string>(), false))
                .Callback((string path, bool _) => files.Delete(path));
            return provider;
        }

        /// <summary>
        /// Supplies a complete registrar application description with two discovery candidates.
        /// </summary>
        private static ApplicationDescription Registrar()
        {
            return new ApplicationDescription
            {
                ApplicationUri = "urn:registrar:one",
                ProductUri = "urn:registrar:product",
                ApplicationName = new LocalizedText("en", "Registrar"),
                ApplicationType = ApplicationType.Server,
                GatewayServerUri = "urn:gateway",
                DiscoveryProfileUri = "urn:discovery",
                DiscoveryUrls = ["opc.tcp://registrar:4840", "opc.tcp://registrar:4841"]
            };
        }

        /// <summary>The virtual device candidate checkpoint.</summary>
        private const string kPath = "device-registrars.bin";
    }
}
