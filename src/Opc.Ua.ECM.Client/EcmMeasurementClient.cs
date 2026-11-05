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
 *
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.IA;
using Opc.Ua.IA.Client;

namespace Opc.Ua.ECM.Client
{
    /// <summary>
    /// Reads standalone E0, E1, E2, E3, D0 and D1 measurements and resets their shared statistics through
    /// generated ECM and IA proxies.
    /// The session is borrowed and measurement reads retain quality and timestamps.
    /// </summary>
    public sealed class EcmMeasurementClient
    {
        /// <summary>
        /// Creates a typed client for one generated ECM measurement point.
        /// </summary>
        /// <param name="session">The connected, borrowed session.</param>
        /// <param name="measurementPointId">The EnergyMeasurementType NodeId.</param>
        /// <param name="telemetry">The telemetry context used by the generated proxy.</param>
        public EcmMeasurementClient(
            ISessionClient session,
            NodeId measurementPointId,
            ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(session, nameof(session));
            ArgumentNullException.ThrowIfNull(telemetry, nameof(telemetry));
            if (measurementPointId.IsNull)
            {
                throw new ArgumentException("A measurement point is required.", nameof(measurementPointId));
            }
            EcmClient.RegisterModel(session);
            MeasurementPoint = new EnergyMeasurementTypeClient(session, measurementPointId, telemetry);
            ApplicationTag = MeasurementPoint.ApplicationTagVariable();
            AcCurrentPe = new IEnergyProfileE0TypeClient(session, measurementPointId, telemetry).AcCurrentPeVariable();
            AcActivePowerTotal = new IEnergyProfileE1TypeClient(
                session, measurementPointId, telemetry).AcActivePowerTotalVariable();
            DcCurrent = new IEnergyProfileD0TypeClient(session, measurementPointId, telemetry).DcCurrentVariable();
            var e2 = new IEnergyProfileE2TypeClient(session, measurementPointId, telemetry);
            AcActiveEnergyTotalExportLp = e2.AcActiveEnergyTotalExportLpVariable();
            AcActiveEnergyTotalImportLp = e2.AcActiveEnergyTotalImportLpVariable();
            var e3 = new IEnergyProfileE3TypeClient(session, measurementPointId, telemetry);
            AcActivePowerPe = e3.AcActivePowerPeVariable();
            AcPowerFactorPe = e3.AcPowerFactorPeVariable();
            AcReactivePowerPe = e3.AcReactivePowerPeVariable();
            AcVoltagePe = e3.AcVoltagePeVariable();
            AcVoltagePp = e3.AcVoltagePpVariable();
            AcActiveEnergyTotalExportHp = e3.AcActiveEnergyTotalExportHpVariable();
            AcActiveEnergyTotalImportHp = e3.AcActiveEnergyTotalImportHpVariable();
            AcReactiveEnergyTotalExportHp = e3.AcReactiveEnergyTotalExportHpVariable();
            AcReactiveEnergyTotalImportHp = e3.AcReactiveEnergyTotalImportHpVariable();
            var d1 = new IEnergyProfileD1TypeClient(session, measurementPointId, telemetry);
            DcActivePower = d1.DcActivePowerVariable();
            DcVoltage = d1.DcVoltageVariable();
            DcElectricalCharge = d1.DcElectricalChargeVariable();
            DcEnergyTotalExportLp = d1.DcEnergyTotalExportLpVariable();
            DcEnergyTotalImportLp = d1.DcEnergyTotalImportLpVariable();
            DcRelativeCharge = d1.DcRelativeChargeVariable();
            Statistics = new IAggregateStatisticsTypeClient(session, measurementPointId, telemetry);
        }

        /// <summary>
        /// Gets the shared IA statistics proxy. It exposes the collection StartTime and the single
        /// ResetStatistics method that resets every E2, E3 and D1 statistic of this measurement point.
        /// </summary>
        public IAggregateStatisticsTypeClient Statistics { get; }

        /// <summary>
        /// Gets typed read access to the E2 low-precision exported active energy statistic.
        /// </summary>
        public ClientVariable<float> AcActiveEnergyTotalExportLp { get; }

        /// <summary>
        /// Gets typed read access to the E2 low-precision imported active energy statistic.
        /// </summary>
        public ClientVariable<float> AcActiveEnergyTotalImportLp { get; }

        /// <summary>
        /// Gets typed read access to the E3 per-phase active power.
        /// </summary>
        public ClientVariable<AcPeDataType> AcActivePowerPe { get; }

        /// <summary>
        /// Gets typed read access to the E3 per-phase power factor.
        /// </summary>
        public ClientVariable<AcPeDataType> AcPowerFactorPe { get; }

        /// <summary>
        /// Gets typed read access to the E3 per-phase reactive power.
        /// </summary>
        public ClientVariable<AcPeDataType> AcReactivePowerPe { get; }

        /// <summary>
        /// Gets typed read access to the E3 phase-to-neutral voltage.
        /// </summary>
        public ClientVariable<AcPeDataType> AcVoltagePe { get; }

        /// <summary>
        /// Gets typed read access to the E3 phase-to-phase voltage.
        /// </summary>
        public ClientVariable<AcPpDataType> AcVoltagePp { get; }

        /// <summary>
        /// Gets typed read access to the E3 high-precision exported active energy statistic.
        /// </summary>
        public ClientVariable<double> AcActiveEnergyTotalExportHp { get; }

        /// <summary>
        /// Gets typed read access to the E3 high-precision imported active energy statistic.
        /// </summary>
        public ClientVariable<double> AcActiveEnergyTotalImportHp { get; }

        /// <summary>
        /// Gets typed read access to the E3 high-precision exported reactive energy statistic.
        /// </summary>
        public ClientVariable<double> AcReactiveEnergyTotalExportHp { get; }

        /// <summary>
        /// Gets typed read access to the E3 high-precision imported reactive energy statistic.
        /// </summary>
        public ClientVariable<double> AcReactiveEnergyTotalImportHp { get; }

        /// <summary>
        /// Gets typed read access to the D1 direct-current active power.
        /// </summary>
        public ClientVariable<float> DcActivePower { get; }

        /// <summary>
        /// Gets typed read access to the D1 direct-current voltage.
        /// </summary>
        public ClientVariable<float> DcVoltage { get; }

        /// <summary>
        /// Gets typed read access to the D1 electrical charge statistic.
        /// </summary>
        public ClientVariable<float> DcElectricalCharge { get; }

        /// <summary>
        /// Gets typed read access to the D1 exported energy statistic.
        /// </summary>
        public ClientVariable<float> DcEnergyTotalExportLp { get; }

        /// <summary>
        /// Gets typed read access to the D1 imported energy statistic.
        /// </summary>
        public ClientVariable<float> DcEnergyTotalImportLp { get; }

        /// <summary>
        /// Gets typed read access to the D1 relative charge statistic.
        /// </summary>
        public ClientVariable<float> DcRelativeCharge { get; }

        /// <summary>
        /// Reads the beginning of the current statistics collection with its quality and timestamps.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        public ValueTask<ClientRead<DateTimeUtc>> ReadStatisticsStartTimeAsync(
            CancellationToken cancellationToken = default) =>
            Statistics.StartTimeVariable().ReadAsync(cancellationToken);

        /// <summary>
        /// Invokes the single ResetStatistics method that resets every statistic. The server's authorization
        /// result surfaces as the Call status; the client never retries a reset.
        /// </summary>
        /// <param name="cancellationToken">Cancels the call.</param>
        public async ValueTask ResetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            await Statistics.ResetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the generated ECM measurement-point proxy.
        /// </summary>
        public EnergyMeasurementTypeClient MeasurementPoint { get; }

        /// <summary>
        /// Gets typed read/write access to the persisted ApplicationTag.
        /// </summary>
        public ClientVariable<string> ApplicationTag { get; }

        /// <summary>
        /// Gets typed read access to the E0 phase-and-energy-factor current.
        /// </summary>
        public ClientVariable<AcPeDataType> AcCurrentPe { get; }

        /// <summary>
        /// Gets typed read access to the E1 total active power.
        /// </summary>
        public ClientVariable<float> AcActivePowerTotal { get; }

        /// <summary>
        /// Gets typed read access to the D0 direct-current value.
        /// </summary>
        public ClientVariable<float> DcCurrent { get; }

        /// <summary>
        /// Discovers direct EnergyMeasurementType children, including conforming subtypes.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="parent">The parent node to browse.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="cancellationToken">Cancels discovery.</param>
        public static IAsyncEnumerable<EcmMeasurementClient> DiscoverAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(session, nameof(session));
            ArgumentNullException.ThrowIfNull(telemetry, nameof(telemetry));
            return DiscoverCoreAsync(session, parent, telemetry, cancellationToken);
        }

        /// <summary>
        /// Enumerates measurement points after validating the parent and model type.
        /// </summary>
        private static async IAsyncEnumerable<EcmMeasurementClient> DiscoverCoreAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(ObjectTypeIds.EnergyMeasurementType, session.NamespaceUris);
            if (expected.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> children = await ClientModelAccess.BrowseAsync(
                session, parent, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
            var seen = new HashSet<NodeId>();
            for (int ii = 0; ii < children.Count; ii++)
            {
                ReferenceDescription child = children[ii];
                NodeId id = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
                NodeId actual = ExpandedNodeId.ToNodeId(child.TypeDefinition, session.NamespaceUris);
                if (!id.IsNull && !actual.IsNull && seen.Add(id) &&
                    await session.NodeCache.IsTypeOfAsync(actual, expected, cancellationToken).ConfigureAwait(false))
                {
                    yield return new EcmMeasurementClient(session, id, telemetry);
                }
            }
        }
    }
}
