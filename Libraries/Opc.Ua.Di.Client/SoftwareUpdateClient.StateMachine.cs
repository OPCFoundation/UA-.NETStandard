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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Part 16 state-machine helpers on
    /// <see cref="SoftwareUpdateClient"/>. Surface the new generic
    /// state-machine API (<see cref="FiniteStateMachineTypeClientExtensions"/>)
    /// for each of the four <c>SoftwareUpdateType</c> children
    /// (<c>PrepareForUpdate</c>, <c>Installation</c>,
    /// <c>Confirmation</c>, <c>PowerCycle</c>) plus typed wrappers
    /// for every cause-method on the corresponding source-generated
    /// <c>*TypeClient</c> proxy.
    /// </summary>
    /// <remarks>
    /// The typed proxies are resolved lazily via the generated
    /// <see cref="SoftwareUpdateTypeClient"/> child accessors; the
    /// proxy instances are cached for the lifetime of the
    /// <see cref="SoftwareUpdateClient"/> so the
    /// browse-path-translate round-trip happens at most once per
    /// state machine.
    /// </remarks>
    public partial class SoftwareUpdateClient
    {
        private SoftwareUpdateTypeClient? m_softwareUpdateProxy;
        private PrepareForUpdateStateMachineTypeClient? m_prepareProxy;
        private InstallationStateMachineTypeClient? m_installationProxy;
        private ConfirmationStateMachineTypeClient? m_confirmationProxy;
        private PowerCycleStateMachineTypeClient? m_powerCycleProxy;

        private SoftwareUpdateTypeClient SoftwareUpdateProxy =>
            m_softwareUpdateProxy ??= new SoftwareUpdateTypeClient(
                Session, SoftwareUpdateNodeId, Telemetry);

        /// <summary>
        /// Reads the current <c>PrepareForUpdate</c> state-machine
        /// snapshot. Returns <see langword="null"/> when the server
        /// does not expose the optional child.
        /// </summary>
        public async ValueTask<FiniteStateSnapshot?> GetPrepareForUpdateStateAsync(
            CancellationToken ct = default)
        {
            PrepareForUpdateStateMachineTypeClient? proxy =
                await ResolvePrepareProxyAsync(ct).ConfigureAwait(false);
            return proxy is null
                ? null
                : await proxy.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams <c>PrepareForUpdate</c> state-machine transitions
        /// via the supplied streaming subscription. Yields one snapshot
        /// per observed transition.
        /// </summary>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObservePrepareForUpdateTransitionsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            PrepareForUpdateStateMachineTypeClient? proxy =
                await ResolvePrepareProxyAsync(ct).ConfigureAwait(false);
            if (proxy is null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snap in proxy
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                yield return snap;
            }
        }

        /// <summary>Invokes <c>PrepareForUpdate.Prepare</c>.</summary>
        public async ValueTask PrepareAsync(CancellationToken ct = default)
        {
            PrepareForUpdateStateMachineTypeClient proxy =
                await RequirePrepareProxyAsync(ct).ConfigureAwait(false);
            await proxy.PrepareAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Invokes <c>PrepareForUpdate.Abort</c>.</summary>
        public async ValueTask AbortPrepareAsync(CancellationToken ct = default)
        {
            PrepareForUpdateStateMachineTypeClient proxy =
                await RequirePrepareProxyAsync(ct).ConfigureAwait(false);
            await proxy.AbortAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current <c>Installation</c> state-machine
        /// snapshot. Returns <see langword="null"/> when the server
        /// does not expose the optional child.
        /// </summary>
        public async ValueTask<FiniteStateSnapshot?> GetInstallationStateAsync(
            CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient? proxy =
                await ResolveInstallationProxyAsync(ct).ConfigureAwait(false);
            return proxy is null
                ? null
                : await proxy.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams <c>Installation</c> state-machine transitions via
        /// the supplied streaming subscription.
        /// </summary>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObserveInstallationTransitionsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient? proxy =
                await ResolveInstallationProxyAsync(ct).ConfigureAwait(false);
            if (proxy is null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snap in proxy
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                yield return snap;
            }
        }

        /// <summary>Invokes <c>Installation.InstallSoftwarePackage</c>.</summary>
        public async ValueTask InstallSoftwarePackageAsync(
            string manufacturerUri,
            string softwareRevision,
            ArrayOf<string> patchIdentifiers,
            ByteString hash,
            CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient proxy =
                await RequireInstallationProxyAsync(ct).ConfigureAwait(false);
            await proxy.InstallSoftwarePackageAsync(
                manufacturerUri, softwareRevision, patchIdentifiers, hash, ct)
                .ConfigureAwait(false);
        }

        /// <summary>Invokes <c>Installation.InstallFiles</c>.</summary>
        public async ValueTask InstallFilesAsync(
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient proxy =
                await RequireInstallationProxyAsync(ct).ConfigureAwait(false);
            await proxy.InstallFilesAsync(nodeIds, ct).ConfigureAwait(false);
        }

        /// <summary>Invokes <c>Installation.Uninstall</c>.</summary>
        public async ValueTask UninstallAsync(CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient proxy =
                await RequireInstallationProxyAsync(ct).ConfigureAwait(false);
            await proxy.UninstallAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Invokes <c>Installation.Resume</c>.</summary>
        public async ValueTask ResumeInstallationAsync(CancellationToken ct = default)
        {
            InstallationStateMachineTypeClient proxy =
                await RequireInstallationProxyAsync(ct).ConfigureAwait(false);
            await proxy.ResumeAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current <c>Confirmation</c> state-machine
        /// snapshot. Returns <see langword="null"/> when the server
        /// does not expose the optional child.
        /// </summary>
        public async ValueTask<FiniteStateSnapshot?> GetConfirmationStateAsync(
            CancellationToken ct = default)
        {
            ConfirmationStateMachineTypeClient? proxy =
                await ResolveConfirmationProxyAsync(ct).ConfigureAwait(false);
            return proxy is null
                ? null
                : await proxy.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams <c>Confirmation</c> state-machine transitions via
        /// the supplied streaming subscription.
        /// </summary>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObserveConfirmationTransitionsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            ConfirmationStateMachineTypeClient? proxy =
                await ResolveConfirmationProxyAsync(ct).ConfigureAwait(false);
            if (proxy is null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snap in proxy
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                yield return snap;
            }
        }

        /// <summary>Invokes <c>Confirmation.Confirm</c>.</summary>
        public async ValueTask ConfirmAsync(CancellationToken ct = default)
        {
            ConfirmationStateMachineTypeClient proxy =
                await RequireConfirmationProxyAsync(ct).ConfigureAwait(false);
            await proxy.ConfirmAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current <c>PowerCycle</c> state-machine snapshot.
        /// Returns <see langword="null"/> when the server does not
        /// expose the optional child.
        /// </summary>
        public async ValueTask<FiniteStateSnapshot?> GetPowerCycleStateAsync(
            CancellationToken ct = default)
        {
            PowerCycleStateMachineTypeClient? proxy =
                await ResolvePowerCycleProxyAsync(ct).ConfigureAwait(false);
            return proxy is null
                ? null
                : await proxy.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams <c>PowerCycle</c> state-machine transitions via
        /// the supplied streaming subscription.
        /// </summary>
        public async IAsyncEnumerable<FiniteStateSnapshot> ObservePowerCycleTransitionsAsync(
            IStreamingSubscription streaming,
            MonitoringOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            PowerCycleStateMachineTypeClient? proxy =
                await ResolvePowerCycleProxyAsync(ct).ConfigureAwait(false);
            if (proxy is null)
            {
                yield break;
            }
            await foreach (FiniteStateSnapshot snap in proxy
                .ObserveFiniteTransitionsAsync(streaming, options, ct)
                .ConfigureAwait(false))
            {
                yield return snap;
            }
        }

        private async ValueTask<PrepareForUpdateStateMachineTypeClient?>
            ResolvePrepareProxyAsync(CancellationToken ct)
        {
            return m_prepareProxy ??= await SoftwareUpdateProxy
                .GetPrepareForUpdateAsync(Telemetry, ct).ConfigureAwait(false);
        }

        private async ValueTask<PrepareForUpdateStateMachineTypeClient>
            RequirePrepareProxyAsync(CancellationToken ct)
        {
            PrepareForUpdateStateMachineTypeClient? proxy =
                await ResolvePrepareProxyAsync(ct).ConfigureAwait(false);
            return proxy ??
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "PrepareForUpdate child not exposed by the server.");
        }

        private async ValueTask<InstallationStateMachineTypeClient?>
            ResolveInstallationProxyAsync(CancellationToken ct)
        {
            return m_installationProxy ??= await SoftwareUpdateProxy
                .GetInstallationAsync(Telemetry, ct).ConfigureAwait(false);
        }

        private async ValueTask<InstallationStateMachineTypeClient>
            RequireInstallationProxyAsync(CancellationToken ct)
        {
            InstallationStateMachineTypeClient? proxy =
                await ResolveInstallationProxyAsync(ct).ConfigureAwait(false);
            return proxy ??
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "Installation child not exposed by the server.");
        }

        private async ValueTask<ConfirmationStateMachineTypeClient?>
            ResolveConfirmationProxyAsync(CancellationToken ct)
        {
            return m_confirmationProxy ??= await SoftwareUpdateProxy
                .GetConfirmationAsync(Telemetry, ct).ConfigureAwait(false);
        }

        private async ValueTask<ConfirmationStateMachineTypeClient>
            RequireConfirmationProxyAsync(CancellationToken ct)
        {
            ConfirmationStateMachineTypeClient? proxy =
                await ResolveConfirmationProxyAsync(ct).ConfigureAwait(false);
            return proxy ??
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "Confirmation child not exposed by the server.");
        }

        private async ValueTask<PowerCycleStateMachineTypeClient?>
            ResolvePowerCycleProxyAsync(CancellationToken ct)
        {
            return m_powerCycleProxy ??= await SoftwareUpdateProxy
                .GetPowerCycleAsync(Telemetry, ct).ConfigureAwait(false);
        }
    }
}
