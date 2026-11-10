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

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Selects the standard-deviation equation in OPC 40200 §7.20.3.
    /// </summary>
    public enum ScaleStatisticsObservationMode
    {
        /// <summary>
        /// Estimates deviation from a sample, using n minus one as the divisor.
        /// </summary>
        Sample,

        /// <summary>
        /// Computes deviation for a 100% check, using n as the divisor.
        /// </summary>
        FullCheck
    }

    /// <summary>
    /// Why a checkweigher rejected a package, mapped onto the
    /// <c>PackagesRejectedBy*</c> counters of <c>CheckweigherStatisticType</c>
    /// (OPC 40200 §7.19).
    /// </summary>
    public enum CheckweigherRejectReason
    {
        /// <summary>Rejected for a reason with no dedicated counter.</summary>
        Other,

        /// <summary>Below the lower tolerance limit T1 (TU1).</summary>
        LowerToleranceLimit1,

        /// <summary>Below the lower tolerance limit T2 (TU2).</summary>
        LowerToleranceLimit2,

        /// <summary>The mean value requirement was not met.</summary>
        MeanValueRequirement,

        /// <summary>A metal detector rejected the package.</summary>
        Metal,

        /// <summary>A vision system rejected the package.</summary>
        Vision,

        /// <summary>An X-ray inspection rejected the package.</summary>
        XRay,

        /// <summary>The distance between packages was too short.</summary>
        DistanceFault,

        /// <summary>The package length was wrong.</summary>
        Length
    }

    /// <summary>
    /// Maintains a <c>StatisticType</c> object (OPC 40200 §7.6): the package
    /// counters, the last weighed item and, for a checkweigher, the accepted
    /// and rejected counters.
    /// </summary>
    /// <remarks>
    /// A <c>StatisticCounterType</c> publishes the item count and, where the
    /// optional members are present, the sum, minimum, maximum, mean and
    /// standard deviation of the weights it counted. They are maintained with
    /// Welford's online algorithm so the deviation does not lose precision on
    /// long runs.
    /// </remarks>
    public sealed class ScaleStatistics
    {
        /// <summary>
        /// Creates a direct statistic recorder with an explicit observation mode.
        /// </summary>
        /// <param name="context">The statistic's system context.</param>
        /// <param name="statistic">The statistic to update.</param>
        /// <param name="observationMode">The equation used for standard deviation.</param>
        /// <param name="nominalWeight">The nominal weight, or null to read the parent checkweigher product.</param>
        public ScaleStatistics(
            ISystemContext context,
            StatisticState statistic,
            ScaleStatisticsObservationMode observationMode,
            double? nominalWeight = null)
            : this(context, statistic, null, observationMode: observationMode, nominalWeight: nominalWeight)
        {
        }

        /// <summary>
        /// Creates the statistic writer and initializes its period unless initialization is deferred.
        /// </summary>
        /// <param name="context">The context for the statistic nodes.</param>
        /// <param name="statistic">The statistic node.</param>
        /// <param name="writeGate">The owning scale gate, when the statistic belongs to a scale.</param>
        /// <param name="initialize">Whether to publish the initial start time synchronously.</param>
        /// <param name="observationMode">The sample or 100%-check deviation equation.</param>
        /// <param name="nominalWeight">The optional nominal weight for a direct recorder.</param>
        internal ScaleStatistics(
            ISystemContext context,
            StatisticState statistic,
            ScaleWriteGate? writeGate = null,
            bool initialize = true,
            ScaleStatisticsObservationMode observationMode = ScaleStatisticsObservationMode.Sample,
            double? nominalWeight = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            Statistic = statistic ?? throw new ArgumentNullException(nameof(statistic));
            if (observationMode is not (
                ScaleStatisticsObservationMode.Sample or ScaleStatisticsObservationMode.FullCheck))
            {
                throw new ArgumentOutOfRangeException(nameof(observationMode));
            }
            if (nominalWeight is { } nominal && (double.IsNaN(nominal) || double.IsInfinity(nominal)))
            {
                throw new ArgumentOutOfRangeException(nameof(nominalWeight));
            }
            ObservationMode = observationMode;
            m_nominalWeight = nominalWeight;
            m_writeGate = writeGate;
            if (initialize)
            {
                foreach (StatisticCounterState counter in SnapshotCounters())
                {
                    SetAggregates(counter, null);
                    SetDeviation(counter, null);
                }
                ScaleValues.Set(context, statistic.StartTime, DateTimeUtc.Now);
            }
        }

        /// <summary>
        /// Gets the statistic node.
        /// </summary>
        public StatisticState Statistic { get; }

        /// <summary>
        /// Gets the configured sample or 100%-check equation.
        /// </summary>
        public ScaleStatisticsObservationMode ObservationMode { get; }

        /// <summary>
        /// Records one weighed item.
        /// </summary>
        /// <param name="weight">The weight of the item.</param>
        /// <param name="itemId">The item id published on <c>LastItem</c>.</param>
        public void Record(double weight, string? itemId = null)
        {
            ValidateWeight(weight);
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    m_totalCount++;
                    Count(Statistic.TotalPackages, weight);
                    if (!double.IsNaN(weight))
                    {
                        Count(Statistic.TotalPackagesWeighed, weight);
                    }
                    PublishLastItem(weight, itemId);
                    UpdateCounterPercentages();
                }
            });
        }

        /// <summary>
        /// Records one weighed item while the owning scale already holds asynchronous write admission.
        /// </summary>
        /// <param name="weight">The weight of the item.</param>
        /// <param name="itemId">The item id published on <c>LastItem</c>.</param>
        internal async ValueTask RecordAsync(double weight, string? itemId = null)
        {
            ValidateWeight(weight);
            m_totalCount++;
            await CountAsync(Statistic.TotalPackages, weight).ConfigureAwait(false);
            if (!double.IsNaN(weight))
            {
                await CountAsync(Statistic.TotalPackagesWeighed, weight).ConfigureAwait(false);
            }
            await PublishLastItemAsync(weight, itemId).ConfigureAwait(false);
            await UpdateCounterPercentagesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Records one package a checkweigher accepted.
        /// </summary>
        /// <param name="weight">The package weight.</param>
        /// <param name="belowLowerToleranceLimit1">
        /// Whether the package was accepted although below T1 (a TU1 package).
        /// </param>
        /// <param name="itemId">The item id.</param>
        public void RecordAccepted(double weight, bool belowLowerToleranceLimit1 = false, string? itemId = null)
        {
            ValidateWeight(weight);
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    m_totalCount++;
                    Count(Statistic.TotalPackages, weight);
                    if (!double.IsNaN(weight))
                    {
                        Count(Statistic.TotalPackagesWeighed, weight);
                    }
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        m_acceptedCount++;
                        m_acceptedWeights.Add(weight);
                        if (belowLowerToleranceLimit1)
                        {
                            m_acceptedBelowToleranceCount++;
                        }
                        Count(checkweigher.TotalPackagesAccepted, weight);
                        if (belowLowerToleranceLimit1)
                        {
                            Count(checkweigher.PackagesAcceptedWithLowerToleranceLimit1, weight);
                        }
                        UpdatePercentage(checkweigher);
                        UpdateGiveAway(checkweigher);
                    }
                    PublishLastItem(weight, itemId);
                    UpdateCounterPercentages();
                }
            });
        }

        /// <summary>
        /// Records one package a checkweigher rejected.
        /// </summary>
        /// <param name="weight">The package weight, or NaN when it was not weighed.</param>
        /// <param name="reason">Why it was rejected.</param>
        /// <param name="itemId">The item id.</param>
        public void RecordRejected(double weight, CheckweigherRejectReason reason, string? itemId = null)
        {
            ValidateWeight(weight);
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    m_totalCount++;
                    Count(Statistic.TotalPackages, weight);
                    if (!double.IsNaN(weight))
                    {
                        Count(Statistic.TotalPackagesWeighed, weight);
                    }
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        Count(checkweigher.TotalPackagesRejected, weight);
                        Count(RejectedCounter(checkweigher, reason), weight);
                        UpdatePercentage(checkweigher);
                        UpdateGiveAway(checkweigher);
                    }
                    if (!double.IsNaN(weight))
                    {
                        PublishLastItem(weight, itemId);
                    }
                    UpdateCounterPercentages();
                }
            });
        }

        /// <summary>
        /// Records explicit acceptance while the owning scale retains asynchronous admission.
        /// </summary>
        /// <param name="weight">The accepted package weight.</param>
        /// <param name="belowLowerToleranceLimit1">Whether the accepted package is in the TU1-TU2 band.</param>
        /// <param name="itemId">The equipment item identifier.</param>
        internal async ValueTask RecordAcceptedAsync(
            double weight, bool belowLowerToleranceLimit1, string? itemId)
        {
            ValidateWeight(weight);
            m_totalCount++;
            await CountAsync(Statistic.TotalPackages, weight).ConfigureAwait(false);
            if (!double.IsNaN(weight))
            {
                await CountAsync(Statistic.TotalPackagesWeighed, weight).ConfigureAwait(false);
            }
            if (Statistic is CheckweigherStatisticState checkweigher)
            {
                m_acceptedCount++;
                m_acceptedWeights.Add(weight);
                if (belowLowerToleranceLimit1)
                {
                    m_acceptedBelowToleranceCount++;
                }
                await CountAsync(checkweigher.TotalPackagesAccepted, weight).ConfigureAwait(false);
                if (belowLowerToleranceLimit1)
                {
                    await CountAsync(checkweigher.PackagesAcceptedWithLowerToleranceLimit1, weight)
                        .ConfigureAwait(false);
                }
                await UpdateDerivedAsync(checkweigher).ConfigureAwait(false);
            }
            await PublishLastItemAsync(weight, itemId).ConfigureAwait(false);
            await UpdateCounterPercentagesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Records explicit rejection while the owning scale retains asynchronous admission.
        /// </summary>
        /// <param name="weight">The package weight, or NaN when it was not weighed.</param>
        /// <param name="reason">The equipment's rejection reason.</param>
        /// <param name="itemId">The equipment item identifier.</param>
        internal async ValueTask RecordRejectedAsync(double weight, CheckweigherRejectReason reason, string? itemId)
        {
            ValidateWeight(weight);
            m_totalCount++;
            await CountAsync(Statistic.TotalPackages, weight).ConfigureAwait(false);
            if (!double.IsNaN(weight))
            {
                await CountAsync(Statistic.TotalPackagesWeighed, weight).ConfigureAwait(false);
            }
            if (Statistic is CheckweigherStatisticState checkweigher)
            {
                await CountAsync(checkweigher.TotalPackagesRejected, weight).ConfigureAwait(false);
                await CountAsync(RejectedCounter(checkweigher, reason), weight).ConfigureAwait(false);
                await UpdateDerivedAsync(checkweigher).ConfigureAwait(false);
            }
            if (!double.IsNaN(weight))
            {
                await PublishLastItemAsync(weight, itemId).ConfigureAwait(false);
            }
            await UpdateCounterPercentagesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the optional counter corresponding to an equipment rejection reason.
        /// </summary>
        private static RejectedStatisticCounterState? RejectedCounter(
            CheckweigherStatisticState checkweigher, CheckweigherRejectReason reason)
        {
            return reason switch
            {
                CheckweigherRejectReason.LowerToleranceLimit1 => checkweigher.PackagesRejectedByLowerToleranceLimit1,
                CheckweigherRejectReason.LowerToleranceLimit2 => checkweigher.PackagesRejectedByLowerToleranceLimit2,
                CheckweigherRejectReason.MeanValueRequirement => checkweigher.PackagesRejectedByMeanValueRequirement,
                CheckweigherRejectReason.Metal => checkweigher.PackagesRejectedByMetal,
                CheckweigherRejectReason.Vision => checkweigher.PackagesRejectedByVision,
                CheckweigherRejectReason.XRay => checkweigher.PackagesRejectedByXRay,
                CheckweigherRejectReason.DistanceFault => checkweigher.PackagesRejectedByDistanceFault,
                CheckweigherRejectReason.Length => checkweigher.PackagesRejectedByLength,
                _ => null
            };
        }

        /// <summary>
        /// Publishes accepted percentage and giveaway through native asynchronous notifications.
        /// </summary>
        private async ValueTask UpdateDerivedAsync(CheckweigherStatisticState checkweigher)
        {
            await SetAsync(checkweigher.PercentageLowerToleranceLimit,
                Variant.From(m_acceptedCount == 0 ? 0.0 : 100.0 * m_acceptedBelowToleranceCount / m_acceptedCount))
                .ConfigureAwait(false);
            if (SetGiveAway(checkweigher) is { } giveAway)
            {
                await NotifyAsync(giveAway).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Resets every counter and restarts the statistic period.
        /// </summary>
        /// <param name="resetCondition">The reason published as <c>ResetCondition</c>.</param>
        public void Reset(string? resetCondition = null)
        {
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    List<StatisticCounterState> counters = SnapshotCounters();
                    m_accumulators.Clear();
                    m_totalCount = 0;
                    m_acceptedCount = 0;
                    m_acceptedBelowToleranceCount = 0;
                    m_acceptedWeights = new Accumulator();
                    foreach (StatisticCounterState counter in counters)
                    {
                        ResetCounter(counter);
                    }
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        // Derived from the counters, so it restarts with them.
                        ScaleValues.Set(m_context, checkweigher.PercentageLowerToleranceLimit, Variant.From(0.0));
                        ScaleValues.Set(m_context, checkweigher.GiveAway, Variant.From(0.0));
                    }
                    ScaleValues.Set(m_context, Statistic.StartTime, DateTimeUtc.Now);
                    if (resetCondition != null)
                    {
                        ScaleValues.Set(m_context, Statistic.ResetCondition, resetCondition);
                    }
                    Statistic.ClearChangeMasks(m_context, includeChildren: true);
                }
            });
        }

        /// <summary>
        /// Resets a period while the owning scale retains asynchronous admission.
        /// </summary>
        /// <param name="resetCondition">The equipment's reset reason.</param>
        internal async ValueTask ResetAsync(string? resetCondition)
        {
            List<StatisticCounterState> counters = SnapshotCounters();
            m_accumulators.Clear();
            m_totalCount = 0;
            m_acceptedCount = 0;
            m_acceptedBelowToleranceCount = 0;
            m_acceptedWeights = new Accumulator();
            foreach (StatisticCounterState counter in counters)
            {
                await ScaleValues.SetAsync(m_context, counter.ItemCount, Variant.From(0UL)).ConfigureAwait(false);
                await ScaleValues.SetAsync(m_context, counter.Weighed, InitialWeighed(counter)).ConfigureAwait(false);
                foreach (BaseVariableState? value in new BaseVariableState?[]
                {
                    counter.PercentageOfTotal
                })
                {
                    await ScaleValues.SetAsync(m_context, value, Variant.From(0.0)).ConfigureAwait(false);
                }
                foreach (BaseVariableState value in SetAggregates(counter, null))
                {
                    await value.ClearChangeMasksAsync(m_context, includeChildren: false).ConfigureAwait(false);
                }
                if (SetDeviation(counter, null) is { } deviation)
                {
                    await deviation.ClearChangeMasksAsync(m_context, includeChildren: false).ConfigureAwait(false);
                }
            }
            if (Statistic is CheckweigherStatisticState checkweigher)
            {
                await UpdateDerivedAsync(checkweigher).ConfigureAwait(false);
            }
            await ScaleValues.SetAsync(m_context, Statistic.StartTime, Variant.From(DateTimeUtc.Now))
                .ConfigureAwait(false);
            if (resetCondition != null)
            {
                await ScaleValues.SetAsync(m_context, Statistic.ResetCondition, Variant.From(resetCondition))
                    .ConfigureAwait(false);
            }
            await Statistic.ClearChangeMasksAsync(m_context, includeChildren: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a provider acquisition and preserves the complete registered item and source metadata.
        /// </summary>
        internal void RecordItem(ScaleItemReport report, string? zoneName, ScaleItemPrice? price)
        {
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    m_totalCount++;
                    Count(Statistic.TotalPackages, report.Weight.Net);
                    Count(Statistic.TotalPackagesWeighed, report.Weight.Net);
                    ScaleItemPublisher.Publish(m_context, Statistic.LastItem, report, zoneName, price);
                    UpdateCounterPercentages();
                }
            });
        }

        /// <summary>
        /// Records a zone counter using the same accumulation semantics as production statistics.
        /// </summary>
        internal void RecordCounter(StatisticCounterState counter, double weight)
        {
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    Count(counter, weight);
                    UpdateCounterPercentages();
                }
            });
        }

        /// <summary>
        /// Publishes the initial period start time asynchronously.
        /// </summary>
        /// <returns>A task that completes after the live variable is notified.</returns>
        internal async ValueTask InitializeAsync()
        {
            foreach (StatisticCounterState counter in SnapshotCounters())
            {
                foreach (BaseVariableState value in SetAggregates(counter, null))
                {
                    await value.ClearChangeMasksAsync(m_context, includeChildren: false).ConfigureAwait(false);
                }
                if (SetDeviation(counter, null) is { } deviation)
                {
                    await deviation.ClearChangeMasksAsync(m_context, includeChildren: false).ConfigureAwait(false);
                }
            }
            await ScaleValues.SetAsync(m_context, Statistic.StartTime, DateTimeUtc.Now).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a provider acquisition and awaits each live statistic setter in order.
        /// </summary>
        /// <param name="report">The acquisition.</param>
        /// <param name="zoneName">The classified zone name.</param>
        /// <param name="price">The calculated or equipment-supplied price.</param>
        internal async ValueTask RecordItemAsync(ScaleItemReport report, string? zoneName, ScaleItemPrice? price)
        {
            m_totalCount++;
            await CountAsync(Statistic.TotalPackages, report.Weight.Net).ConfigureAwait(false);
            await CountAsync(Statistic.TotalPackagesWeighed, report.Weight.Net).ConfigureAwait(false);
            await ScaleItemPublisher.PublishAsync(
                m_context, Statistic.LastItem, report, zoneName, price).ConfigureAwait(false);
            await UpdateCounterPercentagesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Records a zone counter and awaits each live setter in order.
        /// </summary>
        /// <param name="counter">The zone counter.</param>
        /// <param name="weight">The acquisition weight.</param>
        internal async ValueTask RecordCounterAsync(StatisticCounterState counter, double weight)
        {
            await CountAsync(counter, weight).ConfigureAwait(false);
            await UpdateCounterPercentagesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Captures direct and separately registered counters before their values are changed or reset.
        /// </summary>
        private List<StatisticCounterState> SnapshotCounters()
        {
            var children = new List<BaseInstanceState>();
            Statistic.GetChildren(m_context, children);
            var counters = new HashSet<StatisticCounterState>(m_accumulators.Keys);
            foreach (BaseInstanceState child in children)
            {
                if (child is StatisticCounterState counter)
                {
                    counters.Add(counter);
                }
            }
            return [.. counters];
        }

        /// <summary>
        /// Captures percentages for all exposed counters against a denominator independent of optional total nodes.
        /// </summary>
        private List<(BaseVariableState Variable, double Percentage)> CounterPercentages()
        {
            var percentages = new List<(BaseVariableState Variable, double Percentage)>();
            foreach (StatisticCounterState counter in SnapshotCounters())
            {
                if (counter.PercentageOfTotal is { } variable)
                {
                    ulong count = m_accumulators.TryGetValue(counter, out Accumulator? accumulator)
                        ? accumulator.Count : 0;
                    percentages.Add((variable, m_totalCount == 0 ? 0.0 : 100.0 * count / m_totalCount));
                }
            }
            return percentages;
        }

        /// <summary>
        /// Refreshes percentages even when only another counter or the denominator changed.
        /// </summary>
        private void UpdateCounterPercentages()
        {
            foreach ((BaseVariableState variable, double percentage) in CounterPercentages())
            {
                ScaleValues.Set(m_context, variable, Variant.From(percentage));
            }
        }

        /// <summary>
        /// Refreshes all exposed counter percentages through asynchronous notifications.
        /// </summary>
        private async ValueTask UpdateCounterPercentagesAsync()
        {
            foreach ((BaseVariableState variable, double percentage) in CounterPercentages())
            {
                await SetAsync(variable, Variant.From(percentage)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates one accumulator and awaits its exposed counter setters in order.
        /// </summary>
        /// <param name="counter">The statistic counter, when present.</param>
        /// <param name="weight">The weight to add.</param>
        private async ValueTask CountAsync(StatisticCounterState? counter, double weight)
        {
            if (counter == null)
            {
                return;
            }

            Accumulator accumulator;
            lock (m_lock)
            {
                if (!m_accumulators.TryGetValue(counter, out Accumulator? existing))
                {
                    existing = new Accumulator();
                    m_accumulators[counter] = existing;
                }
                accumulator = existing;
                accumulator.Add(weight);
            }

            await SetAsync(counter.ItemCount, Variant.From(accumulator.Count))
                .ConfigureAwait(false);
            if (SetWeighed(counter, accumulator) is { } weighed)
            {
                await NotifyAsync(weighed).ConfigureAwait(false);
            }
            foreach (BaseVariableState value in SetAggregates(counter, accumulator))
            {
                await NotifyAsync(value).ConfigureAwait(false);
            }
            if (SetDeviation(counter, accumulator) is { } deviation)
            {
                await NotifyAsync(deviation).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates deviation quality when the selected equation has insufficient weighed observations.
        /// </summary>
        private BaseVariableState? SetDeviation(StatisticCounterState counter, Accumulator? accumulator)
        {
            BaseVariableState? deviation = counter.StandardDeviation;
            if (deviation == null)
            {
                return null;
            }
            ulong minimum = ObservationMode == ScaleStatisticsObservationMode.Sample ? 2UL : 1UL;
            bool defined = accumulator != null && accumulator.Weighed >= minimum;
            SetAggregate(deviation, defined ? accumulator!.Deviation(ObservationMode) : 0.0,
                defined ? AggregateQuality(accumulator!) : StatusCodes.BadNoData);
            return deviation;
        }

        /// <summary>
        /// Updates measurement aggregates with quality describing missing or overflowing weights.
        /// </summary>
        private static List<BaseVariableState> SetAggregates(
            StatisticCounterState counter, Accumulator? accumulator)
        {
            var variables = new List<BaseVariableState>();
            bool measured = accumulator != null && accumulator.Weighed > 0;
            StatusCode quality = measured ? AggregateQuality(accumulator!) : StatusCodes.BadNoData;
            if (counter.SumWeight is { } sum)
            {
                SetAggregate(sum, accumulator?.Sum ?? 0.0,
                    accumulator == null || accumulator.Count == 0 ? StatusCodes.Good : quality);
                variables.Add(sum);
            }
            foreach ((BaseVariableState? variable, double value) in new[]
            {
                (counter.MinValue, measured ? accumulator!.Min : 0.0),
                (counter.MaxValue, measured ? accumulator!.Max : 0.0),
                (counter.MeanValue, measured ? accumulator!.Mean : 0.0)
            })
            {
                if (variable != null)
                {
                    SetAggregate(variable, value, quality);
                    variables.Add(variable);
                }
            }
            return variables;
        }

        /// <summary>
        /// Distinguishes a measured subset from the complete counter population.
        /// </summary>
        private static StatusCode AggregateQuality(Accumulator accumulator)
        {
            return accumulator.Weighed == accumulator.Count ? StatusCodes.Good : StatusCodes.UncertainSubNormal;
        }

        /// <summary>
        /// Retains the last finite value when an aggregate overflows and reports that failure.
        /// </summary>
        private static void SetAggregate(BaseVariableState variable, double value, StatusCode quality)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                variable.StatusCode = StatusCodes.BadOutOfRange;
            }
            else
            {
                variable.WrappedValue = Variant.From(value);
                variable.StatusCode = quality;
            }
            variable.Timestamp = DateTimeUtc.Now;
        }

        /// <summary>
        /// Rejects infinite input before any counter is changed; NaN denotes an unweighed item.
        /// </summary>
        private static void ValidateWeight(double weight)
        {
            if (double.IsInfinity(weight))
            {
                throw new ArgumentOutOfRangeException(nameof(weight));
            }
        }

        /// <summary>
        /// Publishes whether all counted items were weighed and marks mixed categories as uncertain.
        /// </summary>
        private BaseVariableState? SetWeighed(StatisticCounterState counter, Accumulator accumulator)
        {
            BaseVariableState? weighed = counter.Weighed;
            if (weighed == null)
            {
                return null;
            }
            weighed.WrappedValue = Variant.From(
                counter != Statistic.TotalPackages && accumulator.Weighed == accumulator.Count);
            weighed.StatusCode = counter != Statistic.TotalPackages &&
                accumulator.Weighed > 0 && accumulator.Weighed < accumulator.Count
                ? StatusCodes.UncertainSubNormal : StatusCodes.Good;
            weighed.Timestamp = DateTimeUtc.Now;
            return weighed;
        }

        /// <summary>
        /// Runs a complete synchronous mutation through the scale executor when one is bound.
        /// </summary>
        /// <param name="operation">The mutation.</param>
        private void ExecuteSynchronous(Action operation)
        {
            if (m_writeGate == null)
            {
                operation();
                return;
            }
            m_writeGate.ExecuteSynchronously(operation);
        }

        private void Count(StatisticCounterState? counter, double weight)
        {
            if (counter == null)
            {
                return;
            }
            if (!m_accumulators.TryGetValue(counter, out Accumulator? accumulator))
            {
                accumulator = new Accumulator();
                m_accumulators[counter] = accumulator;
            }
            accumulator.Add(weight);

            ScaleValues.Set(m_context, counter.ItemCount, Variant.From(accumulator.Count));
            SetWeighed(counter, accumulator)?.ClearChangeMasks(m_context, includeChildren: false);
            foreach (BaseVariableState value in SetAggregates(counter, accumulator))
            {
                value.ClearChangeMasks(m_context, includeChildren: false);
            }
            SetDeviation(counter, accumulator)?.ClearChangeMasks(m_context, includeChildren: false);
        }

        private void ResetCounter(StatisticCounterState counter)
        {
            ScaleValues.Set(m_context, counter.ItemCount, Variant.From(0UL));
            ScaleValues.Set(m_context, counter.Weighed, InitialWeighed(counter));
            foreach (BaseVariableState value in SetAggregates(counter, null))
            {
                value.ClearChangeMasks(m_context, includeChildren: false);
            }
            SetDeviation(counter, null)?.ClearChangeMasks(m_context, includeChildren: false);
            ScaleValues.Set(m_context, counter.PercentageOfTotal, Variant.From(0.0));
        }

        /// <summary>
        /// Identifies measurement-only categories before the first item in a reset period.
        /// </summary>
        private bool InitialWeighed(StatisticCounterState counter)
        {
            return counter == Statistic.TotalPackagesWeighed ||
                counter.Parent is ZoneState ||
                (Statistic is CheckweigherStatisticState checkweigher &&
                    (counter == checkweigher.TotalPackagesAccepted ||
                        counter == checkweigher.PackagesAcceptedWithLowerToleranceLimit1));
        }

        private void UpdatePercentage(CheckweigherStatisticState checkweigher)
        {
            if (checkweigher.PercentageLowerToleranceLimit == null)
            {
                return;
            }
            ScaleValues.Set(
                m_context,
                checkweigher.PercentageLowerToleranceLimit,
                Variant.From(m_acceptedCount == 0 ? 0.0 : 100.0 * m_acceptedBelowToleranceCount / m_acceptedCount));
        }

        /// <summary>
        /// Publishes the signed accepted-weight deviation from the nominal weight (§7.19.3).
        /// </summary>
        private void UpdateGiveAway(CheckweigherStatisticState checkweigher)
        {
            SetGiveAway(checkweigher)?.ClearChangeMasks(m_context, includeChildren: false);
        }

        /// <summary>
        /// Recomputes giveaway after a complete nominal cache write while its scale still owns admission.
        /// </summary>
        internal BaseVariableState? RefreshGiveAway()
        {
            lock (m_lock)
            {
                return Statistic is CheckweigherStatisticState checkweigher ? SetGiveAway(checkweigher) : null;
            }
        }

        /// <summary>
        /// Updates giveaway value and quality without dispatching the node notification.
        /// </summary>
        private BaseVariableState? SetGiveAway(CheckweigherStatisticState checkweigher)
        {
            BaseVariableState? giveAway = checkweigher.GiveAway;
            if (giveAway == null)
            {
                return null;
            }
            if (m_acceptedCount == 0)
            {
                giveAway.WrappedValue = Variant.From(0.0);
                giveAway.StatusCode = StatusCodes.Good;
                giveAway.Timestamp = DateTimeUtc.Now;
                return giveAway;
            }
            double nominal;
            StatusCode quality = StatusCodes.Good;
            if (m_nominalWeight is { } configured)
            {
                nominal = configured;
            }
            else if (Statistic.Parent is CheckweigherProductState product && product.NominalWeight is { } target)
            {
                quality = target.StatusCode;
                if (!ScaleItemPublisher.TryNumber(target.WrappedValue, out nominal))
                {
                    quality = StatusCodes.BadNoData;
                }
            }
            else
            {
                nominal = 0;
                quality = StatusCodes.BadNoData;
            }
            if (m_acceptedWeights.Weighed != m_acceptedCount)
            {
                quality = StatusCodes.BadNoData;
            }
            if (!StatusCode.IsBad(quality))
            {
                double value = (m_acceptedWeights.Mean - nominal) * m_acceptedCount;
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    quality = StatusCodes.BadOutOfRange;
                }
                else
                {
                    giveAway.WrappedValue = Variant.From(value);
                }
            }
            giveAway.StatusCode = quality;
            giveAway.Timestamp = DateTimeUtc.Now;
            return giveAway;
        }

        private void PublishLastItem(double weight, string? itemId)
        {
            WeighingItemState? item = Statistic.LastItem;
            if (item == null)
            {
                return;
            }
            if (item.MeasuredWeight != null)
            {
                item.MeasuredWeight.Value = new WeightType { Gross = weight, Net = weight, Tare = 0 };
                ScaleValues.Touch(m_context, item.MeasuredWeight);
            }
            if (itemId != null)
            {
                ScaleValues.Set(m_context, item.ItemId, itemId);
            }
        }

        private async ValueTask PublishLastItemAsync(double weight, string? itemId)
        {
            WeighingItemState? item = Statistic.LastItem;
            if (item == null)
            {
                return;
            }
            if (item.MeasuredWeight != null)
            {
                await SetAsync(
                    item.MeasuredWeight,
                    Variant.From(new ExtensionObject(new WeightType { Gross = weight, Net = weight, Tare = 0 })))
                    .ConfigureAwait(false);
            }
            if (itemId != null)
            {
                await SetAsync(item.ItemId, Variant.From(itemId)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stages one recorder's cache changes into a shared notification sequence.
        /// </summary>
        internal async ValueTask DeferNotificationsAsync(List<NodeState> notifications, Func<ValueTask> operation)
        {
            List<NodeState>? previous = m_deferredNotifications;
            m_deferredNotifications = notifications;
            try
            {
                await operation().ConfigureAwait(false);
            }
            finally
            {
                m_deferredNotifications = previous;
            }
        }

        /// <summary>
        /// Commits a value before dispatching or collecting its notification.
        /// </summary>
        private ValueTask SetAsync(BaseVariableState? variable, Variant value)
        {
            if (variable == null)
            {
                return default;
            }
            variable.WrappedValue = value;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            return NotifyAsync(variable);
        }

        /// <summary>
        /// Retains notification order while multiple recorder caches are committed.
        /// </summary>
        private ValueTask NotifyAsync(NodeState node)
        {
            if (m_deferredNotifications is { } notifications)
            {
                notifications.Add(node);
                return default;
            }
            return node.ClearChangeMasksAsync(m_context, includeChildren: false);
        }

        private sealed class Accumulator
        {
            public ulong Count { get; private set; }

            public ulong Weighed { get; private set; }

            public double Sum { get; private set; }

            public double Min { get; private set; } = double.PositiveInfinity;

            public double Max { get; private set; } = double.NegativeInfinity;

            public double Mean { get; private set; }

            /// <summary>
            /// Calculates the configured equation after the caller has checked the minimum observation count.
            /// </summary>
            public double Deviation(ScaleStatisticsObservationMode mode)
            {
                return Math.Sqrt(m_m2 / (mode == ScaleStatisticsObservationMode.Sample ? Weighed - 1 : Weighed));
            }

            public void Add(double weight)
            {
                Count++;
                if (double.IsNaN(weight))
                {
                    return;
                }
                Weighed++;
                Sum += weight;
                Min = Math.Min(Min, weight);
                Max = Math.Max(Max, weight);
                double delta = weight - Mean;
                Mean += delta / Weighed;
                m_m2 += delta * (weight - Mean);
            }

            private double m_m2;
        }

        private readonly ISystemContext m_context;
        private readonly Dictionary<StatisticCounterState, Accumulator> m_accumulators = [];

        /// <summary>
        /// The overall package count, independent of optional published total-counter nodes.
        /// </summary>
        private ulong m_totalCount;

        /// <summary>
        /// The accepted item count, independent of optional published counters.
        /// </summary>
        private ulong m_acceptedCount;

        /// <summary>
        /// The accepted TU1-TU2 item count, independent of optional published counters.
        /// </summary>
        private ulong m_acceptedBelowToleranceCount;

        /// <summary>
        /// Accepted weight moments, independent of optional published counter nodes.
        /// </summary>
        private Accumulator m_acceptedWeights = new();

        /// <summary>The current report's shared deferred notification sequence, owned by scale admission.</summary>
        private List<NodeState>? m_deferredNotifications;

        /// <summary>
        /// The optional nominal weight supplied to a direct recorder.
        /// </summary>
        private readonly double? m_nominalWeight;

        /// <summary>
        /// The owning scale's writer gate, when this statistic belongs to a scale.
        /// </summary>
        private readonly ScaleWriteGate? m_writeGate;
        private readonly Lock m_lock = new();
    }
}
