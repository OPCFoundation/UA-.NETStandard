/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Globalization;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Creates a new instance of an aggregate factory.
    /// </summary>
    public delegate IAggregateCalculator AggregatorFactory(
        NodeId aggregateId,
        DateTime startTime,
        DateTime endTime,
        double processingInterval,
        bool stepped,
        AggregateConfiguration configuration);

    /// <summary>
    /// The set of built-in aggregate factories.
    /// </summary>
    public static class Aggregators
    {
        /// <summary>
        /// Stores the mapping for a aggregate id to the calculator.
        /// </summary>
        private class FactoryMapping
        {
            public NodeId AggregateId { get; set; }
            public QualifiedName AggregateName { get; set; }
            public Type Calculator { get; set; } 
        }

        /// <summary>
        /// Mapping for all of the standard aggregates.
        /// </summary>
        private static FactoryMapping[] s_Mappings = new FactoryMapping[]
        {
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Interpolative, AggregateName = BrowseNames.AggregateFunction_Interpolative, Calculator = typeof(AggregateCalculator) },        
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Average, AggregateName = BrowseNames.AggregateFunction_Average, Calculator = typeof(AverageAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_TimeAverage, AggregateName = BrowseNames.AggregateFunction_TimeAverage, Calculator = typeof(AverageAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_TimeAverage2, AggregateName = BrowseNames.AggregateFunction_TimeAverage2, Calculator = typeof(AverageAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Total, AggregateName = BrowseNames.AggregateFunction_Total, Calculator = typeof(AverageAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Total2, AggregateName = BrowseNames.AggregateFunction_Total2, Calculator = typeof(AverageAggregateCalculator) },

            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Minimum, AggregateName = BrowseNames.AggregateFunction_Minimum, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Maximum, AggregateName = BrowseNames.AggregateFunction_Maximum, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_MinimumActualTime, AggregateName = BrowseNames.AggregateFunction_MinimumActualTime, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_MaximumActualTime, AggregateName = BrowseNames.AggregateFunction_MaximumActualTime, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Range, AggregateName = BrowseNames.AggregateFunction_Range, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Minimum2, AggregateName = BrowseNames.AggregateFunction_Minimum2, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Maximum2, AggregateName = BrowseNames.AggregateFunction_Maximum2, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_MinimumActualTime2, AggregateName = BrowseNames.AggregateFunction_MinimumActualTime2, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_MaximumActualTime2, AggregateName = BrowseNames.AggregateFunction_MaximumActualTime2, Calculator = typeof(MinMaxAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Range2, AggregateName = BrowseNames.AggregateFunction_Range2, Calculator = typeof(MinMaxAggregateCalculator) },

            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Count, AggregateName = BrowseNames.AggregateFunction_Count, Calculator = typeof(CountAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_AnnotationCount, AggregateName = BrowseNames.AggregateFunction_AnnotationCount, Calculator = typeof(CountAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_DurationInStateZero, AggregateName = BrowseNames.AggregateFunction_DurationInStateZero, Calculator = typeof(CountAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_DurationInStateNonZero, AggregateName = BrowseNames.AggregateFunction_DurationInStateNonZero, Calculator = typeof(CountAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_NumberOfTransitions, AggregateName = BrowseNames.AggregateFunction_NumberOfTransitions, Calculator = typeof(CountAggregateCalculator) },

            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Start, AggregateName = BrowseNames.AggregateFunction_Start, Calculator = typeof(StartEndAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_End, AggregateName = BrowseNames.AggregateFunction_End, Calculator = typeof(StartEndAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_Delta, AggregateName = BrowseNames.AggregateFunction_Delta, Calculator = typeof(StartEndAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_StartBound, AggregateName = BrowseNames.AggregateFunction_StartBound, Calculator = typeof(StartEndAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_EndBound, AggregateName = BrowseNames.AggregateFunction_EndBound, Calculator = typeof(StartEndAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_DeltaBounds, AggregateName = BrowseNames.AggregateFunction_DeltaBounds, Calculator = typeof(StartEndAggregateCalculator) },

            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_DurationGood, AggregateName = BrowseNames.AggregateFunction_DurationGood, Calculator = typeof(StatusAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_DurationBad, AggregateName = BrowseNames.AggregateFunction_DurationBad, Calculator = typeof(StatusAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_PercentGood, AggregateName = BrowseNames.AggregateFunction_PercentGood, Calculator = typeof(StatusAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_PercentBad, AggregateName = BrowseNames.AggregateFunction_PercentBad, Calculator = typeof(StatusAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_WorstQuality, AggregateName = BrowseNames.AggregateFunction_WorstQuality, Calculator = typeof(StatusAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_WorstQuality2, AggregateName = BrowseNames.AggregateFunction_WorstQuality2, Calculator = typeof(StatusAggregateCalculator) },

            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_StandardDeviationPopulation, AggregateName = BrowseNames.AggregateFunction_StandardDeviationPopulation, Calculator = typeof(StdDevAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_VariancePopulation, AggregateName = BrowseNames.AggregateFunction_VariancePopulation, Calculator = typeof(StdDevAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_StandardDeviationSample, AggregateName = BrowseNames.AggregateFunction_StandardDeviationSample, Calculator = typeof(StdDevAggregateCalculator) },
            new FactoryMapping() { AggregateId = ObjectIds.AggregateFunction_VarianceSample, AggregateName = BrowseNames.AggregateFunction_VarianceSample, Calculator = typeof(StdDevAggregateCalculator) },
        };

        /// <summary>
        /// Returns the name for a standard aggregates.
        /// </summary>
        public static QualifiedName GetNameForStandardAggregate(NodeId aggregateId)
        {
            for (int ii = 0; ii < s_Mappings.Length; ii++)
            {
                if (s_Mappings[ii].AggregateId == aggregateId)
                {
                    return s_Mappings[ii].AggregateName;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the id for a standard aggregates.
        /// </summary>
        public static NodeId GetIdForStandardAggregate(QualifiedName aggregateName)
        {
            for (int ii = 0; ii < s_Mappings.Length; ii++)
            {
                if (s_Mappings[ii].AggregateName == aggregateName)
                {
                    return s_Mappings[ii].AggregateId;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a calculator for one of the standard aggregates.
        /// </summary>
        public static IAggregateCalculator CreateStandardCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration) 
        {
            for (int ii = 0; ii < s_Mappings.Length; ii++)
            {
                if (s_Mappings[ii].AggregateId == aggregateId)
                {
                    return (IAggregateCalculator)Activator.CreateInstance(
                        s_Mappings[ii].Calculator,
                        aggregateId, 
                        startTime, 
                        endTime, 
                        processingInterval, 
                        stepped, 
                        configuration);
                }
            }

            return null;
        }
    }
}
