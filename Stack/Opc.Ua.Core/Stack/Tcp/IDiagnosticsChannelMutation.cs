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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Internal extension surface used by the
    /// <c>Opc.Ua.Core.Diagnostics</c> offline-decode pipeline to seed a
    /// secure-channel instance with channel tokens reconstructed from a
    /// keylog file.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a privileged, security-sensitive accessor. It allows a
    /// caller to substitute the symmetric key material of an existing
    /// channel and therefore decrypt or sign arbitrary frames. The
    /// implementation MUST validate that the caller's assembly is the
    /// Pcap binding (or a test assembly thereof) and reject any other
    /// caller. Consumers requiring offline decode must use the supported
    /// Pcap binding APIs; they MUST NOT cast to this interface directly.
    /// </remarks>
    internal interface IDiagnosticsChannelMutation
    {
        /// <summary>
        /// Loads the supplied channel tokens into the current channel for
        /// offline-decode purposes only.
        /// </summary>
        void LoadTokensForOfflineDecode(ChannelToken? current, ChannelToken? previous);
    }
}
