// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Stores context information for message encoding and decoding.
    /// </summary>
    public interface IServiceMessageContext
    {
        /// <summary>
        /// The maximum length for any string, byte string or xml element.
        /// </summary>
        int MaxStringLength { get; }

        /// <summary>
        /// The maximum length for any array.
        /// </summary>
        int MaxArrayLength { get; }

        /// <summary>
        /// The maximum length for any ByteString.
        /// </summary>
        int MaxByteStringLength { get; }

        /// <summary>
        /// The maximum length for any Message.
        /// </summary>
        int MaxMessageSize { get; }

        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        int MaxEncodingNestingLevels { get; }

        /// <summary>
        /// The number of times the decoder can recover from an error
        /// caused by an encoded ExtensionObject before throwing a decoder error.
        /// </summary>
        int MaxDecoderRecoveries { get; }

        /// <summary>
        /// The table of namespaces used by the server.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of servers used by the server.
        /// </summary>
        StringTable ServerUris { get; }

        /// <summary>
        /// The factory used to create encodeable objects.
        /// </summary>
        IEncodeableFactory Factory { get; }

        /// <summary>
        /// Telemetry context
        /// </summary>
        ITelemetryContext Telemetry { get; }
    }
}
