// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A session-less service message.
    /// </summary>
    public class SessionLessServiceMessage
    {
        /// <summary>
        /// The VersionTime of the namespaces URIs on the server.
        /// </summary>
        public uint UriVersion;

        /// <summary>
        /// The namespaces URIs referenced by the message.
        /// </summary>
        public NamespaceTable? NamespaceUris;

        /// <summary>
        /// The server URIs referenced by the message.
        /// </summary>
        /// <remarks>
        /// Index zero is reserved for the local server and is not sent on the wire.
        /// Decoding without remote server URIs does not require a local URI in the context.
        /// </remarks>
        public StringTable? ServerUris;

        /// <summary>
        /// The locale Ids referenced by the message.
        /// </summary>
        public StringTable? LocaleIds;

        /// <summary>
        /// The message to encode or the decoded message.
        /// </summary>
        public IEncodeable? Message;

        /// <inheritdoc cref="IEncodeable.Encode(IEncoder)" />
        public void Encode(IEncoder encoder)
        {
            encoder.WriteUInt32("UriVersion", UriVersion);
            if (NamespaceUris != null && NamespaceUris.Count > 1)
            {
                string?[] uris = new string?[NamespaceUris.Count - 1];

                for (int ii = 1; ii < NamespaceUris.Count; ii++)
                {
                    uris[ii - 1] = NamespaceUris.GetString((uint)ii);
                }

                encoder.WriteStringArray("NamespaceUris", uris!);
            }
            else
            {
                encoder.WriteStringArray("NamespaceUris", Array.Empty<string>());
            }

            if (ServerUris != null && ServerUris.Count > 1)
            {
                string?[] uris = new string?[ServerUris.Count - 1];

                for (int ii = 1; ii < ServerUris.Count; ii++)
                {
                    uris[ii - 1] = ServerUris.GetString((uint)ii);
                }

                encoder.WriteStringArray("ServerUris", uris!);
            }
            else
            {
                encoder.WriteStringArray("ServerUris", Array.Empty<string>());
            }

            if (LocaleIds != null && LocaleIds.Count > 0)
            {
                encoder.WriteStringArray("LocaleIds", LocaleIds.ToArray());
            }
            else
            {
                encoder.WriteStringArray("LocaleIds", Array.Empty<string>());
            }

            if (Message != null)
            {
                encoder.SetMappingTables(NamespaceUris!, ServerUris!);

                if (Message.TypeId.IsNull ||
                    !Message.TypeId.TryGetValue(out uint numericId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingError,
                        "SessionLessServiceMessage message body must have a numeric TypeId defined. ({0})",
                        Message.TypeId);
                }

                encoder.WriteUInt32("ServiceId", numericId);
                encoder.WriteEncodeable("Body", Message, Message.TypeId);
            }
            else
            {
                encoder.WriteUInt32("TypeId", 0);
            }
        }

        /// <inheritdoc cref="IEncodeable.Decode(IDecoder)" />
        /// <exception cref="ServiceResultException">
        /// A namespace URI, server URI, or locale entry is null or empty.
        /// </exception>
        public void Decode(IDecoder decoder)
        {
            UriVersion = decoder.ReadUInt32("UriVersion");

            NamespaceUris = new NamespaceTable();
            ArrayOf<string> uris = decoder.ReadStringArray("NamespaceUris")!;

            foreach (string uri in uris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "NamespaceUris contains an empty URI.");
                }
                NamespaceUris.Append(uri);
            }

            uris = decoder.ReadStringArray("ServerUris")!;

            string? localServerUri = decoder.Context.ServerUris.GetString(0);
            if (uris.Count > 0 && string.IsNullOrEmpty(localServerUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "The decoder context has no local server URI.");
            }

            ServerUris = new StringTable([localServerUri ?? string.Empty]);
            foreach (string uri in uris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "ServerUris contains an empty URI.");
                }
                ServerUris.Append(uri);
            }

            LocaleIds = new StringTable();
            uris = decoder.ReadStringArray("LocaleIds")!;
            foreach (string uri in uris)
            {
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "LocaleIds contains an empty locale.");
                }
                LocaleIds.Append(uri);
            }

            // Without remote URIs, index zero is already local and must not be URI-mapped.
            decoder.SetMappingTables(NamespaceUris, ServerUris.Count > 1 ? ServerUris : new StringTable());

            uint typeId = decoder.ReadUInt32("ServiceId");

            if (typeId > 0)
            {
                Message =
                    decoder.ReadEncodeable<IEncodeable>("Body", new ExpandedNodeId(typeId, 0)) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "SessionLessServiceMessage message body has an unknown TypeId. {0}",
                        typeId);
            }
        }
    }
}
