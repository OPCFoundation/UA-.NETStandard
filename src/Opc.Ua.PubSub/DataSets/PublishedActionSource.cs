// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Exposes a Part 14 PublishedAction as a published DataSet source.
    /// </summary>
    /// <remarks>
    /// Published actions describe callable request schemas and targets. They do not represent a cyclic streaming
    /// publisher-side value source; <see cref="SampleAsync"/> therefore returns an empty snapshot for scheduler paths
    /// that require an <see cref="IPublishedDataSetSource"/>.
    /// </remarks>
    public sealed class PublishedActionSource : IPublishedDataSetSource
    {
        /// <summary>
        /// Initializes a new <see cref="PublishedActionSource"/>.
        /// </summary>
        /// <param name="action">Published action configuration.</param>
        public PublishedActionSource(PublishedActionDataType action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            Action = action;
        }

        /// <summary>
        /// The wrapped PublishedAction configuration.
        /// </summary>
        public PublishedActionDataType Action { get; }

        /// <summary>
        /// Action targets that the runtime can dispatch requests to.
        /// </summary>
        public ArrayOf<ActionTargetDataType> ActionTargets => Action.ActionTargets;

        /// <summary>
        /// Method bindings for method-action datasets, or an empty collection for other action kinds.
        /// </summary>
        public ArrayOf<ActionMethodDataType> ActionMethods => Action is PublishedActionMethodDataType methodAction
            ? methodAction.ActionMethods
            : [];

        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            return Action.RequestDataSetMetaData;
        }

        /// <inheritdoc/>
        public ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new PublishedDataSetSnapshot(
                metaData?.ConfigurationVersion ?? new ConfigurationVersionDataType(),
                [],
                DateTimeUtc.From(DateTimeOffset.UtcNow));

            return new ValueTask<PublishedDataSetSnapshot>(snapshot);
        }
    }
}
