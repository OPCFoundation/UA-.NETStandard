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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Attaches common capabilities to the lifetime of an existing DI/Machinery address-space owner.
    /// </summary>
    /// <remarks>
    /// The common specification defines reusable capabilities, not another machine root.
    /// Device families create their generated instances and select optional members before binding.
    /// </remarks>
    public sealed class PlasticsRubberRuntime
    {
        /// <summary>
        /// Creates a direct runtime over an owner that has loaded the common model.
        /// </summary>
        public PlasticsRubberRuntime(DiNodeManager manager, IPlasticsRubberAccessPolicy accessPolicy)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            AccessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            NodeId typeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.StartDeviceType, manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(typeId) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the common model with AddPlasticsRubberToMachinery before binding its capabilities.");
            }
        }

        /// <summary>
        /// Gets the existing owner; the runtime does not create a competing node manager.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the explicitly selected authorization policy.
        /// </summary>
        public IPlasticsRubberAccessPolicy AccessPolicy { get; }

        /// <summary>
        /// Binds a start/stop capability and releases it with the owner.
        /// </summary>
        public ValueTask<StartDeviceBinding> BindStartDeviceAsync(
            StartDeviceState node,
            IStartDeviceProvider provider)
        {
            return AdoptAsync(() => new StartDeviceBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds maintenance reset and releases it with the owner.
        /// </summary>
        public ValueTask<MaintenanceBinding> BindMaintenanceAsync(
            MaintenanceState node,
            IMaintenanceProvider provider)
        {
            return AdoptAsync(() => new MaintenanceBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds monitoring and selected settings and releases them with the owner.
        /// </summary>
        public ValueTask<MonitoredParameterBinding> BindMonitoredParameterAsync(
            MonitoredParameterState node,
            IMonitoredParameterProvider provider)
        {
            return AdoptAsync(() => new MonitoredParameterBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds selected controller features and releases them with the owner.
        /// </summary>
        public ValueTask<ClosedLoopControlBinding> BindClosedLoopControlAsync(
            ClosedLoopControlState node,
            IClosedLoopControlProvider provider)
        {
            return AdoptAsync(() => new ClosedLoopControlBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds an authoritative material catalogue and releases its operations with the owner.
        /// </summary>
        public ValueTask<MaterialListBinding> BindMaterialsAsync(MaterialListState node, IMaterialListProvider provider)
        {
            return AdoptAsync(() => new MaterialListBinding(Manager, node, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds machine configuration and selected page retrieval with the owner's lifecycle.
        /// </summary>
        public ValueTask<MachineConfigurationBinding> BindMachineConfigurationAsync(
            MachineConfigurationState node,
            IMachineConfigurationProvider provider)
        {
            return AdoptAsync(() => new MachineConfigurationBinding(node, Manager.SystemContext, provider,
                AccessPolicy));
        }

        /// <summary>
        /// Binds machine sleep requests with the owner's lifecycle.
        /// </summary>
        public ValueTask<MachineStatusBinding> BindMachineStatusAsync(
            MachineStatusState node,
            IMachineStatusProvider provider)
        {
            return AdoptAsync(() => new MachineStatusBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds common production-control requests to the explicit authoritative controller.
        /// </summary>
        public ValueTask<ProductionControlBinding> BindProductionControlAsync(
            ProductionControlState node,
            IProductionControlProvider provider)
        {
            return AdoptAsync(() => new ProductionControlBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds common active-job requests and optional cyclic controls.
        /// </summary>
        public ValueTask<ActiveJobBinding> BindActiveJobAsync(
            ActiveJobValuesState node,
            IActiveJobProvider provider,
            ProductionControlState? productionControl = null)
        {
            return AdoptAsync(() => new ActiveJobBinding(
                node, Manager.SystemContext, provider, AccessPolicy, productionControl));
        }

        /// <summary>
        /// Binds diagnostics and its concrete generated event derivatives to the existing device interface.
        /// </summary>
        public async ValueTask<DiagnosticsBinding> BindDiagnosticsAsync(
            DiagnosticsState node,
            BaseObjectState source,
            IDiagnosticsProvider provider,
            CancellationToken cancellationToken = default)
        {
            DiagnosticsBinding binding = await DiagnosticsBinding.CreateAsync(
                Manager, node, source, provider, AccessPolicy, cancellationToken).ConfigureAwait(false);
            return await AdoptAsync(() => binding).ConfigureAwait(false);
        }

        /// <summary>
        /// Binds common job-list exchanges without starting or changing an active job implicitly.
        /// </summary>
        public ValueTask<JobsBinding> BindJobListsAsync(JobsState node, IJobListProvider provider)
        {
            return AdoptAsync(() => new JobsBinding(node, Manager.SystemContext, provider, AccessPolicy));
        }

        /// <summary>
        /// Binds the common cyclic-job configuration method to its explicit device authority.
        /// </summary>
        public ValueTask<CyclicJobInformationBinding> BindCyclicJobInformationAsync(
            CyclicJobInformationState node,
            ICyclicJobConfigurationProvider provider)
        {
            return AdoptAsync(() => new CyclicJobInformationBinding(node, Manager.SystemContext, provider,
                AccessPolicy));
        }

        /// <summary>
        /// Transfers a binding to the existing Machinery lifecycle without leaking a failed registration.
        /// </summary>
        /// <typeparam name="T">The concrete disposable binding.</typeparam>
        private async ValueTask<T> AdoptAsync<T>(Func<T> create)
            where T : IAsyncDisposable
        {
            T binding = create();
            bool adopted = false;
            try
            {
                Manager.RegisterMachineryResource(binding);
                adopted = true;
                return binding;
            }
            finally
            {
                if (!adopted)
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
