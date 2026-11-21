/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Gds.Server.Onboarding;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises durable registrar acceptance and its injected binary codec under NativeAOT.
    /// </summary>
    public sealed class OnboardingPushPersistenceAotTests
    {
        /// <summary>
        /// Reopens an interrupted record through a fresh injected store before completing it.
        /// </summary>
        [Test]
        public async Task AcceptedPushRetainsIdentityAndAttemptStateAcrossStoreRecreationAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            const string path = "onboarding-push-work.bin";
            Guid id = Guid.NewGuid();
            var application = new ApplicationDescription
            {
                ApplicationUri = "urn:aot:onboarding:device",
                ProductUri = "urn:aot:onboarding:product",
                ApplicationType = ApplicationType.Server,
                ApplicationName = new LocalizedText("en-US", "AOT device"),
                DiscoveryUrls = ["opc.tcp://localhost:4840"]
            };
            var services = new ServiceCollection();
            services.AddSingleton(telemetry);
            services.AddFileSystemDeviceRegistrarPushWorkStore(path, fileSystem);
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                IDeviceRegistrarPushWorkStore store = provider.GetRequiredService<IDeviceRegistrarPushWorkStore>();
                await Assert.That(store).IsSameReferenceAs(
                    provider.GetRequiredService<FileSystemDeviceRegistrarPushWorkStore>());
                await store.EnqueueAsync(id, application, 1).ConfigureAwait(false);
                application.ApplicationUri = "urn:caller:changed";
                await Assert.That(await store.BeginAttemptAsync(id).ConfigureAwait(false))
                    .IsEqualTo(DeviceRegistrarPushWorkState.Pending);
            }

            using (var reopened = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem))
            {
                ArrayOf<DeviceRegistrarPushWorkItem> items = await reopened.ReadAsync().ConfigureAwait(false);
                await Assert.That(items.Count).IsEqualTo(1);
                await Assert.That(items[0].Id).IsEqualTo(id);
                await Assert.That(items[0].State).IsEqualTo(DeviceRegistrarPushWorkState.Processing);
                await Assert.That(items[0].Application.ApplicationUri).IsEqualTo("urn:aot:onboarding:device");
                await Assert.That(items[0].Application.DiscoveryUrls[0]).IsEqualTo("opc.tcp://localhost:4840");
                await reopened.MarkInterruptedAsync(id).ConfigureAwait(false);
            }

            using (var recovered = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem))
            {
                await Assert.That(await recovered.BeginAttemptAsync(id).ConfigureAwait(false))
                    .IsEqualTo(DeviceRegistrarPushWorkState.Interrupted);
                await recovered.CompleteAsync(id).ConfigureAwait(false);
            }
            using var completed = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem);
            await Assert.That((await completed.ReadAsync().ConfigureAwait(false)).Count).IsEqualTo(0);
        }

        /// <summary>
        /// Rejects unsupported durable data rather than treating it as an empty queue.
        /// </summary>
        [Test]
        public async Task UnsupportedPushCheckpointIsRejectedWithoutReplacingItsBytesAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            const string path = "unsupported-push-work.bin";
            fileSystem.Add(path, [2, 0, 0, 0]);
            using var store = new FileSystemDeviceRegistrarPushWorkStore(
                path, DefaultTelemetry.Create(static _ => { }), fileSystem);
            await Assert.That(async () => await store.ReadAsync().ConfigureAwait(false))
                .Throws<InvalidDataException>();
            await Assert.That(fileSystem.Get(path)).IsEquivalentTo(new byte[] { 2, 0, 0, 0 });
        }

        /// <summary>
        /// Runs the durable worker against a recreated store and reconciles before repeating an interrupted push.
        /// </summary>
        [Test]
        public async Task RecreatedSchedulerReconcilesInterruptedPushBeforeProcessingAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            const string path = "interrupted-push-work.bin";
            Guid id = Guid.NewGuid();
            var application = new ApplicationDescription
            {
                ApplicationUri = "urn:aot:onboarding:interrupted",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840"]
            };
            using (var original = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem))
            {
                await original.EnqueueAsync(id, application, 1).ConfigureAwait(false);
                await original.BeginAttemptAsync(id).ConfigureAwait(false);
            }

            var processor = new RecoveryProcessor();
            using var reopened = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem);
            using var scheduler = new DeviceRegistrarPushScheduler(processor, telemetry, 1, reopened);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            await Assert.That(processor.Events.Count).IsEqualTo(2);
            await Assert.That(processor.Events[0]).IsEqualTo("reconcile:urn:aot:onboarding:interrupted");
            await Assert.That(processor.Events[1]).IsEqualTo("process:urn:aot:onboarding:interrupted");
            await Assert.That((await reopened.ReadAsync().ConfigureAwait(false)).Count).IsEqualTo(0);
        }

        /// <summary>
        /// Records the worker's recovery order without a transport, reflection, or a dynamic mock.
        /// </summary>
        private sealed class RecoveryProcessor : IDeviceRegistrarPushProcessor, IDeviceRegistrarPushRecoveryProcessor
        {
            /// <summary>
            /// Gets the ordered recovery and processing events.
            /// </summary>
            public List<string> Events { get; } = [];

            /// <summary>
            /// Gets the signal that processing was invoked.
            /// </summary>
            public TaskCompletionSource<bool> Processed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public ValueTask ReconcileInterruptedAsync(
                ApplicationDescription application, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Events.Add("reconcile:" + application.ApplicationUri);
                return default;
            }

            /// <inheritdoc/>
            public ValueTask ProcessAsync(
                ApplicationDescription application, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Events.Add("process:" + application.ApplicationUri);
                Processed.TrySetResult(true);
                return default;
            }
        }
    }
}
