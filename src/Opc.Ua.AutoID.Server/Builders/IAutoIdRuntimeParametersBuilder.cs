/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// Optional reader-builder capability for provider-backed parameters and scan control.
    /// </summary>
    public interface IAutoIdRuntimeParametersBuilder : IAutoIdReaderBuilder
    {
        /// <summary>
        /// Publishes provider-backed RuntimeParameters and ScanSettings. Optionally enables
        /// ScanActive writes through the existing asynchronous scan provider.
        /// </summary>
        /// <param name="enableScanControl">Whether to expose writable ScanActive.</param>
        /// <param name="configure">
        /// Adds reader-specific parameters in the application's namespace below RuntimeParameters,
        /// before address-space registration. The application supplies their metadata and value bindings.
        /// </param>
        /// <remarks>
        /// Requires IAutoIdScanParametersProvider and, for control, IAutoIdAsyncScanProvider.
        /// An optional IAutoIdReaderParametersProvider declares supported standard reader-specific
        /// variables, which use the same complete configuration commit. Undeclared capabilities remain absent.
        /// Settings cannot change during a scan. Variable-triggered scans are device-owned and
        /// survive session closure; method-triggered scans retain their starting session owner.
        /// </remarks>
        IAutoIdReaderBuilder WithRuntimeParameters(
            bool enableScanControl = false,
            Action<ISystemContext, BaseObjectState>? configure = null);

        /// <summary>
        /// Sets role permissions for ScanActive and each standard writable runtime parameter.
        /// </summary>
        /// <param name="permissions">The standard role permissions enforced by the owning server.</param>
        IAutoIdReaderBuilder WithScanWritePermissions(ArrayOf<RolePermissionType> permissions);
    }
}
