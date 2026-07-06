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

using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="IRedundantServerSetProvider"/> whose peer set is
    /// updated at runtime from an <see cref="IPeerDiscovery"/>. <c>FindServers</c> therefore returns the
    /// currently-discovered replica set (OPC 10000-4 §6.6.2.4.5.1), reflecting scale-up and scale-down without
    /// a restart. Reads are lock-free (a volatile snapshot swap).
    /// </summary>
    public sealed class DiscoveredRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            return Volatile.Read(ref m_descriptions);
        }

        /// <summary>
        /// Replaces the published peer set with descriptions built from the discovered peers.
        /// </summary>
        /// <param name="peers">The discovered peers.</param>
        public void Update(ArrayOf<DiscoveredPeer> peers)
        {
            var descriptions = new List<ApplicationDescription>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                DiscoveredPeer peer = peers[i];
                if (peer == null || string.IsNullOrEmpty(peer.ServerUri) || peer.DiscoveryUrls.IsNull)
                {
                    continue;
                }

                descriptions.Add(new ApplicationDescription
                {
                    ApplicationUri = peer.ServerUri,
                    ApplicationName = new LocalizedText(peer.ServerUri),
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = peer.DiscoveryUrls
                });
            }

            Volatile.Write(ref m_descriptions, descriptions.ToArray());
        }

        private ApplicationDescription[] m_descriptions = [];
    }
}
