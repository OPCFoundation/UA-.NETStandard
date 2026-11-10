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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Acquires device values under stable application keys, independently of namespace indexes.
    /// </summary>
    /// <remarks>
    /// The application owns the provider and its persistence. Reads must preserve acquisition
    /// timestamps and quality, including unavailable and stale data.
    /// </remarks>
    public interface IPadimValueProvider
    {
        /// <summary>
        /// Reads an acquired sample without converting its declared numeric or discrete type.
        /// </summary>
        ValueTask<DataValue> ReadAsync(string key, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Accepts changes to explicitly configured device parameters.
    /// </summary>
    public interface IPadimWriteProvider
    {
        /// <summary>
        /// Validates and commits a parameter before returning Good.
        /// </summary>
        ValueTask<StatusCode> WriteAsync(
            string key,
            Variant value,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Authorizes each remote change using the calling session's context.
    /// </summary>
    public interface IPadimAuthorizer
    {
        /// <summary>
        /// Authorizes a parameter write or an operation on the specified node.
        /// </summary>
        ValueTask<bool> AuthorizeAsync(
            ISystemContext caller,
            NodeId nodeId,
            string key,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Device-owned administration and physical adjustment operations.
    /// </summary>
    public interface IPadimOperationProvider
    {
        /// <summary>
        /// Accepts a reset of device parameters, not the hosting server's security.
        /// </summary>
        ValueTask<StatusCode> FactoryResetAsync(
            ResetModeEnum mode,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests a device-owned zero-point adjustment.
        /// </summary>
        ValueTask<StatusCode> ZeroPointAdjustmentAsync(
            string signalKey,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts or aborts device-owned positioner adjustment.
        /// </summary>
        /// <remarks>
        /// Acceptance is not evidence that the physical commissioning cycle has completed.
        /// Publish completion and diagnostic information through device values.
        /// </remarks>
        ValueTask<StatusCode> AutoAdjustPositionerAsync(
            string signalKey,
            ExecutionModeEnum mode,
            CancellationToken cancellationToken = default);
    }
}
