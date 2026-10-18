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
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Composes a native GMS instance on an already selected Machinery namespace owner.
    /// </summary>
    public static class GmsBuilderExtensions
    {
        /// <summary>
        /// Starts an explicitly provider-backed GMS with mandatory structure only.
        /// </summary>
        public static GmsBuilder AddGms(this IMachineryBuildContext context, string browseName)
        {
            return new GmsBuilder(context, browseName);
        }
    }

    /// <summary>
    /// Configures one native GMS root using shared MachineTool, Machinery and result bindings.
    /// </summary>
    public sealed partial class GmsBuilder
    {
        /// <summary>
        /// Creates a direct builder without DI or another namespace manager.
        /// </summary>
        public GmsBuilder(IMachineryBuildContext context, string browseName)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException("A GMS browse name is required.", nameof(browseName));
            }
            m_name = browseName;
        }

        /// <summary>
        /// Supplies this instance's equipment provider; ownership remains with the application.
        /// </summary>
        public GmsBuilder WithProvider(IGmsProvider provider)
        {
            EnsureMutable();
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        /// Configures the existing GMS-derived DI/Machinery nameplate.
        /// </summary>
        public GmsBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            EnsureMutable();
            m_identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Selects the required per-instance result store and existing bounded result capabilities.
        /// No store is silently resolved from another instance or created on the caller's behalf.
        /// </summary>
        public GmsBuilder WithResults(IGmsResultStore store, GmsResultBindingOptions? options = null)
        {
            EnsureMutable();
            m_results = store ?? throw new ArgumentNullException(nameof(store));
            m_resultOptions = options ?? new GmsResultBindingOptions { EnableFileTransfer = false };
            return this;
        }

        /// <summary>
        /// Selects optional GMS observation surfaces and their aggregate resource bounds.
        /// </summary>
        public GmsBuilder WithOptions(GmsSystemOptions options)
        {
            EnsureMutable();
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            return this;
        }

        /// <summary>
        /// Selects inherited MachineTool observation capabilities, without replacing native GMS factories.
        /// </summary>
        public GmsBuilder WithMachineToolFeatures(MachineToolFeatures features)
        {
            EnsureMutable();
            if ((features & ~MachineToolFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            m_machineFeatures = features;
            return this;
        }

        /// <summary>
        /// Configures retained production limits and optional inherited behavior while retaining native GMS factories.
        /// </summary>
        public GmsBuilder WithProduction(MachineToolProductionOptions options)
        {
            EnsureMutable();
            m_production = options ?? throw new ArgumentNullException(nameof(options));
            if (options.CreateJobAsync != null || options.CreatePartAsync != null)
            {
                throw new ArgumentException("GMS supplies the native job and part factories.", nameof(options));
            }
            m_options = m_options with { Features = m_options.Features | GmsFeatures.Production };
            return this;
        }

        /// <summary>
        /// Selects inherited forecasts alongside native calibration, using the existing generic prognosis module.
        /// </summary>
        public GmsBuilder WithPrognoses(MachineToolPrognosisOptions options)
        {
            EnsureMutable();
            m_prognoses = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>
        /// Selects the existing complete MachineTool error/alert/message provider independently of sensor warnings.
        /// </summary>
        public GmsBuilder WithNotifications(MachineToolNotificationOptions options)
        {
            EnsureMutable();
            m_notifications = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>
        /// Configures additional shared Machinery bindings on this actual unpublished GMS root.
        /// The callback must not build, register or dispose the supplied builder.
        /// </summary>
        public GmsBuilder WithMachinery(Action<IMachineBuilder<GMSState>> configure)
        {
            EnsureMutable();
            m_machinery.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
            return this;
        }

        /// <summary>
        /// Builds and registers exactly one GMS root, with all mandatory bindings established before success.
        /// </summary>
        public async ValueTask<GmsHandle> BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureMutable();
            if (Interlocked.CompareExchange(ref m_building, 1, 0) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The GMS is already being built.");
            }
            GmsResultBinding? binding = null;
            MachineToolRuntime<GMSState>? machine = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IGmsResultStore results = m_results ?? throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "GMS requires its own result store.");
                IGmsProvider provider = m_provider ?? throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "GMS requires an equipment observation provider.");
                Action<MachineryIdentificationData> identification = m_identification ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "GMS requires a machine nameplate.");
                m_options.Validate();
                var nativeScope = new GmsBindingScope(m_context, provider, m_options);
                await using (nativeScope.ConfigureAwait(false))
                {
                    GmsSystemBinding systems = nativeScope.Binding;
                    MachineToolBuilder<GMSState> author = m_context.AddMachineTool(m_name, systems.CreateRoot)
                        .WithProvider(systems)
                        .WithIdentification(identification)
                        .WithFeatures(m_machineFeatures)
                        .WithProduction(systems.ProductionOptions(m_production))
                        .WithEquipment(systems.EquipmentOptions())
                        .WithPrognoses(systems.PrognosisOptions(m_prognoses))
                        .WithObservationProjection(systems.Prepare)
                        .WithMachinery(builder =>
                        {
                            systems.Configure(builder);
                            builder.WithGmsResults(results, out binding, m_resultOptions);
                            foreach (Action<IMachineBuilder<GMSState>> configure in m_machinery)
                            {
                                configure(builder);
                            }
                        });
                    if (m_notifications != null)
                    {
                        author.WithNotifications(m_notifications);
                    }
                    machine = await author.BuildAsync(cancellationToken).ConfigureAwait(false);
                    var handle = new GmsHandle(
                        machine, binding!.CreatePublisher(machine.Machinery), systems, m_resultOptions);
                    systems.RegisterCapabilities(handle);
                    nativeScope.Complete();
                    m_built = true;
                    return handle;
                }
            }
            catch
            {
                if (machine != null)
                {
                    await machine.DisposeAsync().ConfigureAwait(false);
                }
                binding?.Dispose();
                throw;
            }
            finally
            {
                Volatile.Write(ref m_building, 0);
            }
        }

        /// <summary>
        /// Rejects mutation after publication and concurrent construction.
        /// </summary>
        private void EnsureMutable()
        {
            if (m_built || Volatile.Read(ref m_building) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The GMS builder is not mutable.");
            }
        }

        /// <summary>The selected existing namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The application instance browse name.</summary>
        private readonly string m_name;

        /// <summary>The application-owned observations.</summary>
        private IGmsProvider? m_provider;

        /// <summary>The configuration of the actual derived nameplate.</summary>
        private Action<MachineryIdentificationData>? m_identification;

        /// <summary>The explicit instance-isolated result store.</summary>
        private IGmsResultStore? m_results;

        /// <summary>The existing result-module limits and optional features.</summary>
        private GmsResultBindingOptions m_resultOptions = new() { EnableFileTransfer = false };

        /// <summary>The explicitly selected native surfaces and finite limits.</summary>
        private GmsSystemOptions m_options = new();

        /// <summary>The explicitly selected inherited observation surfaces.</summary>
        private MachineToolFeatures m_machineFeatures;

        /// <summary>The existing retained-production module's limits and behavior options.</summary>
        private MachineToolProductionOptions m_production = new();

        /// <summary>The existing generic prognosis capabilities and bounds.</summary>
        private MachineToolPrognosisOptions m_prognoses = new();

        /// <summary>The explicitly selected existing generic notification provider.</summary>
        private MachineToolNotificationOptions? m_notifications;

        /// <summary>Additional shared bindings, applied only to this root before publication.</summary>
        private readonly List<Action<IMachineBuilder<GMSState>>> m_machinery = [];

        /// <summary>Prevents concurrent or repeated builds.</summary>
        private int m_building;

        /// <summary>Records successful registration.</summary>
        private bool m_built;

    }

    /// <summary>
    /// Owns a staged native binding until the existing Machinery registration and facade take ownership together.
    /// </summary>
    internal sealed class GmsBindingScope : IAsyncDisposable
    {
        /// <summary>
        /// Stages the per-instance native resource.
        /// </summary>
        internal GmsBindingScope(IMachineryBuildContext context, IGmsProvider provider, GmsSystemOptions options)
        {
            Binding = new GmsSystemBinding(context, provider, options);
        }

        /// <summary>
        /// Gets the resource staged by this scope.
        /// </summary>
        internal GmsSystemBinding Binding { get; }

        /// <summary>
        /// Transfers successful construction to the node-scoped Machinery registration.
        /// </summary>
        internal void Complete()
        {
            m_completed = true;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_completed ? default : Binding.DisposeAsync();
        }

        /// <summary>Whether the containing machine owns the resource.</summary>
        private bool m_completed;
    }
}
