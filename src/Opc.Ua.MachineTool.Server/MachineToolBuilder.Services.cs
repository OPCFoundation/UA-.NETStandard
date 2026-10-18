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
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolBuilder<TState> where TState : MachineToolState
    {
        /// <summary>
        /// Composes existing Machinery bindings before this machine is published, preserving one owner and registration.
        /// </summary>
        /// <remarks>
        /// The callback configures the supplied unpublished builder only. It must not build, register or dispose it.
        /// Derived companions can bind their already declared result or other Machinery descendants through this path.
        /// Resources returned by registration bindings remain owned by the containing machine.
        /// </remarks>
        /// <param name="configure">Configures the actual typed Machinery builder used by this machine.</param>
        /// <returns>This machine builder.</returns>
        public MachineToolBuilder<TState> WithMachinery(Action<IMachineBuilder<TState>> configure)
        {
            EnsureMutable();
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            m_machineryBindings.Add(configure);
            return this;
        }

        /// <summary>
        /// Binds both standard ISA-95 control and response providers at the declared MachineryBuildingBlocks path.
        /// Providers and catalogs remain application-owned and must describe the same equipment as its observations.
        /// </summary>
        /// <param name="options">The complete per-instance job providers and parameter policy.</param>
        /// <returns>This machine builder.</returns>
        public MachineToolBuilder<TState> WithJobManagement(MachineToolJobManagementOptions options)
        {
            EnsureMutable();
            m_jobManagement = options ?? throw new ArgumentNullException(nameof(options));
            return this;
        }

        /// <summary>
        /// Exposes an explicit provider through the standard FileSystem directory and optionally its WorkMasters child.
        /// The provider stays application-owned; file handles and materialized nodes follow the machine's lifetime.
        /// </summary>
        /// <param name="provider">The root provider, including the WorkMasters directory when selected.</param>
        /// <param name="includeWorkMasters">Qualifies the provider's WorkMasters directory in the MachineTool namespace.</param>
        /// <param name="options">The existing file-directory operation and materialization limits.</param>
        /// <returns>This machine builder.</returns>
        public MachineToolBuilder<TState> WithFileSystem(
            IFileSystemProvider provider,
            bool includeWorkMasters = false,
            FileDirectoryBindingOptions? options = null)
        {
            EnsureMutable();
            m_fileSystem = provider ?? throw new ArgumentNullException(nameof(provider));
            m_includeWorkMasters = includeWorkMasters;
            options ??= new FileDirectoryBindingOptions();
            m_fileOptions = new FileDirectoryBindingOptions
            {
                AllowCreate = options.AllowCreate,
                AllowDelete = options.AllowDelete,
                AllowMoveOrCopy = options.AllowMoveOrCopy,
                MaxEntries = options.MaxEntries,
                MaxDepth = options.MaxDepth
            };
            return this;
        }

        /// <summary>
        /// Validates explicitly selected providers before the generated machine is allocated.
        /// </summary>
        partial void ValidateIntegratedServices()
        {
            m_jobManagement?.Validate();
            if (m_fileOptions is { } options &&
                (options.MaxEntries < 0 || options.MaxDepth < 0 ||
                    (m_includeWorkMasters && (options.MaxEntries == 0 || options.MaxDepth < 2))))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "File materialization limits must be nonnegative and include the selected WorkMasters contents.");
            }
        }

        /// <summary>
        /// Validates provider-backed directory selection before the machine factory runs.
        /// </summary>
        private partial async ValueTask PrepareIntegratedServicesAsync(CancellationToken cancellationToken)
        {
            if (m_fileSystem == null)
            {
                return;
            }
            FileSystemEntry? root = await m_fileSystem.GetEntryAsync(string.Empty, cancellationToken)
                .ConfigureAwait(false);
            if (root is not { IsDirectory: true })
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The machine file provider must expose an existing directory root.");
            }
            if (m_includeWorkMasters)
            {
                FileSystemEntry? masters = await m_fileSystem.GetEntryAsync(BrowseNames.WorkMasters, cancellationToken)
                    .ConfigureAwait(false);
                if (masters is not FileSystemEntry { IsDirectory: true } masterDirectory)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The selected WorkMasters directory must exist in the machine file provider.");
                }
                m_workMastersProviderName = masterDirectory.Name;
            }
        }

        /// <summary>
        /// Configures existing declared descendants through the shared Machinery binding transaction.
        /// </summary>
        partial void ConfigureIntegratedServices(
            IMachineBuilder<TState> builder, TState state, MachineToolRuntime<TState> runtime)
        {
            foreach (Action<IMachineBuilder<TState>> configure in m_machineryBindings)
            {
                configure(builder);
            }
            if (m_jobManagement is { } options)
            {
                ConfigureJobManagement(builder, state, options);
            }
            if (m_fileSystem != null)
            {
                ConfigureFileSystem(builder, state, runtime, m_fileSystem);
            }
        }

        /// <summary>
        /// Stages the generated Jobs AddIn and both standard provider bindings.
        /// </summary>
        private void ConfigureJobManagement(
            IMachineBuilder<TState> builder, TState state, MachineToolJobManagementOptions options)
        {
            BaseInstanceState blocks = CreateServiceChild<BaseInstanceState>(
                state, "MachineryBuildingBlocks", Opc.Ua.Machinery.Namespaces.Machinery);
            QualifiedName name = ServiceName(
                Opc.Ua.Machinery.Jobs.BrowseNames.JobManagement, Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            BaseInstanceState? existing = blocks.FindChild(m_context.Context, name);
            JobManagementState management;
            if (existing == null)
            {
                management = m_context.Context.CreateInstanceOfJobManagementType(blocks, name);
                management.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasAddIn;
                blocks.AddChild(management);
            }
            else
            {
                management = existing as JobManagementState ??
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "The declared JobManagement child is not a generated JobManagementType instance.");
            }
            builder.WithJobManagement(management, jobs =>
            {
                jobs.WithJobOrderReceiver(options.Receiver!)
                    .WithJobResponseProvider(options.ResponseProvider!)
                    .WithJobOrderCatalog(options.EffectiveCatalog!);
                if (options.PredefinedParameters)
                {
                    jobs.WithPredefinedParameters();
                }
            });
        }

        /// <summary>
        /// Binds provider-authored contents without creating another file-system node manager.
        /// </summary>
        private void ConfigureFileSystem(
            IMachineBuilder<TState> builder,
            TState state,
            MachineToolRuntime<TState> runtime,
            IFileSystemProvider provider)
        {
            FileDirectoryState directory = CreateServiceChild<FileDirectoryState>(
                state, Opc.Ua.BrowseNames.FileSystem, Opc.Ua.Namespaces.OpcUa);
            QualifiedName workMastersName = ServiceName(BrowseNames.WorkMasters, Namespaces.MachineTool);
            if (m_includeWorkMasters && directory.FindChild(m_context.Context, workMastersName) != null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "WorkMasters contents must be supplied by the file provider, not separately authored twice.");
            }
            IFileDirectoryBinder binder = m_context.GetService<IFileDirectoryBinder>() ?? new FileDirectoryBinder();
            builder.WithRegistrationBinding(async (_, systemContext, cancellationToken) =>
            {
                IFileDirectoryBinding binding = await binder.BindAsync(
                    directory,
                    provider,
                    systemContext,
                    m_fileOptions,
                    registerNode: async (node, ct) =>
                    {
                        if (m_includeWorkMasters && node is FileDirectoryState child &&
                            ReferenceEquals(child.Parent, directory) &&
                            string.Equals(child.BrowseName.Name, m_workMastersProviderName, StringComparison.Ordinal))
                        {
                            child.BrowseName = workMastersName;
                        }
                        NodeState? registered = m_context.Manager.FindPredefinedNode(node.NodeId);
                        if (registered == null)
                        {
                            await m_context.Manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
                        }
                        else if (!ReferenceEquals(registered, node))
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdExists,
                                "The provider file identifier conflicts with an existing node.");
                        }
                    },
                    deregisterNode: async (node, ct) =>
                    {
                        if (ReferenceEquals(m_context.Manager.FindPredefinedNode(node.NodeId), node))
                        {
                            await m_context.Manager.DeleteNodeAsync(m_context.Manager.SystemContext, node.NodeId, ct)
                                .ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    runtime.SetFileSystemBinding(binding);
                    return binding;
                }
                catch (Exception error)
                {
                    try
                    {
                        await binding.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException("File binding registration and rollback both failed.", error, cleanup);
                    }
                    throw;
                }
            });
        }

        /// <summary>
        /// Selects a generated optional descendant without replacing an existing companion-specific instance.
        /// </summary>
        /// <typeparam name="TChild">The expected generated descendant type.</typeparam>
        private TChild CreateServiceChild<TChild>(NodeState parent, string name, string namespaceUri)
            where TChild : BaseInstanceState
        {
            QualifiedName browseName = ServiceName(name, namespaceUri);
            BaseInstanceState? child = parent.FindChild(m_context.Context, browseName) ??
                parent.CreateChild(m_context.Context, browseName);
            return child as TChild ?? throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                $"The generated machine does not declare '{browseName}' with the required type.");
        }

        /// <summary>
        /// Resolves a declared service namespace without silently substituting namespace zero.
        /// </summary>
        private QualifiedName ServiceName(string name, string namespaceUri)
        {
            int namespaceIndex = m_context.Context.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    $"The selected machine functionality requires namespace '{namespaceUri}'.");
            }
            return new QualifiedName(name, (ushort)namespaceIndex);
        }

        /// <summary>The explicitly selected per-machine job-management providers.</summary>
        private MachineToolJobManagementOptions? m_jobManagement;

        /// <summary>The explicit, application-owned root file provider.</summary>
        private IFileSystemProvider? m_fileSystem;

        /// <summary>The copied file-directory binding policy.</summary>
        private FileDirectoryBindingOptions? m_fileOptions;

        /// <summary>Whether the provider's standard WorkMasters child is selected.</summary>
        private bool m_includeWorkMasters;

        /// <summary>The exact provider directory spelling captured before publication.</summary>
        private string? m_workMastersProviderName;

        /// <summary>Derived-companion bindings composed into the machine's existing registration transaction.</summary>
        private readonly List<Action<IMachineBuilder<TState>>> m_machineryBindings = [];
    }
}
