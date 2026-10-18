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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Non-secret application evidence, including exact complete NetworkMessages at both transport boundaries.
    /// </summary>
    internal sealed record EndpointEvidence
    {
        /// <summary>
        /// Gets the hosting process role.
        /// </summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>
        /// Gets the actual application publishing role.
        /// </summary>
        public bool Publisher { get; init; }

        /// <summary>
        /// Gets the actual negotiated carrier profile.
        /// </summary>
        public string Profile { get; init; } = string.Empty;

        /// <summary>
        /// Gets the independently negotiated payload protocol.
        /// </summary>
        public string Protocol { get; init; } = string.Empty;

        /// <summary>
        /// Gets the authenticated context identifier.
        /// </summary>
        public Guid ContextId { get; init; }

        /// <summary>
        /// Gets the context-generation-local flow identifier.
        /// </summary>
        public uint ChannelId { get; init; }

        /// <summary>
        /// Gets the final committed revision.
        /// </summary>
        public uint Revision { get; init; }

        /// <summary>
        /// Gets the final negotiated complete-message bound.
        /// </summary>
        public uint MaxMessageSize { get; init; }

        /// <summary>
        /// Gets the final negotiated receive budget.
        /// </summary>
        public uint MaxReceiveBytes { get; init; }

        /// <summary>
        /// Gets whether this peer actually opened the flow.
        /// </summary>
        public bool LocalOpener { get; init; }

        /// <summary>
        /// Gets the independently authenticated transport-initiator role.
        /// </summary>
        public bool TransportInitiator { get; init; }

        /// <summary>
        /// Gets the actual opener-relative direction.
        /// </summary>
        public string Direction { get; init; } = string.Empty;

        /// <summary>
        /// Gets the actually negotiated confidentiality protection.
        /// </summary>
        public string Protection { get; init; } = string.Empty;

        /// <summary>
        /// Gets the independently authenticated application peer URI.
        /// </summary>
        public string PeerApplicationUri { get; init; } = string.Empty;

        /// <summary>
        /// Gets the actual client SecureChannel identifier.
        /// </summary>
        public uint SecureChannelId { get; init; }

        /// <summary>
        /// Gets the ordinary Session transport's actual SecureChannel identifier.
        /// </summary>
        public uint ServiceSecureChannelId { get; init; }

        /// <summary>
        /// Gets the verified object-identity comparison of Services and the Session transport.
        /// </summary>
        public bool SameServiceTransport { get; init; }

        /// <summary>
        /// Gets the number of established Sessions observed by this peer.
        /// </summary>
        public int Sessions { get; init; }

        /// <summary>
        /// Gets the number of successful ordinary Reads.
        /// </summary>
        public int Reads { get; init; }

        /// <summary>
        /// Gets the number of successful ordinary Publish responses.
        /// </summary>
        public int Publishes { get; init; }

        /// <summary>
        /// Gets the number of decoded monitored-item notifications.
        /// </summary>
        public int Notifications { get; init; }

        /// <summary>
        /// Gets the number of completed grant or revision Calls.
        /// </summary>
        public int Calls { get; init; }

        /// <summary>
        /// Gets whether the optional generated model was actually installed.
        /// </summary>
        public bool ModelEnabled { get; init; }

        /// <summary>
        /// Gets whether orderly directional END and peer acknowledgement were observed.
        /// </summary>
        public bool OrderlyEnd { get; init; }

        /// <summary>
        /// Gets the actual complete messages accepted by the transmitting transport, in order.
        /// </summary>
        public string[] Sent { get; init; } = [];

        /// <summary>
        /// Gets the complete received messages before decoding, in order.
        /// </summary>
        public string[] Received { get; init; } = [];

        /// <summary>
        /// Gets the live revision observed for each actual successful NetworkMessage emission.
        /// </summary>
        public uint[] SentRevisions { get; init; } = [];

        /// <summary>
        /// Gets the live revision observed for each actual NetworkMessage reception.
        /// </summary>
        public uint[] ReceivedRevisions { get; init; } = [];

        /// <summary>
        /// Gets each successful WriterGroup data publishing cycle's revision, excluding metadata announcements.
        /// </summary>
        public uint[] PublishedRevisions { get; init; } = [];

        /// <summary>
        /// Gets primary writer values materialized by the real subscriber.
        /// </summary>
        public int[] Counters { get; init; } = [];

        /// <summary>
        /// Gets the independent second writer's decoded values.
        /// </summary>
        public int[] OtherCounters { get; init; } = [];

        /// <summary>
        /// Gets decoded Boolean field values.
        /// </summary>
        public bool[] Toggles { get; init; } = [];

        /// <summary>
        /// Gets decoded Double field values.
        /// </summary>
        public double[] Temperatures { get; init; } = [];

        /// <summary>
        /// Gets the unmatched-reader delivery count, which must remain zero.
        /// </summary>
        public int RejectedReaderDeliveries { get; init; }
    }

    /// <summary>
    /// Finite output from a single- or two-peer invocation.
    /// </summary>
    internal sealed record RunEvidence
    {
        /// <summary>
        /// Gets the successfully validated endpoint evidence.
        /// </summary>
        public EndpointEvidence[] Endpoints { get; init; } = [];
    }

    /// <summary>
    /// Non-secret endpoint exchange; certificate trust is loaded independently from the explicit PKI.
    /// </summary>
    internal sealed record ServerReady
    {
        /// <summary>
        /// Gets the selected flow endpoint.
        /// </summary>
        public string FlowEndpoint { get; init; } = string.Empty;

        /// <summary>
        /// Gets the ordinary control endpoint, never a dedicated flow resource.
        /// </summary>
        public string ControlEndpoint { get; init; } = string.Empty;

        /// <summary>
        /// Gets the explicit carrier selection.
        /// </summary>
        public bool Shared { get; init; }

        /// <summary>
        /// Gets the requested independent payload.
        /// </summary>
        public bool Json { get; init; }

        /// <summary>
        /// Gets the selected grant route.
        /// </summary>
        public bool SessionAuthorization { get; init; }

        /// <summary>
        /// Gets the publisher-role selection.
        /// </summary>
        public bool Reverse { get; init; }

        /// <summary>
        /// Gets the finite sample count.
        /// </summary>
        public int Count { get; init; }
    }

    /// <summary>
    /// Reflection-free serialization for diagnostics and process readiness, never for PubSub wire data.
    /// </summary>
    [JsonSerializable(typeof(RunEvidence))]
    [JsonSerializable(typeof(ServerReady))]
    internal sealed partial class EvidenceJsonContext : JsonSerializerContext;

    /// <summary>
    /// Atomic local evidence output and bounded readiness input.
    /// </summary>
    internal static class EvidenceFiles
    {
        /// <summary>
        /// Publishes a complete file atomically in the user-selected directory.
        /// </summary>
        internal static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string pending = fullPath + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                await File.WriteAllTextAsync(pending, content, cancellationToken).ConfigureAwait(false);
                File.Move(pending, fullPath, true);
            }
            finally
            {
                File.Delete(pending);
            }
        }

        /// <summary>
        /// Waits only for the explicit server readiness exchange, not for an assumed connection delay.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        internal static async Task<ServerReady> ReadReadyAsync(
            SampleOptions options,
            CancellationToken cancellationToken)
        {
            while (!File.Exists(options.ReadyFile))
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            string json = await File.ReadAllTextAsync(options.ReadyFile!, cancellationToken).ConfigureAwait(false);
            ServerReady ready = JsonSerializer.Deserialize(json, EvidenceJsonContext.Default.ServerReady) ??
                throw new InvalidDataException("The server readiness file is empty.");
            if (ready.Shared != options.Shared ||
                ready.Json != options.Json ||
                ready.SessionAuthorization != options.SessionAuthorization ||
                ready.Reverse != options.Reverse ||
                ready.Count != options.Count)
            {
                throw new InvalidDataException("Client options differ from the explicit server readiness contract.");
            }
            return ready;
        }
    }
}
