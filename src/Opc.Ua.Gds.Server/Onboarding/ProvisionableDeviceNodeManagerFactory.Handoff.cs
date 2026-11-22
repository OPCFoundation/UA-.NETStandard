// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.Onboarding
{
    public sealed partial class ProvisionableDeviceNodeManagerFactory
    {
        /// <summary>
        /// Submits an in-memory replacement for the exact registered device. The result is acceptance only.
        /// </summary>
        /// <remarks>
        /// Call from the management host, not from an OPC UA method handler exposed to clients.
        /// Retains the serving runtime and policies. The host lifecycle worker owns commit and reports failures
        /// through telemetry; callers must not wait for publication from inside a device callback.
        /// </remarks>
        public StatusCode QueueReplacement(
            INodeManagerLifecycle lifecycle, NodeManagerRegistration predecessor,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle == null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (predecessor == null)
            {
                throw new ArgumentNullException(nameof(predecessor));
            }
            return lifecycle is NodeManagerLifecycle worker
                ? worker.QueueHandoff(predecessor, this, cancellationToken)
                : StatusCodes.BadNotSupported;
        }

        /// <inheritdoc/>
        ValueTask<NodeManagerHandoffCandidate> INodeManagerHandoffFactory.PrepareReplacementAsync(
            IServerInternal stagingServer, ApplicationConfiguration configuration,
            IAsyncNodeManager predecessor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predecessor is not ProvisionableDeviceNodeManager previous ||
                !previous.MatchesRuntime(configuration, m_runtime, m_applicationsProvider, m_security, m_softwareUpdates))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "Replacement must retain the exact device runtime and policies.");
            }
#pragma warning disable CA2000 // The lifecycle owns the returned candidate. TODO: Express ownership transfer to the analyzer.
            ProvisionableDeviceNodeManager candidate = CreateManager(stagingServer, configuration, previous);
            return new ValueTask<NodeManagerHandoffCandidate>(
                new NodeManagerHandoffCandidate(candidate, () => previous.PauseCalls(candidate)));
#pragma warning restore CA2000
        }

        private sealed partial class ProvisionableDeviceNodeManager
        {
            /// <summary>
            /// Gets the published layout without calling the provider during replacement preparation.
            /// </summary>
            public bool Singleton => m_device!.IsSingleton!.Value;

            /// <summary>
            /// Compares the retained host-owned dependencies without invoking providers.
            /// </summary>
            public bool MatchesRuntime(
                ApplicationConfiguration configuration,
                IProvisionableDeviceRuntime runtime, IProvisionableDeviceApplicationsProvider? applications,
                IProvisionableDeviceSecurity? security, IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates)
            {
                return ReferenceEquals(configuration, m_managementConfiguration) &&
                    ReferenceEquals(runtime, m_runtime) && ReferenceEquals(applications, m_applicationsProvider) &&
                    ReferenceEquals(security, m_security) && ReferenceEquals(softwareUpdates, m_softwareUpdates);
            }

            /// <summary>
            /// Pauses new calls while the worker drains the predecessor.
            /// </summary>
            public INodeManagerHandoffPause PauseCalls(ProvisionableDeviceNodeManager successor)
            {
                var trust = new List<ConfigurationNodeManager.TrustHandoff>(m_pushBindings.Count);
                INodeManagerHandoffPause calls = m_calls.Pause();
                try
                {
                    foreach (ApplicationConfigurationPushBinding binding in m_pushBindings)
                    {
                        trust.Add(binding.PauseTrustEffects());
                    }
                    return new DeviceHandoffPause(this, successor, calls, trust);
                }
                catch
                {
                    foreach (ConfigurationNodeManager.TrustHandoff pause in trust)
                    {
                        pause.Finish(m_calls.IsServing());
                    }
                    calls.Finish(m_calls.IsServing());
                    throw;
                }
            }

            private readonly DeviceCalls m_calls;

            private sealed class DeviceHandoffPause(
                ProvisionableDeviceNodeManager previous,
                ProvisionableDeviceNodeManager successor,
                INodeManagerHandoffPause calls,
                List<ConfigurationNodeManager.TrustHandoff> trust) : INodeManagerHandoffPause
            {
                public async ValueTask DrainAsync(CancellationToken cancellationToken)
                {
                    await calls.DrainAsync(cancellationToken).ConfigureAwait(false);
                    for (int i = 0; i < trust.Count; i++)
                    {
                        await trust[i].Drained.WaitAsync(cancellationToken).ConfigureAwait(false);
                        ApplicationConfigurationPushBinding binding = previous.m_pushBindings[i];
                        await binding.DrainPendingApplyChangesAsync(cancellationToken).ConfigureAwait(false);
                        binding.PrepareIdleSuccessor(successor.m_pushBindings[i]);
                    }
                }

                public void Finish(bool reopen)
                {
                    if (reopen)
                    {
                        foreach (ApplicationConfigurationPushBinding binding in previous.m_pushBindings)
                        {
                            binding.ReopenAfterAbortedHandoff();
                        }
                    }
                    calls.Finish(reopen);
                    bool serving = reopen || successor.m_calls.IsServing();
                    foreach (ConfigurationNodeManager.TrustHandoff pause in trust)
                    {
                        pause.Finish(serving);
                    }
                }
            }
        }

        /// <summary>
        /// Admits effects to one generation of a host-owned device runtime.
        /// </summary>
        private sealed class DeviceCalls(
            ProvisionableDeviceNodeManager owner, IServerInternal server,
            IProvisionableDeviceRuntime runtime, bool? singleton) : IProvisionableDeviceRuntime
        {
            /// <inheritdoc/>
            public bool IsSingleton => singleton ?? runtime.IsSingleton;

            /// <inheritdoc/>
            public async ValueTask<ArrayOf<string>> RequestTicketsAsync(CancellationToken cancellationToken = default)
            {
                Enter();
                try
                {
                    return await runtime.RequestTicketsAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Exit();
                }
            }

            /// <inheritdoc/>
            public async ValueTask SetRegistrarEndpointsAsync(
                ISystemContext context, ArrayOf<ApplicationDescription> registrars,
                CancellationToken cancellationToken = default)
            {
                Enter();
                try
                {
                    await runtime.SetRegistrarEndpointsAsync(context, registrars, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    Exit();
                }
            }

            /// <summary>
            /// Creates an exclusive pause token without waiting under admission.
            /// </summary>
            public INodeManagerHandoffPause Pause()
            {
                lock (m_admission)
                {
                    RequireServing();
                    if (m_pause is not null)
                    {
                        throw new ServiceResultException(StatusCodes.BadWouldBlock);
                    }
                    var pause = new CallPause(this);
                    m_pause = pause;
                    if (m_active == 0)
                    {
                        pause.Drained.TrySetResult(true);
                    }
                    return pause;
                }
            }

            public void Enter()
            {
                lock (m_admission)
                {
                    RequireServing();
                    if (m_pause is not null)
                    {
                        throw new ServiceResultException(StatusCodes.BadWouldBlock);
                    }
                    m_active++;
                }
            }

            public void Exit()
            {
                lock (m_admission)
                {
                    if (--m_active == 0)
                    {
                        m_pause?.Drained.TrySetResult(true);
                    }
                }
            }

            private void RequireServing()
            {
                if (m_closed || !IsServing())
                {
                    throw new ServiceResultException(StatusCodes.BadShutdown);
                }
            }

            public bool IsServing()
            {
                if (server.NodeManager is IDynamicNodeManagerHost host &&
                    NodeManagerGenerationRegistry.TryGet(owner, out NodeManagerGeneration? generation) &&
                    generation is not null)
                {
                    return NodeManagerLifecycle.IsSelected(host, generation);
                }
                return server is not ChangePreparer.StagingServer;
            }

            private sealed class CallPause(DeviceCalls calls) : INodeManagerHandoffPause
            {
                public TaskCompletionSource<bool> Drained { get; } =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                public ValueTask DrainAsync(CancellationToken cancellationToken)
                {
                    return new ValueTask(Drained.Task.WaitAsync(cancellationToken));
                }

                public void Finish(bool reopen)
                {
                    lock (calls.m_admission)
                    {
                        if (ReferenceEquals(calls.m_pause, this))
                        {
                            calls.m_closed = !reopen;
                            calls.m_pause = null;
                        }
                    }
                }
            }

            private readonly Lock m_admission = new();
            private bool m_closed;
            private int m_active;
            private CallPause? m_pause;
        }
    }
}
