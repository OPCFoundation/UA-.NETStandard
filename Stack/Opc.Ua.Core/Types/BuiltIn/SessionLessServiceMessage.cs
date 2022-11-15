/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
        public UInt32 UriVersion;

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

                if (Message.TypeId == null || Message.TypeId.IdType != IdType.Numeric)
                {
                    throw ServiceResultException.Create(StatusCodes.BadEncodingError, "SessionLessServiceMessage message body must have a numeric TypeId defined. ({0})", Message.TypeId);
                }

                encoder.WriteUInt32("ServiceId", (uint)Message.TypeId.Identifier);
                encoder.WriteEncodeable("Body", Message, null);
            }
            else
            {
                encoder.WriteUInt32("TypeId", (uint)0);
            }
        }

        /// <inheritdoc cref="IEncodeable.Decode(IDecoder)" />
        public void Decode(IDecoder decoder)
        {
            UriVersion = decoder.ReadUInt32("UriVersion");

            NamespaceUris = new NamespaceTable();
            var uris = decoder.ReadStringArray("NamespaceUris");

            if (uris != null && uris.Count > 0)
            {
                foreach (var uri in uris)
                {
                    NamespaceUris.Append(uri);
                }
            }

            ServerUris = new StringTable();
            uris = decoder.ReadStringArray("ServerUris");

            if (uris != null && uris.Count > 0)
            {
                foreach (var uri in uris)
                {
                    ServerUris.Append(uri);
                }
            }

            LocaleIds = new StringTable();
            uris = decoder.ReadStringArray("LocaleIds");
            if (uris != null && uris.Count > 0)
            {
                foreach (var uri in uris)
                {
                    LocaleIds.Append(uri);
                }
            }

            decoder.SetMappingTables(NamespaceUris, ServerUris);

            uint typeId = decoder.ReadUInt32("ServiceId");

            if (typeId > 0)
            {
                var systemType = decoder.Context.Factory.GetSystemType(new ExpandedNodeId(typeId, 0));

                if (systemType == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError, "SessionLessServiceMessage message body has an unknown TypeId. {0}", typeId);
                }

                Message = decoder.ReadEncodeable("Body", systemType);
            }
        }
    }
}//namespace
