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
