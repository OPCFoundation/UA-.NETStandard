/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Text;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A class that builds NodeIds used by the DataAccess NodeManager
    /// </summary>
    public static class AeModelUtils
    {
        /// <summary>
        /// The RootType for a AE Simple Event Type node.
        /// </summary>
        public const int AeSimpleEventType = OpcRcw.Ae.Constants.SIMPLE_EVENT;

        /// <summary>
        /// The RootType for a AE Tracking Event Type node.
        /// </summary>
        public const int AeTrackingEventType = OpcRcw.Ae.Constants.TRACKING_EVENT;

        /// <summary>
        /// The RootType for a AE Condition Event Type node.
        /// </summary>
        public const int AeConditionEventType = OpcRcw.Ae.Constants.CONDITION_EVENT;

        /// <summary>
        /// The RootType for a AE Area
        /// </summary>
        public const int AeArea = 5;

        /// <summary>
        /// The RootType for an AE Source
        /// </summary>
        public const int AeSource = 6;

        /// <summary>
        /// The RootType for an AE Condition
        /// </summary>
        public const int AeCondition = 7;

        /// <summary>
        /// The RootType for a node defined by the UA server.
        /// </summary>
        public const int InternalNode = 8;

        /// <summary>
        /// The RootType for an EventType defined by the AE server.
        /// </summary>
        public const int AeEventTypeMapping = 9;

        /// <summary>
        /// The RootType for a ConditionClass defined by the AE server.
        /// </summary>
        public const int AeConditionClassMapping = 10;

        /// <summary>
        /// Constructs a NodeId from the BrowseName of an internal node.
        /// </summary>
        /// <param name="browseName">The browse name.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The node id.</returns>
        public static NodeId ConstructIdForInternalNode(QualifiedName browseName, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = browseName.Name;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = InternalNode;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the id for an area.
        /// </summary>
        /// <param name="areaId">The area id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId ConstructIdForArea(string areaId, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = areaId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = AeArea;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the id for a source.
        /// </summary>
        /// <param name="areaId">The area id.</param>
        /// <param name="sourceName">Name of the source.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId ConstructIdForSource(string areaId, string sourceName, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootType = AeSource;
            parsedNodeId.RootId = areaId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.ComponentPath = sourceName;

            return parsedNodeId.Construct();
        }
    }
}
