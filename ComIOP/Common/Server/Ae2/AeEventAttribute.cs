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
    /// Stores an instance declaration fetched from the server.
    /// </summary>
    public class AeEventAttribute
    {
        /// <summary>
        /// The proxy assigned identifier for the attribute.
        /// </summary>
        public uint LocalId;

        /// <summary>
        /// The type that the declaration belongs to.
        /// </summary>
        public NodeId RootTypeId { get; set; }

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        public QualifiedNameCollection BrowsePath { get; set; }

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        public string BrowsePathDisplayText { get; set; }

        /// <summary>
        /// A localized path to the instance declaration.
        /// </summary>
        public string DisplayPath { get; set; }

        /// <summary>
        /// The node id for the instance declaration.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// The node class of the instance declaration.
        /// </summary>
        public NodeClass NodeClass { get; set; }

        /// <summary>
        /// The browse name for the instance declaration.
        /// </summary>
        public QualifiedName BrowseName { get; set; }

        /// <summary>
        /// The display name for the instance declaration.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The description for the instance declaration.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The modelling rule for the instance declaration (i.e. Mandatory or Optional).
        /// </summary>
        public NodeId ModellingRule { get; set; }

        /// <summary>
        /// The data type for the instance declaration.
        /// </summary>
        public NodeId DataType { get; set; }

        /// <summary>
        /// The value rank for the instance declaration.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// The built-in type parent for the data type.
        /// </summary>
        public BuiltInType BuiltInType { get; set; }

        /// <summary>
        /// An instance declaration that has been overridden by the current instance.
        /// </summary>
        public AeEventAttribute OverriddenDeclaration { get; set; }

        /// <summary>
        /// The attribute is not visible to clients.
        /// </summary>
        public bool Hidden { get; set; }
    }
}
