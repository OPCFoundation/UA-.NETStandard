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
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Materialises the OPC 10000-100 §10.3 <c>SoftwareUpdateType</c>
    /// instance under a device and binds its method invocations to
    /// user-supplied callbacks (with library-supplied default stubs
    /// when callbacks are omitted).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Address-space layout:
    /// </para>
    /// <code>
    /// {device}
    /// └─ SoftwareUpdate (SoftwareUpdateType)
    ///    ├─ Loading           (PackageLoadingType | DirectLoadingType | CachedLoadingType)
    ///    ├─ PrepareForUpdate  (PrepareForUpdateStateMachineType)
    ///    │   • Methods: Prepare, Abort, Resume
    ///    ├─ Installation      (InstallationStateMachineType)
    ///    │   • Methods: InstallSoftwarePackage, Resume, Uninstall
    ///    ├─ PowerCycle        (PowerCycleStateMachineType)
    ///    └─ Confirmation      (ConfirmationStateMachineType)
    ///        • Methods: Confirm
    /// </code>
    /// <para>
    /// The library ships "succeed immediately" defaults for every
    /// method handler so callers can opt in to SU with one line and
    /// later override individual hooks.
    /// </para>
    /// </remarks>
    internal static class SoftwareUpdateFacetWiring
    {
        private const string SoftwareUpdateBrowseName = "SoftwareUpdate";
        private const string LoadingBrowseName = "Loading";
        private const string PrepareForUpdateBrowseName = "PrepareForUpdate";
        private const string InstallationBrowseName = "Installation";
        private const string PowerCycleBrowseName = "PowerCycle";
        private const string ConfirmationBrowseName = "Confirmation";

        public static SoftwareUpdateState BuildAndAttach(
            DiNodeManager manager,
            BaseInstanceState device,
            ISoftwarePackageStore packageStore,
            SoftwareUpdateBuilder config)
        {
            var context = manager.SystemContext;
            ushort diNs = manager.DiNamespaceIndex;
            ILogger logger = manager.Server.Telemetry.CreateLogger(
                typeof(SoftwareUpdateFacetWiring));

            // Per-device software folder (auto-registered MemorySoftwareFolder
            // unless the caller supplied one via WithSoftwareFolder).
            ISoftwareFolder folder = config.Folder
                ?? new MemorySoftwareFolder(device.NodeId);

            var callbackContext = new SoftwareUpdateContext(
                device.NodeId, context, packageStore, folder);

            // Materialise the SoftwareUpdate node itself.
            SoftwareUpdateState su = context.CreateInstanceOfSoftwareUpdateType(
                parent: device,
                browseName: new QualifiedName(SoftwareUpdateBrowseName, diNs));

            su.SymbolicName = SoftwareUpdateBrowseName;
            su.BrowseName = new QualifiedName(SoftwareUpdateBrowseName, diNs);
            su.DisplayName = new LocalizedText(SoftwareUpdateBrowseName);
            su.NodeId = context.NodeIdFactory.New(context, su);
            su.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            su.ModellingRuleId = NodeId.Null;

            // Loading subtype (the address-space slot is non-abstract;
            // pick the concrete type per the builder's LoadingMode).
            su.Loading = CreateLoadingChild(context, su, config.LoadingMode, diNs);

            // State machines — all four are present but only Installation
            // and Confirmation have method handlers wired by default;
            // PrepareForUpdate.Prepare/Abort/Resume also wired.
            su.PrepareForUpdate = CreatePrepareForUpdate(
                context, su, diNs, config, callbackContext, logger);

            su.Installation = CreateInstallation(
                context, su, diNs, config, callbackContext, logger);

            su.PowerCycle = CreatePowerCycle(context, su, diNs);

            su.Confirmation = CreateConfirmation(
                context, su, diNs, config, callbackContext, logger);

            // Link the SU node into the device subtree and register
            // recursively with the manager.
            device.AddChild(su);
            manager.AddPredefinedNodeAsync(su, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            return su;
        }

        private static SoftwareLoadingState CreateLoadingChild(
            ISystemContext context,
            SoftwareUpdateState parent,
            SoftwareLoadingMode mode,
            ushort diNs)
        {
            var browseName = new QualifiedName(LoadingBrowseName, diNs);

            SoftwareLoadingState loading = mode switch
            {
                SoftwareLoadingMode.Package =>
                    context.CreateInstanceOfPackageLoadingType(parent, browseName),
                SoftwareLoadingMode.Direct =>
                    context.CreateInstanceOfDirectLoadingType(parent, browseName),
                SoftwareLoadingMode.Cached =>
                    context.CreateInstanceOfCachedLoadingType(parent, browseName),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };

            FinaliseChild(context, loading, browseName);
            return loading;
        }

        private static PrepareForUpdateStateMachineState CreatePrepareForUpdate(
            ISystemContext context,
            SoftwareUpdateState parent,
            ushort diNs,
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext callbackContext,
            ILogger logger)
        {
            var browseName = new QualifiedName(PrepareForUpdateBrowseName, diNs);
            PrepareForUpdateStateMachineState sm =
                context.CreateInstanceOfPrepareForUpdateStateMachineType(parent, browseName);
            FinaliseChild(context, sm, browseName);

            // CreateInstanceOf already populated Prepare + Abort; only the
            // Resume slot is left empty by the source-gen factory.
            sm.Resume ??= CreateMethodChild(context, sm,
                new QualifiedName("Resume", diNs));

            if (sm.Prepare != null)
            {
                FinaliseChild(context, sm.Prepare,
                    new QualifiedName("Prepare", diNs));
                sm.Prepare.OnCallMethod2Async = (ctx, m, oid, ins, outs, ct) =>
                    InvokePrepareAsync(config, callbackContext, sm, diNs, logger, ct);
            }
            if (sm.Abort != null)
            {
                FinaliseChild(context, sm.Abort,
                    new QualifiedName("Abort", diNs));
            }
            if (sm.Resume != null)
            {
                sm.Resume.OnCallMethod2Async = (ctx, m, oid, ins, outs, ct) =>
                    InvokePrepareAsync(config, callbackContext, sm, diNs, logger, ct);
            }

            SoftwareUpdateStateMachineDispatcher.InitializeToInitialState(
                sm,
                SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle,
                diNs,
                context);

            return sm;
        }

        private static InstallationStateMachineState CreateInstallation(
            ISystemContext context,
            SoftwareUpdateState parent,
            ushort diNs,
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext callbackContext,
            ILogger logger)
        {
            var browseName = new QualifiedName(InstallationBrowseName, diNs);
            InstallationStateMachineState sm =
                context.CreateInstanceOfInstallationStateMachineType(parent, browseName);
            FinaliseChild(context, sm, browseName);

            // CreateInstanceOf only adds CurrentState + Resume. Materialise
            // the optional InstallSoftwarePackage / InstallFiles / Uninstall
            // method children explicitly.
            var installPkgBn = new QualifiedName("InstallSoftwarePackage", diNs);
            InstallSoftwarePackageMethodState installPkg =
                context.CreateInstanceOfInstallSoftwarePackageMethodType(sm, installPkgBn);
            FinaliseChild(context, installPkg, installPkgBn);
            sm.AddChild(installPkg);
            sm.InstallSoftwarePackage = installPkg;
            installPkg.OnCallMethod2Async =
                (ctx, m, oid, ins, outs, ct) =>
                    InvokeInstallSoftwarePackageAsync(
                        config, callbackContext, sm, diNs, logger, ins, ct);

            var installFilesBn = new QualifiedName("InstallFiles", diNs);
            InstallFilesMethodState installFiles =
                context.CreateInstanceOfInstallFilesMethodType(sm, installFilesBn);
            FinaliseChild(context, installFiles, installFilesBn);
            sm.AddChild(installFiles);
            sm.InstallFiles = installFiles;
            installFiles.OnCallMethod2Async =
                (ctx, m, oid, ins, outs, ct) =>
                    InvokeInstallFilesAsync(config, callbackContext, sm, diNs, logger, ct);

            var uninstallBn = new QualifiedName("Uninstall", diNs);
            MethodState uninstall = CreateMethodChild(context, sm, uninstallBn);
            sm.Uninstall = uninstall;
            uninstall.OnCallMethod2Async =
                (ctx, m, oid, ins, outs, ct) =>
                    InvokeUninstallAsync(config, callbackContext, sm, diNs, logger, ct);

            if (sm.Resume != null)
            {
                FinaliseChild(context, sm.Resume,
                    new QualifiedName("Resume", diNs));
            }

            SoftwareUpdateStateMachineDispatcher.InitializeToInitialState(
                sm,
                SoftwareUpdateStateMachineDispatcher.Installation_Idle,
                diNs,
                context);

            return sm;
        }

        private static PowerCycleStateMachineState CreatePowerCycle(
            ISystemContext context,
            SoftwareUpdateState parent,
            ushort diNs)
        {
            var browseName = new QualifiedName(PowerCycleBrowseName, diNs);
            PowerCycleStateMachineState sm =
                context.CreateInstanceOfPowerCycleStateMachineType(parent, browseName);
            FinaliseChild(context, sm, browseName);

            SoftwareUpdateStateMachineDispatcher.InitializeToInitialState(
                sm,
                SoftwareUpdateStateMachineDispatcher.PowerCycle_NotWaiting,
                diNs,
                context);

            return sm;
        }

        private static ConfirmationStateMachineState CreateConfirmation(
            ISystemContext context,
            SoftwareUpdateState parent,
            ushort diNs,
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext callbackContext,
            ILogger logger)
        {
            var browseName = new QualifiedName(ConfirmationBrowseName, diNs);
            ConfirmationStateMachineState sm =
                context.CreateInstanceOfConfirmationStateMachineType(parent, browseName);
            FinaliseChild(context, sm, browseName);

            if (sm.Confirm != null)
            {
                FinaliseChild(context, sm.Confirm,
                    new QualifiedName("Confirm", diNs));
                sm.Confirm.OnCallMethod2Async =
                    (ctx, m, oid, ins, outs, ct) =>
                        InvokeConfirmAsync(config, callbackContext, sm, diNs, logger, ct);
            }

            SoftwareUpdateStateMachineDispatcher.InitializeToInitialState(
                sm,
                SoftwareUpdateStateMachineDispatcher.Confirmation_NotWaitingForConfirm,
                diNs,
                context);

            return sm;
        }

        /// <summary>
        /// Creates a bare <see cref="MethodState"/> child with a
        /// per-instance NodeId and the supplied browse name. Used for
        /// inherited methods (Resume / Uninstall) that aren't typed
        /// in the source-generated model.
        /// </summary>
        private static MethodState CreateMethodChild(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName)
        {
            var method = new MethodState(parent);
            FinaliseChild(context, method, browseName);
            method.Executable = true;
            method.UserExecutable = true;
            return method;
        }

        /// <summary>
        /// Common finalisation step for an SU child: assign a per-instance
        /// NodeId via the manager's <see cref="INodeIdFactory"/>, set the
        /// HasComponent reference, and clear the type's modelling rule
        /// so the instance isn't treated as a placeholder.
        /// </summary>
        private static void FinaliseChild(
            ISystemContext context,
            BaseInstanceState child,
            QualifiedName browseName)
        {
            child.SymbolicName = browseName.Name ?? string.Empty;
            child.BrowseName = browseName;
            child.DisplayName = new LocalizedText(browseName.Name);
            child.NodeId = context.NodeIdFactory.New(context, child);
            child.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            child.ModellingRuleId = NodeId.Null;
        }

        // ---------------------------------------------------------------
        // Method-handler shims — invoke the user-supplied (or default)
        // callback and translate any exception into a ServiceResult.
        // ---------------------------------------------------------------

        private static async ValueTask<ServiceResult> InvokePrepareAsync(
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext context,
            PrepareForUpdateStateMachineState sm,
            ushort diNs,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            ISystemContext sys = context.SystemContext;
            SoftwareUpdateStateMachineDispatcher.Move(
                sm,
                SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Preparing,
                SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_IdleToPreparing,
                diNs,
                sys);
            await SoftwareUpdateStateMachineDispatcher.FireAsync(
                config.PrepareStateChanged, context,
                new SoftwareUpdateStateChange(SoftwareUpdatePhase.Started, string.Empty, null),
                logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                if (config.PrepareHandler != null)
                {
                    await config.PrepareHandler(context, cancellationToken)
                        .ConfigureAwait(false);
                }

                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparedForUpdate,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparingToPreparedForUpdate,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.PrepareStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Completed, string.Empty, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparingToIdle,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.PrepareStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Failed, ex.Message, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return new ServiceResult(ex);
            }
        }

        private static async ValueTask<ServiceResult> InvokeInstallSoftwarePackageAsync(
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext context,
            InstallationStateMachineState sm,
            ushort diNs,
            ILogger logger,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            ISystemContext sys = context.SystemContext;
            SoftwareUpdateStateMachineDispatcher.Move(
                sm,
                SoftwareUpdateStateMachineDispatcher.Installation_Installing,
                SoftwareUpdateStateMachineDispatcher.Installation_IdleToInstalling,
                diNs,
                sys);
            SoftwareUpdateStateMachineDispatcher.SetPercentComplete(sm, 0, sys);
            await SoftwareUpdateStateMachineDispatcher.FireAsync(
                config.InstallationStateChanged, context,
                new SoftwareUpdateStateChange(SoftwareUpdatePhase.Started, string.Empty, 0),
                logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                // InstallSoftwarePackage(ManufacturerUri, SoftwareRevision,
                //                        PatchIdentifiers[], Hash)
                string manufacturerUri = ExtractString(inputs, 0);
                string softwareRevision = ExtractString(inputs, 1);

                var package = new SoftwarePackage(
                    Id: BuildPackageId(manufacturerUri, softwareRevision),
                    Version: softwareRevision,
                    Vendor: manufacturerUri,
                    Description: string.Empty,
                    SizeBytes: 0,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Hash: string.Empty);

                if (config.InstallHandler != null)
                {
                    await config.InstallHandler(context, package, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Default stub: record the new version in the folder.
                    SoftwarePackage? existing = await context.SoftwareFolder
                        .GetVersionAsync(package.Version, cancellationToken)
                        .ConfigureAwait(false);
                    if (existing is null)
                    {
                        await context.SoftwareFolder
                            .AddVersionAsync(package, System.IO.Stream.Null, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    await context.SoftwareFolder
                        .SetCurrentVersionAsync(package.Version, cancellationToken)
                        .ConfigureAwait(false);
                }

                SoftwareUpdateStateMachineDispatcher.SetPercentComplete(sm, 100, sys);
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Idle,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToIdle,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Completed, string.Empty, 100),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Error,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToError,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Failed, ex.Message, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return new ServiceResult(ex);
            }
        }

        private static async ValueTask<ServiceResult> InvokeInstallFilesAsync(
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext context,
            InstallationStateMachineState sm,
            ushort diNs,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            ISystemContext sys = context.SystemContext;
            SoftwareUpdateStateMachineDispatcher.Move(
                sm,
                SoftwareUpdateStateMachineDispatcher.Installation_Installing,
                SoftwareUpdateStateMachineDispatcher.Installation_IdleToInstalling,
                diNs,
                sys);
            SoftwareUpdateStateMachineDispatcher.SetPercentComplete(sm, 0, sys);
            await SoftwareUpdateStateMachineDispatcher.FireAsync(
                config.InstallationStateChanged, context,
                new SoftwareUpdateStateChange(SoftwareUpdatePhase.Started, string.Empty, 0),
                logger,
                cancellationToken).ConfigureAwait(false);

            // InstallFiles flows the same way as InstallSoftwarePackage
            // from the application's point of view, just with a NodeId[]
            // input. The default callback synthesises a minimal package
            // entry; production servers override OnInstall to carry the
            // real file metadata.
            try
            {
                if (config.InstallHandler != null)
                {
                    var package = new SoftwarePackage(
                        Id: $"install-files-{DateTime.UtcNow:yyyyMMddHHmmss}",
                        Version: string.Empty,
                        Vendor: string.Empty,
                        Description: "Files-based install",
                        SizeBytes: 0,
                        CreatedAt: DateTimeOffset.UtcNow,
                        Hash: string.Empty);
                    await config.InstallHandler(context, package, cancellationToken)
                        .ConfigureAwait(false);
                }

                SoftwareUpdateStateMachineDispatcher.SetPercentComplete(sm, 100, sys);
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Idle,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToIdle,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Completed, string.Empty, 100),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Error,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToError,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Failed, ex.Message, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return new ServiceResult(ex);
            }
        }

        private static async ValueTask<ServiceResult> InvokeConfirmAsync(
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext context,
            ConfirmationStateMachineState sm,
            ushort diNs,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            ISystemContext sys = context.SystemContext;
            await SoftwareUpdateStateMachineDispatcher.FireAsync(
                config.ConfirmStateChanged, context,
                new SoftwareUpdateStateChange(SoftwareUpdatePhase.Started, string.Empty, null),
                logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                if (config.ConfirmHandler != null)
                {
                    await config.ConfirmHandler(context, true, cancellationToken)
                        .ConfigureAwait(false);
                }

                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Confirmation_NotWaitingForConfirm,
                    SoftwareUpdateStateMachineDispatcher.Confirmation_WaitingToNotWaiting,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.ConfirmStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Completed, string.Empty, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.ConfirmStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Failed, ex.Message, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return new ServiceResult(ex);
            }
        }

        private static async ValueTask<ServiceResult> InvokeUninstallAsync(
            SoftwareUpdateBuilder config,
            SoftwareUpdateContext context,
            InstallationStateMachineState sm,
            ushort diNs,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            ISystemContext sys = context.SystemContext;
            SoftwareUpdateStateMachineDispatcher.Move(
                sm,
                SoftwareUpdateStateMachineDispatcher.Installation_Installing,
                SoftwareUpdateStateMachineDispatcher.Installation_IdleToInstalling,
                diNs,
                sys);
            await SoftwareUpdateStateMachineDispatcher.FireAsync(
                config.InstallationStateChanged, context,
                new SoftwareUpdateStateChange(SoftwareUpdatePhase.Started, "Uninstall", null),
                logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                if (config.UninstallHandler != null)
                {
                    await config.UninstallHandler(context, cancellationToken)
                        .ConfigureAwait(false);
                }

                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Idle,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToIdle,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Completed, "Uninstall", null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                SoftwareUpdateStateMachineDispatcher.Move(
                    sm,
                    SoftwareUpdateStateMachineDispatcher.Installation_Error,
                    SoftwareUpdateStateMachineDispatcher.Installation_InstallingToError,
                    diNs,
                    sys);
                await SoftwareUpdateStateMachineDispatcher.FireAsync(
                    config.InstallationStateChanged, context,
                    new SoftwareUpdateStateChange(SoftwareUpdatePhase.Failed, ex.Message, null),
                    logger,
                    cancellationToken).ConfigureAwait(false);

                return new ServiceResult(ex);
            }
        }

        private static string ExtractString(ArrayOf<Variant> inputs, int index)
        {
            if (inputs.Count <= index)
            {
                return string.Empty;
            }
            return inputs[index].TryGetValue(out string s) ? s ?? string.Empty : string.Empty;
        }

        private static string BuildPackageId(string manufacturerUri, string version)
        {
            string m = string.IsNullOrEmpty(manufacturerUri) ? "unknown" : manufacturerUri;
            string v = string.IsNullOrEmpty(version) ? "0.0.0" : version;
            return $"{m}:{v}";
        }
    }
}
