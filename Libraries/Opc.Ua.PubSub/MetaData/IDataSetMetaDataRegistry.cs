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

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Shared registry of <see cref="DataSetMetaDataType"/> descriptors
    /// keyed by <see cref="DataSetMetaDataKey"/>. Encoders consult the
    /// registry to discover an incoming payload's field order; decoders
    /// consult it to rehydrate Variant and RawData fields into the
    /// concrete Built-In type indicated by the metadata.
    /// </summary>
    /// <remarks>
    /// Implements the shared-metadata model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.2.3">
    /// Part 14 §5.2.3 DataSetMetaData</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.4">
    /// Part 14 §6.2.9.4 DataSetReader DataSetMetaData</see>, and the
    /// UADP DataSetMetaData announcement message of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
    /// Part 14 §7.2.4.6.4</see>. Implementations must be thread-safe;
    /// <see cref="Register"/> must be atomic with respect to
    /// <see cref="TryGet"/>.
    /// </remarks>
    public interface IDataSetMetaDataRegistry
    {
        /// <summary>
        /// Attempts to resolve the metadata description for the requested
        /// identity tuple.
        /// </summary>
        /// <param name="key">Lookup key.</param>
        /// <param name="metaData">
        /// On <see cref="MetaDataMatchResult.Match"/> or
        /// <see cref="MetaDataMatchResult.MinorVersionMismatch"/>, the
        /// resolved metadata description. On
        /// <see cref="MetaDataMatchResult.MajorVersionMismatch"/> the
        /// currently registered description for the same
        /// PublisherId/WriterGroupId/DataSetWriterId is returned for
        /// diagnostics; the caller must still reject the payload. On
        /// <see cref="MetaDataMatchResult.NotFound"/>,
        /// <see langword="null"/>.
        /// </param>
        /// <returns>The match classification.</returns>
        MetaDataMatchResult TryGet(in DataSetMetaDataKey key, out DataSetMetaDataType? metaData);

        /// <summary>
        /// Adds or replaces the metadata description for the requested
        /// identity tuple. Atomic with respect to concurrent
        /// <see cref="TryGet"/> calls.
        /// </summary>
        /// <param name="key">Identity tuple.</param>
        /// <param name="metaData">
        /// Metadata description to register. Must not be
        /// <see langword="null"/>; the registry takes a reference, not a
        /// clone.
        /// </param>
        void Register(in DataSetMetaDataKey key, DataSetMetaDataType metaData);

        /// <summary>
        /// Removes the metadata description registered for the requested
        /// identity tuple. No-op if no entry exists.
        /// </summary>
        /// <param name="key">Identity tuple.</param>
        void Remove(in DataSetMetaDataKey key);

        /// <summary>
        /// Snapshot of the currently registered keys. Safe to enumerate
        /// without holding any lock; the snapshot does not observe
        /// concurrent <see cref="Register"/> or <see cref="Remove"/>
        /// calls.
        /// </summary>
        IReadOnlyCollection<DataSetMetaDataKey> Keys { get; }

        /// <summary>
        /// Raised whenever a metadata description is registered or
        /// updated. Listeners should detach in-flight reception state
        /// bound to the previous version on a
        /// <see cref="DataSetMetaDataChangedEventArgs.Previous"/>
        /// non-null event.
        /// </summary>
        event EventHandler<DataSetMetaDataChangedEventArgs>? MetaDataChanged;
    }
}
