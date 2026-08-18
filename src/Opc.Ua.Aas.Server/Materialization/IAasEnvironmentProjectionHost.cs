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

using Opc.Ua.Aas.V3;
using AasV2Environment = Opc.Ua.Aas.V2.AasEnvironment;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.Server.Materialization
{
    /// <summary>
    /// Projects materialized AAS environments into a live server.
    /// </summary>
    public interface IAasEnvironmentProjectionHost
    {
        /// <summary>
        /// Adds a live environment projection.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> AddAsync(
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a live AAS V2 environment projection.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> AddAsync(
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Shadow-reloads a live environment projection.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Shadow-reloads a live AAS V2 environment projection.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reloads a live environment projection and immediately retires the previous generation.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasEnvironment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reloads a live AAS V2 environment projection and immediately retires the previous generation.
        /// </summary>
        ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
            AasEnvironmentProjectionHandle current,
            AasV2Environment environment,
            IAasValueProvider valueProvider,
            IAasOperationHandler operationHandler,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a live environment projection.
        /// </summary>
        ValueTask RemoveAsync(
            AasEnvironmentProjectionHandle handle,
            CancellationToken cancellationToken = default);
    }
}
