/* Copyright (c) 1996-2023 The OPC Foundation. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua.Export
{
    /// <summary>
    /// An interface which browses for references of an INode.
    /// </summary>
    public interface INodeClientBrowser
    {
        /// <summary>
        /// Browses all references of an INode.
        /// </summary>
        INodeBrowser CreateBrowser(INode node, BrowseDirection browseDirection);

        /// <summary>
        /// Returns a parent of a node.
        /// </summary>
        Node GetParent(INode node);

        /// <summary>
        /// Returns the children of a node.
        /// </summary>
        IList<Node> GetChildren(INode node);
    }
}
