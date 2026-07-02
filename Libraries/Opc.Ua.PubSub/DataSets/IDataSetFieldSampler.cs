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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Provider sampling exactly one field of a published DataSet
    /// (a monitored variable, a literal constant, a calculated
    /// projection, etc.). Composed into an
    /// <see cref="IPublishedDataSetSource"/> by the runtime to
    /// produce one <see cref="DataSetField"/> per sample.
    /// </summary>
    /// <remarks>
    /// Implements the per-field sampling extension implied by
    /// <see cref="PublishedDataItemsDataType.PublishedData"/> in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3.4">
    /// Part 14 §6.2.3.4 PublishedDataItems</see>.
    /// </remarks>
    public interface IDataSetFieldSampler
    {
        /// <summary>
        /// Configured field name (matches
        /// <see cref="FieldMetaData.Name"/>).
        /// </summary>
        string FieldName { get; }

        /// <summary>
        /// Samples the field at the current time. The supplied
        /// metadata is passed by <see langword="in"/> so the sampler
        /// can derive type-info without re-reading the registry.
        /// </summary>
        /// <param name="metaData">Field metadata.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<DataSetField> SampleAsync(
            in FieldMetaData metaData,
            CancellationToken cancellationToken = default);
    }
}
