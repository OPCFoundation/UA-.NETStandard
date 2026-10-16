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
using Opc.Ua.Machinery;
using Opc.Ua.PlasticsRubber;
using static Opc.Ua.GranulateDrying.Client.GranulateDryingClientContext;

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Complete typed entry point for a drying device and its selected optional groups.
    /// </summary>
    public sealed class GranulateDryingDeviceClient
    {
        /// <summary>
        /// Creates a generated-proxy-backed device interface.
        /// </summary>
        internal GranulateDryingDeviceClient(GranulateDryingClientContext context, NodeId node)
        {
            m_context = context;
            Proxy = new GranulateDryingDeviceTypeClient(context.Session, node, context.Telemetry);
        }

        /// <summary>
        /// Gets the generated root proxy and all of its typed child accessors.
        /// </summary>
        public GranulateDryingDeviceTypeClient Proxy { get; }

        /// <summary>
        /// Gets the HMI language variable.
        /// </summary>
        public GranulateDryingVariable<string> DisplayLanguage =>
            m_context.Variable(Proxy.ObjectId, GranulateDryingCodecs.String, true, Gdd("DisplayLanguage"));

        /// <summary>
        /// Gets the mandatory generated Machinery identification proxy.
        /// </summary>
        public async ValueTask<MachineIdentificationTypeClient> IdentificationAsync(
            CancellationToken cancellationToken = default)
        {
            return await Proxy.GetIdentificationAsync(m_context.Telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Gets typed mandatory and optional Machinery/DI identification values.
        /// </summary>
        public async ValueTask<GranulateDryingIdentificationClient> IdentificationValuesAsync(
            CancellationToken cancellationToken = default)
        {
            return new GranulateDryingIdentificationClient(
                m_context, await IdentificationAsync(cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Gets common machine-configuration operations through their generated proxy.
        /// </summary>
        public async ValueTask<MachineConfigurationTypeClient> ConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            return await Proxy.GetMachineConfigurationAsync(m_context.Telemetry,
                cancellationToken).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Gets the mandatory device-operation interface.
        /// </summary>
        public async ValueTask<GranulateDryingOperationClient> OperationAsync(
            CancellationToken cancellationToken = default)
        {
            OperationTypeClient proxy = await Proxy.GetOperationAsync(m_context.Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
            return new GranulateDryingOperationClient(m_context, proxy);
        }

        /// <summary>
        /// Gets optional HMI user login and current-user information, or null when not selected.
        /// </summary>
        public async ValueTask<GranulateDryingUserClient?> UsersAsync(CancellationToken cancellationToken = default)
        {
            UserLoginTypeClient? proxy = await Proxy.GetUserLoginAsync(m_context.Telemetry, cancellationToken)
                .ConfigureAwait(false);
            return proxy == null ? null : new GranulateDryingUserClient(m_context, proxy);
        }

        /// <summary>
        /// Discovers actual BinType instances below the generated Bins container.
        /// </summary>
        public async ValueTask<ArrayOf<GranulateDryingBinClient>> BinsAsync(
            CancellationToken cancellationToken = default)
        {
            DryerTypeClient dryer = await DryerAsync(cancellationToken).ConfigureAwait(false);
            BinsTypeClient bins = await dryer.GetBinsAsync(m_context.Telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
            var result = new List<GranulateDryingBinClient>();
            foreach (NodeId node in await ChildrenAsync(bins.ObjectId, ObjectTypeIds.BinType, cancellationToken)
                .ConfigureAwait(false))
            {
                result.Add(new GranulateDryingBinClient(m_context, node));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Discovers optional DesiccantBedType instances; an absent group produces an empty array.
        /// </summary>
        public async ValueTask<ArrayOf<GranulateDryingBedClient>> DesiccantBedsAsync(
            CancellationToken cancellationToken = default)
        {
            DryerTypeClient dryer = await DryerAsync(cancellationToken).ConfigureAwait(false);
            DesiccantBedsTypeClient? beds = await dryer.GetDesiccantBedsAsync(m_context.Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (beds == null)
            {
                return [];
            }
            var result = new List<GranulateDryingBedClient>();
            foreach (NodeId node in await ChildrenAsync(beds.ObjectId, ObjectTypeIds.DesiccantBedType,
                cancellationToken)
                .ConfigureAwait(false))
            {
                result.Add(new GranulateDryingBedClient(m_context, node));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Discovers every selected maintenance category, including its attributed reset and optional instructions.
        /// </summary>
        public async ValueTask<ArrayOf<GranulateDryingMaintenanceClient>> MaintenanceAsync(
            CancellationToken cancellationToken = default)
        {
            GranulateDryingOperationClient operation = await OperationAsync(cancellationToken).ConfigureAwait(false);
            MaintenanceInformationTypeClient? information = await operation.Proxy
                .GetMaintenanceInformationAsync(m_context.Telemetry, cancellationToken).ConfigureAwait(false);
            if (information == null)
            {
                return [];
            }
            var result = new List<GranulateDryingMaintenanceClient>();
            foreach (NodeId node in await ChildrenAsync(
                information.ObjectId, ObjectTypeIds.GDDMaintenanceType, cancellationToken).ConfigureAwait(false))
            {
                result.Add(new GranulateDryingMaintenanceClient(m_context, node));
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Opens the mandatory generated Dryer object.
        /// </summary>
        private async ValueTask<DryerTypeClient> DryerAsync(CancellationToken cancellationToken)
        {
            return await Proxy.GetDryerAsync(m_context.Telemetry, cancellationToken).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Filters placeholder children by actual type, including vendor subtypes and duplicate references.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ChildrenAsync(
            NodeId parent,
            ExpandedNodeId expectedType,
            CancellationToken cancellationToken)
        {
            NodeId type = ExpandedNodeId.ToNodeId(expectedType, m_context.Session.NamespaceUris);
            var found = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            ArrayOf<ReferenceDescription> references = await m_context.BrowseAsync(parent, cancellationToken)
                .ConfigureAwait(false);
            for (int index = 0; index < references.Count; index++)
            {
                ReferenceDescription reference = references[index];
                NodeId actual = ExpandedNodeId.ToNodeId(reference.TypeDefinition, m_context.Session.NamespaceUris);
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, m_context.Session.NamespaceUris);
                if (!node.IsNull && !actual.IsNull && seen.Add(node) &&
                    await m_context.Session.NodeCache.IsTypeOfAsync(actual, type,
                        cancellationToken).ConfigureAwait(false))
                {
                    found.Add(node);
                }
            }
            return found.ToArrayOf();
        }

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// Typed operation values and the generated identify/error/energy-reset methods.
    /// </summary>
    public sealed class GranulateDryingOperationClient
    {
        /// <summary>
        /// Opens one generated Operation instance.
        /// </summary>
        internal GranulateDryingOperationClient(GranulateDryingClientContext context, OperationTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets every generated GDD operation method.
        /// </summary>
        public OperationTypeClient Proxy { get; }

        /// <summary>
        /// Gets the stable device mapping number.
        /// </summary>
        public GranulateDryingVariable<uint> DeviceMappingNumber =>
            Value("DeviceMappingNumber", GranulateDryingCodecs.UInt32, true);

        /// <summary>
        /// Gets the highest active alarm severity.
        /// </summary>
        public GranulateDryingVariable<ushort> HighestActiveAlarmSeverity =>
            Value("HighestActiveAlarmSeverity", GranulateDryingCodecs.UInt16);

        /// <summary>
        /// Gets the full generated classified-error array.
        /// </summary>
        public GranulateDryingVariable<ArrayOf<ClassifiedActiveErrorDataType>> ActiveErrors =>
            Value("ActiveErrors", GranulateDryingCodecs.Structures<ClassifiedActiveErrorDataType>());

        /// <summary>
        /// Gets persistent remote-control enablement.
        /// </summary>
        public GranulateDryingVariable<bool> RemoteControlOpcUa =>
            Value("RemoteControlOpcUa", GranulateDryingCodecs.Boolean);

        /// <summary>
        /// Gets the selected power-failure behavior.
        /// </summary>
        public GranulateDryingVariable<MainFailureBehaviourEnumeration> MainFailureBehaviour =>
            Value("MainFailureBehaviour", GranulateDryingCodecs.Enumeration<MainFailureBehaviourEnumeration>(), true);

        /// <summary>
        /// Gets resettable energy in kWh.
        /// </summary>
        public GranulateDryingVariable<double> EnergyConsumptionResettable =>
            Value("EnergyConsumptionResettable", GranulateDryingCodecs.Double);

        /// <summary>
        /// Gets the energy-consumption limit in percent.
        /// </summary>
        public GranulateDryingVariable<double> EnergyConsumptionLimit =>
            Value("EnergyConsumptionLimit", GranulateDryingCodecs.Double, true);

        /// <summary>
        /// Gets the generated optional common EnergyType proxy.
        /// </summary>
        public ValueTask<EnergyTypeClient?> TotalEnergyAsync(CancellationToken cancellationToken = default) =>
            Proxy.GetTotalEnergyAsync(m_context.Telemetry, cancellationToken);

        /// <summary>
        /// Creates an operation-qualified variable.
        /// </summary>
        /// <typeparam name="T">The declared variable value type.</typeparam>
        private GranulateDryingVariable<T> Value<T>(string name, GranulateDryingCodec<T> codec,
            bool writable = false) =>
            m_context.Variable(Proxy.ObjectId, codec, writable, Gdd(name));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }

    /// <summary>
    /// The generated HMI login/logout methods and actual-user values, separate from UA identity.
    /// </summary>
    public sealed class GranulateDryingUserClient
    {
        /// <summary>
        /// Opens a selected UserLogin instance.
        /// </summary>
        internal GranulateDryingUserClient(GranulateDryingClientContext context, UserLoginTypeClient proxy)
        {
            m_context = context;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the generated exact-signature login/logout methods.
        /// </summary>
        public UserLoginTypeClient Proxy { get; }

        /// <summary>
        /// Gets whether the HMI has an active user.
        /// </summary>
        public GranulateDryingVariable<bool> IsPresent =>
            Value("IsPresent", GranulateDryingCodecs.Boolean);

        /// <summary>
        /// Gets the current HMI user identifier.
        /// </summary>
        public GranulateDryingVariable<string> Id => Value("Id", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the current HMI user's name.
        /// </summary>
        public GranulateDryingVariable<string> Name => Value("Name", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the optional card identifier.
        /// </summary>
        public GranulateDryingVariable<string> CardUid => Value("CardUid", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the machine's user-level label.
        /// </summary>
        public GranulateDryingVariable<string> UserLevel => Value("UserLevel", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the machine's user-role label.
        /// </summary>
        public GranulateDryingVariable<string> UserRole => Value("UserRole", GranulateDryingCodecs.String);

        /// <summary>
        /// Gets the user's locale.
        /// </summary>
        public GranulateDryingVariable<string> Language => Value("Language", GranulateDryingCodecs.String);

        /// <summary>
        /// Resolves a common UserType property under the generated GDD ActualUser child.
        /// </summary>
        /// <typeparam name="T">The declared property value type.</typeparam>
        private GranulateDryingVariable<T> Value<T>(string name, GranulateDryingCodec<T> codec) =>
            m_context.Variable(Proxy.ObjectId, codec, false, Gdd("ActualUser"), Common(name));

        /// <summary>
        /// The connected-session context.
        /// </summary>
        private readonly GranulateDryingClientContext m_context;
    }
}
