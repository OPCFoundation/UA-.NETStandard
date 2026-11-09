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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Composes independent wireless peripherals with the existing Machinery owner and MachineTool hosting selection.
    /// </summary>
    public static class WmtpPeripheralBuilderExtensions
    {
        /// <summary>
        /// Starts a mandatory-only peripheral, not a MachineToolType instance.
        /// </summary>
        public static WmtpPeripheralBuilder AddWirelessPeripheral(
            this IMachineryBuildContext context, string browseName)
        {
            return new WmtpPeripheralBuilder(context, browseName);
        }
    }

    /// <summary>
    /// Stages one independent peripheral and validates every selected provider before allocating/publishing its nodes.
    /// Providers remain application-owned unless explicitly supplied as owned resources.
    /// </summary>
    public sealed class WmtpPeripheralBuilder
    {
        /// <summary>
        /// Creates a direct builder using the selected, already configured namespace owner.
        /// </summary>
        public WmtpPeripheralBuilder(IMachineryBuildContext context, string browseName)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A peripheral name is required.", nameof(browseName));
            }
            m_name = new QualifiedName(browseName, context.InstanceNamespaceIndex);
        }

        /// <summary>
        /// Explicitly selects this peripheral's equipment adapter; its additional provider interfaces can be reused.
        /// An unkeyed application service is never an implicit device selection.
        /// </summary>
        /// <remarks>
        /// Deliberately shared adapters must remain application-owned and coordinate access to their shared equipment.
        /// </remarks>
        public WmtpPeripheralBuilder WithProvider(IWmtpDeviceProvider provider)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                Device = provider ?? throw new ArgumentNullException(nameof(provider))
            };
            return this;
        }

        /// <summary>
        /// Configures the existing DI-qualified component identification inside DeviceInformation.
        /// </summary>
        public WmtpPeripheralBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            EnsureMutable();
            m_identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Selects optional peripheral features, independently of the bundled MachineTool feature set.
        /// </summary>
        public WmtpPeripheralBuilder WithFeatures(WmtpFeatures features)
        {
            EnsureMutable();
            if ((features & ~WmtpFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            m_configuration = m_configuration with { Features = features };
            return this;
        }

        /// <summary>
        /// Selects the shared authorization policy, including explicit competing-caller abort policy.
        /// </summary>
        public WmtpPeripheralBuilder WithAuthorization(IWmtpAuthorization authorization)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization))
            };
            return this;
        }

        /// <summary>
        /// Selects the explicit sensor adapter.
        /// </summary>
        public WmtpPeripheralBuilder WithMeasurementProvider(IWmtpMeasurementProvider provider)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                Measurements = provider ?? throw new ArgumentNullException(nameof(provider))
            };
            return this;
        }

        /// <summary>
        /// Selects the explicit calibration adapter.
        /// </summary>
        public WmtpPeripheralBuilder WithCalibrationProvider(IWmtpCalibrationProvider provider)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                Calibration = provider ?? throw new ArgumentNullException(nameof(provider))
            };
            return this;
        }

        /// <summary>
        /// Selects the explicit device-time adapter, not the server's TimeProvider.
        /// </summary>
        public WmtpPeripheralBuilder WithDeviceTimeProvider(IWmtpDeviceTimeProvider provider)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                DeviceTime = provider ?? throw new ArgumentNullException(nameof(provider))
            };
            return this;
        }

        /// <summary>
        /// Selects the explicit bounded firmware/license-transfer adapter.
        /// </summary>
        public WmtpPeripheralBuilder WithTransferProvider(IWmtpTransferProvider provider)
        {
            EnsureMutable();
            m_configuration = m_configuration with
            {
                Transfers = provider ?? throw new ArgumentNullException(nameof(provider))
            };
            return this;
        }

        /// <summary>
        /// Adds one explicitly described measurement kind; repeated kinds with distinct names are supported.
        /// </summary>
        public WmtpPeripheralBuilder AddMeasurement(WmtpMeasurementDefinition definition)
        {
            EnsureMutable();
            WmtpMeasurementDefinition validated = WmtpValidation.Definition(definition);
            if (m_definitions.Count == 256 || m_definitions.Exists(item => item.Name == validated.Name))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Measurement names must be unique and a peripheral is bounded to 256 sensors.");
            }
            m_definitions.Add(validated);
            return this;
        }

        /// <summary>
        /// Enables work-cycle records with independently selected optional methods.
        /// </summary>
        public WmtpPeripheralBuilder WithWorkRecords(
            IWmtpRecordStore store, WmtpRecordFeatures features = WmtpRecordFeatures.None)
        {
            EnsureMutable();
            ValidateRecordFeatures(features);
            m_configuration = m_configuration with
            {
                WorkRecords = store ?? throw new ArgumentNullException(nameof(store)),
                WorkFeatures = features,
                Features = m_configuration.Features | WmtpFeatures.WorkRecords
            };
            return this;
        }

        /// <summary>
        /// Enables a separate service-cycle dataset with independently selected optional methods.
        /// </summary>
        public WmtpPeripheralBuilder WithServiceRecords(
            IWmtpRecordStore store, WmtpRecordFeatures features = WmtpRecordFeatures.None)
        {
            EnsureMutable();
            ValidateRecordFeatures(features);
            m_configuration = m_configuration with
            {
                ServiceRecords = store ?? throw new ArgumentNullException(nameof(store)),
                ServiceFeatures = features,
                Features = m_configuration.Features | WmtpFeatures.ServiceRecords
            };
            return this;
        }

        /// <summary>
        /// Selects bounded byte budgets, with room left to the host for OPC UA transport/service headers.
        /// </summary>
        public WmtpPeripheralBuilder WithByteLimits(int maximumTransferBytes, int maximumReportBytes)
        {
            EnsureMutable();
            if (maximumTransferBytes is < 1 or > 64 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTransferBytes));
            }
            if (maximumReportBytes is < 256 or > 64 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumReportBytes));
            }
            m_configuration = m_configuration with
            {
                MaximumTransferBytes = maximumTransferBytes,
                MaximumReportBytes = maximumReportBytes
            };
            return this;
        }

        /// <summary>
        /// Overrides the injectable server receipt/counter clock for this peripheral.
        /// </summary>
        public WmtpPeripheralBuilder WithClock(TimeProvider timeProvider)
        {
            EnsureMutable();
            m_clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            return this;
        }

        /// <summary>
        /// Transfers asynchronous resource ownership only after successful construction.
        /// Failed builds do not dispose application-owned providers.
        /// Do not transfer a resource shared with another peripheral.
        /// </summary>
        public WmtpPeripheralBuilder WithOwnedResource(IAsyncDisposable resource)
        {
            EnsureMutable();
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            if (!m_resources.Contains(resource))
            {
                m_resources.Add(resource);
            }
            return this;
        }

        /// <summary>
        /// Acquires, validates, stages and publishes one peripheral with rollback through the Machinery transaction.
        /// </summary>
        public async ValueTask<WmtpPeripheral> BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureMutable();
            if (Interlocked.CompareExchange(ref m_building, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            IMachineBuilder<WirelessMachineToolPeripheralState>? builder = null;
            WirelessMachineToolPeripheralState? state = null;
            WmtpPeripheral? peripheral = null;
            bool registering = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WmtpPeripheralConfiguration configuration = ResolveConfiguration();
                Action<MachineryIdentificationData> identify = m_identification ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "A component nameplate is required.");
                WmtpDeviceSnapshot device = await configuration.Device!.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                WmtpValidation.Device(device, configuration.Features);
                var initial = new WmtpMeasurementSample[m_definitions.Count];
                for (int index = 0; index < initial.Length; index++)
                {
                    initial[index] = await configuration.Measurements!.ReadAsync(
                        m_definitions[index].Name, cancellationToken).ConfigureAwait(false);
                    WmtpValidation.Sample(initial[index], m_definitions[index].DataType);
                }
                cancellationToken.ThrowIfCancellationRequested();
                state = m_context.Context.CreateInstanceOfWirelessMachineToolPeripheralType(
                    m_context.MachinesFolder, m_name);
                var nodes = new WmtpDeviceNodes(m_context.Context, state, configuration.Features);
                builder = m_context.AddMachine(state, m_name);
                builder.WithIdentification(nodes.Identification, identify);
                peripheral = new WmtpPeripheral(m_context, state, nodes, configuration,
                    m_clock ?? m_context.GetService<TimeProvider>() ?? TimeProvider.System);
                peripheral.Configure(builder, m_definitions.ToArrayOf());
                await peripheral.InitializeAsync(device, initial.ToArrayOf()).ConfigureAwait(false);
                builder.WithRegistrationBinding((_, _, _) =>
                    new ValueTask<IAsyncDisposable>(peripheral.CreateRegistration()));
                registering = true;
                await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
                peripheral.StartMaintenance();
                await peripheral.CommitConformanceAsync(cancellationToken).ConfigureAwait(false);
                peripheral.TakeOwnership(m_resources.ToArrayOf());
                m_built = true;
                return peripheral;
            }
            catch
            {
                if (builder != null && !registering)
                {
                    await builder.AbortAsync().ConfigureAwait(false);
                }
                if (peripheral != null)
                {
                    await peripheral.DisposeAsync().ConfigureAwait(false);
                }
                if (state != null)
                {
                    ReleaseReservations(state);
                }
                throw;
            }
            finally
            {
                Volatile.Write(ref m_building, 0);
            }
        }

        /// <summary>
        /// Validates provider availability before any factory allocation or publication.
        /// </summary>
        private WmtpPeripheralConfiguration ResolveConfiguration()
        {
            IWmtpDeviceProvider device = m_configuration.Device ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Select this peripheral's equipment adapter explicitly with WithProvider.");
            WmtpPeripheralConfiguration resolved = m_configuration with
            {
                Device = device,
                Measurements = m_configuration.Measurements ?? device as IWmtpMeasurementProvider,
                Calibration = m_configuration.Calibration ?? device as IWmtpCalibrationProvider,
                DeviceTime = m_configuration.DeviceTime ?? device as IWmtpDeviceTimeProvider,
                Transfers = m_configuration.Transfers ?? device as IWmtpTransferProvider,
                Authorization = m_configuration.Authorization ?? m_context.GetService<IWmtpAuthorization>()
            };
            WmtpFeatures features = resolved.Features;
            bool commands = m_definitions.Count != 0 ||
                (features & (WmtpFeatures.Calibration | WmtpFeatures.DeviceTime | WmtpFeatures.Firmware |
                    WmtpFeatures.License | WmtpFeatures.WorkRecords | WmtpFeatures.ServiceRecords)) != 0;
            if ((commands && resolved.Authorization == null) ||
                (m_definitions.Count != 0 && resolved.Measurements == null) ||
                ((features & WmtpFeatures.Calibration) != 0 && resolved.Calibration == null) ||
                ((features & WmtpFeatures.DeviceTime) != 0 && resolved.DeviceTime == null) ||
                ((features & (WmtpFeatures.Firmware | WmtpFeatures.License)) != 0 && resolved.Transfers == null) ||
                ((features & WmtpFeatures.WorkRecords) != 0 && resolved.WorkRecords == null) ||
                ((features & WmtpFeatures.ServiceRecords) != 0 && resolved.ServiceRecords == null))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Every selected method surface requires its explicit provider and authorization policy.");
            }
            if ((features & (WmtpFeatures.WorkRecords | WmtpFeatures.ServiceRecords)) ==
                    (WmtpFeatures.WorkRecords | WmtpFeatures.ServiceRecords) &&
                ReferenceEquals(resolved.WorkRecords, resolved.ServiceRecords))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Work and service records require isolated dataset providers.");
            }
            return resolved;
        }

        /// <summary>
        /// Releases unpublished generated identifiers even when adoption failed before creating the transaction.
        /// </summary>
        private void ReleaseReservations(NodeState node)
        {
            if (m_context.Context.NodeIdFactory is ICompanionNodeIdReservationOwner owner)
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    ReleaseReservations(child);
                }
                owner.ReleaseNodeIdReservation(node);
            }
        }

        /// <summary>
        /// Rejects undefined method selections.
        /// </summary>
        private static void ValidateRecordFeatures(WmtpRecordFeatures features)
        {
            if ((features & ~WmtpRecordFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
        }

        /// <summary>
        /// Prevents setup mutation during or after a successful build.
        /// </summary>
        private void EnsureMutable()
        {
            if (m_built || Volatile.Read(ref m_building) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "This peripheral is already building/built.");
            }
        }

        /// <summary>The shared owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The application instance name.</summary>
        private readonly QualifiedName m_name;

        /// <summary>The validated sensor definitions.</summary>
        private readonly List<WmtpMeasurementDefinition> m_definitions = [];

        /// <summary>The explicitly transferred lifetime resources.</summary>
        private readonly List<IAsyncDisposable> m_resources = [];

        /// <summary>The configured provider and feature selection.</summary>
        private WmtpPeripheralConfiguration m_configuration = new();

        /// <summary>The existing nested component nameplate data.</summary>
        private Action<MachineryIdentificationData>? m_identification;

        /// <summary>The optional per-peripheral clock override.</summary>
        private TimeProvider? m_clock;

        /// <summary>The build admission flag.</summary>
        private int m_building;

        /// <summary>Whether publication has succeeded.</summary>
        private bool m_built;
    }

    /// <summary>
    /// Carries a validated immutable provider selection into the runtime.
    /// </summary>
    internal sealed record WmtpPeripheralConfiguration
    {
        /// <summary>
        /// Gets the device observer.
        /// </summary>
        internal IWmtpDeviceProvider? Device { get; init; }

        /// <summary>
        /// Gets the sensor adapter.
        /// </summary>
        internal IWmtpMeasurementProvider? Measurements { get; init; }

        /// <summary>
        /// Gets the calibration adapter.
        /// </summary>
        internal IWmtpCalibrationProvider? Calibration { get; init; }

        /// <summary>
        /// Gets the time adapter.
        /// </summary>
        internal IWmtpDeviceTimeProvider? DeviceTime { get; init; }

        /// <summary>
        /// Gets the transfer adapter.
        /// </summary>
        internal IWmtpTransferProvider? Transfers { get; init; }

        /// <summary>
        /// Gets the authorization policy.
        /// </summary>
        internal IWmtpAuthorization? Authorization { get; init; }

        /// <summary>
        /// Gets the work dataset.
        /// </summary>
        internal IWmtpRecordStore? WorkRecords { get; init; }

        /// <summary>
        /// Gets the service dataset.
        /// </summary>
        internal IWmtpRecordStore? ServiceRecords { get; init; }

        /// <summary>
        /// Gets the explicit optional peripheral capabilities.
        /// </summary>
        internal WmtpFeatures Features { get; init; }

        /// <summary>
        /// Gets the work dataset's method selection.
        /// </summary>
        internal WmtpRecordFeatures WorkFeatures { get; init; }

        /// <summary>
        /// Gets the service dataset's method selection.
        /// </summary>
        internal WmtpRecordFeatures ServiceFeatures { get; init; }

        /// <summary>
        /// Gets the transfer-byte budget.
        /// </summary>
        internal int MaximumTransferBytes { get; init; } = 1024 * 1024;

        /// <summary>
        /// Gets the complete report-byte budget.
        /// </summary>
        internal int MaximumReportBytes { get; init; } = 1024 * 1024;
    }
}
