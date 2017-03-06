/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    public struct SessionLessServiceMessage : IEncodeable
    {
        public NamespaceTable NamespaceUris;

        public StringTable ServerUris;

        public IEncodeable Message;

        public ExpandedNodeId TypeId
        {
            get { return DataTypeIds.SessionLessServiceMessageType; }
        }

        public ExpandedNodeId BinaryEncodingId
        {
            get { return ObjectIds.SessionLessServiceMessageType_Encoding_DefaultBinary; }
        }

        public ExpandedNodeId XmlEncodingId
        {
            get { return ObjectIds.SessionLessServiceMessageType_Encoding_DefaultXml; }
        }

        public void Encode(IEncoder encoder)
        {
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
                encoder.WriteStringArray("NamespaceUris", new string[0]);
            }

            if (ServerUris != null && ServerUris.Count > 1)
            {
                string[] uris = new string[ServerUris.Count - 1];

                for (int ii = 1; ii < NamespaceUris.Count; ii++)
                {
                    uris[ii - 1] = ServerUris.GetString((uint)ii);
                }

                encoder.WriteStringArray("ServerUris", uris);
            }
            else
            {
                encoder.WriteStringArray("ServerUris", new string[0]);
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

        public void Decode(IDecoder decoder)
        {
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

        public bool IsEqual(IEncodeable encodeable)
        {
            throw new NotImplementedException();
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }
    }

}//namespace
