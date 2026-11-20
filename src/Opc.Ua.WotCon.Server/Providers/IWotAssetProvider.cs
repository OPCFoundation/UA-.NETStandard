/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// value's <see cref="Variant"/> type info must match the
        /// property's declared <c>DataType</c> / <c>ValueRank</c>.
        /// </returns>
        ValueTask<(ServiceResult Status, Variant Value)> ReadAsync(
            WotPropertyTag tag,
            CancellationToken ct);

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="tag"/>.
        /// </summary>
        ValueTask<ServiceResult> WriteAsync(
            WotPropertyTag tag,
            Variant value,
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
            IReadOnlyList<Variant> inputs,
            IList<Variant> outputs,
            CancellationToken ct);

        /// <summary>
        /// Begins observing the WoT event affordance <paramref name="tag"/>
        /// (OPC 10100-1 §6.3.10). The provider must call
        /// <paramref name="callback"/> for every occurrence the asset reports
        /// until <see cref="UnsubscribeEventAsync"/> is invoked with the same
        /// <paramref name="subscriberId"/>.
        /// </summary>
        /// <param name="tag">The event affordance to observe.</param>
        /// <param name="subscriberId">
        /// Identifies this subscription so it can be cancelled independently
        /// of other subscribers to the same affordance.
        /// </param>
        /// <param name="callback">Invoked for every event occurrence.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SubscribeEventAsync(
            WotEventTag tag,
            uint subscriberId,
            OnWotEvent callback,
            CancellationToken ct);

        /// <summary>
        /// Stops an event subscription previously started by
        /// <see cref="SubscribeEventAsync"/>.
        /// </summary>
        /// <param name="tag">The event affordance to stop observing.</param>
        /// <param name="subscriberId">
        /// The identifier supplied to <see cref="SubscribeEventAsync"/>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask UnsubscribeEventAsync(
            WotEventTag tag,
            uint subscriberId,
            CancellationToken ct);
    }
}
