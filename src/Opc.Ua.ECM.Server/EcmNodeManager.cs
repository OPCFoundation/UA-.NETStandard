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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.IA;
using Opc.Ua.Redaction;
using Opc.Ua.Server;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Standalone address-space owner for the DI, IA and ECM models.
    /// </summary>
    public sealed class EcmNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates an ECM owner for direct construction or fluent hosting.
        /// </summary>
        public EcmNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner = null,
            ArrayOf<IDiModelProvider> providers = default)
            : base(server, configuration, postSetupRunner, providers, [Namespaces.ECM, IA.Namespaces.IA])
        {
            m_ecmLogger = server.Telemetry.CreateLogger<EcmNodeManager>();
            m_conformance = EcmConformanceContributor.For(this, server);
        }

        /// <summary>
        /// Creates provider-backed standby and optional WOL objects beneath a selected parent.
        /// Providers are borrowed; the manager owns and releases the returned binding.
        /// </summary>
        public ValueTask<EcmPowerBinding> CreatePowerAsync(
            QualifiedName browseName,
            IEcmStandbyProvider provider,
            NodeState? parent = null,
            IEcmWolProvider? wolProvider = null,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A standby BrowseName is required.", nameof(browseName));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            return ExecuteNodeManagerOperationAsync(ct => CreatePowerCoreAsync(
                browseName, provider, parent, wolProvider, exclusiveAccess, authorize, ct), cancellationToken);
        }

        /// <summary>
        /// Creates a WOL-only object without publishing standby management.
        /// The manager owns the binding; the application retains provider ownership.
        /// </summary>
        public ValueTask<EcmWolBinding> CreateWolAsync(
            QualifiedName browseName,
            IEcmWolProvider provider,
            NodeState? parent = null,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A WOL BrowseName is required.", nameof(browseName));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            return ExecuteNodeManagerOperationAsync(ct => CreateWolCoreAsync(
                browseName, provider, parent, exclusiveAccess, authorize, ct), cancellationToken);
        }

        /// <summary>
        /// Creates a provider-backed standalone E0, E1, E2, E3, D0 or D1 measurement point beneath a selected
        /// parent. Statistic profiles require an <see cref="IEcmStatisticsProvider"/>.
        /// The provider remains application-owned; the manager owns and releases the returned binding.
        /// </summary>
        /// <param name="browseName">The qualified measurement-point BrowseName.</param>
        /// <param name="provider">The application-owned measurement provider.</param>
        /// <param name="parent">The optional parent; DI DeviceSet is used when omitted.</param>
        /// <param name="exclusiveAccess">The optional DI lock service for ApplicationTag writes.</param>
        /// <param name="authorize">The optional asynchronous write-authorization policy.</param>
        /// <param name="cancellationToken">Cancels creation and the initial provider read.</param>
        public ValueTask<EcmMeasurementBinding> CreateMeasurementAsync(
            QualifiedName browseName,
            IEcmMeasurementProvider provider,
            NodeState? parent = null,
            ILockService? exclusiveAccess = null,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize = null,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A measurement BrowseName is required.", nameof(browseName));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            return ExecuteNodeManagerOperationAsync(ct => CreateMeasurementCoreAsync(
                browseName, provider, parent, exclusiveAccess, authorize, ct), cancellationToken);
        }

        /// <summary>
        /// Creates a custom accuracy domain in the ECM AccuracyDomains folder.
        /// </summary>
        /// <param name="browseName">The qualified domain name.</param>
        /// <param name="enumValues">The non-empty set of accuracy-class values.</param>
        /// <param name="cancellationToken">Cancels creation.</param>
        public ValueTask<AccuracyDomainState> CreateAccuracyDomainAsync(
            QualifiedName browseName,
            ArrayOf<EnumValueType> enumValues,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("An accuracy-domain BrowseName is required.", nameof(browseName));
            }
            if (enumValues.IsNull || enumValues.Count == 0)
            {
                throw new ArgumentException("An accuracy domain must define at least one class.", nameof(enumValues));
            }
            var detachedValues = new EnumValueType[enumValues.Count];
            for (int ii = 0; ii < enumValues.Count; ii++)
            {
                EnumValueType value = enumValues[ii] ??
                    throw new ArgumentException("An accuracy domain cannot contain a null class.", nameof(enumValues));
                detachedValues[ii] = EcmOwnership.Own(value, nameof(enumValues));
            }
            ArrayOf<EnumValueType> detachedEnumValues = detachedValues.ToArrayOf();
            return ExecuteNodeManagerOperationAsync(
                ct => CreateAccuracyDomainCoreAsync(browseName, detachedEnumValues, ct), cancellationToken);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaECM(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>
        /// Releases bindings and the creation gate after admitted graph creation has finished.
        /// </summary>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                m_creation.Dispose();
            }
        }

        /// <summary>
        /// Publishes a provider-backed graph within one admitted manager operation.
        /// </summary>
        private async ValueTask<EcmPowerBinding> CreatePowerCoreAsync(
            QualifiedName browseName,
            IEcmStandbyProvider provider,
            NodeState? parent,
            IEcmWolProvider? wolProvider,
            ILockService? exclusiveAccess,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize,
            CancellationToken cancellationToken)
        {
            NodeState owner = parent ?? FindPredefinedNode(NodeId.Create(
                Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, SystemContext.NamespaceUris)) ??
                throw new InvalidOperationException("The DI DeviceSet is not available.");
            await m_creation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (owner.FindChild(SystemContext, browseName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                EnergyStandbyManagementState standby =
                    SystemContext.CreateInstanceOfEnergyStandbyManagementType(owner, browseName);
                standby.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                EcmPowerBinding? binding = null;
                try
                {
                    EnergyDevicePowerOffState? powerOff = null;
                    if (wolProvider != null)
                    {
                        powerOff = SystemContext.CreateInstanceOfEnergyDevicePowerOffType(
                            standby, new QualifiedName("DevicePowerOff", InstanceNamespaceIndex));
                        powerOff.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                        standby.AddChild(powerOff);
                    }
                    binding = new EcmPowerBinding(
                        SystemContext, standby, provider, powerOff, wolProvider, exclusiveAccess, authorize);
                    await binding.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    owner.AddChild(standby);
                    await AddPredefinedNodeAsync(standby, cancellationToken).ConfigureAwait(false);
                    RegisterResource(binding);
                    m_conformance.Register(binding);
                    await m_conformance.PublishAsync(binding, cancellationToken).ConfigureAwait(false);
                    return binding;
                }
                catch
                {
                    await RollbackPowerAsync(owner, standby, binding).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                m_creation.Release();
            }
        }

        /// <summary>
        /// Publishes a standalone WOL graph under the same creation and rollback rules as standby.
        /// </summary>
        private async ValueTask<EcmWolBinding> CreateWolCoreAsync(
            QualifiedName browseName,
            IEcmWolProvider provider,
            NodeState? parent,
            ILockService? exclusiveAccess,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize,
            CancellationToken cancellationToken)
        {
            NodeState owner = parent ?? FindPredefinedNode(NodeId.Create(
                Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, SystemContext.NamespaceUris)) ??
                throw new InvalidOperationException("The DI DeviceSet is not available.");
            await m_creation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (owner.FindChild(SystemContext, browseName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                EnergyDevicePowerOffState powerOff =
                    SystemContext.CreateInstanceOfEnergyDevicePowerOffType(owner, browseName);
                powerOff.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                EcmWolBinding? binding = null;
                try
                {
                    binding = new EcmWolBinding(SystemContext, powerOff, provider, exclusiveAccess, authorize);
                    owner.AddChild(powerOff);
                    await AddPredefinedNodeAsync(powerOff, cancellationToken).ConfigureAwait(false);
                    RegisterResource(binding);
                    m_conformance.Register(binding);
                    await m_conformance.PublishAsync(binding, cancellationToken).ConfigureAwait(false);
                    return binding;
                }
                catch
                {
                    await RollbackPowerAsync(owner, powerOff, binding).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                m_creation.Release();
            }
        }

        /// <summary>
        /// Publishes one standalone measurement graph within an admitted manager operation.
        /// </summary>
        private async ValueTask<EcmMeasurementBinding> CreateMeasurementCoreAsync(
            QualifiedName browseName,
            IEcmMeasurementProvider provider,
            NodeState? parent,
            ILockService? exclusiveAccess,
            Func<ISystemContext, CancellationToken, ValueTask<bool>>? authorize,
            CancellationToken cancellationToken)
        {
            NodeState owner = parent ?? FindPredefinedNode(NodeId.Create(
                Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, SystemContext.NamespaceUris)) ??
                throw new InvalidOperationException("The DI DeviceSet is not available.");
            await m_creation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (owner.FindChild(SystemContext, browseName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                EnergyMeasurementState measurement =
                    SystemContext.CreateInstanceOfEnergyMeasurementType(owner, browseName);
                measurement.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                EcmMeasurementBinding? binding = null;
                try
                {
                    binding = new EcmMeasurementBinding(
                        SystemContext, measurement, provider, exclusiveAccess, authorize,
                        nodeId => FindPredefinedNode(nodeId));
                    await binding.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    owner.AddChild(measurement);
                    await AddPredefinedNodeAsync(measurement, cancellationToken).ConfigureAwait(false);
                    RegisterResource(binding);
                    m_conformance.Register(binding);
                    await m_conformance.PublishAsync(binding, cancellationToken).ConfigureAwait(false);
                    return binding;
                }
                catch
                {
                    await RollbackPowerAsync(owner, measurement, binding).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                m_creation.Release();
            }
        }

        /// <summary>
        /// Publishes a custom accuracy domain beneath the model's standard domain folder.
        /// </summary>
        private async ValueTask<AccuracyDomainState> CreateAccuracyDomainCoreAsync(
            QualifiedName browseName,
            ArrayOf<EnumValueType> enumValues,
            CancellationToken cancellationToken)
        {
            NodeId folderId = NodeId.Create(
                Objects.AccuracyDomains, Namespaces.ECM, SystemContext.NamespaceUris);
            NodeState owner = FindPredefinedNode(folderId) ??
                throw new InvalidOperationException("The ECM AccuracyDomains folder is not available.");
            await m_creation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (owner.FindChild(SystemContext, browseName) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                var values = new System.Collections.Generic.HashSet<long>();
                var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                for (int ii = 0; ii < enumValues.Count; ii++)
                {
                    EnumValueType value = enumValues[ii] ??
                        throw new ArgumentException(
                            "An accuracy domain cannot contain a null class.",
                            nameof(enumValues));
                    string name = value.DisplayName.Text ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name) || !values.Add(value.Value) || !names.Add(name))
                    {
                        throw new ArgumentException(
                            "Accuracy classes must have unique values and non-empty, unique display names.",
                            nameof(enumValues));
                    }
                }

                AccuracyDomainState domain = SystemContext.CreateInstanceOfAccuracyDomainType(owner, browseName);
                domain.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
                Required(domain.EnumValues).Value = enumValues;
                owner.AddChild(domain);
                try
                {
                    await AddPredefinedNodeAsync(domain, cancellationToken).ConfigureAwait(false);
                    return domain;
                }
                catch
                {
                    await RollbackPowerAsync(owner, domain, null).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                m_creation.Release();
            }
        }

        /// <summary>
        /// Gets a required generated child or throws if the published model is incomplete.
        /// </summary>
        /// <typeparam name="T">The generated child type.</typeparam>
        private static T Required<T>(T? value) where T : class
        {
            return value ?? throw new InvalidOperationException("The generated ECM model is incomplete.");
        }

        /// <summary>
        /// Attempts each cleanup step without replacing the original creation exception.
        /// </summary>
        private async ValueTask RollbackPowerAsync(
            NodeState owner,
            BaseInstanceState standby,
            IAsyncDisposable? binding)
        {
            try
            {
                if (binding != null)
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                LogRollbackFailure(exception);
            }
            try
            {
                if (ReferenceEquals(FindPredefinedNode(standby.NodeId), standby))
                {
                    await DeleteNodeAsync(SystemContext, standby.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                LogRollbackFailure(exception);
            }
            try
            {
                owner.RemoveChild(standby);
            }
            catch (Exception exception)
            {
                LogRollbackFailure(exception);
            }
        }

        /// <summary>
        /// Records a cleanup error without exposing provider or address-space details.
        /// </summary>
        private void LogRollbackFailure(Exception exception)
        {
            if (m_ecmLogger.IsEnabled(LogLevel.Error))
            {
                m_ecmLogger.PowerRollbackFailed(exception.ToString());
            }
        }

        /// <summary>The manager's diagnostic logger.</summary>
        private readonly ILogger m_ecmLogger;

        /// <summary>The ECM capabilities published by this manager.</summary>
        private readonly EcmConformanceContributor m_conformance;

        /// <summary>Serializes duplicate checks and publication.</summary>
        private readonly SemaphoreSlim m_creation = new(1, 1);
    }

    /// <summary>
    /// Source-generated ECM graph-creation diagnostics.
    /// </summary>
    internal static partial class EcmNodeManagerLog
    {
        /// <summary>
        /// Reports a cleanup failure while preserving the original creation error.
        /// </summary>
        [LoggerMessage(
            EventId = EcmServerEventIds.EcmNodeManager,
            Level = LogLevel.Error,
            Message = "Could not remove the ECM power graph after its creation failed: {Error}")]
        public static partial void PowerRollbackFailed(this ILogger logger, [SensitiveText] string error);
    }
}
