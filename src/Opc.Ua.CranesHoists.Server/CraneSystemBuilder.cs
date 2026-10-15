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
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Stages one independently provided, native crane system for a single awaited registration.
    /// </summary>
    public sealed class CraneSystemBuilder
    {
        /// <summary>
        /// Creates a system definition without reserving or publishing address-space nodes.
        /// </summary>
        internal CraneSystemBuilder(CranesHoistsBuildContext context, string id)
        {
            Context = context;
            ValidateId(id);
            Id = id;
            m_ids.Add(id);
        }

        /// <summary>
        /// Gets the system's browse name and provider identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the shared build context.
        /// </summary>
        internal CranesHoistsBuildContext Context { get; }

        /// <summary>
        /// Gets the source of real or explicitly simulated equipment data.
        /// </summary>
        internal ICraneDataProvider? Provider { get; private set; }

        /// <summary>
        /// Gets whether provider disposal belongs to the runtime.
        /// </summary>
        internal bool OwnsProvider { get; private set; }

        /// <summary>
        /// Gets the explicitly configured request provider.
        /// </summary>
        internal ICraneLimitProvider? LimitProvider { get; private set; }

        /// <summary>
        /// Gets the explicitly configured write authorizer.
        /// </summary>
        internal ICraneWriteAuthorizer? Authorizer { get; private set; }

        /// <summary>
        /// Gets the mandatory shared nameplate configuration.
        /// </summary>
        internal Action<MachineryIdentificationData>? Identification { get; private set; }

        /// <summary>
        /// Gets whether external-control state is selected.
        /// </summary>
        internal bool ExternalControl { get; private set; }

        /// <summary>
        /// Gets whether the standard global speed-limit request is selected.
        /// </summary>
        internal bool GlobalSpeedLimit { get; private set; }

        /// <summary>
        /// Gets whether optional Operational and Configuration groups are selected.
        /// </summary>
        internal bool FunctionalGroups { get; private set; }

        /// <summary>
        /// Gets whether Machinery Components discovery is selected.
        /// </summary>
        internal bool Components { get; private set; }

        /// <summary>
        /// Gets whether the existing Machinery Notifications add-in is selected.
        /// </summary>
        internal bool Events { get; private set; }

        /// <summary>
        /// Gets the staged devices.
        /// </summary>
        internal List<CraneDeviceBuilder> Devices { get; } = [];

        /// <summary>
        /// Gets the selected protections.
        /// </summary>
        internal List<CraneProtectionBuilder> Protections { get; } = [];

        /// <summary>
        /// Gets the inherited controller configurations.
        /// </summary>
        internal List<(string Id, Action<IControllerBuilder> Configure)> Controllers { get; } = [];

        /// <summary>
        /// Gets the independently reported inherited safety states.
        /// </summary>
        internal List<string> SafetyStates { get; } = [];

        /// <summary>
        /// Selects the per-system provider; ownership transfers when runtime construction starts.
        /// </summary>
        public CraneSystemBuilder WithProvider(ICraneDataProvider provider, bool ownsProvider = false)
        {
            EnsureMutable();
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            OwnsProvider = ownsProvider;
            return this;
        }

        /// <summary>
        /// Configures mandatory identity using the existing Machinery writer and validation.
        /// </summary>
        public CraneSystemBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            EnsureMutable();
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Supplies explicit authorization and acceptance for any selected standard limit requests.
        /// </summary>
        public CraneSystemBuilder WithLimitRequests(ICraneLimitProvider provider, ICraneWriteAuthorizer authorizer)
        {
            EnsureMutable();
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            LimitProvider = provider;
            Authorizer = authorizer;
            return this;
        }

        /// <summary>
        /// Selects provider-reported external-control state.
        /// </summary>
        public CraneSystemBuilder WithExternalControl()
        {
            EnsureMutable();
            ExternalControl = true;
            return this;
        }

        /// <summary>
        /// Selects the standard global request; this does not enable or disable any physical protection.
        /// </summary>
        public CraneSystemBuilder WithGlobalSpeedLimit()
        {
            EnsureMutable();
            GlobalSpeedLimit = true;
            return this;
        }

        /// <summary>
        /// Adds functional-group references to the same ParameterSet variables, not duplicate variables.
        /// </summary>
        public CraneSystemBuilder WithFunctionalGroups()
        {
            EnsureMutable();
            FunctionalGroups = true;
            return this;
        }

        /// <summary>
        /// Selects the Machinery Components add-in for discovery of existing crane devices.
        /// </summary>
        public CraneSystemBuilder WithComponents()
        {
            EnsureMutable();
            Components = true;
            return this;
        }

        /// <summary>
        /// Selects the shared Machinery event publisher without inventing crane event types.
        /// </summary>
        public CraneSystemBuilder WithEvents()
        {
            EnsureMutable();
            Events = true;
            return this;
        }

        /// <summary>
        /// Adds an inherited controller; its software, task control and user are configured through Robotics.
        /// </summary>
        public CraneSystemBuilder AddController(string id, Action<IControllerBuilder> configure)
        {
            EnsureMutable();
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ReserveId(id);
            Controllers.Add((id, configure));
            return this;
        }

        /// <summary>
        /// Adds an inherited safety state whose mandatory signals are supplied by the data provider.
        /// </summary>
        public CraneSystemBuilder AddSafetyState(string id)
        {
            EnsureMutable();
            ReserveId(id);
            SafetyStates.Add(id);
            return this;
        }

        /// <summary>
        /// Adds a native crane motion device with its standard crane category.
        /// </summary>
        public CraneSystemBuilder AddDevice(
            string id,
            CraneMotionDeviceCategoryEnum category,
            Action<CraneDeviceBuilder> configure)
        {
            EnsureMutable();
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if ((int)category is < 0 or > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }
            ReserveId(id);
            var device = new CraneDeviceBuilder(this, id, category);
            configure(device);
            Devices.Add(device);
            return this;
        }

        /// <summary>
        /// Adds a protective function and its explicit protected-axis relationships.
        /// </summary>
        public CraneSystemBuilder AddProtectiveFunction(
            string id,
            ArrayOf<string> protectedAxes,
            Action<CraneProtectionBuilder>? configure = null)
        {
            EnsureMutable();
            if (protectedAxes.IsNull)
            {
                throw new ArgumentNullException(nameof(protectedAxes));
            }
            ReserveId(id);
            var protection = new CraneProtectionBuilder(this, id, protectedAxes);
            configure?.Invoke(protection);
            Protections.Add(protection);
            return this;
        }

        /// <summary>
        /// Validates selected capabilities and providers, acquires initial data, then publishes the tree once.
        /// </summary>
        public async ValueTask<CraneSystemHandle> BuildAsync(CancellationToken cancellationToken = default)
        {
            EnsureMutable();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefinition();
            m_buildStarted = true;
            CraneSystemHandle? handle = null;
            try
            {
                await Context.Robotics.AddMotionDeviceSystemAsync(
                    new QualifiedName(Id, Context.Robotics.InstanceNamespaceIndex),
                    static (context, parent, name) =>
                        context.CreateInstanceOfCraneMotionDeviceSystemType(parent, name),
                    system =>
                    {
                        var graph = new CraneGraph(this, system);
                        graph.Materialize();
                        handle = new CraneSystemHandle(this, graph);
                        system.WithRegistrationParticipant(Context.Machinery.BindMachine(
                            graph.State,
                            machine =>
                            {
                                graph.BindIdentifications(machine);
                                if (Events)
                                {
                                    machine.WithNotifications(notifications =>
                                        notifications.Bind(out graph.EventPublisher));
                                }
                                machine.WithRegistrationBinding(
                                    (_, _, _) => new ValueTask<IAsyncDisposable>(handle));
                            }));
                        system.WithRegistrationParticipant(new CraneRuntimeParticipant(Context, handle));
                    },
                    cancellationToken).ConfigureAwait(false);
                return handle ?? throw new InvalidOperationException("The crane runtime was not created.");
            }
            catch
            {
                if (handle != null)
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Rejects mutation after the awaited build starts.
        /// </summary>
        internal void EnsureMutable()
        {
            if (m_buildStarted)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The crane builder is single-use.");
            }
        }

        /// <summary>
        /// Reserves a provider identity only within this in-memory definition, not in the node manager.
        /// </summary>
        internal void ReserveId(string id)
        {
            ValidateId(id);
            if (m_ids.Count >= 128)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTooManyOperations, "A crane supports 128 components.");
            }
            if (!m_ids.Add(id))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated, "Duplicate component '{0}'.", id);
            }
        }

        /// <summary>
        /// Validates bounded, nonempty local identifiers.
        /// </summary>
        internal static void ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            {
                throw new ArgumentException(
                    "An identifier must contain 1 to 128 non-whitespace characters.", nameof(id));
            }
        }

        /// <summary>
        /// Refuses incomplete or unbacked configurations before reserving any address-space identity.
        /// </summary>
        private void ValidateDefinition()
        {
            if (Provider == null || Identification == null || Devices.Count == 0 ||
                Controllers.Count == 0 || SafetyStates.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "A crane requires a data provider, identity, device, controller and safety state.");
            }
            bool limits = GlobalSpeedLimit;
            var axes = new HashSet<string>(StringComparer.Ordinal);
            var devices = new Dictionary<string, CraneDeviceBuilder>(StringComparer.Ordinal);
            foreach (CraneDeviceBuilder device in Devices)
            {
                devices.Add(device.Id, device);
                if (device.Identification == null || device.Axes.Count == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "Device '{0}' requires identity and an axis.", device.Id);
                }
                foreach (CraneAxisBuilder axis in device.Axes)
                {
                    axes.Add(axis.Id);
                    limits |= axis.SpeedLimits;
                    if (axis.Motors.Count == 0 || (int)axis.MotionProfile is < 0 or > 4)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "Axis '{0}' requires a motor and valid profile.", axis.Id);
                    }
                }
            }
            if (limits && (LimitProvider == null || Authorizer == null))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Limit requests require a provider and explicit authorizer.");
            }
            foreach (CraneProtectionBuilder protection in Protections)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string axis in protection.Axes)
                {
                    if (!axes.Contains(axis) || !seen.Add(axis))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError, "ProtectsAxis requires distinct axes in this system.");
                    }
                }
                if (protection.Mode.HasValue && (int)protection.Mode.Value is < 0 or > 4)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "The protective function category is invalid.");
                }
            }
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CraneDeviceBuilder device in Devices)
            {
                foreach (string child in device.PhysicalChildren)
                {
                    if (!devices.ContainsKey(child) || child == device.Id || parents.ContainsKey(child))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError, "Physical composition requires unique local children.");
                    }
                    parents.Add(child, device.Id);
                }
            }
            foreach (string device in devices.Keys)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                string current = device;
                while (parents.TryGetValue(current, out string? parent))
                {
                    if (!visited.Add(current))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError, "Physical composition cannot contain a cycle.");
                    }
                    current = parent;
                }
            }
        }

        private readonly HashSet<string> m_ids = new(StringComparer.Ordinal);
        private bool m_buildStarted;
    }
}
