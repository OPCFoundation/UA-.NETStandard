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
using System.Linq;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Exact component arithmetic, stable allocation of integer remainders and contribution-preserving splits.
    /// </summary>
    internal static class MaterialLedger
    {
        /// <summary>
        /// Sums component vectors without floating-point conversion.
        /// </summary>
        internal static MaterialMass Sum(IEnumerable<MaterialMass> masses)
        {
            MaterialMass result = default;
            foreach (MaterialMass mass in masses)
            {
                result += mass;
            }
            return result;
        }

        /// <summary>
        /// Canonicalizes a merge by ordinal source identifier.
        /// </summary>
        internal static ArrayOf<SourceContribution> Merge(IEnumerable<SourceContribution> contributions)
        {
            return contributions.GroupBy(static contribution => contribution.SourceId, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(MergeSource)
                .Where(static contribution => contribution.Mass.Total > 0)
                .ToArrayOf();
        }

        /// <summary>
        /// Selects an exact wet mass. Cumulative allocation gives every rounding milligram a deterministic owner.
        /// </summary>
        internal static (ArrayOf<SourceContribution> Selected, ArrayOf<SourceContribution> Remainder) Take(
            MaterialParcel parcel,
            long milligrams)
        {
            long total = parcel.Mass.Total;
            if (milligrams < 0 || milligrams > total)
            {
                throw new ArgumentOutOfRangeException(nameof(milligrams));
            }
            long cumulative = 0;
            long allocated = 0;
            var selected = new List<SourceContribution>();
            var remainder = new List<SourceContribution>();
            foreach (SourceContribution source in parcel.Contributions)
            {
                MaterialMass mass = source.Mass;
                var part = new MaterialMass(
                    Allocate(mass.NaturalPolymer),
                    Allocate(mass.ColoredPolymer),
                    Allocate(mass.DensePolymer),
                    Allocate(mass.Labels),
                    Allocate(mass.Dirt),
                    Allocate(mass.Water));
                Add(selected, source, part);
                Add(remainder, source, mass - part);
            }
            return (selected.ToArrayOf(), remainder.ToArrayOf());

            long Allocate(long mass)
            {
                cumulative = checked(cumulative + mass);
                long next = (long)((decimal)cumulative * milligrams / total);
                long result = next - allocated;
                allocated = next;
                return result;
            }
        }

        /// <summary>
        /// Partitions every source and component according to an explicit physical retention recipe.
        /// </summary>
        internal static (ArrayOf<SourceContribution> Kept, ArrayOf<SourceContribution> Removed) Retain(
            ArrayOf<SourceContribution> contributions,
            MaterialMass permille)
        {
            var kept = new List<SourceContribution>();
            var removed = new List<SourceContribution>();
            foreach (SourceContribution source in contributions)
            {
                MaterialMass mass = source.Mass;
                var part = new MaterialMass(
                    Scale(mass.NaturalPolymer, permille.NaturalPolymer),
                    Scale(mass.ColoredPolymer, permille.ColoredPolymer),
                    Scale(mass.DensePolymer, permille.DensePolymer),
                    Scale(mass.Labels, permille.Labels),
                    Scale(mass.Dirt, permille.Dirt),
                    Scale(mass.Water, permille.Water));
                Add(kept, source, part);
                Add(removed, source, mass - part);
            }
            return (kept.ToArrayOf(), removed.ToArrayOf());
        }

        /// <summary>
        /// Evaluates an explicitly quantized retention/supply fraction.
        /// </summary>
        internal static long Scale(long value, long permille)
        {
            return checked((long)((decimal)value * permille / 1000));
        }

        private static SourceContribution MergeSource(IGrouping<string, SourceContribution> group)
        {
            SourceContribution first = group.First();
            if (group.Any(source => source.SourceZone != first.SourceZone))
            {
                throw new InvalidOperationException(
                    "One source identity cannot change its source zone during a merge.");
            }
            return first with { Mass = Sum(group.Select(static source => source.Mass)) };
        }

        private static void Add(List<SourceContribution> target, SourceContribution source, MaterialMass mass)
        {
            if (mass.Total > 0)
            {
                target.Add(source with { Mass = mass });
            }
        }
    }
}
