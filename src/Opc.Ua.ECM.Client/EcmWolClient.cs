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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.ECM.Client
{
    /// <summary>
    /// Controls a standalone WOL object through its generated proxy without owning the session.
    /// </summary>
    public sealed class EcmWolClient
    {
        /// <summary>
        /// Creates a WOL-only client. Construction does not call a method or send wake traffic.
        /// </summary>
        public EcmWolClient(ISessionClient session, NodeId powerOffId, ITelemetryContext telemetry)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (powerOffId.IsNull)
            {
                throw new ArgumentException("A WOL object is required.", nameof(powerOffId));
            }
            EcmClient.RegisterModel(session);
            PowerOff = new EnergyDevicePowerOffTypeClient(session, powerOffId, telemetry);
        }

        /// <summary>
        /// Gets the generated WOL proxy with model-derived variable read and observation accessors.
        /// </summary>
        public EnergyDevicePowerOffTypeClient PowerOff { get; }

        /// <summary>
        /// Discovers direct WOL children, including vendor subtypes, without requiring a standby parent.
        /// </summary>
        public static IAsyncEnumerable<EcmWolClient> DiscoverAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return DiscoverCoreAsync(session, parent, telemetry, cancellationToken);
        }

        /// <summary>
        /// Explicitly requests WOL sleep, preserving Call status separately from ReturnCode without replay.
        /// </summary>
        public async ValueTask<EcmControlResult> SwitchOffWolAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (status, mode, destination, recovery, stay, code) =
                    await PowerOff.SwitchOffWOLWithStatusAsync(cancellationToken).ConfigureAwait(false);
                if ((StatusCode.IsGood(status) && mode != 0xFE) ||
                    (StatusCode.IsUncertain(status) && mode != 0))
                {
                    return new EcmControlResult(StatusCodes.BadDecodingError, null);
                }
                return EcmClient.DecodeResult(status, code, mode, destination, recovery, stay);
            }
            catch (ServiceResultException exception) when (StatusCode.IsBad(exception.StatusCode))
            {
                return new EcmControlResult(exception.StatusCode, null);
            }
        }

        /// <summary>
        /// Enumerates WOL children after validating discovery arguments.
        /// </summary>
        private static async IAsyncEnumerable<EcmWolClient> DiscoverCoreAsync(
            ISession session,
            NodeId parent,
            ITelemetryContext telemetry,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(ObjectTypeIds.EnergyDevicePowerOffType, session.NamespaceUris);
            if (expected.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> children = await ClientModelAccess.BrowseAsync(
                session, parent, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
            var seen = new HashSet<NodeId>();
            for (int ii = 0; ii < children.Count; ii++)
            {
                ReferenceDescription child = children[ii];
                NodeId id = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
                NodeId actual = ExpandedNodeId.ToNodeId(child.TypeDefinition, session.NamespaceUris);
                if (!id.IsNull && !actual.IsNull && seen.Add(id) &&
                    await session.NodeCache.IsTypeOfAsync(actual, expected, cancellationToken).ConfigureAwait(false))
                {
                    yield return new EcmWolClient(session, id, telemetry);
                }
            }
        }
    }
}
