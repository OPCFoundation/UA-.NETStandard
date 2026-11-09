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
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace CranesHoistsSample.Server
{
    /// <summary>
    /// Owns the sample's providers and awaited acquisition loop, borrowing the single host's address space.
    /// </summary>
    internal sealed class CraneDemo : IAsyncDisposable, IServerStartupTask
    {
        /// <summary>
        /// Receives the clock, authorization policy and telemetry through dependency injection.
        /// </summary>
        public CraneDemo(
            CraneServerOptions options,
            TimeProvider clock,
            ICraneWriteAuthorizer authorizer,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
            m_authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry))).CreateLogger<CraneDemo>();
        }

        /// <summary>
        /// Completes only after the hosted OPC UA server has finished initializing its address space.
        /// </summary>
        internal Task Ready => m_ready.Task;

        /// <summary>
        /// Drains each crane before disposing its borrowed provider; repeated host disposal is harmless.
        /// </summary>
        /// <exception cref="AggregateException">One or more crane handles could not be drained.</exception>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            var failures = new List<Exception>();
            foreach (CraneInstance instance in m_instances)
            {
                try
                {
                    Console.WriteLine(
                        $"REQUESTS {instance.Handle.Id} accepted={instance.Provider.AcceptedRequests.Count}");
                    if (instance.Handle.IsDisposed)
                    {
                        await instance.Handle.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await instance.Handle.RemoveAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                finally
                {
                    await instance.Provider.DisposeAsync().ConfigureAwait(false);
                }
            }
            int count = m_instances.Count;
            m_instances.Clear();
            if (failures.Count != 0)
            {
                throw new AggregateException("Crane cleanup failed.", failures);
            }
            m_logger.CraneResourcesReleased(count);
        }

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
                throw new InvalidOperationException("The configured crane instances did not finish building.");
            }
            m_ready.TrySetResult(true);
            return default;
        }

        /// <summary>
        /// Builds each native system once; optional capabilities are never selected implicitly.
        /// </summary>
        internal async ValueTask ConfigureAsync(CranesHoistsBuildContext context, CancellationToken cancellationToken)
        {
            m_context = context.Robotics.Context;
            for (int i = 1; i <= m_options.Instances; i++)
            {
                string id = FormattableString.Invariant($"Crane{i}");
                SimulatedCraneProvider? provider = new(
                    CraneAcquisition.Create(id, m_options.Advanced, 0, AcquiredAt()),
                    acceptLimitRequests: true, maximumSamples: 32, maximumRequests: 1);
                try
                {
                    CraneSystemHandle handle = await Define(context, id, provider)
                        .BuildAsync(cancellationToken).ConfigureAwait(false);
                    m_instances.Add(new CraneInstance(handle, provider));
                    provider = null;
                    Console.WriteLine(
                        $"INSTANCE {id} root={handle.NodeId} eventNotifier={handle.EventNotifierId} " +
                        $"selectedCUs={handle.ConformanceUnits.Count}");
                }
                finally
                {
                    if (provider != null)
                    {
                        await provider.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Acquires explicit inputs and publishes standard diagnostic events until the bounded window ends.
        /// </summary>
        internal async Task RunAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource window =
                TimeProviderExtensions.CreateCancellationTokenSource(m_clock, TimeSpan.FromSeconds(m_options.Seconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(window.Token, cancellationToken);
            try
            {
                for (int tick = 1; tick <= m_options.Seconds; tick++)
                {
                    await TimeProviderExtensions.Delay(m_clock, TimeSpan.FromSeconds(1), linked.Token)
                        .ConfigureAwait(false);
                    foreach (CraneInstance instance in m_instances)
                    {
                        DateTimeUtc acquiredAt = AcquiredAt();
                        await instance.Provider.SetSamplesAsync(
                            CraneAcquisition.Create(instance.Handle.Id, m_options.Advanced, tick, acquiredAt),
                            linked.Token).ConfigureAwait(false);
                        await instance.Handle.RefreshAsync(linked.Token).ConfigureAwait(false);
                        if (m_options.Advanced)
                        {
                            SystemEventState notification = m_context!.CreateInstanceOfSystemEventType();
                            notification.TypeDefinitionId = Opc.Ua.ObjectTypeIds.SystemEventType;
                            notification.EventType!.Value = Opc.Ua.ObjectTypeIds.SystemEventType;
                            notification.EventId!.Value = ByteString.From(Guid.NewGuid().ToByteArray());
                            notification.SourceNode!.Value = instance.Handle.NodeId;
                            notification.SourceName!.Value = instance.Handle.Id;
                            notification.Time!.Value = acquiredAt;
                            notification.ReceiveTime!.Value = acquiredAt;
                            notification.Message!.Value = new LocalizedText("Explicitly simulated crane acquisition.");
                            notification.Severity!.Value = (ushort)EventSeverity.Low;
                            await instance.Handle.PublishEventAsync(notification, linked.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                // The demo window ended; cancellation from the application still propagates.
            }
        }

        /// <summary>
        /// Uses the same construction path for minimal and explicitly selected advanced instances.
        /// </summary>
        private CraneSystemBuilder Define(
            CranesHoistsBuildContext context,
            string id,
            SimulatedCraneProvider provider)
        {
            CraneSystemBuilder system = context.AddCraneSystem(id)
                .WithProvider(provider, ownsProvider: false)
                .WithIdentification(data => Identify(data, id))
                .AddController("Controller", controller =>
                {
                    controller.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("OPC Foundation sample");
                        data.Model = new LocalizedText("Simulated controller");
                        data.ProductCode = "Simulation";
                        data.SerialNumber = id + "-controller";
                    })
                        .WithCurrentUser(user => user.WithLevel("Observer").WithName("Simulation"));
                    controller.AddSoftware("Runtime", software => software.WithIdentification(data =>
                    {
                        data.Manufacturer = new LocalizedText("OPC Foundation sample");
                        data.Model = new LocalizedText("Simulated acquisition");
                        data.SoftwareRevision = "1.0";
                    }));
                    controller.AddTaskControl("Main");
                })
                .AddSafetyState("Safety")
                .AddDevice("Hoist", CraneMotionDeviceCategoryEnum.HOIST, device =>
                {
                    device.WithIdentification(data => Identify(data, id + "-hoist"));
                    if (m_options.Advanced)
                    {
                        device.WithWorkingPeriod().WithComponents();
                    }
                    device.AddAxis("Lift", axis =>
                    {
                        if (m_options.Advanced)
                        {
                            axis.WithPositionCapability(true, AxisMotionProfileEnumeration.LINEAR)
                                .WithAxisFunctions().WithMovingState().WithSpeedLimits().WithActualLoad()
                                .WithRatedSpeed(2.5, new EUInformation
                                {
                                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                                    UnitId = 5067859,
                                    DisplayName = new LocalizedText("m/s")
                                })
                                .AddBackupBrake("BackupBrake", data => Identify(data, id + "-brake"));
                        }
                        axis.AddMotor("LiftMotor", motor =>
                        {
                            if (m_options.Advanced)
                            {
                                motor.WithOverheated().WithServiceBrake()
                                    .WithIdentification(data => Identify(data, id + "-motor"));
                            }
                        });
                    });
                });
            if (m_options.Advanced)
            {
                system.WithExternalControl().WithGlobalSpeedLimit().WithFunctionalGroups().WithComponents().WithEvents()
                    .WithLimitRequests(provider, m_authorizer)
                    .AddProtectiveFunction("Overload", ["Lift"], protection => protection.WithStateMonitoring()
                        .WithDescription(
                            ProtectiveFunctionEnumeration.FORCE_LIMITER, "Simulated overload monitor", true)
                        .WithDirections(true, true, false, true)
                        .WithIdentification(data => Identify(data, id + "-protection")));
            }
            return system;
        }

        /// <summary>
        /// Supplies a unique nameplate through the shared Machinery writer.
        /// </summary>
        private static void Identify(MachineryIdentificationData data, string serial)
        {
            data.Manufacturer = new LocalizedText("OPC Foundation sample");
            data.SerialNumber = serial;
            data.ProductInstanceUri = "urn:opcfoundation.org:crane-simulation:" + serial;
            data.Model = new LocalizedText("Explicit simulation; no equipment connection");
            data.ProductCode = "Simulation-only";
        }

        /// <summary>
        /// Separates source acquisition time from the client's eventual receive time.
        /// </summary>
        private DateTimeUtc AcquiredAt()
        {
            return new DateTimeUtc(m_clock.GetUtcNow().UtcDateTime);
        }

        /// <summary>
        /// Keeps an independent provider paired with its native system for deterministic teardown.
        /// </summary>
        private sealed record CraneInstance(CraneSystemHandle Handle, SimulatedCraneProvider Provider);

        /// <summary>The explicit topology, trust and runtime bounds.</summary>
        private readonly CraneServerOptions m_options;

        /// <summary>The acquisition and delay clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The separate per-request authorization policy.</summary>
        private readonly ICraneWriteAuthorizer m_authorizer;

        /// <summary>The injected lifecycle logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The independently owned system/provider pairs.</summary>
        private readonly List<CraneInstance> m_instances = [];

        /// <summary>The actual OPC UA post-start readiness signal, not merely Generic Host readiness.</summary>
        private readonly TaskCompletionSource<bool> m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The borrowed context used to instantiate standard events.</summary>
        private ISystemContext? m_context;

        /// <summary>Whether ordered application shutdown has already started.</summary>
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated lifecycle diagnostics for the simulation.
    /// </summary>
    internal static partial class CraneDemoLog
    {
        /// <summary>
        /// Records successful draining and removal, not a requested shutdown.
        /// </summary>
        [LoggerMessage(EventId = CranesHoistsServerEventIds.CraneDemo, Level = LogLevel.Information,
            Message = "Released {Count} native crane trees and their simulated providers.")]
        public static partial void CraneResourcesReleased(this ILogger logger, int count);
    }
}
