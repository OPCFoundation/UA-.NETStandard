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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;

namespace Opc.Ua
{
    /// <summary>
    /// Channel token key
    /// </summary>
    /// <param name="Iv"></param>
    /// <param name="Key"></param>
    /// <param name="SigLen"></param>
    public record class ChannelKey(
        IReadOnlyList<byte> Iv, IReadOnlyList<byte> Key, int SigLen);

    /// <summary>
    /// Channel diagnostics
    /// </summary>
    public record class TransportChannelDiagnostic
    {
        /// <summary>
        /// Timestamp of the diagnostic information
        /// </summary>
        public required DateTimeOffset TimeStamp { get; init; }

        /// <summary>
        /// The endpoint the channel is connected to
        /// </summary>
        public required EndpointDescription Endpoint { get; init; }

        /// <summary>
        /// Effective remote ip address used for the
        /// connection if connected. Empty if disconnected.
        /// </summary>
        public IPAddress? RemoteIpAddress { get; init; }

        /// <summary>
        /// The effective remote port used when connected,
        /// null if disconnected.
        /// </summary>
        public int? RemotePort { get; init; }

        /// <summary>
        /// Effective local ip address used for the connection
        /// if connected. Empty if disconnected.
        /// </summary>
        public IPAddress? LocalIpAddress { get; init; }

        /// <summary>
        /// The effective local port used when connected,
        /// null if disconnected.
        /// </summary>
        public int? LocalPort { get; init; }

        /// <summary>
        /// The id assigned to the channel that the token
        /// belongs to.
        /// </summary>
        public uint? ChannelId { get; init; }

        /// <summary>
        /// The id assigned to the token.
        /// </summary>
        public uint? TokenId { get; init; }

        /// <summary>
        /// When the token was created by the server
        /// (refers to the server's clock).
        /// </summary>
        public DateTime? CreatedAt { get; init; }

        /// <summary>
        /// The lifetime of the token
        /// </summary>
        public TimeSpan? Lifetime { get; init; }

        /// <summary>
        /// Client keys
        /// </summary>
        public ChannelKey? Client { get; init; }

        /// <summary>
        /// Server keys
        /// </summary>
        public ChannelKey? Server { get; init; }

        /// <summary>
        /// <para>
        /// Wireshark 4.3 now allows decryption of the UA binary protocol using a keyset log
        /// (opc ua debug file). The file contains records in the following format:
        /// </para>
        /// <para>
        /// client_iv_%channel_id%_%token_id%: %hex-string%
        /// client_key_%channel_id%_%token_id%: %hex-string%
        /// client_siglen_%channel_id%_%token_id%: 32
        /// server_iv_%channel_id%_%token_id%: %hex-string%
        /// server_key_%channel_id%_%token_id%: %hex-string%
        /// server_siglen_%channel_id%_%token_id%: 16|24|32
        /// ...
        /// </para>
        /// <para>
        /// This class writes the file to disk if a file was configured. See:
        /// https://gitlab.com/wireshark/wireshark/-/blob/master/plugins/epan/opcua/opcua.c#L232
        /// </para>
        /// </summary>
        /// <param name="keyset"></param>
        /// <returns></returns>
        public void WriteToWiresharkKeySetFile(StreamWriter keyset)
        {
            if (Client == null || Server == null)
            {
                // Not a valid change, channel without keys
                return;
            }
            keyset.Write($"client_iv_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Utils.ToHexString([.. Client.Iv]));
            keyset.Write($"client_key_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Utils.ToHexString([.. Client.Key]));
            keyset.Write($"client_siglen_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Client.SigLen.ToString(CultureInfo.InvariantCulture));
            keyset.Write($"server_iv_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Utils.ToHexString([.. Server.Iv]));
            keyset.Write($"server_key_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Utils.ToHexString([.. Server.Key]));
            keyset.Write($"server_siglen_{ChannelId}_{TokenId}: ");
            keyset.WriteLine(Server.SigLen.ToString(CultureInfo.InvariantCulture));
            keyset.Flush();
        }
    }
}
