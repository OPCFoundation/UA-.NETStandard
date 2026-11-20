/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Server.Configuration
{
    /// <summary>
    /// <see cref="IAssetConfigurationStore"/> that keeps the configured
    /// values in memory, for tests and servers whose assets are configured
    /// from the application on every start.
    /// </summary>
    /// <remarks>
    /// Values do not survive the process, so a server that lets clients
    /// configure assets and has to keep that configuration over a restart uses
    /// <see cref="FileSystemAssetConfigurationStore"/> or its own store.
    /// </remarks>
    public sealed class MemoryAssetConfigurationStore : IAssetConfigurationStore
    {
        /// <inheritdoc/>
        /// <remarks>Always <see langword="false"/>: the values live in memory.</remarks>
        public bool IsPersistent => false;

        /// <inheritdoc/>
        public ValueTask<string?> GetValueAsync(
            string productInstanceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            Validate(productInstanceUri, name);
            lock (m_lock)
            {
                return new ValueTask<string?>(
                    m_values.TryGetValue((productInstanceUri, name), out string? value) ? value : null);
            }
        }

        /// <inheritdoc/>
        public ValueTask SetValueAsync(
            string productInstanceUri,
            string name,
            string? value,
            CancellationToken cancellationToken = default)
        {
            Validate(productInstanceUri, name, value);
            lock (m_lock)
            {
                if (value == null)
                {
                    m_values.Remove((productInstanceUri, name));
                }
                else
                {
                    m_values[(productInstanceUri, name)] = value;
                }
            }
            return default;
        }

        internal static void Validate(string productInstanceUri, string name, string? value)
        {
            Validate(productInstanceUri, name);
            if (value != null && value.Length > AssetConfigurationNames.MaxValueLength)
            {
                throw new ArgumentException(
                    $"The value is longer than {AssetConfigurationNames.MaxValueLength} characters.",
                    nameof(value));
            }
        }

        internal static void Validate(string productInstanceUri, string name)
        {
            if (string.IsNullOrEmpty(productInstanceUri))
            {
                throw new ArgumentException(
                    "The ProductInstanceUri must not be empty.",
                    nameof(productInstanceUri));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("The value name must not be empty.", nameof(name));
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<(string ProductInstanceUri, string Name), string> m_values = [];
    }
}
