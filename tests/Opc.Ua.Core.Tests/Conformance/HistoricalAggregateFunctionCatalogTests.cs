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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Conformance
{
    /// <summary>
    /// Validates the mapping of all 37 standard OPC UA aggregate
    /// functions to the optional conformance units of the "Historical
    /// Aggregate 2022 Server Facet" and "Historical Aggregate Client
    /// Facet" profiles in <see cref="HistoricalAggregateFunctionCatalog"/>.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Parallelizable]
    public class HistoricalAggregateFunctionCatalogTests
    {
        /// <summary>
        /// The 37 standard aggregate function names defined by OPC UA
        /// Part 13, in the order the "Historical Aggregate 2022 Server
        /// Facet" profile's optional conformance units list them
        /// (excluding "Custom" and the two configuration-capability
        /// conformance units, which are not individual functions).
        /// </summary>
        private static readonly string[] s_expectedFunctionNames =
        [
            "Interpolative", "Average", "TimeAverage", "TimeAverage2", "Total", "Total2",
            "Minimum", "Maximum", "MinimumActualTime", "MaximumActualTime", "Range",
            "Minimum2", "Maximum2", "MinimumActualTime2", "MaximumActualTime2", "Range2",
            "Count", "AnnotationCount", "DurationInStateZero", "DurationInStateNonZero",
            "NumberOfTransitions", "Start", "End", "Delta", "StartBound", "EndBound",
            "DeltaBounds", "DurationGood", "DurationBad", "PercentGood", "PercentBad",
            "WorstQuality", "WorstQuality2", "StandardDeviationPopulation", "VariancePopulation",
            "StandardDeviationSample", "VarianceSample"
        ];

        [Test]
        public void CatalogContainsThirtySevenAggregateFunctions()
        {
            Assert.That(HistoricalAggregateFunctionCatalog.AllFunctions.Count, Is.EqualTo(37));
            Assert.That(s_expectedFunctionNames, Has.Length.EqualTo(37));
        }

        [Test]
        public void EveryStandardAggregateFunctionIsMapped()
        {
            var actualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                actualNames.Add(function.Name);
            }

            foreach (string expectedName in s_expectedFunctionNames)
            {
                Assert.That(
                    actualNames,
                    Does.Contain(expectedName),
                    $"Missing aggregate function mapping: {expectedName}");
            }
            Assert.That(actualNames, Has.Count.EqualTo(s_expectedFunctionNames.Length));
        }

        [Test]
        public void AllFunctionNamesAreUnique()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                Assert.That(names.Add(function.Name), Is.True, $"Duplicate aggregate function name: {function.Name}");
            }
        }

        [Test]
        public void AllAggregateIdsAreUniqueAndNotNull()
        {
            var ids = new HashSet<NodeId>();
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                Assert.That(function.AggregateId.IsNull, Is.False, $"Null aggregate id for {function.Name}");
                Assert.That(ids.Add(function.AggregateId), Is.True, $"Duplicate aggregate id for {function.Name}");
            }
        }

        [Test]
        public void EveryFunctionBrowseNameMatchesItsName()
        {
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                Assert.That(function.BrowseName.Name, Is.EqualTo(function.Name));
            }
        }

        [Test]
        public void ServerConformanceUnitNamesFollowTheExpectedPattern()
        {
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                Assert.That(function.ServerConformanceUnit, Is.EqualTo("Aggregate \u2013 " + function.Name));
            }
        }

        [Test]
        public void ClientConformanceUnitNamesFollowTheExpectedPattern()
        {
            foreach (HistoricalAggregateFunctionDescriptor function in HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                Assert.That(function.ClientConformanceUnit, Is.EqualTo("Aggregate \u2013 Client " + function.Name));
            }
        }

        [Test]
        public void ServerProfileUriMatchesTheAggregateServerProfileInTheCatalog()
        {
            bool found = HistoricalAccessProfileCatalog.TryGetProfile(
                HistoricalAggregateFunctionCatalog.ServerProfileUri,
                out HistoricalAccessProfileDescriptor descriptor);

            Assert.That(found, Is.True);
            Assert.That(descriptor.Family, Is.EqualTo(HistoricalAccessProfileFamily.Aggregate));
            Assert.That(descriptor.Side, Is.EqualTo(HistoricalAccessProfileSide.Server));
        }

        [Test]
        public void ClientProfileUriMatchesTheAggregateClientProfileInTheCatalog()
        {
            bool found = HistoricalAccessProfileCatalog.TryGetProfile(
                HistoricalAggregateFunctionCatalog.ClientProfileUri,
                out HistoricalAccessProfileDescriptor descriptor);

            Assert.That(found, Is.True);
            Assert.That(descriptor.Family, Is.EqualTo(HistoricalAccessProfileFamily.Aggregate));
            Assert.That(descriptor.Side, Is.EqualTo(HistoricalAccessProfileSide.Client));
        }
    }
}
