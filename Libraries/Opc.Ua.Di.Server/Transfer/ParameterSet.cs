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

namespace Opc.Ua.Di.Server.Transfer
{
    /// <summary>
    /// A single parameter value carried by a <see cref="ParameterSet"/>.
    /// The <see cref="NodePath"/> is the chain of
    /// <see cref="QualifiedName"/>s that locate the parameter relative
    /// to the topology element (typically a device).
    /// </summary>
    /// <param name="NodePath">
    /// Browse-name chain identifying the parameter under the topology
    /// element (e.g. <c>["ParameterSet", "DeviceManual"]</c>).
    /// </param>
    /// <param name="Value">The parameter value.</param>
    /// <param name="StatusCode">
    /// Per-parameter status. <see cref="Opc.Ua.StatusCodes.Good"/>
    /// for successful transfers; per-parameter bad codes are surfaced
    /// to the client via <c>FetchTransferResultData</c>.
    /// </param>
    public sealed record ParameterEntry(
        QualifiedName[] NodePath,
        Variant Value,
        StatusCode StatusCode);

    /// <summary>
    /// An ordered set of parameters representing one snapshot of a
    /// topology element's configurable state. Created by
    /// <c>ITransferService.ExportAsync</c> and consumed by
    /// <c>ITransferService.ImportAsync</c>.
    /// </summary>
    public sealed class ParameterSet
    {
        /// <summary>
        /// Creates an empty parameter set for
        /// <paramref name="elementId"/>.
        /// </summary>
        public ParameterSet(NodeId elementId)
        {
            if (elementId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(elementId));
            }
            ElementId = elementId;
            Entries = new List<ParameterEntry>();
        }

        /// <summary>
        /// Creates a parameter set with the supplied entries.
        /// </summary>
        public ParameterSet(NodeId elementId, IList<ParameterEntry> entries)
        {
            if (elementId.IsNull)
            {
                throw new System.ArgumentNullException(nameof(elementId));
            }
            ElementId = elementId;
            Entries = entries ?? throw new System.ArgumentNullException(nameof(entries));
        }

        /// <summary>
        /// The topology element this set belongs to.
        /// </summary>
        public NodeId ElementId { get; }

        /// <summary>
        /// The parameter entries in this set, in insertion order.
        /// </summary>
        public IList<ParameterEntry> Entries { get; }
    }
}
