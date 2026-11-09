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
using Opc.Ua.AutoID.Server.Builders;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Configures optional capabilities without adding members to the original reader-builder contract.
    /// </summary>
    public static class AutoIdReaderBuilderExtensions
    {
        /// <inheritdoc cref="IAutoIdIODataBuilder.WithIOData"/>
        /// <param name="builder">The reader builder.</param>
        /// <param name="configure">Binds application-owned IO variables before registration.</param>
        public static IAutoIdReaderBuilder WithIOData(
            this IAutoIdReaderBuilder builder, Action<ISystemContext, BaseObjectState> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAutoIdIODataBuilder capability)
            {
                throw new NotSupportedException("The reader builder does not support IOData composition.");
            }
            return capability.WithIOData(configure);
        }

        /// <inheritdoc cref="IAutoIdImagesBuilder.WithImages"/>
        /// <param name="builder">The reader builder.</param>
        /// <param name="images">The fixed image catalogue.</param>
        public static IAutoIdReaderBuilder WithImages(
            this IAutoIdReaderBuilder builder, ArrayOf<AutoIdImageSlot> images)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAutoIdImagesBuilder capability)
            {
                throw new NotSupportedException("The reader builder does not support image slots.");
            }
            return capability.WithImages(images);
        }

        /// <inheritdoc cref="IAutoIdRuntimeParametersBuilder.WithRuntimeParameters"/>
        /// <param name="builder">The reader builder.</param>
        /// <param name="enableScanControl">Whether to expose writable ScanActive.</param>
        /// <param name="configure">Binds application-specific parameters before registration.</param>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        /// <exception cref="NotSupportedException">The builder does not support runtime parameters.</exception>
        public static IAutoIdReaderBuilder WithRuntimeParameters(
            this IAutoIdReaderBuilder builder,
            bool enableScanControl = false,
            Action<ISystemContext, BaseObjectState>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAutoIdRuntimeParametersBuilder capability)
            {
                throw new NotSupportedException("The reader builder does not support runtime parameters.");
            }
            return capability.WithRuntimeParameters(enableScanControl, configure);
        }

        /// <inheritdoc cref="IAutoIdRuntimeParametersBuilder.WithScanWritePermissions"/>
        /// <param name="builder">The reader builder.</param>
        /// <param name="permissions">The standard role permissions enforced by the owning server.</param>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        /// <exception cref="NotSupportedException">The builder does not support scan write permissions.</exception>
        public static IAutoIdReaderBuilder WithScanWritePermissions(
            this IAutoIdReaderBuilder builder,
            ArrayOf<RolePermissionType> permissions)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAutoIdRuntimeParametersBuilder capability)
            {
                throw new NotSupportedException("The reader builder does not support scan write permissions.");
            }
            return capability.WithScanWritePermissions(permissions);
        }

        /// <inheritdoc cref="IAutoIdDiagnosticsBuilder.WithDiagnostics"/>
        /// <param name="builder">The reader builder.</param>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        /// <exception cref="NotSupportedException">The builder does not support diagnostic projections.</exception>
        public static IAutoIdReaderBuilder WithDiagnostics(this IAutoIdReaderBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAutoIdDiagnosticsBuilder capability)
            {
                throw new NotSupportedException("The reader builder does not support diagnostic projections.");
            }
            return capability.WithDiagnostics();
        }
    }
}
