/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.IA;
using Opc.Ua.IA.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Range = Opc.Ua.Range;

namespace IndustrialAutomationSample
{
    /// <summary>
    /// Composes IA information below one Machinery-owned DI device.
    /// </summary>
    public sealed class IaSampleModel : IServerStartupTask, IDisposable
    {
        /// <summary>
        /// Creates simulated providers; no physical signaling or calibration occurs.
        /// </summary>
        public IaSampleModel()
        {
            m_lightSource = new StacklightSource();
            m_aggregateSource = new StatisticsSource(0);
            m_rollingSource = new StatisticsSource(3);
        }

        /// <summary>
        /// Gets completion after the configured model and the actual server have started.
        /// </summary>
        public Task Ready => m_ready.Task;

        /// <summary>
        /// Gets the aggregate quantity used by real-client checks.
        /// </summary>
        public NodeId AggregateQuantity { get; private set; }

        /// <summary>
        /// Gets the rolling quantity used by real-client checks.
        /// </summary>
        public NodeId RollingQuantity { get; private set; }

        /// <summary>
        /// Gets the published device.
        /// </summary>
        public NodeId Device { get; private set; }

        /// <summary>
        /// Controls the sample reset policy; production applications supply their own user/role policy.
        /// </summary>
        public bool AllowReset
        {
            get => m_aggregateSource.AllowReset;
            set
            {
                m_aggregateSource.AllowReset = value;
                m_rollingSource.AllowReset = value;
            }
        }

        /// <summary>
        /// Builds generated IA subtrees and binds providers before their node-manager registration.
        /// </summary>
        public async ValueTask BuildAsync(IDiPostSetupContext setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException(nameof(setup));
            }
            if (m_stacklight != null)
            {
                throw new InvalidOperationException("The sample model has already been configured.");
            }
            CancellationToken ct = setup.CancellationToken;
            IDeviceBuilder<DeviceState> device = await setup.CreateDeviceAsync(
                new QualifiedName("InspectionCell", setup.Manager.InstanceNamespaceIndex)).ConfigureAwait(false);
            device.WithIdentification(nameplate =>
            {
                nameplate.Manufacturer = new LocalizedText("OPC Foundation");
                nameplate.Model = new LocalizedText("IA information simulation");
                nameplate.SerialNumber = "IA-SIMULATION-001";
            });
            Device = device.Device.NodeId;
            ISystemContext context = device.Context;
            ushort ns = setup.Manager.InstanceNamespaceIndex;
            BasicStacklightState stack = context.CreateInstanceOfBasicStacklightType(
                device.Device, new QualifiedName("Stacklight", ns));
            stack.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            m_stacklight = new IaStacklightBuilder(context, stack).WithModes()
                .AddLight(new QualifiedName("Lamp", ns))
                .AddAcoustic(new QualifiedName("Sound", ns), new QualifiedName("Warning", ns))
                .Build(m_lightSource);
            await m_stacklight.RefreshAsync(ct).ConfigureAwait(false);
            await setup.Manager.AddNodeAsync(setup.Manager.SystemContext, Device, stack, ct).ConfigureAwait(false);

            (m_aggregate, BaseObjectState aggregate, BaseDataVariableState aggregateQuantity) =
                CreateStatistics(context, device.Device, new QualifiedName("Aggregate", ns), m_aggregateSource, false);
            AggregateQuantity = aggregateQuantity.NodeId;
            await m_aggregate.RefreshAsync(ct).ConfigureAwait(false);
            await setup.Manager.AddNodeAsync(setup.Manager.SystemContext, Device, aggregate, ct).ConfigureAwait(false);
            (m_rolling, BaseObjectState rolling, BaseDataVariableState rollingQuantity) =
                CreateStatistics(context, device.Device, new QualifiedName("Rolling", ns), m_rollingSource, true);
            RollingQuantity = rollingQuantity.NodeId;
            await m_rolling.RefreshAsync(ct).ConfigureAwait(false);
            await setup.Manager.AddNodeAsync(setup.Manager.SystemContext, Device, rolling, ct).ConfigureAwait(false);

            var source = new CalibrationSource(ns);
            CalibrationTargetState target = context.CreateInstanceOfCalibrationTargetType(
                device.Device, new QualifiedName("ReferenceTarget", ns));
            target.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            m_calibration = new IaCalibrationTarget(context, target, source);
            var geometry = new BaseObjectState(target.CalibrationTargetFeatures);
            geometry.Create(context, NodeId.Null, new QualifiedName("VendorGeometry", ns),
                new LocalizedText("Target geometry definition"), true);
            target.CalibrationTargetFeatures!.AddChild(geometry);
            await m_calibration.RefreshAsync(ct).ConfigureAwait(false);
            await setup.Manager.AddNodeAsync(setup.Manager.SystemContext, Device, target, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Completes readiness only after the actual server has started.
        /// </summary>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_calibration == null)
            {
                throw new InvalidOperationException("IA model configuration did not finish.");
            }
            m_ready.TrySetResult(true);
            return default;
        }

        /// <summary>
        /// Drives one simulated observation with independent aggregate and three-sample rolling quantities.
        /// </summary>
        public async ValueTask AdvanceAsync(StacklightOperationMode mode, uint value,
            CancellationToken cancellationToken = default)
        {
            if (m_stacklight == null || m_aggregate == null || m_rolling == null || m_calibration == null)
            {
                throw new InvalidOperationException("The IA sample is not ready.");
            }
            m_lightSource.Mode = mode;
            m_aggregateSource.Add(value);
            m_rollingSource.Add(value);
            await m_stacklight.RefreshAsync(cancellationToken).ConfigureAwait(false);
            await m_aggregate.RefreshAsync(cancellationToken).ConfigureAwait(false);
            await m_rolling.RefreshAsync(cancellationToken).ConfigureAwait(false);
            await m_calibration.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases bindings after host shutdown has drained incoming requests.
        /// </summary>
        public void Dispose()
        {
            m_stacklight?.Dispose();
            m_aggregate?.Dispose();
            m_rolling?.Dispose();
        }

        /// <summary>
        /// Composes a single provider-owned sum with the appropriate IA statistics interface.
        /// </summary>
        private static (IaStatistics Binding, BaseObjectState Owner, BaseDataVariableState Quantity) CreateStatistics(
            ISystemContext context, NodeState parent, QualifiedName name, StatisticsSource provider, bool rolling)
        {
            var owner = new BaseObjectState(parent);
            owner.Create(context, NodeId.Null, name, new LocalizedText(name.Name), true);
            var quantity = new BaseDataVariableState(owner);
            quantity.Create(context, NodeId.Null, new QualifiedName("Sum", name.NamespaceIndex),
                new LocalizedText("Sum"), true);
            quantity.DataType = Opc.Ua.DataTypeIds.UInt32;
            quantity.ValueRank = ValueRanks.Scalar;
            owner.AddChild(quantity);
            var builder = new IaStatisticsBuilder().AddComponent(quantity);
            if (rolling)
            {
                builder.WithCountWindow(3);
            }
            else
            {
                builder.WithAggregate("Sample process restart; no persistent store configured");
            }
            return (builder.Build(context, owner, provider), owner, quantity);
        }

        /// <summary>
        /// Simulates acquisition without mapping alarms to physical outputs.
        /// </summary>
        private sealed class StacklightSource : IIaStacklightProvider
        {
            /// <summary>
            /// Gets or sets the next observed representation.
            /// </summary>
            public StacklightOperationMode Mode { get; set; }

            /// <inheritdoc/>
            public ValueTask<IaStacklightSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTimeUtc acquired = DateTime.UtcNow;
                var mode = new IaStacklightSample<StacklightOperationMode>(Mode, StatusCodes.Good, acquired);
                var on = new IaStacklightSample<bool>(false, StatusCodes.Good, acquired);
                return new ValueTask<IaStacklightSnapshot>(new IaStacklightSnapshot(mode,
                [
                    new IaStacklightLightSnapshot(on,
                        new IaStacklightSample<SignalColor>(SignalColor.Green, StatusCodes.Good, acquired),
                        new IaStacklightSample<SignalModeLight>(SignalModeLight.Continuous, StatusCodes.Good, acquired),
                        null, null, []),
                    new IaStacklightAcousticSnapshot(null,
                        new IaStacklightSample<uint>(0u, StatusCodes.Good, acquired), null)
                ],
                    Mode == StacklightOperationMode.Levelmeter
                        ? new IaStacklightSample<float>(65f, StatusCodes.Good, acquired) : null,
                    Mode == StacklightOperationMode.Levelmeter
                        ? new IaStacklightSample<LevelDisplayMode>(LevelDisplayMode.Dimmed, StatusCodes.Good, acquired)
                        : null, null));
            }
        }

        /// <summary>
        /// Owns the illustrative calculation, reset authorization and atomic commit.
        /// </summary>
        private sealed class StatisticsSource : IIaStatisticsProvider
        {
            /// <summary>
            /// Selects an unbounded aggregate or a bounded count window.
            /// </summary>
            public StatisticsSource(int window)
            {
                m_window = window;
                m_start = DateTime.UtcNow;
            }

            /// <summary>
            /// Gets or sets explicit sample-only permission for anonymous resets.
            /// </summary>
            public bool AllowReset
            {
                get => Volatile.Read(ref m_allowReset);
                set => Volatile.Write(ref m_allowReset, value);
            }

            /// <summary>
            /// Commits the next acquisition under the same gate as reset and snapshot reads.
            /// </summary>
            public void Add(uint value)
            {
                lock (m_gate)
                {
                    bool full = m_window > 0 && m_values.Count == m_window;
                    uint outgoing = full ? m_values.Peek() : 0;
                    uint sum = checked(m_sum - outgoing + value);
                    if (m_window > 0)
                    {
                        if (full)
                        {
                            m_values.Dequeue();
                        }
                        m_values.Enqueue(value);
                    }
                    m_sum = sum;
                    m_acquired = DateTime.UtcNow;
                }
            }

            /// <inheritdoc/>
            public ValueTask<IaStatisticsSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    return new ValueTask<IaStatisticsSnapshot>(Snapshot());
                }
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> AuthorizeResetAsync(
                ISystemContext requestContext, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ServiceResult>(AllowReset
                    ? ServiceResult.Good : new ServiceResult(StatusCodes.BadUserAccessDenied));
            }

            /// <inheritdoc/>
            public ValueTask<IaStatisticsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    m_sum = 0;
                    m_values.Clear();
                    m_start = m_acquired = DateTime.UtcNow;
                    return new ValueTask<IaStatisticsSnapshot>(Snapshot());
                }
            }

            /// <summary>
            /// Captures the committed collection while the provider gate is held.
            /// </summary>
            private IaStatisticsSnapshot Snapshot()
            {
                return new IaStatisticsSnapshot
                {
                    StartTime = m_start,
                    Values = [new DataValue(Variant.From(m_sum), StatusCodes.Good, m_acquired)]
                };
            }

            /// <summary>Protects the provider's committed image.</summary>
            private readonly System.Threading.Lock m_gate = new();

            /// <summary>The selected maximum sample count, or zero for an aggregate.</summary>
            private readonly int m_window;

            /// <summary>The rolling sample inventory.</summary>
            private readonly Queue<uint> m_values = new();

            /// <summary>The current sum.</summary>
            private uint m_sum;

            /// <summary>The committed collection start.</summary>
            private DateTimeUtc m_start;

            /// <summary>The source acquisition time.</summary>
            private DateTimeUtc m_acquired = DateTime.UtcNow;

            /// <summary>The explicit sample reset policy.</summary>
            private bool m_allowReset;
        }

        /// <summary>
        /// Publishes target information and a certificate URI without performing calibration.
        /// </summary>
        private sealed class CalibrationSource : IIaCalibrationTargetProvider
        {
            /// <summary>
            /// Configures one reusable device with value and range features.
            /// </summary>
            public CalibrationSource(ushort ns)
            {
                var units = new IaCalibrationUnit("http://www.opcfoundation.org/UA/units/un/cefact",
                    5066068, new LocalizedText("mm"));
                var length = new IaCalibrationFeature(new QualifiedName("ReferenceLength", ns),
                    IaCalibrationFeatureKind.CalibrationValue, units);
                var range = new IaCalibrationFeature(new QualifiedName("Capacity", ns),
                    IaCalibrationFeatureKind.CapacityRange, units, Resolution: 0.01);
                Configuration = new IaCalibrationTargetBuilder(IaCalibrationTargetCategory.ReusableDevice,
                    new IaCalibrationIdentification
                    {
                        Manufacturer = new LocalizedText("OPC Foundation"),
                        Model = new LocalizedText("Simulated reference"),
                        SerialNumber = "IA-REFERENCE-001"
                    }).WithFeature(length).WithFeature(range)
                    .WithOptionalFields(IaCalibrationTargetFields.Quality | IaCalibrationTargetFields.CertificateUri)
                    .Build();
                DateTimeUtc acquired = DateTime.UtcNow;
                m_snapshot = new IaCalibrationTargetSnapshot(
                [
                    new IaCalibrationReading(length.BrowseName,
                        new DataValue(Variant.From(100.0), StatusCodes.Good, acquired)),
                    new IaCalibrationReading(range.BrowseName,
                        new DataValue(Variant.FromStructure(new Range { Low = 0, High = 200 }),
                            StatusCodes.Good, acquired))
                ])
                {
                    LastValidationDate = new IaCalibrationSample<DateTimeUtc>(acquired, StatusCodes.Good, acquired),
                    Quality = new IaCalibrationSample<byte>(100, StatusCodes.Good, acquired),
                    CertificateUri = new IaCalibrationSample<string>(
                        "https://example.invalid/ia/reference-certificate", StatusCodes.Good, acquired)
                };
            }

            /// <inheritdoc/>
            public IaCalibrationTargetConfiguration Configuration { get; }

            /// <inheritdoc/>
            public ValueTask<IaCalibrationTargetSnapshot> GetSnapshotAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IaCalibrationTargetSnapshot>(m_snapshot);
            }

            /// <summary>The immutable application-owned reference information.</summary>
            private readonly IaCalibrationTargetSnapshot m_snapshot;
        }

        /// <summary>The instance namespace, independent of server-assigned namespace indexes.</summary>
        public const string InstanceUri = "urn:opcua:ia:sample:instances";

        /// <summary>The observed stacklight source.</summary>
        private readonly StacklightSource m_lightSource;

        /// <summary>The non-rolling quantity owner.</summary>
        private readonly StatisticsSource m_aggregateSource;

        /// <summary>The rolling quantity owner.</summary>
        private readonly StatisticsSource m_rollingSource;

        /// <summary>The server readiness signal.</summary>
        private readonly TaskCompletionSource<bool> m_ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The bound stacklight.</summary>
        private IaStacklight? m_stacklight;

        /// <summary>The bound aggregate collection.</summary>
        private IaStatistics? m_aggregate;

        /// <summary>The bound rolling collection.</summary>
        private IaStatistics? m_rolling;

        /// <summary>The bound target information.</summary>
        private IaCalibrationTarget? m_calibration;
    }
}
