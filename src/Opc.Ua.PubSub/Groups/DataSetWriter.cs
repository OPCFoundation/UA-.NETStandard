// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Default sealed <see cref="IDataSetWriter"/> implementation. Owns
    /// the configuration, the linked <see cref="IPublishedDataSet"/> and
    /// the writer's state machine.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side per-writer surface described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 DataSetWriter</see>.
    /// </remarks>
    public sealed class DataSetWriter : IDataSetWriter
    {
        /// <summary>
        /// Initializes a new <see cref="DataSetWriter"/>.
        /// </summary>
        /// <param name="configuration">Configured writer.</param>
        /// <param name="publishedDataSet">Source dataset to publish.</param>
        /// <param name="telemetry">
        /// Telemetry context used for the per-writer logger.
        /// </param>
        public DataSetWriter(
            DataSetWriterDataType configuration,
            IPublishedDataSet publishedDataSet,
            ITelemetryContext telemetry)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (publishedDataSet is null)
            {
                throw new ArgumentNullException(nameof(publishedDataSet));
            }
            Configuration = configuration;
            PublishedDataSet = publishedDataSet;
            Name = configuration.Name ?? string.Empty;
            DataSetWriterId = configuration.DataSetWriterId;
            FieldContentMask = (DataSetFieldContentMask)configuration.DataSetFieldContentMask;
            KeyFrameCount = configuration.KeyFrameCount;
            ILogger logger = telemetry.CreateLogger<DataSetWriter>();
            State = new PubSubStateMachine(
                string.IsNullOrEmpty(Name) ? $"writer-{DataSetWriterId}" : Name,
                PubSubComponentKind.DataSetWriter,
                logger);
        }

        /// <inheritdoc/>
        public ushort DataSetWriterId { get; }

        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public IPublishedDataSet PublishedDataSet { get; }

        /// <inheritdoc/>
        public DataSetFieldContentMask FieldContentMask { get; }

        /// <inheritdoc/>
        public uint KeyFrameCount { get; }

        /// <inheritdoc/>
        public DataSetWriterDataType Configuration { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }
    }
}
