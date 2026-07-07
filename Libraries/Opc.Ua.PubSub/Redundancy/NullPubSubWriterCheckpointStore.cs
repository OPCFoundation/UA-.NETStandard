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

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// No-op <see cref="IPubSubWriterCheckpointStore"/> used when Hot redundancy is not
    /// configured, so the non-redundant publish path keeps its process-local SequenceNumbers
    /// without any shared-store interaction.
    /// </summary>
    public sealed class NullPubSubWriterCheckpointStore : IPubSubWriterCheckpointStore
    {
        /// <summary>
        /// Gets the shared no-op instance.
        /// </summary>
        public static NullPubSubWriterCheckpointStore Instance { get; } = new();

        private NullPubSubWriterCheckpointStore()
        {
        }

        /// <inheritdoc/>
        public ValueTask<uint?> GetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<uint?>((uint?)null);
        }

        /// <inheritdoc/>
        public ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
