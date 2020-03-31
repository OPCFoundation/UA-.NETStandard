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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Stores information about an HDA read request.
    /// </summary>
    public class HdaReadRequest
    {
        /// <summary>
        /// The handle for the requested item.
        /// </summary>
        public HdaItemHandle Handle;

        /// <summary>
        /// The node id to read.
        /// </summary>
        public NodeId NodeId;

        /// <summary>
        /// The client handle.
        /// </summary>
        public int ClientHandle; 

        /// <summary>
        /// The attribute being read.
        /// </summary>
        public uint AttributeId;

        /// <summary>
        /// The aggregate used to calculate the results.
        /// </summary>
        public uint AggregateId;

        /// <summary>
        /// Any error associated with the item.
        /// </summary>
        public int Error;

        /// <summary>
        /// Any error associated with the item.
        /// </summary>
        public List<DaValue> Values;

        /// <summary>
        /// Metadata associated with the values.
        /// </summary>
        public List<ModificationInfo> ModificationInfos;

        /// <summary>
        /// A continuation point returned by the server.
        /// </summary>
        public byte[] ContinuationPoint;

        /// <summary>
        /// A flag that indicates that all data has been read.
        /// </summary>
        public bool IsComplete;
    }
}
