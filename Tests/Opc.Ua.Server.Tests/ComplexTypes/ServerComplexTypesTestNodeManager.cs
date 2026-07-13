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
using System.IO;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A minimal node manager that loads the synthetic
    /// <c>ServerComplexTypesTestModel.NodeSet2.xml</c> from an embedded
    /// resource at runtime. The custom DataTypes in that NodeSet have no
    /// compiled .NET backing, so the server must build dynamic stand-in
    /// encodeables from their <c>DataTypeDefinition</c> attributes. This is the
    /// integration fixture for the server side complex type system.
    /// </summary>
    internal sealed class ServerComplexTypesTestNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// The namespace URI of the runtime-loaded test model.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/ServerComplexTypesTest/";

        /// <summary>
        /// Identifier of the runtime <c>TestColor</c> enumeration DataType.
        /// </summary>
        public const uint TestColorDataType = 15001;

        /// <summary>
        /// Identifier of the runtime <c>TestPoint</c> structure DataType.
        /// </summary>
        public const uint TestPointDataType = 15010;

        /// <summary>
        /// Identifier of the <c>TestPoint</c> Default Binary encoding node.
        /// </summary>
        public const uint TestPointBinaryEncoding = 15011;

        /// <summary>
        /// Identifier of the <c>PointValue</c> variable (of type TestPoint).
        /// </summary>
        public const uint PointValueVariable = 15021;

        /// <summary>
        /// Identifier of the <c>ColorValue</c> variable (of type TestColor).
        /// </summary>
        public const uint ColorValueVariable = 15022;

        /// <summary>
        /// Initializes the node manager for the test namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        public ServerComplexTypesTestNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, NamespaceUri)
        {
        }

        /// <inheritdoc/>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            var predefinedNodes = new NodeStateCollection();

            using Stream stream = typeof(ServerComplexTypesTestNodeManager).Assembly
                .GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded NodeSet2 resource '{ResourceName}' was not found.");

            var nodeSet = Export.UANodeSet.Read(stream);

            // Ensure the model namespaces exist in the server namespace table so
            // the imported node ids map to the correct server indexes.
            if (nodeSet.NamespaceUris != null)
            {
                foreach (string namespaceUri in nodeSet.NamespaceUris)
                {
                    context.NamespaceUris.GetIndexOrAppend(namespaceUri);
                }
            }

            nodeSet.Import(context, predefinedNodes);
            return predefinedNodes;
        }

        private const string ResourceName =
            "Opc.Ua.Server.Tests.ComplexTypes.ServerComplexTypesTestModel.NodeSet2.xml";
    }
}
