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

using System.Collections.Concurrent;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Maps server-side SecureChannel identifiers to the UASC channels that
    /// own them.
    /// </summary>
    /// <remarks>
    /// A Service request carries a SecureChannel identifier rather than the
    /// channel object, so anything that has to reach the transport behind a
    /// request - a message extension binding itself to the connection the
    /// request arrived on, for example - needs this lookup.
    /// <para>
    /// TODO: This is process-global rather than scoped to the listener that
    /// owns the channels, which means two servers in one process share it.
    /// Scoping it to the listener requires threading a registry instance
    /// through the transport bindings and is left as separate work.
    /// </para>
    /// </remarks>
    public static class UaSCSecureChannelRegistry
    {
        /// <summary>
        /// Finds the server-side UASC channel that owns a SecureChannel.
        /// </summary>
        /// <param name="secureChannelId">The SecureChannel identifier.</param>
        /// <param name="channel">The channel that owns it.</param>
        public static bool TryGet(
            string secureChannelId,
            out UaSCUaBinaryChannel? channel)
        {
            return s_channels.TryGetValue(secureChannelId, out channel);
        }

        /// <summary>
        /// Records the channel that owns a SecureChannel.
        /// </summary>
        /// <param name="secureChannelId">The SecureChannel identifier.</param>
        /// <param name="channel">The channel that owns it.</param>
        public static void Bind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId))
            {
                s_channels[secureChannelId] = channel;
            }
        }

        /// <summary>
        /// Releases the record for a SecureChannel, if the channel given still
        /// owns it.
        /// </summary>
        /// <param name="secureChannelId">The SecureChannel identifier.</param>
        /// <param name="channel">The channel that owned it.</param>
        public static void Unbind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId) &&
                s_channels.TryGetValue(secureChannelId, out UaSCUaBinaryChannel? current) &&
                ReferenceEquals(current, channel))
            {
                s_channels.TryRemove(secureChannelId, out _);
            }
        }

        private static readonly ConcurrentDictionary<string, UaSCUaBinaryChannel> s_channels = new();
    }
}
