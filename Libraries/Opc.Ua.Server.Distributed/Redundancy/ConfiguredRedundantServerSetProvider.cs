/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Builds non-transparent <c>RedundantServerSet</c> peer <see cref="ApplicationDescription"/> entries from
    /// <see cref="ServerRedundancyOptions"/> for <c>FindServers</c> (OPC 10000-4 §6.6.2.4.5.1).
    /// </summary>
    public sealed class ConfiguredRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <summary>
        /// Creates the provider.
        /// </summary>
        /// <param name="options">The redundancy options.</param>
        public ConfiguredRedundantServerSetProvider(ServerRedundancyOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            if (!m_options.IsNonTransparentMode)
            {
                return [];
            }

            var descriptions = new List<ApplicationDescription>(m_options.RedundantPeers.Count);
            foreach (RedundantPeer peer in m_options.RedundantPeers)
            {
                if (string.IsNullOrEmpty(peer.ApplicationUri) || peer.DiscoveryUrls.IsNull)
                {
                    continue;
                }

                descriptions.Add(new ApplicationDescription
                {
                    ApplicationUri = peer.ApplicationUri,
                    ApplicationName = peer.ApplicationName.IsNull
                        ? new LocalizedText(peer.ApplicationUri)
                        : peer.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = peer.DiscoveryUrls
                });
            }

            return new ArrayOf<ApplicationDescription>(descriptions.ToArray());
        }

        private readonly ServerRedundancyOptions m_options;
    }
}
