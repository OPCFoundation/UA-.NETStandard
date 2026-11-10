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
