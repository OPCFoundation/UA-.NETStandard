// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Subscriber-side sink that materialises decoded DataSet fields
    /// into the application's target state (TargetVariables,
    /// mirrored DataSet, custom sink). Writes are atomic: either all
    /// fields are applied or none are.
    /// </summary>
    /// <remarks>
    /// Implements the subscriber sink contract described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9">
    /// Part 14 §6.2.9 DataSetReader</see>, in particular the
    /// TargetVariables and SubscribedDataSetMirror variants.
    /// </remarks>
    public interface ISubscribedDataSetSink
    {
        /// <summary>
        /// Atomically applies <paramref name="fields"/> to the
        /// target state. Implementations must either apply every
        /// field or none; partial writes are not permitted.
        /// </summary>
        /// <param name="fields">Decoded DataSetMessage fields.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask WriteAsync(
            IReadOnlyList<DataSetField> fields,
            CancellationToken cancellationToken = default);
    }
}
