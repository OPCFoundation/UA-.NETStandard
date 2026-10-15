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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Describes a material with its authority-assigned persistent slot and density.
    /// </summary>
    /// <param name="Number">The stable three-digit material slot, from one through 999.</param>
    /// <param name="Id">The material identifier used by jobs and equipment.</param>
    /// <param name="Name">The material's localized name.</param>
    /// <param name="Density">The density in the owning MaterialList's DensityUnit.</param>
    public sealed record MaterialListEntry(int Number, string Id, LocalizedText Name, double Density);

    /// <summary>
    /// Carries a complete committed material catalogue, not a delta or an independently generated inventory.
    /// </summary>
    /// <param name="Revision">The monotonic authority revision used for NodeVersion and replay checks.</param>
    /// <param name="SourceTimestamp">The source time of the catalogue commit.</param>
    /// <param name="Materials">The full catalogue, ordered by persistent material number.</param>
    public sealed record MaterialListSnapshot(
        ulong Revision,
        DateTimeUtc SourceTimestamp,
        ArrayOf<MaterialListEntry> Materials)
    {
        /// <summary>
        /// Validates bounded identifiers, unique stable slots and finite positive material density.
        /// </summary>
        public MaterialListSnapshot Validate()
        {
            if (SourceTimestamp == DateTimeUtc.MinValue || Materials.Count > 999)
            {
                throw new ArgumentException("A source timestamp and at most 999 materials are required.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int previous = 0;
            foreach (MaterialListEntry material in Materials)
            {
                if (material == null || material.Number <= previous || material.Number > 999 ||
                    string.IsNullOrWhiteSpace(material.Id) || material.Id.Length > 1024 ||
                    material.Name.IsNull || material.Density <= 0 ||
                    double.IsNaN(material.Density) || double.IsInfinity(material.Density) ||
                    !ids.Add(material.Id))
                {
                    throw new ArgumentException(
                        "Materials require increasing unique slots, unique nonempty identifiers, " +
                        "names and positive density.");
                }
                previous = material.Number;
            }
            return this;
        }
    }

    /// <summary>
    /// Supplies committed material catalogues and validates hardware/job constraints on their mutation.
    /// </summary>
    /// <remarks>
    /// Mutations return the entire committed catalogue with persistent slot numbers. Retrying a request
    /// after publication failed must reconcile the same material rather than create a duplicate.
    /// Implementations reject deletion of an in-use material as appropriate for their device.
    /// They return snapshots rather than reentering this binding's publication while a request is running.
    /// </remarks>
    public interface IMaterialListProvider
    {
        /// <summary>
        /// Reads the committed catalogue using the original caller context.
        /// </summary>
        ValueTask<MaterialListSnapshot> ReadAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits an added material and returns the resulting catalogue.
        /// </summary>
        ValueTask<MaterialListSnapshot> AddAsync(
            PlasticsRubberRequestContext context,
            string id,
            LocalizedText name,
            double density,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits removal of the specified material and returns the resulting catalogue.
        /// </summary>
        ValueTask<MaterialListSnapshot> RemoveAsync(
            PlasticsRubberRequestContext context,
            string id,
            CancellationToken cancellationToken = default);
    }
}
