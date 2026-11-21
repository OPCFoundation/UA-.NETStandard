// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages aggregate factories supported by the server.
    /// </summary>
    public class AggregateManager : IDisposable
    {
        /// <summary>
        /// Initilizes the manager.
        /// </summary>
        public AggregateManager(IServerInternal server)
        {
            m_server = server;
            m_factories = [];
            m_minimumProcessingInterval = 1000;
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }

        /// <summary>
        /// Checks if the aggregate is supported by the server.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <returns>True if the aggregate is supported.</returns>
        public bool IsSupported(NodeId aggregateId)
        {
            if (aggregateId.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_factories.ContainsKey(aggregateId);
            }
        }

        /// <summary>
        /// The maximum number of raw values that the calculator of an aggregate monitored item
        /// keeps for its current processing interval (zero or less for no limit). See
        /// <see cref="ServerConfiguration.MaxMonitoredItemAggregateRawValues"/>.
        /// </summary>
        public int MaxMonitoredItemRawValues { get; set; } =
            ServerConfiguration.DefaultMaxMonitoredItemAggregateRawValues;

        /// <summary>
        /// The maximum number of raw values kept for one processing interval when processed
        /// history is calculated from raw history (zero or less for no limit). See
        /// <see cref="ServerConfiguration.MaxProcessedHistoryRawValues"/>.
        /// </summary>
        public int MaxProcessedHistoryRawValues { get; set; } =
            ServerConfiguration.DefaultMaxProcessedHistoryRawValues;

        /// <summary>
        /// The minimum processing interval for any aggregate calculation.
        /// </summary>
        public double MinimumProcessingInterval
        {
            get
            {
                lock (m_lock)
                {
                    return m_minimumProcessingInterval;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_minimumProcessingInterval = value;
                }
            }
        }

        /// <summary>
        /// Returns the default configuration for the specified variable id.
        /// </summary>
        /// <param name="variableId">The id of history data node.</param>
        /// <returns>The configuration.</returns>
        public AggregateConfiguration GetDefaultConfiguration(NodeId variableId)
        {
            lock (m_lock)
            {
                m_defaultConfiguration ??= new AggregateConfiguration
                {
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    // Part 13 v1.05.07 §4.2.1.2: the TreatUncertainAsBad default is True.
                    TreatUncertainAsBad = true,
                    UseSlopedExtrapolation = false,
                    UseServerCapabilitiesDefaults = false
                };

                return m_defaultConfiguration;
            }
        }

        /// <summary>
        /// Sets the default aggregate configuration.
        /// </summary>
        /// <param name="configuration">The default aggregate configuration..</param>
        public void SetDefaultConfiguration(AggregateConfiguration configuration)
        {
            lock (m_lock)
            {
                m_defaultConfiguration = configuration;
            }
        }

        /// <summary>
        /// Creates a new aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <param name="startTime">When to start processing.</param>
        /// <param name="endTime">When to stop processing.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether stepped interpolation should be used.</param>
        /// <param name="configuration">The configuration to use.</param>
        public IAggregateCalculator? CreateCalculator(
            NodeId aggregateId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            if (aggregateId.IsNull)
            {
                return null;
            }

            AggregatorFactory? factory;
            lock (m_lock)
            {
                if (!m_factories.TryGetValue(aggregateId, out factory))
                {
                    return null;
                }
            }

            if (configuration.UseServerCapabilitiesDefaults)
            {
                // ensure the configuration is initialized
                configuration = GetDefaultConfiguration(default);
            }

            return factory!(
                aggregateId,
                startTime,
                endTime,
                processingInterval,
                stepped,
                configuration,
                m_server.Telemetry);
        }

        /// <summary>
        /// The aggregate functions of OPC 10000-13 that a server registers by default,
        /// all calculated by the standard calculator of <see cref="Aggregators"/>, in the
        /// order they are added to the server's AggregateFunctions folder.
        /// </summary>
        internal static readonly (NodeId AggregateId, string AggregateName)[] StandardAggregates =
        [
            (ObjectIds.AggregateFunction_Interpolative, BrowseNames.AggregateFunction_Interpolative),
            (ObjectIds.AggregateFunction_Average, BrowseNames.AggregateFunction_Average),
            (ObjectIds.AggregateFunction_TimeAverage, BrowseNames.AggregateFunction_TimeAverage),
            (ObjectIds.AggregateFunction_TimeAverage2, BrowseNames.AggregateFunction_TimeAverage2),
            (ObjectIds.AggregateFunction_Total, BrowseNames.AggregateFunction_Total),
            (ObjectIds.AggregateFunction_Total2, BrowseNames.AggregateFunction_Total2),
            (ObjectIds.AggregateFunction_Minimum, BrowseNames.AggregateFunction_Minimum),
            (ObjectIds.AggregateFunction_Maximum, BrowseNames.AggregateFunction_Maximum),
            (ObjectIds.AggregateFunction_MinimumActualTime, BrowseNames.AggregateFunction_MinimumActualTime),
            (ObjectIds.AggregateFunction_MaximumActualTime, BrowseNames.AggregateFunction_MaximumActualTime),
            (ObjectIds.AggregateFunction_Range, BrowseNames.AggregateFunction_Range),
            (ObjectIds.AggregateFunction_Minimum2, BrowseNames.AggregateFunction_Minimum2),
            (ObjectIds.AggregateFunction_Maximum2, BrowseNames.AggregateFunction_Maximum2),
            (ObjectIds.AggregateFunction_MinimumActualTime2, BrowseNames.AggregateFunction_MinimumActualTime2),
            (ObjectIds.AggregateFunction_MaximumActualTime2, BrowseNames.AggregateFunction_MaximumActualTime2),
            (ObjectIds.AggregateFunction_Range2, BrowseNames.AggregateFunction_Range2),
            (ObjectIds.AggregateFunction_Count, BrowseNames.AggregateFunction_Count),
            (ObjectIds.AggregateFunction_AnnotationCount, BrowseNames.AggregateFunction_AnnotationCount),
            (ObjectIds.AggregateFunction_DurationInStateZero, BrowseNames.AggregateFunction_DurationInStateZero),
            (ObjectIds.AggregateFunction_DurationInStateNonZero, BrowseNames.AggregateFunction_DurationInStateNonZero),
            (ObjectIds.AggregateFunction_NumberOfTransitions, BrowseNames.AggregateFunction_NumberOfTransitions),
            (ObjectIds.AggregateFunction_Start, BrowseNames.AggregateFunction_Start),
            (ObjectIds.AggregateFunction_End, BrowseNames.AggregateFunction_End),
            (ObjectIds.AggregateFunction_Delta, BrowseNames.AggregateFunction_Delta),
            (ObjectIds.AggregateFunction_StartBound, BrowseNames.AggregateFunction_StartBound),
            (ObjectIds.AggregateFunction_EndBound, BrowseNames.AggregateFunction_EndBound),
            (ObjectIds.AggregateFunction_DeltaBounds, BrowseNames.AggregateFunction_DeltaBounds),
            (ObjectIds.AggregateFunction_DurationGood, BrowseNames.AggregateFunction_DurationGood),
            (ObjectIds.AggregateFunction_DurationBad, BrowseNames.AggregateFunction_DurationBad),
            (ObjectIds.AggregateFunction_PercentGood, BrowseNames.AggregateFunction_PercentGood),
            (ObjectIds.AggregateFunction_PercentBad, BrowseNames.AggregateFunction_PercentBad),
            (ObjectIds.AggregateFunction_WorstQuality, BrowseNames.AggregateFunction_WorstQuality),
            (ObjectIds.AggregateFunction_WorstQuality2, BrowseNames.AggregateFunction_WorstQuality2),
            (ObjectIds.AggregateFunction_StandardDeviationPopulation, BrowseNames.AggregateFunction_StandardDeviationPopulation),
            (ObjectIds.AggregateFunction_VariancePopulation, BrowseNames.AggregateFunction_VariancePopulation),
            (ObjectIds.AggregateFunction_StandardDeviationSample, BrowseNames.AggregateFunction_StandardDeviationSample),
            (ObjectIds.AggregateFunction_VarianceSample, BrowseNames.AggregateFunction_VarianceSample),
        ];

        /// <summary>
        /// Registers the <see cref="StandardAggregates"/> with the standard calculator.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        internal async ValueTask RegisterStandardAggregatesAsync(CancellationToken cancellationToken = default)
        {
            foreach ((NodeId aggregateId, string aggregateName) in StandardAggregates)
            {
                await RegisterFactoryAsync(
                    aggregateId,
                    aggregateName,
                    Aggregators.CreateStandardCalculator,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Registers an aggregate factory.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        /// <param name="aggregateName">The id of the aggregate name.</param>
        /// <param name="factory">The factory used to create calculators.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async ValueTask RegisterFactoryAsync(
            NodeId aggregateId,
            string aggregateName,
            AggregatorFactory factory,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                m_factories[aggregateId] = factory;
            }

            await m_server.DiagnosticsNodeManager.AddAggregateFunctionAsync(aggregateId, aggregateName, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Unregisters an aggregate factory.
        /// </summary>
        /// <param name="aggregateId">The id of the aggregate function.</param>
        public void RegisterFactory(NodeId aggregateId)
        {
            lock (m_lock)
            {
                m_factories.Remove(aggregateId);
            }
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private AggregateConfiguration? m_defaultConfiguration;
        private readonly NodeIdDictionary<AggregatorFactory> m_factories;
        private double m_minimumProcessingInterval;
    }
}
