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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Convenience abstract base class for <see cref="IHistorianProvider"/>
    /// implementations that want to opt into individual capability
    /// interfaces via overrides instead of explicit interface
    /// implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class deliberately does <strong>not</strong> implement the
    /// optional capability interfaces. Concrete providers add the
    /// interface declarations to their class signature themselves:
    /// the dispatcher inspects the concrete type when routing reads
    /// and updates, so opting in is a matter of writing
    /// <c>class MyProvider : HistorianProviderBase, IHistorianDataProvider</c>.
    /// </para>
    /// </remarks>
    public abstract class HistorianProviderBase : IHistorianProvider
    {
        /// <inheritdoc/>
        public virtual ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
        {
            return new(true);
        }

        /// <inheritdoc/>
        public virtual ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
            NodeId nodeId,
            CancellationToken ct)
        {
            return new(HistorianNodeCapabilities.ReadOnly);
        }

        /// <summary>
        /// Helper that returns a status list pre-filled with the same value.
        /// </summary>
        protected static IList<StatusCode> RepeatStatus(StatusCode code, int count)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < count; i++)
            {
                statuses[i] = code;
            }
            return statuses;
        }
    }
}
