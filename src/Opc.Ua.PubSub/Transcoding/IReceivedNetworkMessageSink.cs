/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Opt-in observer of decoded NetworkMessages on a PubSubConnection's
    /// receive path. A registered sink is invoked for every data
    /// NetworkMessage the connection decodes, in addition to the normal
    /// reader-group dispatch. Used by <c>PubSubTranscodingBridge</c>
    /// to forward received messages to a publisher side.
    /// </summary>
    /// <remarks>
    /// The sink runs inline on the receive loop; implementations must not
    /// block and must not throw. Exceptions are caught and logged by the
    /// connection so a faulty sink cannot terminate reception.
    /// </remarks>
    public interface IReceivedNetworkMessageSink
    {
        /// <summary>
        /// Called for each decoded data NetworkMessage.
        /// </summary>
        /// <param name="received">The received message and raw frame.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask OnReceivedAsync(
            ReceivedNetworkMessage received,
            CancellationToken cancellationToken = default);
    }
}
