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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Aggregators")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AggregatorsTests
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

        [Test]
        public void GetNameForStandardAggregateReturnsNameForInterpolative()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Interpolative);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Interpolative)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForAverage()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Average);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Average)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForMinimum()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Minimum);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Minimum)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForMaximum()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Maximum);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Maximum)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForCount()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Count);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Count)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForStart()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Start);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Start)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForStdDevPopulation()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_StandardDeviationPopulation);
            Assert.That(name, Is.EqualTo(
                QualifiedName.From(BrowseNames.AggregateFunction_StandardDeviationPopulation)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsDefaultForUnknownId()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(new NodeId(999999));
            Assert.That(name, Is.EqualTo(default(QualifiedName)));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForAverage()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Average));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Average));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForMinimum()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Minimum));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Minimum));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForCount()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Count));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Count));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForStart()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Start));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Start));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForStdDevPopulation()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_StandardDeviationPopulation));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_StandardDeviationPopulation));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsDefaultForUnknownName()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(new QualifiedName("NonExistentAggregate"));
            Assert.That(id, Is.EqualTo(default(NodeId)));
        }

        [Test]
        public void CreateStandardCalculatorReturnsCalculatorForInterpolative()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Interpolative,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsAverageCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsTimeAverageCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_TimeAverage,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsTimeAverage2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_TimeAverage2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsTotalCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Total,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsTotal2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Total2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMinMaxCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Minimum,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsCountCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Count,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<CountAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsStartEndCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Start,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsStdDevCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StdDevAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsStatusCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DurationGood,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsNullForUnknownId()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                new NodeId(999999),
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Null);
        }

        [Test]
        public void CreateStandardCalculatorReturnsAnnotationCountCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_AnnotationCount,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<CountAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDurationInStateZeroCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DurationInStateZero,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<CountAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsNumberOfTransitionsCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_NumberOfTransitions,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<CountAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDeltaCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Delta,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDeltaBoundsCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DeltaBounds,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsVariancePopulationCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_VariancePopulation,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StdDevAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsVarianceSampleCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_VarianceSample,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StdDevAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsStdDevSampleCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_StandardDeviationSample,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StdDevAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsWorstQualityCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_WorstQuality,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsWorstQuality2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_WorstQuality2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsPercentGoodCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_PercentGood,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsPercentBadCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_PercentBad,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDurationBadCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DurationBad,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForEnd()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_End);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_End)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForDelta()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Delta);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Delta)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForRange()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Range);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Range)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForTotal()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_Total);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_Total)));
        }

        [Test]
        public void GetNameForStandardAggregateReturnsNameForTimeAverage()
        {
            QualifiedName name = Aggregators.GetNameForStandardAggregate(
                ObjectIds.AggregateFunction_TimeAverage);
            Assert.That(name, Is.EqualTo(QualifiedName.From(BrowseNames.AggregateFunction_TimeAverage)));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForEnd()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_End));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_End));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForDelta()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Delta));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Delta));
        }

        [Test]
        public void GetIdForStandardAggregateReturnsIdForRange()
        {
            NodeId id = Aggregators.GetIdForStandardAggregate(
                QualifiedName.From(BrowseNames.AggregateFunction_Range));
            Assert.That(id, Is.EqualTo(ObjectIds.AggregateFunction_Range));
        }

        [Test]
        public void CreateStandardCalculatorReturnsEndBoundCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_EndBound,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsStartBoundCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_StartBound,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDurationInStateNonZeroCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DurationInStateNonZero,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<CountAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMinimum2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Minimum2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMaximum2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Maximum2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsRange2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Range2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMinimumActualTime2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_MinimumActualTime2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMaximumActualTime2Calculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_MaximumActualTime2,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void GetNameAndIdAreConsistentForAllMappings()
        {
            NodeId[] aggregateIds =
            [
                ObjectIds.AggregateFunction_Interpolative,
                ObjectIds.AggregateFunction_Average,
                ObjectIds.AggregateFunction_TimeAverage,
                ObjectIds.AggregateFunction_TimeAverage2,
                ObjectIds.AggregateFunction_Total,
                ObjectIds.AggregateFunction_Total2,
                ObjectIds.AggregateFunction_Minimum,
                ObjectIds.AggregateFunction_Maximum,
                ObjectIds.AggregateFunction_Count,
                ObjectIds.AggregateFunction_Start,
                ObjectIds.AggregateFunction_End,
                ObjectIds.AggregateFunction_Delta,
                ObjectIds.AggregateFunction_StandardDeviationPopulation,
                ObjectIds.AggregateFunction_VariancePopulation,
                ObjectIds.AggregateFunction_StandardDeviationSample,
                ObjectIds.AggregateFunction_VarianceSample,
                ObjectIds.AggregateFunction_DurationGood,
                ObjectIds.AggregateFunction_DurationBad,
                ObjectIds.AggregateFunction_PercentGood,
                ObjectIds.AggregateFunction_PercentBad,
                ObjectIds.AggregateFunction_WorstQuality,
                ObjectIds.AggregateFunction_WorstQuality2,
            ];

            foreach (NodeId aggregateId in aggregateIds)
            {
                QualifiedName name = Aggregators.GetNameForStandardAggregate(aggregateId);
                Assert.That(name, Is.Not.EqualTo(default(QualifiedName)),
                    $"Name should be found for aggregate {aggregateId}");

                NodeId roundTrippedId = Aggregators.GetIdForStandardAggregate(name);
                Assert.That(roundTrippedId, Is.EqualTo(aggregateId),
                    $"Round-tripped id should match for aggregate {aggregateId}");
            }
        }

        [Test]
        public void CreateStandardCalculatorWithSteppedFlagReturnsCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Average,
                startTime, endTime, 10000, true, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<AverageAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsEndCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_End,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StartEndAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsDurationGoodCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_DurationGood,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<StatusAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMaximumActualTimeCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_MaximumActualTime,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMinimumActualTimeCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_MinimumActualTime,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }

        [Test]
        public void CreateStandardCalculatorReturnsMaximumCalculator()
        {
            var startTime = new DateTimeUtc(2024, 1, 1, 0, 0, 0);
            DateTimeUtc endTime = startTime.AddMilliseconds(10000);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                ObjectIds.AggregateFunction_Maximum,
                startTime, endTime, 10000, false, m_configuration, m_telemetry);

            Assert.That(calculator, Is.Not.Null);
            Assert.That(calculator, Is.InstanceOf<MinMaxAggregateCalculator>());
        }
    }
}
