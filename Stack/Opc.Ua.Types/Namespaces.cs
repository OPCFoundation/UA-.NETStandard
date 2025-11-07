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

namespace Opc.Ua
{
    /// <summary>
    /// Defines well-known namespaces.
    /// </summary>
    internal static class Namespaces
    {
        /// <summary>
        /// The XML Schema Instance namespace.
        /// </summary>
        public const string XmlSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

        /// <summary>
        /// The URI for the built-in types namespace.
        /// </summary>
        public const string OpcUaBuiltInTypes = OpcUa + "BuiltInTypes/";

        /// <summary>
        /// The URI for the OPC Binary Schema.
        /// </summary>
        public const string OpcBinarySchema = "http://opcfoundation.org/BinarySchema/";

        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
}
