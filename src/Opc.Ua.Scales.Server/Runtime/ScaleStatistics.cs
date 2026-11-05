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
        /// Creates the statistic writer and initializes its period unless initialization is deferred.
        /// </summary>
        /// <param name="context">The context for the statistic nodes.</param>
        /// <param name="statistic">The statistic node.</param>
        /// <param name="writeGate">The owning scale gate, when the statistic belongs to a scale.</param>
        /// <param name="initialize">Whether to publish the initial start time synchronously.</param>
        internal ScaleStatistics(
            ISystemContext context,
            StatisticState statistic,
            ScaleWriteGate? writeGate = null,
            bool initialize = true)
        {
            m_context = context;
            Statistic = statistic;
            m_writeGate = writeGate;
            if (initialize)
            {
                ScaleValues.Set(context, statistic.StartTime, DateTimeUtc.Now);
            }
        }

        /// <summary>
        /// Gets the statistic node.
        /// </summary>
        public StatisticState Statistic { get; }

        /// <summary>
        /// Records one weighed item.
        /// </summary>
        /// <param name="weight">The weight of the item.</param>
        /// <param name="itemId">The item id published on <c>LastItem</c>.</param>
        public void Record(double weight, string? itemId = null)
        {
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    Count(Statistic.TotalPackages, weight);
                    Count(Statistic.TotalPackagesWeighed, weight);
                    PublishLastItem(weight, itemId);
                }
            });
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
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    Count(Statistic.TotalPackages, weight);
                    Count(Statistic.TotalPackagesWeighed, weight);
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        Count(checkweigher.TotalPackagesAccepted, weight);
                        if (belowLowerToleranceLimit1)
                        {
                            Count(checkweigher.PackagesAcceptedWithLowerToleranceLimit1, weight);
                        }
                        UpdatePercentage(checkweigher);
                    }
                    PublishLastItem(weight, itemId);
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
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    Count(Statistic.TotalPackages, weight);
                    if (!double.IsNaN(weight))
                    {
                        Count(Statistic.TotalPackagesWeighed, weight);
                    }
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        Count(checkweigher.TotalPackagesRejected, weight);
                        Count(
                            reason switch
                            {
                                CheckweigherRejectReason.LowerToleranceLimit1 =>
                                    checkweigher.PackagesRejectedByLowerToleranceLimit1,
                                CheckweigherRejectReason.LowerToleranceLimit2 =>
                                    checkweigher.PackagesRejectedByLowerToleranceLimit2,
                                CheckweigherRejectReason.MeanValueRequirement =>
                                    checkweigher.PackagesRejectedByMeanValueRequirement,
                                CheckweigherRejectReason.Metal => checkweigher.PackagesRejectedByMetal,
                                CheckweigherRejectReason.Vision => checkweigher.PackagesRejectedByVision,
                                CheckweigherRejectReason.XRay => checkweigher.PackagesRejectedByXRay,
                                CheckweigherRejectReason.DistanceFault => checkweigher.PackagesRejectedByDistanceFault,
                                CheckweigherRejectReason.Length => checkweigher.PackagesRejectedByLength,
                                _ => null
                            },
                            weight);
                        UpdatePercentage(checkweigher);
                    }
                    if (!double.IsNaN(weight))
                    {
                        PublishLastItem(weight, itemId);
                    }
                }
            });
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
                    m_accumulators.Clear();
                    var counters = new List<BaseInstanceState>();
                    Statistic.GetChildren(m_context, counters);
                    foreach (BaseInstanceState child in counters)
                    {
                        if (child is StatisticCounterState counter)
                        {
                            ResetCounter(counter);
                        }
                    }
                    if (Statistic is CheckweigherStatisticState checkweigher)
                    {
                        // Derived from the counters, so it restarts with them.
                        ScaleValues.Set(m_context, checkweigher.PercentageLowerToleranceLimit, Variant.From(0.0));
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
        /// Records a provider acquisition and preserves the complete registered item and source metadata.
        /// </summary>
        internal void RecordItem(ScaleItemReport report, string? zoneName, ScaleItemPrice? price)
        {
            ExecuteSynchronous(() =>
            {
                lock (m_lock)
                {
                    Count(Statistic.TotalPackages, report.Weight.Net);
                    Count(Statistic.TotalPackagesWeighed, report.Weight.Net);
                    ScaleItemPublisher.Publish(m_context, Statistic.LastItem, report, zoneName, price);
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
                }
            });
        }

        /// <summary>
        /// Publishes the initial period start time asynchronously.
        /// </summary>
        /// <returns>A task that completes after the live variable is notified.</returns>
        internal ValueTask InitializeAsync()
        {
            return ScaleValues.SetAsync(m_context, Statistic.StartTime, DateTimeUtc.Now);
        }

        /// <summary>
        /// Records a provider acquisition and awaits each live statistic setter in order.
        /// </summary>
        /// <param name="report">The acquisition.</param>
        /// <param name="zoneName">The classified zone name.</param>
        /// <param name="price">The calculated or equipment-supplied price.</param>
        internal async ValueTask RecordItemAsync(ScaleItemReport report, string? zoneName, ScaleItemPrice? price)
        {
            await CountAsync(Statistic.TotalPackages, report.Weight.Net).ConfigureAwait(false);
            await CountAsync(Statistic.TotalPackagesWeighed, report.Weight.Net).ConfigureAwait(false);
            await ScaleItemPublisher.PublishAsync(
                m_context, Statistic.LastItem, report, zoneName, price).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a zone counter and awaits each live setter in order.
        /// </summary>
        /// <param name="counter">The zone counter.</param>
        /// <param name="weight">The acquisition weight.</param>
        internal ValueTask RecordCounterAsync(StatisticCounterState counter, double weight)
        {
            return CountAsync(counter, weight);
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

            await ScaleValues.SetAsync(m_context, counter.ItemCount, Variant.From(accumulator.Count))
                .ConfigureAwait(false);
            if (accumulator.Weighed > 0)
            {
                await ScaleValues.SetAsync(m_context, counter.SumWeight, Variant.From(accumulator.Sum))
                    .ConfigureAwait(false);
                await ScaleValues.SetAsync(m_context, counter.MinValue, Variant.From(accumulator.Min))
                    .ConfigureAwait(false);
                await ScaleValues.SetAsync(m_context, counter.MaxValue, Variant.From(accumulator.Max))
                    .ConfigureAwait(false);
                await ScaleValues.SetAsync(m_context, counter.MeanValue, Variant.From(accumulator.Mean))
                    .ConfigureAwait(false);
                await ScaleValues.SetAsync(
                    m_context, counter.StandardDeviation, Variant.From(accumulator.StandardDeviation))
                    .ConfigureAwait(false);
            }
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
            if (accumulator.Weighed > 0)
            {
                ScaleValues.Set(m_context, counter.SumWeight, Variant.From(accumulator.Sum));
                ScaleValues.Set(m_context, counter.MinValue, Variant.From(accumulator.Min));
                ScaleValues.Set(m_context, counter.MaxValue, Variant.From(accumulator.Max));
                ScaleValues.Set(m_context, counter.MeanValue, Variant.From(accumulator.Mean));
                ScaleValues.Set(m_context, counter.StandardDeviation, Variant.From(accumulator.StandardDeviation));
            }
        }

        private void ResetCounter(StatisticCounterState counter)
        {
            ScaleValues.Set(m_context, counter.ItemCount, Variant.From(0UL));
            ScaleValues.Set(m_context, counter.SumWeight, Variant.From(0.0));
            ScaleValues.Set(m_context, counter.MinValue, Variant.From(0.0));
            ScaleValues.Set(m_context, counter.MaxValue, Variant.From(0.0));
            ScaleValues.Set(m_context, counter.MeanValue, Variant.From(0.0));
            ScaleValues.Set(m_context, counter.StandardDeviation, Variant.From(0.0));
            ScaleValues.Set(m_context, counter.PercentageOfTotal, Variant.From(0.0));
        }

        private void UpdatePercentage(CheckweigherStatisticState checkweigher)
        {
            if (checkweigher.PercentageLowerToleranceLimit == null ||
                checkweigher.TotalPackages == null ||
                !m_accumulators.TryGetValue(checkweigher.TotalPackages, out Accumulator? total) ||
                total.Count == 0)
            {
                return;
            }
            ulong belowT1 = 0;
            if (checkweigher.PackagesAcceptedWithLowerToleranceLimit1 is { } accepted &&
                m_accumulators.TryGetValue(accepted, out Accumulator? acceptedBelow))
            {
                belowT1 = acceptedBelow.Count;
            }
            ScaleValues.Set(
                m_context,
                checkweigher.PercentageLowerToleranceLimit,
                Variant.From(100.0 * belowT1 / total.Count));
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

        private sealed class Accumulator
        {
            public ulong Count { get; private set; }

            public ulong Weighed { get; private set; }

            public double Sum { get; private set; }

            public double Min { get; private set; } = double.PositiveInfinity;

            public double Max { get; private set; } = double.NegativeInfinity;

            public double Mean { get; private set; }

            public double StandardDeviation => Weighed > 1 ? Math.Sqrt(m_m2 / (Weighed - 1)) : 0;

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
        /// The owning scale's writer gate, when this statistic belongs to a scale.
        /// </summary>
        private readonly ScaleWriteGate? m_writeGate;
        private readonly Lock m_lock = new();
    }
}
