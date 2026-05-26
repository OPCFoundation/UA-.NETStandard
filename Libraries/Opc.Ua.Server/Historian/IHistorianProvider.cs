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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// The umbrella interface implemented by every historian provider.
    /// Providers opt into specific Part 11 features by additionally
    /// implementing one or more of the narrow capability interfaces:
    /// <see cref="IHistorianDataProvider"/>,
    /// <see cref="IHistorianModifiedProvider"/>,
    /// <see cref="IHistorianAtTimeProvider"/>,
    /// <see cref="IHistorianProcessedProvider"/>,
    /// <see cref="IHistorianAnnotationProvider"/>, or
    /// <see cref="IHistorianEventProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This umbrella exists so consumers can resolve "the historian for a
    /// node" without first knowing which capability they need. The
    /// dispatcher inspects which sub-interfaces the concrete provider
    /// implements and routes calls accordingly. Capability advertising
    /// goes through <see cref="GetCapabilitiesAsync"/> — a provider may
    /// implement <see cref="IHistorianDataProvider"/> but advertise
    /// <see cref="HistorianNodeCapabilities.InsertData"/> = <c>false</c>
    /// for a specific read-only node.
    /// </para>
    /// </remarks>
    public interface IHistorianProvider
    {
        /// <summary>
        /// Returns <c>true</c> when the provider can serve history for the
        /// specified node. False values let the registry continue resolving
        /// against other providers / the default fallback.
        /// </summary>
        /// <param name="nodeId">The historizing variable or notifier.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct);

        /// <summary>
        /// Returns the per-node capability set.
        /// </summary>
        /// <param name="nodeId">The historizing variable or notifier.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct);
    }
}
