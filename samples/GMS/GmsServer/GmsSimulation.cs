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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace GmsSample.Server
{
    /// <summary>
    /// Owns independent simulated providers and result stores on the one injected Machinery namespace owner.
    /// </summary>
    internal sealed class GmsSimulation : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Receives lifetime bounds, the simulation clock and source-generated telemetry through DI.
        /// </summary>
        public GmsSimulation(GmsServerOptions options, TimeProvider clock, ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry))).CreateLogger<GmsSimulation>();
        }

        /// <summary>
        /// Completes only after real OPC UA address-space initialization.
        /// </summary>
        internal Task Ready => m_ready.Task;

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_instances.Count != m_options.Instances)
            {
                throw new InvalidOperationException("Not all GMS instances completed configuration.");
            }
            m_ready.TrySetResult(true);
            return default;
        }

        /// <summary>
        /// Drains handles before releasing their borrowed, application-owned providers and stores.
        /// </summary>
        /// <exception cref="AggregateException">One or more instance cleanups failed.</exception>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            var failures = new List<Exception>();
            foreach (GmsInstance instance in m_instances)
            {
                try
                {
                    await instance.Handle.DisposeAsync().ConfigureAwait(false);
                    ArrayOf<string> results = await instance.Store.ListIdsAsync(256).ConfigureAwait(false);
                    Console.WriteLine($"RELEASED {instance.Id} storedResults={results.Count} capacity=256");
                    for (int index = 0; index < results.Count; index++)
                    {
                        await instance.Store.RemoveAsync(results[index]).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
            int count = m_instances.Count;
            m_instances.Clear();
            if (failures.Count != 0)
            {
                throw new AggregateException("GMS sample cleanup failed.", failures);
            }
            m_logger.InstancesReleased(count);
        }

        /// <summary>
        /// Builds actual generated roots with explicitly selected capabilities and isolated result ownership.
        /// </summary>
        internal async ValueTask ConfigureAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            for (int index = 1; index <= m_options.Instances; index++)
            {
                string id = FormattableString.Invariant($"Gms{index}");
                DateTimeUtc acquired = AcquiredAt();
                var provider = new InMemoryGmsProvider(
                    GmsSimulationData.Snapshot(id, m_options.Advanced, 0, acquired));
                var store = new InMemoryGmsResultStore(
                    context.Context.AsMessageContext(), GmsSimulationData.ResultLimits);
                GmsBuilder builder = context.AddGms(id)
                    .WithProvider(provider)
                    .WithIdentification(identity =>
                    {
                        identity.Manufacturer = new LocalizedText("OPC Foundation simulation");
                        identity.SerialNumber = id;
                        identity.ProductInstanceUri = "urn:opcfoundation.org:gms:simulation:" + id;
                        identity.Model = new LocalizedText("SIMULATION ONLY; no physical equipment");
                    })
                    .WithResults(store, new GmsResultBindingOptions
                    {
                        Limits = GmsSimulationData.ResultLimits,
                        PublishedResults = 2,
                        EnableFileTransfer = m_options.Advanced,
                        IncludeUsedTools = m_options.Advanced,
                        EnableIntermediateEvents = m_options.Advanced,
                        Corrections = m_options.Advanced ? [GmsSimulationData.Correction(id, 0, acquired)] : default
                    });
                if (m_options.Advanced)
                {
                    builder.WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                        .WithOptions(new GmsSystemOptions
                        {
                            Features = GmsFeatures.Identification | GmsFeatures.Sensors | GmsFeatures.Loading |
                                GmsFeatures.ToolMonitoring | GmsFeatures.Accessories | GmsFeatures.AdditionalSensors |
                                GmsFeatures.Calibration | GmsFeatures.Production | GmsFeatures.SensorWarnings,
                            MaximumSensors = 1,
                            MaximumEquipment = 2,
                            MaximumSensorWarnings = 1,
                            MaximumCalibrationHistory = 2,
                            MaximumArrayLength = 8,
                            MaximumTextLength = 256
                        });
                }
                GmsHandle handle = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
                var instance = new GmsInstance(id, handle, provider, store);
                m_instances.Add(instance);
                await PublishAsync(instance, 0, acquired, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(
                    $"INSTANCE {id} root={handle.NodeId} advanced={m_options.Advanced} " +
                    $"selectedCUs={handle.ConformanceUnits.Count} store=independent provider=independent");
                foreach (string limitation in handle.Capabilities.Limitations)
                {
                    Console.WriteLine($"CONFORMANCE_EXCEPTION {id}: {limitation}");
                }
            }
            Console.WriteLine("Released model 1.0.0/reference 1.0.1; no complete GMS Production facet claim.");
            Console.WriteLine(
                "BOUNDS perInstance: results=256 storedBytes=8388608 metadataBytes=131072 fileBytes=32768 " +
                "metadataHandles=1 fileHandles=1 handleTimeoutSeconds=180");
        }

        /// <summary>
        /// Updates simulated observations and publishes real result/data/event flows for a finite running window.
        /// </summary>
        internal async Task RunAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource window =
                TimeProviderExtensions.CreateCancellationTokenSource(m_clock, TimeSpan.FromSeconds(m_options.Seconds));
            using var running = CancellationTokenSource.CreateLinkedTokenSource(window.Token, cancellationToken);
            try
            {
                for (int tick = 1; tick <= m_options.Seconds; tick++)
                {
                    await TimeProviderExtensions.Delay(m_clock, TimeSpan.FromSeconds(1), running.Token)
                        .ConfigureAwait(false);
                    foreach (GmsInstance instance in m_instances)
                    {
                        DateTimeUtc acquired = AcquiredAt();
                        await instance.Provider.SetSnapshotAsync(
                            GmsSimulationData.Snapshot(instance.Id, m_options.Advanced, tick, acquired), running.Token)
                            .ConfigureAwait(false);
                        await instance.Handle.RefreshAsync(running.Token).ConfigureAwait(false);
                        await PublishAsync(instance, tick, acquired, running.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                // Normal finite-window expiry; external cancellation still propagates through host cleanup.
            }
        }

        /// <summary>
        /// Uses only the existing per-instance result publisher, never another service or file-transfer engine.
        /// </summary>
        private async ValueTask PublishAsync(
            GmsInstance instance, int tick, DateTimeUtc acquired, CancellationToken cancellationToken)
        {
            if (m_options.Advanced)
            {
                await instance.Handle.Results.PublishAsync(
                    GmsSimulationData.Result(instance.Id, true, tick, acquired, intermediate: true), cancellationToken)
                    .ConfigureAwait(false);
                await instance.Handle.Results.PublishCorrectionAsync(
                    GmsSimulationData.Correction(instance.Id, tick, acquired), cancellationToken).ConfigureAwait(false);
            }
            await instance.Handle.Results.PublishAsync(
                GmsSimulationData.Result(instance.Id, m_options.Advanced, tick, acquired, intermediate: false),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stamps simulation inputs separately from eventual server and client receipt.
        /// </summary>
        private DateTimeUtc AcquiredAt() => new(m_clock.GetUtcNow().UtcDateTime);

        /// <summary>
        /// Couples only one native handle to each pair of application-owned providers.
        /// </summary>
        private sealed record GmsInstance(
            string Id, GmsHandle Handle, InMemoryGmsProvider Provider, InMemoryGmsResultStore Store);

        /// <summary>The explicit simulation and lifetime bounds.</summary>
        private readonly GmsServerOptions m_options;

        /// <summary>The injected input and delay clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The injected lifecycle logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The separately owned instance resources.</summary>
        private readonly List<GmsInstance> m_instances = [];

        /// <summary>The actual OPC UA readiness signal.</summary>
        private readonly TaskCompletionSource<bool> m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Prevents repeated application/host disposal from replaying cleanup.</summary>
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated simulation lifetime diagnostics.
    /// </summary>
    internal static partial class GmsSimulationLog
    {
        /// <summary>
        /// Reports completed cleanup, not just a shutdown request.
        /// </summary>
        [LoggerMessage(EventId = GmsServerEventIds.GmsSimulation, Level = LogLevel.Information,
            Message = "Released {Count} GMS handles, emptied stores and released their provider references.")]
        public static partial void InstancesReleased(this ILogger logger, int count);
    }
}
