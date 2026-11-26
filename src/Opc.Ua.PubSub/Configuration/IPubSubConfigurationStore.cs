// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Async store for the PubSub configuration document. Backed by
    /// a file, an address-space resource, an in-memory snapshot or
    /// a remote configuration source. Notifies subscribers when the
    /// configuration changes so the runtime can apply the delta.
    /// </summary>
    /// <remarks>
    /// Implements the configuration-storage contract derived from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PubSub configuration model</see>. A default
    /// file-backed implementation is provided.
    /// </remarks>
    public interface IPubSubConfigurationStore
    {
        /// <summary>
        /// Raised whenever the persisted configuration changes.
        /// </summary>
        event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

        /// <summary>
        /// Loads the current configuration.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PubSubConfigurationDataType> LoadAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists <paramref name="configuration"/>. Raises
        /// <see cref="Changed"/> on success.
        /// </summary>
        /// <param name="configuration">Configuration to save.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SaveAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the persisted application ConfigurationVersion.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ConfigurationVersionDataType?> GetConfigurationVersionAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the application ConfigurationVersion.
        /// </summary>
        /// <param name="configurationVersion">ConfigurationVersion to persist.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetConfigurationVersionAsync(
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the persisted ConfigurationVersion for a PublishedDataSet.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ConfigurationVersionDataType?> GetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the ConfigurationVersion for a PublishedDataSet.
        /// </summary>
        /// <param name="publishedDataSetName">PublishedDataSet name.</param>
        /// <param name="configurationVersion">ConfigurationVersion to persist.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default);
    }
}
