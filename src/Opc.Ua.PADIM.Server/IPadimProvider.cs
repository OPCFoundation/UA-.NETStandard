/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
