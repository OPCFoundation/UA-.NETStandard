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
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Loading variant for the OPC 10000-100 §10.3 software-update
    /// facet. Picks the concrete <c>SoftwareLoadingType</c> subtype
    /// that will be materialised under the device's
    /// <c>SoftwareUpdate.Loading</c> slot.
    /// </summary>
    public enum SoftwareLoadingMode
    {
        /// <summary>
        /// <c>PackageLoadingType</c> — file transfer via
        /// <c>FileTransfer.GenerateFileForWrite</c> +
        /// <c>CloseAndCommit</c>. Recommended default.
        /// </summary>
        Package = 0,

        /// <summary>
        /// <c>DirectLoadingType</c> — direct property write of the
        /// new firmware. No file transfer.
        /// </summary>
        Direct = 1,

        /// <summary>
        /// <c>CachedLoadingType</c> — pending + fallback versions
        /// archived for two-stage rollouts.
        /// </summary>
        Cached = 2,
    }

    /// <summary>
    /// Phase reported by <see cref="ISoftwareUpdateBuilder"/>'s
    /// state-changed hooks. Numeric-state-ID-free domain enum so
    /// applications don't have to depend on Part-16 dispatcher
    /// internals.
    /// </summary>
    public enum SoftwareUpdatePhase
    {
        /// <summary>
        /// The state-machine method invocation was accepted.
        /// </summary>
        Started = 0,

        /// <summary>
        /// The work is in progress.
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// The work completed successfully.
        /// </summary>
        Completed = 2,

        /// <summary>
        /// The work failed.
        /// </summary>
        Failed = 3,
    }

    /// <summary>
    /// Snapshot passed to the SU state-changed hooks each time the
    /// server-side state machine moves between phases.
    /// </summary>
    /// <param name="Phase">The new phase.</param>
    /// <param name="Message">
    /// Optional human-readable message (e.g. installation failure
    /// reason). Empty when no message is supplied.
    /// </param>
    /// <param name="ProgressPercent">
    /// Optional 0..100 progress indicator for <see cref="SoftwareUpdatePhase.InProgress"/>;
    /// <see langword="null"/> when progress is not tracked.
    /// </param>
    public sealed record SoftwareUpdateStateChange(
        SoftwareUpdatePhase Phase,
        string Message,
        byte? ProgressPercent);

    /// <summary>
    /// Context object passed to <see cref="ISoftwareUpdateBuilder"/>
    /// callbacks. Exposes the bits the application needs to react to
    /// state-machine method invocations without dragging in the
    /// full <c>SoftwareUpdateState</c> surface.
    /// </summary>
    public interface ISoftwareUpdateContext
    {
        /// <summary>
        /// The NodeId of the device that owns the SU facet.
        /// </summary>
        NodeId DeviceId { get; }

        /// <summary>
        /// The server's system context.
        /// </summary>
        ISystemContext SystemContext { get; }

        /// <summary>
        /// The package store the SU facet is bound to.
        /// </summary>
        ISoftwarePackageStore PackageStore { get; }

        /// <summary>
        /// The per-device software folder.
        /// </summary>
        ISoftwareFolder SoftwareFolder { get; }
    }

    /// <summary>
    /// Fluent configuration surface for a device's
    /// <c>SoftwareUpdateType</c> facet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Created via
    /// <see cref="DeviceBuilderSoftwareUpdateExtensions.WithSoftwareUpdate{TDevice}(IDeviceBuilder{TDevice}, ISoftwarePackageStore, Action{ISoftwareUpdateBuilder})"/>.
    /// </para>
    /// <para>
    /// Pick the Loading subtype with one of <see cref="UsePackageLoading"/> /
    /// <see cref="UseDirectLoading"/> / <see cref="UseCachedLoading"/>;
    /// the default is <see cref="SoftwareLoadingMode.Package"/>.
    /// </para>
    /// <para>
    /// Override the four state-machine method handlers
    /// (Prepare / Install / Confirm / Uninstall) via the
    /// <c>On*</c> methods. The library ships
    /// "succeed-immediately" stubs so the facet is fully functional
    /// out of the box — sample servers don't have to supply any of
    /// the callbacks. Production servers override the relevant
    /// hooks to drive the real firmware-flashing logic.
    /// </para>
    /// </remarks>
    public interface ISoftwareUpdateBuilder
    {
        /// <summary>
        /// Selects <see cref="SoftwareLoadingMode.Package"/> — file
        /// transfer + <c>CloseAndCommit</c>. Default behaviour when
        /// <see cref="DeviceBuilderSoftwareUpdateExtensions.WithSoftwareUpdate{TDevice}(IDeviceBuilder{TDevice}, ISoftwarePackageStore, Action{ISoftwareUpdateBuilder})"/>
        /// is called with no configuration delegate.
        /// </summary>
        ISoftwareUpdateBuilder UsePackageLoading();

        /// <summary>
        /// Selects <see cref="SoftwareLoadingMode.Direct"/>.
        /// </summary>
        ISoftwareUpdateBuilder UseDirectLoading();

        /// <summary>
        /// Selects <see cref="SoftwareLoadingMode.Cached"/>.
        /// </summary>
        ISoftwareUpdateBuilder UseCachedLoading();

        /// <summary>
        /// Overrides the per-device <see cref="ISoftwareFolder"/>.
        /// Defaults to an in-memory <c>MemorySoftwareFolder</c>
        /// keyed by the device's NodeId.
        /// </summary>
        ISoftwareUpdateBuilder WithSoftwareFolder(ISoftwareFolder folder);

        /// <summary>
        /// Sets the handler for <c>PrepareForUpdate.Prepare</c>.
        /// The default stub marks the state machine as Prepared
        /// immediately.
        /// </summary>
        ISoftwareUpdateBuilder OnPrepare(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler);

        /// <summary>
        /// Sets the handler for
        /// <c>Installation.InstallSoftwarePackage</c>. The default
        /// stub records the supplied
        /// <see cref="SoftwarePackage"/> as the device's current
        /// version (via the folder) and transitions to Idle.
        /// </summary>
        ISoftwareUpdateBuilder OnInstall(
            Func<ISoftwareUpdateContext, SoftwarePackage, CancellationToken, ValueTask> handler);

        /// <summary>
        /// Sets the handler for <c>Confirmation.Confirm</c>. The
        /// default stub clears the Confirmation state machine.
        /// </summary>
        ISoftwareUpdateBuilder OnConfirm(
            Func<ISoftwareUpdateContext, bool, CancellationToken, ValueTask> handler);

        /// <summary>
        /// Sets the handler for <c>Installation.Uninstall</c>. The
        /// default stub clears the current version in the folder.
        /// </summary>
        ISoftwareUpdateBuilder OnUninstall(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler);

        /// <summary>
        /// Subscribes a callback that fires whenever the server-side
        /// <c>PrepareForUpdate</c> state machine moves between
        /// phases (Started → InProgress → Completed | Failed). Useful
        /// for surfacing progress in application telemetry.
        /// </summary>
        ISoftwareUpdateBuilder OnPrepareStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler);

        /// <summary>
        /// Subscribes a callback that fires whenever the server-side
        /// <c>Installation</c> state machine moves between phases.
        /// <see cref="SoftwareUpdateStateChange.ProgressPercent"/>
        /// reflects the InstallationStateMachine's
        /// <c>PercentComplete</c> child when the application updates it.
        /// </summary>
        ISoftwareUpdateBuilder OnInstallationStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler);

        /// <summary>
        /// Subscribes a callback that fires whenever the server-side
        /// <c>Confirmation</c> state machine moves between phases.
        /// </summary>
        ISoftwareUpdateBuilder OnConfirmStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler);
    }
}
