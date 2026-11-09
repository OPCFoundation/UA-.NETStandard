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

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Creates MachineTool instances without introducing another DI, Machinery or ISA95 namespace owner.
    /// </summary>
    public static class MachineToolBuilderExtensions
    {
        /// <summary>
        /// Starts a mandatory-only MachineTool with explicitly supplied equipment observations.
        /// </summary>
        /// <param name="context">The selected Machinery owner's build context.</param>
        /// <param name="browseName">The instance's application-owned browse name.</param>
        public static MachineToolBuilder<MachineToolState> AddMachineTool(
            this IMachineryBuildContext context,
            string browseName)
        {
            return new MachineToolBuilder<MachineToolState>(
                context, browseName, static (system, parent, name) =>
                    system.CreateInstanceOfMachineToolType(parent, name));
        }

        /// <summary>
        /// Composes the same runtime on an actual generated MachineTool subtype, including GMS.
        /// </summary>
        /// <typeparam name="TState">The generated companion or vendor subtype.</typeparam>
        /// <param name="context">The existing namespace owner's build context.</param>
        /// <param name="browseName">The instance's application-owned browse name.</param>
        /// <param name="createState">The typed generated factory, invoked only after provider validation.</param>
        public static MachineToolBuilder<TState> AddMachineTool<TState>(
            this IMachineryBuildContext context,
            string browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createState)
            where TState : MachineToolState
        {
            return new MachineToolBuilder<TState>(context, browseName, createState);
        }
    }

    /// <summary>
    /// Stages one generated machine and binds its observation provider before publication.
    /// </summary>
    /// <typeparam name="TState">The actual generated MachineTool subtype.</typeparam>
    public sealed partial class MachineToolBuilder<TState> where TState : MachineToolState
    {
        /// <summary>
        /// Creates a builder over the shared owner with a typed instance factory.
        /// </summary>
        public MachineToolBuilder(
            IMachineryBuildContext context,
            string browseName,
            Func<ISystemContext, NodeState, QualifiedName, TState> createState)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_createState = createState ?? throw new ArgumentNullException(nameof(createState));
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A machine browse name is required.", nameof(browseName));
            }
            m_name = new QualifiedName(browseName, context.InstanceNamespaceIndex);
        }

        /// <summary>
        /// Selects the instance's explicit equipment provider; it remains application-owned.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public MachineToolBuilder<TState> WithProvider(IMachineToolProvider provider)
        {
            EnsureMutable();
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        /// Configures the shared DI/Machinery nameplate without replacing MachineTool's derived identification.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public MachineToolBuilder<TState> WithIdentification(Action<MachineryIdentificationData> configure)
        {
            EnsureMutable();
            m_identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Selects the optional surfaces to publish from the equipment provider.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public MachineToolBuilder<TState> WithFeatures(MachineToolFeatures features)
        {
            EnsureMutable();
            if ((features & ~MachineToolFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            m_features = features;
            return this;
        }

        /// <summary>
        /// Declares deployment monitoring requirements for honest capability evaluation against configured quotas.
        /// This does not change server quotas or implicitly enable any optional machine feature.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="requirements"/> is <c>null</c>.</exception>
        public MachineToolBuilder<TState> WithMonitoringRequirements(MachineToolMonitoringRequirements requirements)
        {
            EnsureMutable();
            if (requirements == null)
            {
                throw new ArgumentNullException(nameof(requirements));
            }
            requirements.Validate();
            m_monitoringRequirements = requirements;
            return this;
        }

        /// <summary>
        /// Stages companion-specific observations in the same validated monitoring/production transaction.
        /// Preparation runs after all generic modules prepare and before any commit; it may reject the observation.
        /// It must not mutate published nodes, perform I/O, reenter the runtime or acquire equipment observations.
        /// The returned action must perform only validated, nonthrowing assignments. It runs after generic assignments
        /// and before any change-mask or event publication. A later call replaces the earlier projection.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public MachineToolBuilder<TState> WithObservationProjection(
            Func<ISystemContext, TState, MachineToolSnapshot, Action> prepare)
        {
            EnsureMutable();
            m_observationProjection = prepare ?? throw new ArgumentNullException(nameof(prepare));
            return this;
        }

        /// <summary>
        /// Configures bounded retained production, including typed GMS job/part factories.
        /// The supplied feature flags are added to the current selection; a later WithFeatures call can replace it.
        /// A missing delivery delegate uses the selected namespace owner's real publication barrier, never a no-op.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public MachineToolBuilder<TState> WithProduction(MachineToolProductionOptions options)
        {
            EnsureMutable();
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            WithFeatures(m_features | options.Features);
            m_productionOptions = options;
            return this;
        }

        /// <summary>
        /// Selects bounded tool and life observations and optional actual generated companion factories.
        /// A later WithFeatures call can replace the combined feature selection.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public MachineToolBuilder<TState> WithEquipment(MachineToolEquipmentOptions options)
        {
            EnsureMutable();
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            WithFeatures(m_features | options.Features);
            m_equipmentOptions = options;
            return this;
        }

        /// <summary>
        /// Configures explicit bounded prediction capabilities and optional actual generated companion factories.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public MachineToolBuilder<TState> WithPrognoses(MachineToolPrognosisOptions options)
        {
            EnsureMutable();
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            WithFeatures(m_features | options.Features);
            m_prognosisOptions = options;
            return this;
        }

        /// <summary>
        /// Validates and publishes the generated machine, returning its per-instance runtime.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<MachineToolRuntime<TState>> BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureMutable();
            if (Interlocked.CompareExchange(ref m_building, 1, 0) != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The MachineTool is already being built.");
            }
            TState? state = null;
            MachineToolRuntime<TState>? runtime = null;
            IMachineBuilder<TState>? builder = null;
            bool registrationStarted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMachineToolProvider provider = m_provider ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A MachineTool equipment provider is required.");
                Action<MachineryIdentificationData> identification = m_identification ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A machine nameplate is required.");
                MachineToolSnapshot snapshot = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                MachineToolSnapshotValidation.Validate(snapshot);
                cancellationToken.ThrowIfCancellationRequested();
                m_notificationOptions?.Validate();
                ValidateIntegratedServices();
                await PrepareIntegratedServicesAsync(cancellationToken).ConfigureAwait(false);
                MachineToolNotificationSnapshot? notifications = null;
                if (m_notificationOptions != null)
                {
                    TimeProvider clock = m_context.GetService<TimeProvider>() ?? TimeProvider.System;
                    using CancellationTokenSource timeout =
                        clock.CreateCancellationTokenSource(m_notificationOptions.RefreshTimeout);
                    using var linked =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    notifications = await m_notificationOptions.Provider!.ReadAsync(0, linked.Token)
                        .ConfigureAwait(false);
                    MachineToolNotificationController.ValidateSnapshot(
                        m_context, m_notificationOptions, notifications, 0);
                }
                cancellationToken.ThrowIfCancellationRequested();
                state = m_createState(m_context.Context, m_context.MachinesFolder, m_name);
                if (m_context.Manager.FindPredefinedNode(state.NodeId) != null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists,
                        "The typed factory must return an unpublished machine.");
                }
                runtime = new MachineToolRuntime<TState>(
                    m_context, state, provider, m_features, m_productionOptions, m_equipmentOptions, m_prognosisOptions,
                    m_observationProjection);
                builder = m_context.AddMachine(state, m_name);
                builder.WithIdentification(state.Identification!, data =>
                {
                    identification(data);
                    if ((m_features & MachineToolFeatures.AdditionalIdentification) != 0 &&
                        (data.ComponentName.IsNull ||
                            data.Model.IsNull ||
                            data.YearOfConstruction == null ||
                            data.MonthOfConstruction == null ||
                            string.IsNullOrWhiteSpace(data.DeviceClass)))
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            "Additional identification requires ComponentName, Model, year, month and DeviceClass.");
                    }
                });
                ConfigureIntegratedServices(builder, state, runtime);
                ConfigureComponents(builder, state, runtime);
                await runtime.InitializeAsync(snapshot, cancellationToken).ConfigureAwait(false);
                if (m_notificationOptions != null)
                {
                    await runtime.InitializeNotificationsAsync(m_notificationOptions, notifications!, cancellationToken)
                        .ConfigureAwait(false);
                }
                runtime.ConfigureCapabilities(m_monitoringRequirements, m_jobManagement != null);
                builder.WithRegistrationBinding(async (_, _, ct) =>
                {
                    m_context.Manager.AddRootNotifier(state);
                    if (runtime.Notifications != null)
                    {
                        await runtime.Notifications.PublishPendingAsync(ct).ConfigureAwait(false);
                    }
                    return runtime.CreateRegistration();
                });
                registrationStarted = true;
                IMachineHandle<TState> handle = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
                runtime.SetMachineryHandle(handle);
                runtime.RegisterCapabilities();
                m_built = true;
                return runtime;
            }
            catch
            {
                if (builder != null && !registrationStarted)
                {
                    await builder.AbortAsync().ConfigureAwait(false);
                }
                if (runtime != null)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
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
        /// Validates opt-in integrated services before allocating the generated instance.
        /// An unimplemented private partial hook is omitted by the compiler.
        /// </summary>
        partial void ValidateIntegratedServices();

        /// <summary>
        /// Prepares selected shared services asynchronously before allocating or publishing the generated machine.
        /// </summary>
        private partial ValueTask PrepareIntegratedServicesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Configures real shared registration bindings after nameplate selection and before runtime publication.
        /// An unimplemented private partial hook is omitted by the compiler.
        /// </summary>
        partial void ConfigureIntegratedServices(
            IMachineBuilder<TState> builder, TState state, MachineToolRuntime<TState> runtime);

        /// <summary>
        /// Releases factory allocations even when adoption failed before acquiring a Machinery build scope.
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
        /// Rejects changes after successful registration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void EnsureMutable()
        {
            if (m_built || Volatile.Read(ref m_building) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The MachineTool is already built.");
            }
        }

        /// <summary>The namespace owner's build context.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The application-owned root browse name.</summary>
        private readonly QualifiedName m_name;

        /// <summary>The generated subtype factory.</summary>
        private readonly Func<ISystemContext, NodeState, QualifiedName, TState> m_createState;

        /// <summary>The selected application-owned provider.</summary>
        private IMachineToolProvider? m_provider;

        /// <summary>The shared nameplate configuration.</summary>
        private Action<MachineryIdentificationData>? m_identification;

        /// <summary>Whether registration has succeeded.</summary>
        private bool m_built;

        /// <summary>Excludes configuration changes and competing builds while acquisition is pending.</summary>
        private int m_building;

        /// <summary>The explicitly selected optional runtime surfaces.</summary>
        private MachineToolFeatures m_features;

        /// <summary>The bounded production configuration and optional companion factories.</summary>
        private MachineToolProductionOptions m_productionOptions = new();

        /// <summary>The bounded equipment configuration and typed tool factories.</summary>
        private MachineToolEquipmentOptions m_equipmentOptions = new();

        /// <summary>The bounded prognosis configuration and typed companion factories.</summary>
        private MachineToolPrognosisOptions m_prognosisOptions = new();

        /// <summary>The deployment requirements used to evaluate configured monitoring capacity.</summary>
        private MachineToolMonitoringRequirements m_monitoringRequirements = new();

        /// <summary>The optional pure companion observation preparation and no-throw assignment projection.</summary>
        private Func<ISystemContext, TState, MachineToolSnapshot, Action>? m_observationProjection;
    }
}
