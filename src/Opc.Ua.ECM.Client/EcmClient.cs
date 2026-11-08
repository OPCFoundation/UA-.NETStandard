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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.ECM.Client
{
    /// <summary>
    /// Typed power control over generated proxies. The supplied session is borrowed and never disposed.
    /// Commands are not automatically replayed after an uncertain result or disconnect.
    /// </summary>
    public sealed class EcmClient
    {
        /// <summary>
        /// Creates a client for a discovered standby object and optional WOL object.
        /// </summary>
        public EcmClient(
            ISessionClient session,
            NodeId standbyId,
            ITelemetryContext telemetry,
            NodeId powerOffId = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (standbyId.IsNull)
            {
                throw new ArgumentException("A standby object is required.", nameof(standbyId));
            }
            RegisterModel(session);
            Standby = new EnergyStandbyManagementTypeClient(session, standbyId, telemetry);
            m_wol = powerOffId.IsNull ? null : new EcmWolClient(session, powerOffId, telemetry);
        }

        /// <summary>
        /// Gets the generated standby proxy and its model-derived variable read/write/observation accessors.
        /// </summary>
        public EnergyStandbyManagementTypeClient Standby { get; }

        /// <summary>
        /// Gets the generated WOL proxy, when configured.
        /// </summary>
        public EnergyDevicePowerOffTypeClient? PowerOff => m_wol?.PowerOff;

        /// <summary>
        /// Discovers direct standby children and conforming vendor subtypes using the current namespace table.
        /// Use the application's device/component parent or DI DeviceSet as the starting node.
        /// </summary>
        public static IAsyncEnumerable<EcmClient> DiscoverAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return DiscoverCoreAsync(session, parent, telemetry, cancellationToken);
        }

        /// <summary>
        /// Starts a pause and retains Call status, device return code and all timing outputs.
        /// </summary>
        public async ValueTask<EcmControlResult> StartPauseAsync(
            double pauseTime,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (status, mode, destination, recovery, stay, code) =
                    await Standby.StartPauseWithStatusAsync(pauseTime, cancellationToken).ConfigureAwait(false);
                if (StatusCode.IsUncertain(status) && mode != 0)
                {
                    return new EcmControlResult(StatusCodes.BadDecodingError, null);
                }
                return DecodeResult(status, code, mode, destination, recovery, stay);
            }
            catch (ServiceResultException exception) when (StatusCode.IsBad(exception.StatusCode))
            {
                return new EcmControlResult(exception.StatusCode, null);
            }
        }

        /// <summary>
        /// Requests a specific energy-saving mode without interpreting rejected calls as arrival.
        /// </summary>
        public async ValueTask<EcmControlResult> SwitchToEnergySavingModeAsync(
            byte modeId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (status, mode, destination, recovery, stay, code) =
                    await Standby.SwitchToEnergySavingModeWithStatusAsync(modeId, cancellationToken)
                        .ConfigureAwait(false);
                return DecodeResult(status, code, mode, destination, recovery, stay);
            }
            catch (ServiceResultException exception) when (StatusCode.IsBad(exception.StatusCode))
            {
                return new EcmControlResult(exception.StatusCode, null);
            }
        }

        /// <summary>
        /// Requests return to operation, preserving its separate estimate and device status.
        /// </summary>
        public async ValueTask<EcmControlResult> EndPauseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (status, recovery, code) =
                    await Standby.EndPauseWithStatusAsync(cancellationToken).ConfigureAwait(false);
                return DecodeResult(status, code, currentTimeToOperate: recovery);
            }
            catch (ServiceResultException exception) when (StatusCode.IsBad(exception.StatusCode))
            {
                return new EcmControlResult(exception.StatusCode, null);
            }
        }

        /// <summary>
        /// Explicitly requests WOL sleep. No wake packet is sent and no disconnected command is replayed.
        /// </summary>
        public ValueTask<EcmControlResult> SwitchOffWolAsync(CancellationToken cancellationToken = default)
        {
            return m_wol == null ?
                new ValueTask<EcmControlResult>(new EcmControlResult(StatusCodes.BadNotSupported, null)) :
                m_wol.SwitchOffWolAsync(cancellationToken);
        }

        /// <summary>
        /// Registers generated ECM codecs in the borrowed session's message context.
        /// </summary>
        internal static void RegisterModel(ISessionClient session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException("The session must provide a message context.", nameof(session));
            if (!messageContext.Factory.ContainsEncodeableType(DataTypeIds.EnergyStateInformationDataType))
            {
                messageContext.Factory.Builder.AddOpcUaECM().Commit();
            }
        }

        /// <summary>
        /// Reports inconsistent wire outputs as a decoding error instead of an application argument error.
        /// </summary>
        internal static EcmControlResult DecodeResult(
            StatusCode status,
            byte code,
            byte mode = 0,
            double destination = 0,
            double recovery = 0,
            double stay = 0,
            double currentTimeToOperate = 0)
        {
            try
            {
                return new EcmControlResult(status, code, mode, destination, recovery, stay, currentTimeToOperate);
            }
            catch (ArgumentException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The ECM Call status, return code or timing outputs are inconsistent.");
            }
        }

        /// <summary>
        /// Enumerates standby children after validating discovery arguments.
        /// </summary>
        private static async IAsyncEnumerable<EcmClient> DiscoverCoreAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(ObjectTypeIds.EnergyStandbyManagementType, session.NamespaceUris);
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
                    NodeId powerOff = await FindPowerOffAsync(session, id, cancellationToken).ConfigureAwait(false);
                    yield return new EcmClient(session, id, telemetry, powerOff);
                }
            }
        }

        /// <summary>
        /// Finds an optional WOL object by type rather than a vendor-specific BrowseName.
        /// </summary>
        private static async ValueTask<NodeId> FindPowerOffAsync(
            ISession session,
            NodeId standby,
            CancellationToken cancellationToken)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(ObjectTypeIds.EnergyDevicePowerOffType, session.NamespaceUris);
            ArrayOf<ReferenceDescription> children = await ClientModelAccess.BrowseAsync(
                session, standby, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
            for (int ii = 0; ii < children.Count; ii++)
            {
                NodeId actual = ExpandedNodeId.ToNodeId(children[ii].TypeDefinition, session.NamespaceUris);
                if (!actual.IsNull &&
                    await session.NodeCache.IsTypeOfAsync(actual, expected, cancellationToken).ConfigureAwait(false))
                {
                    return ExpandedNodeId.ToNodeId(children[ii].NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        /// <summary>The optional WOL client sharing the borrowed session.</summary>
        private readonly EcmWolClient? m_wol;
    }
}
