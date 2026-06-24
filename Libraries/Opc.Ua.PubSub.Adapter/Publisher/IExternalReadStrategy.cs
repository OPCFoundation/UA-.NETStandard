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

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// Supplies current values for a set of external-server node attributes to the
    /// external-server published-dataset source. Implementations differ only in how the
    /// values are obtained: a cyclic Read service call per publish cycle, or a latest-value
    /// cache maintained by a client Subscription with monitored items.
    /// </summary>
    public interface IExternalReadStrategy
    {
        /// <summary>
        /// Returns the current <see cref="DataValue"/> for each requested node attribute,
        /// aligned positionally to <paramref name="nodesToRead"/>.
        /// </summary>
        /// <param name="nodesToRead">
        /// The node attributes to resolve (typically a PublishedDataSet's published variables).
        /// </param>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead,
            CancellationToken cancellationToken = default);
    }
}
