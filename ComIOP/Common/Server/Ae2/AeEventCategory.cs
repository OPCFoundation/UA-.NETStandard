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
    /// Stores a type declaration retrieved from a server.
    /// </summary>
    public class AeEventCategory
    {
        /// <summary>
        /// The AE event type that the category belongs to.
        /// </summary>
        public int EventType { get; set; }

        /// <summary>
        /// The proxy assigned category id for the event type.
        /// </summary>
        public uint LocalId;

        /// <summary>
        /// The UA event type node id for the category.
        /// </summary>
        public NodeId TypeId { get; set; }

        /// <summary>
        /// The UA event type node id for the supertype. 
        /// </summary>
        public NodeId SuperTypeId { get; set; }

        /// <summary>
        /// A description for the event type.
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// The fully inhierited list of instance declarations for the UA event type.
        /// </summary>
        public List<AeEventAttribute> Attributes { get; set; }
    }

}
