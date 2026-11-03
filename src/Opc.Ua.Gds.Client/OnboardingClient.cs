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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-21
    /// <c>DeviceRegistrarAdminType</c> facet.
    /// </summary>
    /// <remarks>
    /// The specification-native <c>RegisterTickets</c> and
    /// <c>UnregisterTickets</c> methods are inherited from
    /// <see cref="DeviceRegistrarAdminTypeClient"/>.
    /// </remarks>
    public sealed class OnboardingClient : DeviceRegistrarAdminTypeClient
    {
        /// <summary>
        /// Creates a new onboarding client rooted at the supplied
        /// <c>DeviceRegistrarAdminType</c> instance.
        /// </summary>
        public OnboardingClient(
            ISession session,
            NodeId registrarNodeId,
            ITelemetryContext telemetry)
            : base(
                PrepareSession(session),
                ValidateRegistrarNodeId(registrarNodeId),
                telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
        {
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public new ISession Session => (ISession)base.Session;

        /// <summary>
        /// The NodeId of the registrar administration instance.
        /// </summary>
        public NodeId RegistrarNodeId => ObjectId;

        /// <summary>
        /// Registers signed JSON tickets using the Part 21 String wire representation.
        /// </summary>
        public new ValueTask<ArrayOf<StatusCode>> RegisterTicketsAsync(
            ArrayOf<string> tickets,
            CancellationToken ct = default)
        {
            return base.RegisterTicketsAsync(tickets, ct);
        }

        /// <summary>
        /// Removes signed JSON tickets using the Part 21 String wire representation.
        /// </summary>
        public new ValueTask<ArrayOf<StatusCode>> UnregisterTicketsAsync(
            ArrayOf<string> tickets,
            CancellationToken ct = default)
        {
            return base.UnregisterTicketsAsync(tickets, ct);
        }

        /// <summary>
        /// Registers UTF-8 encoded JSON tickets through the corrected String wire representation.
        /// </summary>
        [Obsolete("EncodedTicket is a String subtype. Pass ArrayOf<string> instead.")]
        public ValueTask<ArrayOf<StatusCode>> RegisterTicketsAsync(
            ArrayOf<ByteString> tickets,
            CancellationToken ct = default)
        {
            return base.RegisterTicketsAsync(DecodeTickets(tickets), ct);
        }

        /// <summary>
        /// Removes UTF-8 encoded JSON tickets through the corrected String wire representation.
        /// </summary>
        [Obsolete("EncodedTicket is a String subtype. Pass ArrayOf<string> instead.")]
        public ValueTask<ArrayOf<StatusCode>> UnregisterTicketsAsync(
            ArrayOf<ByteString> tickets,
            CancellationToken ct = default)
        {
            return base.UnregisterTicketsAsync(DecodeTickets(tickets), ct);
        }

        /// <summary>
        /// Rejects invalid UTF-8 instead of silently changing signed ticket content.
        /// </summary>
        private static ArrayOf<string> DecodeTickets(ArrayOf<ByteString> tickets)
        {
            if (tickets.IsNull)
            {
                return ArrayOf<string>.Null;
            }
            var utf8 = new UTF8Encoding(false, true);
            var decoded = new string[tickets.Count];
            for (int i = 0; i < tickets.Count; i++)
            {
                if (!tickets[i].IsNull)
                {
                    decoded[i] = utf8.GetString(tickets[i].ToArray());
                }
            }
            return decoded;
        }

        private static ISession PrepareSession(ISession session)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            NamespaceTable messageContextNamespaces =
                session.MessageContext.NamespaceUris;
            NamespaceTable sessionNamespaces = session.NamespaceUris;
            if (!NamespaceTablesMatch(messageContextNamespaces, sessionNamespaces))
            {
                messageContextNamespaces.Update(sessionNamespaces.ToArray());
            }
            return session;
        }

        private static bool NamespaceTablesMatch(
            NamespaceTable first,
            NamespaceTable second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }
            if (first.Count != second.Count)
            {
                return false;
            }
            for (int i = 0; i < first.Count; i++)
            {
                if (!string.Equals(
                    first.GetString((uint)i),
                    second.GetString((uint)i),
                    StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static NodeId ValidateRegistrarNodeId(NodeId registrarNodeId)
        {
            if (registrarNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Registrar NodeId is required.",
                    nameof(registrarNodeId));
            }
            return registrarNodeId;
        }
    }
}
