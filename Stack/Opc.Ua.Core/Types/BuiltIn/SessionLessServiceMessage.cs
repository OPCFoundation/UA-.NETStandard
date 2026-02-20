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
        public NamespaceTable NamespaceUris;

        /// <summary>
        /// The server URIs referenced by the message.
        /// </summary>
        public StringTable ServerUris;

        /// <summary>
        /// The locale Ids referenced by the message.
        /// </summary>
        public StringTable LocaleIds;

        /// <summary>
        /// The message to encode or the decoded message.
        /// </summary>
        public IEncodeable Message;

        /// <inheritdoc cref="IEncodeable.Encode(IEncoder)" />
        public void Encode(IEncoder encoder)
        {
            encoder.WriteUInt32("UriVersion", UriVersion);
            if (NamespaceUris != null && NamespaceUris.Count > 1)
            {
                string[] uris = new string[NamespaceUris.Count - 1];

                for (int ii = 1; ii < NamespaceUris.Count; ii++)
                {
                    uris[ii - 1] = NamespaceUris.GetString((uint)ii);
                }

                encoder.WriteStringArray("NamespaceUris", uris);
            }
            else
            {
                encoder.WriteStringArray("NamespaceUris", Array.Empty<string>());
            }

            if (ServerUris != null && ServerUris.Count > 1)
            {
                string[] uris = new string[ServerUris.Count - 1];

                for (int ii = 1; ii < ServerUris.Count; ii++)
                {
                    uris[ii - 1] = ServerUris.GetString((uint)ii);
                }

                encoder.WriteStringArray("ServerUris", uris);
            }
            else
            {
                encoder.WriteStringArray("ServerUris", Array.Empty<string>());
            }

            if (LocaleIds != null && LocaleIds.Count > 1)
            {
                encoder.WriteStringArray("LocaleIds", LocaleIds.ToArray());
            }
            else
            {
                encoder.WriteStringArray("LocaleIds", Array.Empty<string>());
            }

            if (Message != null)
            {
                encoder.SetMappingTables(NamespaceUris, ServerUris);

                if (Message.TypeId.IsNull ||
                    !Message.TypeId.TryGetIdentifier(out uint numericId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingError,
                        "SessionLessServiceMessage message body must have a numeric TypeId defined. ({0})",
                        Message.TypeId);
                }

                encoder.WriteUInt32("ServiceId", numericId);
                encoder.WriteEncodeable("Body", Message);
            }
            else
            {
                encoder.WriteUInt32("TypeId", 0);
            }
        }

        /// <inheritdoc cref="IEncodeable.Decode(IDecoder)" />
        public void Decode(IDecoder decoder)
        {
            UriVersion = decoder.ReadUInt32("UriVersion");

            NamespaceUris = new NamespaceTable();
            StringCollection uris = decoder.ReadStringArray("NamespaceUris");

            if (uris != null && uris.Count > 0)
            {
                foreach (string uri in uris)
                {
                    NamespaceUris.Append(uri);
                }
            }

            ServerUris = new StringTable();
            uris = decoder.ReadStringArray("ServerUris");

            if (uris != null && uris.Count > 0)
            {
                foreach (string uri in uris)
                {
                    ServerUris.Append(uri);
                }
            }

            LocaleIds = new StringTable();
            uris = decoder.ReadStringArray("LocaleIds");
            if (uris != null && uris.Count > 0)
            {
                foreach (string uri in uris)
                {
                    LocaleIds.Append(uri);
                }
            }

            decoder.SetMappingTables(NamespaceUris, ServerUris);

            uint typeId = decoder.ReadUInt32("ServiceId");

            if (typeId > 0)
            {
                Type systemType =
                    decoder.Context.Factory.GetSystemType(new ExpandedNodeId(typeId, 0))
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "SessionLessServiceMessage message body has an unknown TypeId. {0}",
                        typeId);

                Message = decoder.ReadEncodeable("Body", systemType);
            }
        }
    }
}
