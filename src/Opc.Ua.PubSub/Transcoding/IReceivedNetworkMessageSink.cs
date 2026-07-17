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
