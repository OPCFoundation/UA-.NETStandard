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
