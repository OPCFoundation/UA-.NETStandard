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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Client-side wrapper for the OPC 10000-100 Part 21
    /// <c>DeviceRegistrarAdminType</c> facet. Calls
    /// <c>RegisterTickets</c> / <c>UnregisterTickets</c> on the
    /// server-side registrar and returns the per-ticket status
    /// codes.
    /// </summary>
    public sealed class OnboardingClient
    {
        /// <summary>
        /// Creates a new onboarding client rooted at the supplied
        /// <c>DeviceRegistrarAdminType</c> instance.
        /// </summary>
        public OnboardingClient(
            ISession session,
            NodeId registrarNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (registrarNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Registrar NodeId is required.", nameof(registrarNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            Session = session;
            RegistrarNodeId = registrarNodeId;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// The NodeId of the registrar instance.
        /// </summary>
        public NodeId RegistrarNodeId { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Invokes <c>RegisterTickets</c>. Returns the per-ticket
        /// status array reported by the server.
        /// </summary>
        public ValueTask<int[]> RegisterTicketsAsync(
            byte[][] tickets, CancellationToken ct = default)
        {
            if (tickets == null)
            {
                throw new ArgumentNullException(nameof(tickets));
            }
            return CallTicketArrayMethodAsync("RegisterTickets", tickets, ct);
        }

        /// <summary>
        /// Invokes <c>UnregisterTickets</c>. Returns the per-ticket
        /// status array.
        /// </summary>
        public ValueTask<int[]> UnregisterTicketsAsync(
            byte[][] tickets, CancellationToken ct = default)
        {
            if (tickets == null)
            {
                throw new ArgumentNullException(nameof(tickets));
            }
            return CallTicketArrayMethodAsync("UnregisterTickets", tickets, ct);
        }

        private async ValueTask<int[]> CallTicketArrayMethodAsync(
            string methodBrowseName,
            byte[][] tickets,
            CancellationToken ct)
        {
            NodeId methodId = await ResolveMethodAsync(methodBrowseName, ct)
                .ConfigureAwait(false);

            var bs = new ByteString[tickets.Length];
            for (int i = 0; i < tickets.Length; i++)
            {
                bs[i] = new ByteString(tickets[i] ?? []);
            }

            var request = new CallMethodRequest
            {
                ObjectId = RegistrarNodeId,
                MethodId = methodId,
                InputArguments =
                    new Variant[] { new(bs.ToArrayOf()) }.ToArrayOf()
            };

            CallResponse response = await Session
                .CallAsync(
                    requestHeader: null,
                    methodsToCall: new[] { request }.ToArrayOf(),
                    ct: ct)
                .ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Registrar call returned no results.");
            }

            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(
                    result.StatusCode,
                    $"Registrar call returned bad status {result.StatusCode}.");
            }

            if (result.OutputArguments.Count == 0)

            {

                return [];

            }
            object? boxed = result.OutputArguments[0].AsBoxedObject();
            return boxed switch
            {
                int[] arr => arr,
                ArrayOf<int> ai => ai.ToArray() ?? [],
                _ => []
            };
        }

        private async ValueTask<NodeId> ResolveMethodAsync(
            string browseName, CancellationToken ct)
        {
            var path = new BrowsePath
            {
                StartingNode = RegistrarNodeId,
                RelativePath = new RelativePath
                {
                    Elements = new[]
                    {
                        new RelativePathElement
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName)
                        }
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse translate = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, new[] { path }.ToArrayOf(), ct)
                .ConfigureAwait(false);

            if (translate.Results.Count == 0 ||
                StatusCode.IsBad(translate.Results[0].StatusCode) ||
                translate.Results[0].Targets.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"Could not resolve method '{browseName}' under registrar.");
            }

            return ExpandedNodeId.ToNodeId(
                translate.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);
        }
    }
}
