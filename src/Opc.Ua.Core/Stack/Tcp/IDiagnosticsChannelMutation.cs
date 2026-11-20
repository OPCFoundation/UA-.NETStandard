// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Internal extension surface used by the
    /// <c>Opc.Ua.Pcap</c> offline-decode pipeline to seed a
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
