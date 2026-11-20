// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default <see cref="ISoftwareUpdateBuilder"/> implementation.
    /// Captures the chosen Loading mode + callback overrides; the
    /// actual address-space wiring is delegated to
    /// <see cref="SoftwareUpdateFacetWiring"/> at
    /// <see cref="DeviceBuilderSoftwareUpdateExtensions.WithSoftwareUpdate{TDevice}(IDeviceBuilder{TDevice}, ISoftwarePackageStore, Action{ISoftwareUpdateBuilder})"/>
    /// time.
    /// </summary>
    internal sealed class SoftwareUpdateBuilder : ISoftwareUpdateBuilder
    {
        public SoftwareLoadingMode LoadingMode { get; private set; } =
            SoftwareLoadingMode.Package;

        public ISoftwareFolder? Folder { get; private set; }

        public Func<ISoftwareUpdateContext, CancellationToken, ValueTask>?
            PrepareHandler
        { get; private set; }

        public Func<ISoftwareUpdateContext, SoftwarePackage, CancellationToken, ValueTask>?
            InstallHandler
        { get; private set; }

        public Func<ISoftwareUpdateContext, bool, CancellationToken, ValueTask>?
            ConfirmHandler
        { get; private set; }

        public Func<ISoftwareUpdateContext, CancellationToken, ValueTask>?
            UninstallHandler
        { get; private set; }

        public Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask>?
            PrepareStateChanged
        { get; private set; }

        public Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask>?
            InstallationStateChanged
        { get; private set; }

        public Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask>?
            ConfirmStateChanged
        { get; private set; }

        public ISoftwareUpdateBuilder UsePackageLoading()
        {
            LoadingMode = SoftwareLoadingMode.Package;
            return this;
        }

        public ISoftwareUpdateBuilder UseDirectLoading()
        {
            LoadingMode = SoftwareLoadingMode.Direct;
            return this;
        }

        public ISoftwareUpdateBuilder UseCachedLoading()
        {
            LoadingMode = SoftwareLoadingMode.Cached;
            return this;
        }

        public ISoftwareUpdateBuilder WithSoftwareFolder(ISoftwareFolder folder)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            return this;
        }

        public ISoftwareUpdateBuilder OnPrepare(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler)
        {
            PrepareHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnInstall(
            Func<ISoftwareUpdateContext, SoftwarePackage, CancellationToken, ValueTask> handler)
        {
            InstallHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnConfirm(
            Func<ISoftwareUpdateContext, bool, CancellationToken, ValueTask> handler)
        {
            ConfirmHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnUninstall(
            Func<ISoftwareUpdateContext, CancellationToken, ValueTask> handler)
        {
            UninstallHandler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnPrepareStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler)
        {
            PrepareStateChanged = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnInstallationStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler)
        {
            InstallationStateChanged = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public ISoftwareUpdateBuilder OnConfirmStateChanged(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask> handler)
        {
            ConfirmStateChanged = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }
    }
}
