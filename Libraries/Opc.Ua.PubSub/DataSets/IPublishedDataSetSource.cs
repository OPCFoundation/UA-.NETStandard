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

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Pluggable factory + sampler for the data behind one
    /// <see cref="IPublishedDataSet"/>. Used by the runtime to
    /// abstract over variable sampling, event sampling and custom
    /// in-memory sources.
    /// </summary>
    /// <remarks>
    /// Implements the source-of-data extension point implied by
    /// <see cref="PublishedDataItemsDataType"/> and
    /// <see cref="PublishedEventsDataType"/> in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>. Phase 4 ships the
    /// default variable-sampling source; Phase 1 only commits the
    /// contract.
    /// </remarks>
    public interface IPublishedDataSetSource
    {
        /// <summary>
        /// Builds the MetaData describing the fields this source
        /// will emit. Called once at PublishedDataSet construction
        /// time and again whenever the source detects a schema
        /// change.
        /// </summary>
        DataSetMetaDataType BuildMetaData();

        /// <summary>
        /// Samples all fields described by <paramref name="metaData"/>
        /// and returns a snapshot.
        /// </summary>
        /// <param name="metaData">
        /// MetaData describing the field set. The source uses this
        /// to determine which variables / events to read.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default);
    }
}
