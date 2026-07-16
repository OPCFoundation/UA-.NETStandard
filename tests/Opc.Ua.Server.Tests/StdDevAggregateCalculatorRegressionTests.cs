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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the regression (RegSlope/RegConst/RegStdDev) calculation exposed by
    /// <see cref="StdDevAggregateCalculator.ComputeRegression"/>. The protected method is
    /// exercised through a derived calculator that routes <c>ComputeValue</c> to it.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StdDevAggregateCalculatorRegressionTests
    {
        private ITelemetryContext m_telemetry;
        private AggregateConfiguration m_configuration;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_configuration = new AggregateConfiguration
            {
                TreatUncertainAsBad = false,
                PercentDataBad = 100,
                PercentDataGood = 100,
                UseSlopedExtrapolation = false
            };
        }

        private sealed class RegressionCalculator : StdDevAggregateCalculator
        {
            private readonly int m_valueType;

            public RegressionCalculator(
                int valueType,
                NodeId aggregateId,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                double processingInterval,
                bool stepped,
                AggregateConfiguration configuration,
                ITelemetryContext telemetry)
                : base(aggregateId, startTime, endTime, processingInterval, stepped, configuration, telemetry)
            {
                m_valueType = valueType;
            }

            protected override DataValue ComputeValue(TimeSlice slice)
            {
                return ComputeRegression(slice, m_valueType);
            }
        }

        private DataValue Compute(int valueType, List<DataValue> values, DateTimeUtc startTime, DateTimeUtc endTime)
        {
            var calculator = new RegressionCalculator(
                valueType,
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                startTime,
                endTime,
                (endTime - startTime).TotalMilliseconds,
                false,
                m_configuration,
                m_telemetry);

            foreach (DataValue value in values)
            {
                calculator.QueueRawValue(value);
            }

            DataValue result = default;
            bool any = false;
            while (calculator.TryGetProcessedValue(true, out DataValue value))
            {
                if (!any)
                {
                    result = value;
                    any = true;
                }
            }

            return result;
        }

        private static List<DataValue> GoodSeries(DateTimeUtc first, double[] values, double intervalMs)
        {
            var list = new List<DataValue>();
            for (int i = 0; i < values.Length; i++)
            {
                DateTimeUtc ts = first.AddMilliseconds(i * intervalMs);
                list.Add(new DataValue(new Variant(values[i]), StatusCodes.Good, ts, ts));
            }
            return list;
        }

        [Test]
        public void RegSlopeReturnsCalculatedResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            List<DataValue> values = GoodSeries(startTime.AddMilliseconds(500), [10, 20, 30, 40, 50], 2000);

            DataValue result = Compute(1, values, startTime, endTime);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void RegConstReturnsCalculatedResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            List<DataValue> values = GoodSeries(startTime.AddMilliseconds(500), [10, 20, 30, 40, 50], 2000);

            DataValue result = Compute(2, values, startTime, endTime);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.StatusCode.AggregateBits.HasFlag(AggregateBits.Calculated), Is.True);
        }

        [Test]
        public void RegStdDevReturnsCalculatedResult()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);
            List<DataValue> values = GoodSeries(startTime.AddMilliseconds(500), [10, 25, 33, 44, 51], 2000);

            DataValue result = Compute(3, values, startTime, endTime);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.WrappedValue.IsNull, Is.False);
            Assert.That(result.WrappedValue.ConvertToDouble().GetDouble(), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void RegSlopeWithNonGoodDataMarksSubNormal()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(12000);

            var values = new List<DataValue>
            {
                new(new Variant(10.0), StatusCodes.Good, t0, t0),
                new(new Variant(20.0), StatusCodes.Good, t0.AddMilliseconds(2000), t0.AddMilliseconds(2000)),
                new(new Variant(0.0), StatusCodes.Bad, t0.AddMilliseconds(4000), t0.AddMilliseconds(4000)),
                new(new Variant(40.0), StatusCodes.Good, t0.AddMilliseconds(6000), t0.AddMilliseconds(6000)),
                new(new Variant(50.0), StatusCodes.Good, t0.AddMilliseconds(8000), t0.AddMilliseconds(8000))
            };

            DataValue result = Compute(1, values, startTime, endTime);

            Assert.That(result.IsNull, Is.False);
            Assert.That(result.StatusCode.CodeBits, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
        }

        [Test]
        public void RegSlopeWithAllBadDataReturnsNoData()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc t0 = startTime.AddMilliseconds(500);
            DateTimeUtc endTime = startTime.AddMilliseconds(6000);

            var values = new List<DataValue>
            {
                new(new Variant(1.0), StatusCodes.Bad, t0, t0),
                new(new Variant(2.0), StatusCodes.Bad, t0.AddMilliseconds(2000), t0.AddMilliseconds(2000))
            };

            DataValue result = Compute(1, values, startTime, endTime);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }
    }
}
