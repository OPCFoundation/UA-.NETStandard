/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

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

namespace Opc.Ua.Sample.PubSub
{
    public static class NodeIdentifier
    {
        /// <summary>
        /// AddBrokerConnection Method ExpandedNodeId
        /// </summary>
        public static readonly ExpandedNodeId ADDBROKER_ID = new ExpandedNodeId(NodeIds.ADDBROKER, 0);

        public static readonly ExpandedNodeId ADDUADP_ID = new ExpandedNodeId(NodeIds.ADDUADP, 0);

        public static readonly ExpandedNodeId REMOVECONNECTION_ID = new ExpandedNodeId(NodeIds.ADDUADP, 0);
    }

    public static class NodeIds
    {
        public const string ADDBROKER = "14456";
        public const string ADDUADP = "14904";
        public const string REMOVECONNECTION = "14459";

    }
}