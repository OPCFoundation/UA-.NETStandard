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

namespace Opc.Ua
{
    /// <summary>
    /// The 37 standard OPC UA aggregate functions and the optional
    /// conformance unit each one maps to on the "Historical Aggregate
    /// 2022 Server Facet" (<see cref="HistoricalAccessProfileCatalog"/>
    /// profile URI <see cref="ServerProfileUri"/>) and the "Historical
    /// Aggregate Client Facet" (<see cref="ClientProfileUri"/>).
    /// </summary>
    /// <remarks>
    /// Every function listed here corresponds to one optional
    /// conformance unit on each of the two Aggregate profiles; the
    /// "Aggregate – Custom" conformance unit (support for
    /// implementation-specific aggregates beyond this standard set) and
    /// the "Aggregate (Historical) Configuration" / "Aggregate Master
    /// Configuration" capability conformance units are intentionally not
    /// included because they are not individual aggregate functions.
    /// </remarks>
    public static class HistoricalAggregateFunctionCatalog
    {
        /// <summary>
        /// Profile URI of the "Historical Aggregate 2022 Server Facet".
        /// </summary>
        public const string ServerProfileUri = "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022";

        /// <summary>
        /// Profile URI of the "Historical Aggregate Client Facet".
        /// </summary>
        public const string ClientProfileUri = "http://opcfoundation.org/UA-Profile/Client/HistoricalAccessAggregate";

        /// <summary>
        /// All 37 standard aggregate functions, in the same order as
        /// <c>Opc.Ua.Server.Aggregators</c>'s built-in factory table.
        /// </summary>
        public static ArrayOf<HistoricalAggregateFunctionDescriptor> AllFunctions { get; } =
        [
            Create(ObjectIds.AggregateFunction_Interpolative, BrowseNames.AggregateFunction_Interpolative),
            Create(ObjectIds.AggregateFunction_Average, BrowseNames.AggregateFunction_Average),
            Create(ObjectIds.AggregateFunction_TimeAverage, BrowseNames.AggregateFunction_TimeAverage),
            Create(ObjectIds.AggregateFunction_TimeAverage2, BrowseNames.AggregateFunction_TimeAverage2),
            Create(ObjectIds.AggregateFunction_Total, BrowseNames.AggregateFunction_Total),
            Create(ObjectIds.AggregateFunction_Total2, BrowseNames.AggregateFunction_Total2),
            Create(ObjectIds.AggregateFunction_Minimum, BrowseNames.AggregateFunction_Minimum),
            Create(ObjectIds.AggregateFunction_Maximum, BrowseNames.AggregateFunction_Maximum),
            Create(ObjectIds.AggregateFunction_MinimumActualTime, BrowseNames.AggregateFunction_MinimumActualTime),
            Create(ObjectIds.AggregateFunction_MaximumActualTime, BrowseNames.AggregateFunction_MaximumActualTime),
            Create(ObjectIds.AggregateFunction_Range, BrowseNames.AggregateFunction_Range),
            Create(ObjectIds.AggregateFunction_Minimum2, BrowseNames.AggregateFunction_Minimum2),
            Create(ObjectIds.AggregateFunction_Maximum2, BrowseNames.AggregateFunction_Maximum2),
            Create(ObjectIds.AggregateFunction_MinimumActualTime2, BrowseNames.AggregateFunction_MinimumActualTime2),
            Create(ObjectIds.AggregateFunction_MaximumActualTime2, BrowseNames.AggregateFunction_MaximumActualTime2),
            Create(ObjectIds.AggregateFunction_Range2, BrowseNames.AggregateFunction_Range2),
            Create(ObjectIds.AggregateFunction_Count, BrowseNames.AggregateFunction_Count),
            Create(ObjectIds.AggregateFunction_AnnotationCount, BrowseNames.AggregateFunction_AnnotationCount),
            Create(ObjectIds.AggregateFunction_DurationInStateZero, BrowseNames.AggregateFunction_DurationInStateZero),
            Create(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                BrowseNames.AggregateFunction_DurationInStateNonZero),
            Create(ObjectIds.AggregateFunction_NumberOfTransitions, BrowseNames.AggregateFunction_NumberOfTransitions),
            Create(ObjectIds.AggregateFunction_Start, BrowseNames.AggregateFunction_Start),
            Create(ObjectIds.AggregateFunction_End, BrowseNames.AggregateFunction_End),
            Create(ObjectIds.AggregateFunction_Delta, BrowseNames.AggregateFunction_Delta),
            Create(ObjectIds.AggregateFunction_StartBound, BrowseNames.AggregateFunction_StartBound),
            Create(ObjectIds.AggregateFunction_EndBound, BrowseNames.AggregateFunction_EndBound),
            Create(ObjectIds.AggregateFunction_DeltaBounds, BrowseNames.AggregateFunction_DeltaBounds),
            Create(ObjectIds.AggregateFunction_DurationGood, BrowseNames.AggregateFunction_DurationGood),
            Create(ObjectIds.AggregateFunction_DurationBad, BrowseNames.AggregateFunction_DurationBad),
            Create(ObjectIds.AggregateFunction_PercentGood, BrowseNames.AggregateFunction_PercentGood),
            Create(ObjectIds.AggregateFunction_PercentBad, BrowseNames.AggregateFunction_PercentBad),
            Create(ObjectIds.AggregateFunction_WorstQuality, BrowseNames.AggregateFunction_WorstQuality),
            Create(ObjectIds.AggregateFunction_WorstQuality2, BrowseNames.AggregateFunction_WorstQuality2),
            Create(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                BrowseNames.AggregateFunction_StandardDeviationPopulation),
            Create(ObjectIds.AggregateFunction_VariancePopulation, BrowseNames.AggregateFunction_VariancePopulation),
            Create(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                BrowseNames.AggregateFunction_StandardDeviationSample),
            Create(ObjectIds.AggregateFunction_VarianceSample, BrowseNames.AggregateFunction_VarianceSample)
        ];

        private static HistoricalAggregateFunctionDescriptor Create(NodeId aggregateId, string browseName)
        {
            return new HistoricalAggregateFunctionDescriptor(
                browseName,
                aggregateId,
                QualifiedName.From(browseName),
                "Aggregate \u2013 " + browseName,
                "Aggregate \u2013 Client " + browseName);
        }
    }
}
