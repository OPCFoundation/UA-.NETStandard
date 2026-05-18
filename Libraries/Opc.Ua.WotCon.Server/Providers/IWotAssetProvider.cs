/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Data-plane interface implemented by a WoT protocol binding driver
    /// (Modbus, HTTP, MQTT, …). One provider instance is created per WoT
    /// asset and lives for the lifetime of the OPC UA asset object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be safe for concurrent use because the OPC UA
    /// stack does not serialise access to providers. Long-running I/O must
    /// honour the supplied <see cref="CancellationToken"/>.
    /// </para>
    /// <para>
    /// Errors should be reported via the returned
    /// <see cref="ServiceResult"/>; throwing is permitted but discouraged
    /// for expected failure paths (e.g. a bad register, a timeout).
    /// </para>
    /// </remarks>
    public interface IWotAssetProvider : IAsyncDisposable
    {
        /// <summary>
        /// Reads the current value of <paramref name="tag"/>.
        /// </summary>
        /// <returns>
        /// A pair of <see cref="ServiceResult"/> describing the operation
        /// outcome and the value to publish on the OPC UA variable. The
        /// value must be assignable to a <c>Variant</c> for the property's
        /// declared <c>DataType</c> / <c>ValueRank</c>.
        /// </returns>
        ValueTask<(ServiceResult Status, object? Value)> ReadAsync(
            WotPropertyTag tag,
            CancellationToken ct);

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="tag"/>.
        /// </summary>
        ValueTask<ServiceResult> WriteAsync(
            WotPropertyTag tag,
            object? value,
            CancellationToken ct);

        /// <summary>
        /// Begins observing the value of <paramref name="tag"/>.
        /// The provider must call <paramref name="callback"/> whenever the
        /// asset reports a new value, status, or timestamp until
        /// <see cref="UnsubscribeAsync"/> is invoked with the same
        /// <paramref name="subscriberId"/>.
        /// </summary>
        ValueTask SubscribeAsync(
            WotPropertyTag tag,
            uint subscriberId,
            OnWotValueChange callback,
            CancellationToken ct);

        /// <summary>
        /// Stops a subscription previously started by
        /// <see cref="SubscribeAsync"/>.
        /// </summary>
        ValueTask UnsubscribeAsync(
            WotPropertyTag tag,
            uint subscriberId,
            CancellationToken ct);

        /// <summary>
        /// Invokes a WoT action (OPC 10100-1 §6.3.9) on the asset.
        /// </summary>
        /// <param name="action">The action descriptor.</param>
        /// <param name="inputs">Parsed input argument values, one per
        /// <see cref="WotActionTag.InputArguments"/> entry.</param>
        /// <param name="outputs">Buffer to populate with output argument
        /// values; pre-sized to <see cref="WotActionTag.OutputArguments"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<ServiceResult> InvokeActionAsync(
            WotActionTag action,
            IReadOnlyList<object?> inputs,
            IList<object?> outputs,
            CancellationToken ct);
    }
}
