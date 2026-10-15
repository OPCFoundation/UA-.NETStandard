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
using Opc.Ua;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Stable section identifiers, independent of optional specification parts.
    /// </summary>
    internal enum MachinerySection
    {
        Overview,
        ProcessValues,
        Jobs,
        Energy,
        Results
    }

    /// <summary>
    /// A compact task label for the section selector.
    /// </summary>
    internal sealed record MachinerySectionChoice(MachinerySection Section, string Label);

    /// <summary>
    /// One named, namespace-qualified machine or directly exposed optional management object.
    /// </summary>
    internal sealed record MachineryMachine(NodeId NodeId, string Name, string Kind);

    /// <summary>
    /// One bounded row; technical evidence is separate from the ordinary summary.
    /// </summary>
    internal sealed record MachineryItem(NodeId NodeId, string Name, string Summary, string Details)
    {
        /// <summary>
        /// Gets the exact source Variable for observation or history navigation.
        /// </summary>
        public NodeId SourceNodeId { get; init; }

        /// <summary>
        /// Gets the exact job or result identity, distinct from its display label.
        /// </summary>
        public string? ItemId { get; init; }

        /// <summary>
        /// Gets the source values belonging to this displayed item, not another page or selection.
        /// </summary>
        public ArrayOf<CompanionValue> Values { get; init; }
    }

    /// <summary>
    /// A single lazily loaded section. Absence and denied/failed data remain distinct statuses.
    /// </summary>
    internal sealed record MachineryPage(
        ArrayOf<CompanionValue> Overview,
        ArrayOf<MachineryItem> Items,
        bool HasMore,
        string Status);

    /// <summary>
    /// Borrows the current connection and fetches only the chosen machine section and page.
    /// </summary>
    internal interface IMachineryDataSource
    {
        /// <summary>
        /// Discovers actual machines or exposed management objects without loading their parts.
        /// </summary>
        Task<ArrayOf<MachineryMachine>> DiscoverAsync(CompanionContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Reads at most pageSize rows in one section; no inactive section may be fetched.
        /// </summary>
        Task<MachineryPage> ReadAsync(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            int pageIndex, int pageSize, CancellationToken cancellationToken);

        /// <summary>
        /// Inspects one explicitly selected item without loading another item or section.
        /// </summary>
        Task<ArrayOf<CompanionValue>> InspectAsync(
            CompanionContext context, MachineryMachine machine, MachinerySection section,
            MachineryItem item, CancellationToken cancellationToken);
    }
}
