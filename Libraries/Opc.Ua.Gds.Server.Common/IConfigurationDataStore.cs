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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Describes a managed application that appears under the
    /// <c>ManagedApplications</c> folder per OPC 10000-12 §7.10.16.
    /// </summary>
    public sealed class ManagedApplicationInfo
    {
        /// <summary>The ApplicationUri of the managed application.</summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>The ProductUri of the managed application.</summary>
        public string? ProductUri { get; set; }

        /// <summary>The OPC UA application type.</summary>
        public ApplicationType ApplicationType { get; set; }

        /// <summary>Whether the managed application is enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether this is a non-UA application (e.g. MQTT bridge).
        /// </summary>
        public bool IsNonUaApplication { get; set; }
    }

    /// <summary>
    /// Abstraction for the persistence layer behind the
    /// <c>ManagedApplications</c> folder (OPC 10000-12 §7.10.16) and
    /// the <c>CloseAndUpdate</c> / <c>ConfirmUpdate</c> transaction
    /// lifecycle (§7.7.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A production GDS injects an implementation that persists
    /// configuration data to a file system, database, or configuration
    /// management service. The <see cref="InMemoryConfigurationDataStore"/>
    /// provides a simple in-process implementation for testing.
    /// </para>
    /// <para>
    /// The store is used by <see cref="DefaultManagedApplicationsNodeManager"/>
    /// to enumerate managed applications at startup and to execute the
    /// read/write/confirm transaction flow on each application's
    /// configuration file.
    /// </para>
    /// </remarks>
    public interface IConfigurationDataStore
    {
        /// <summary>
        /// Returns the set of managed applications that should be
        /// exposed under the <c>ManagedApplications</c> folder.
        /// </summary>
        ValueTask<IReadOnlyList<ManagedApplicationInfo>> GetManagedApplicationsAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Reads the current configuration data for the specified
        /// managed application.
        /// </summary>
        /// <param name="applicationUri">
        /// The ApplicationUri of the managed application.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The raw configuration data, or an empty array if the
        /// application has no stored configuration.
        /// </returns>
        ValueTask<byte[]> ReadConfigurationAsync(
            string applicationUri,
            CancellationToken ct = default);

        /// <summary>
        /// Writes updated configuration data for the specified managed
        /// application and returns a new configuration version number.
        /// </summary>
        /// <param name="applicationUri">
        /// The ApplicationUri of the managed application.
        /// </param>
        /// <param name="data">The updated configuration data.</param>
        /// <param name="currentVersion">
        /// The version the caller believes is current. The store should
        /// reject the write with <see cref="StatusCodes.BadInvalidState"/>
        /// when the actual version differs (optimistic concurrency).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The new configuration version number.</returns>
        ValueTask<uint> WriteConfigurationAsync(
            string applicationUri,
            byte[] data,
            uint currentVersion,
            CancellationToken ct = default);

        /// <summary>
        /// Confirms a pending configuration update. The managed
        /// application has acknowledged the new configuration and
        /// applied it successfully.
        /// </summary>
        /// <param name="applicationUri">
        /// The ApplicationUri of the managed application.
        /// </param>
        /// <param name="configVersion">
        /// The configuration version that was applied.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ConfirmUpdateAsync(
            string applicationUri,
            uint configVersion,
            CancellationToken ct = default);
    }

    /// <summary>
    /// In-memory implementation of <see cref="IConfigurationDataStore"/>
    /// for testing and single-process GDS deployments.
    /// </summary>
    /// <remarks>
    /// All data is held in process memory and lost on restart. A
    /// production deployment should replace this with a persistent
    /// implementation.
    /// </remarks>
    public sealed class InMemoryConfigurationDataStore : IConfigurationDataStore
    {
        private readonly ConcurrentDictionary<string, ManagedApplicationInfo> m_apps = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConfigEntry> m_configs = new(StringComparer.Ordinal);

        /// <summary>
        /// Registers a managed application. Call this before starting
        /// the GDS to seed the <c>ManagedApplications</c> folder.
        /// </summary>
        public void AddApplication(ManagedApplicationInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (string.IsNullOrEmpty(info.ApplicationUri))
            {
                throw new ArgumentException("ApplicationUri must not be empty.", nameof(info));
            }

            m_apps[info.ApplicationUri] = info;
        }

        /// <summary>
        /// Removes a previously registered managed application.
        /// </summary>
        public bool RemoveApplication(string applicationUri)
        {
            m_configs.TryRemove(applicationUri, out _);
            return m_apps.TryRemove(applicationUri, out _);
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<ManagedApplicationInfo>> GetManagedApplicationsAsync(
            CancellationToken ct)
        {
            IReadOnlyList<ManagedApplicationInfo> result = m_apps.Values.ToList().AsReadOnly();
            return new ValueTask<IReadOnlyList<ManagedApplicationInfo>>(result);
        }

        /// <inheritdoc/>
        public ValueTask<byte[]> ReadConfigurationAsync(
            string applicationUri,
            CancellationToken ct)
        {
            if (m_configs.TryGetValue(applicationUri, out ConfigEntry? entry))
            {
                return new ValueTask<byte[]>(entry.Data);
            }

            return new ValueTask<byte[]>([]);
        }

        /// <inheritdoc/>
        public ValueTask<uint> WriteConfigurationAsync(
            string applicationUri,
            byte[] data,
            uint currentVersion,
            CancellationToken ct)
        {
            var entry = m_configs.GetOrAdd(applicationUri, _ => new ConfigEntry());

            lock (entry)
            {
                if (entry.Version != currentVersion)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Configuration version mismatch.");
                }

                entry.Data = data;
                entry.Version++;
                entry.Confirmed = false;
                return new ValueTask<uint>(entry.Version);
            }
        }

        /// <inheritdoc/>
        public ValueTask ConfirmUpdateAsync(
            string applicationUri,
            uint configVersion,
            CancellationToken ct)
        {
            if (!m_configs.TryGetValue(applicationUri, out ConfigEntry? entry))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "No configuration data for the specified application.");
            }

            lock (entry)
            {
                if (entry.Version != configVersion)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Configuration version does not match the pending update.");
                }

                entry.Confirmed = true;
            }

#if NET6_0_OR_GREATER
            return ValueTask.CompletedTask;
#else
            return new ValueTask(Task.CompletedTask);
#endif
        }

        private sealed class ConfigEntry
        {
            public byte[] Data { get; set; } = [];
            public uint Version { get; set; }
            public bool Confirmed { get; set; } = true;
        }
    }
}
