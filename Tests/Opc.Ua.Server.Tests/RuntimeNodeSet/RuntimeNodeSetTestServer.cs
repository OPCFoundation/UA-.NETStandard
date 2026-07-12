/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that loads the existing
    /// <c>ServerComplexTypesTestModel.NodeSet2.xml</c> embedded resource through
    /// <see cref="RuntimeNodeSetNodeManagerFactory"/> instead of the hand-written
    /// <c>ServerComplexTypesTestNodeManager</c>. This exercises the full runtime
    /// NodeSet import path including namespace registration, parent-child linking,
    /// fluent configure callbacks, and the default complex-type loading sequence.
    /// </summary>
    internal sealed class RuntimeNodeSetTestServer : ReferenceServer
    {
        /// <summary>
        /// The namespace URI of the runtime test model (reuses the complex types test model).
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/ServerComplexTypesTest/";

        /// <summary>
        /// Node identifier of the container object that holds the test variables.
        /// </summary>
        public const uint ComplexTypesTestDataObject = 15020;

        /// <summary>
        /// Node identifier of the <c>PointValue</c> variable.
        /// </summary>
        public const uint PointValueVariable = 15021;

        /// <summary>
        /// Node identifier of the <c>ColorValue</c> variable.
        /// </summary>
        public const uint ColorValueVariable = 15022;

        /// <summary>
        /// Node identifier of the <c>TestPoint</c> structure DataType.
        /// </summary>
        public const uint TestPointDataType = 15010;

        /// <summary>
        /// Node identifier of the <c>TestPoint</c> Default Binary encoding node.
        /// </summary>
        public const uint TestPointBinaryEncoding = 15011;

        /// <summary>
        /// Node identifier of the <c>TestColor</c> enumeration DataType.
        /// </summary>
        public const uint TestColorDataType = 15001;

        /// <summary>
        /// Counter incremented by the <c>OnRead</c> callback wired on
        /// <c>ColorValue</c>. Tests may read this to verify the callback fired.
        /// </summary>
        public static int ColorValueReadCallbackCount;

        private const string kResourceName =
            "Opc.Ua.Server.Tests.ComplexTypes.ServerComplexTypesTestModel.NodeSet2.xml";

        /// <summary>
        /// Initializes the server and registers the
        /// <see cref="RuntimeNodeSetNodeManagerFactory"/> that loads the embedded
        /// test NodeSet via a stream source.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context forwarded to the base server.
        /// </param>
        public RuntimeNodeSetTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            Interlocked.Exchange(ref ColorValueReadCallbackCount, 0);

            var options = new RuntimeNodeSetOptions
            {
                Sources = [RuntimeNodeSetSource.FromStream(
                    "ServerComplexTypesTestModel",
                    _ => new System.Threading.Tasks.ValueTask<Stream>(OpenTestStream()),
                    [NamespaceUri])],
                DefaultNamespaceUri = NamespaceUri,
                Configure = builder =>
                {
                    // Wire an OnRead callback on ColorValue to prove the fluent
                    // configure path and parent-child browse resolution work.
                    builder
                        .Node("ComplexTypesTestData/ColorValue")
                        .OnRead(static (ISystemContext c, NodeState n, ref Variant v) =>
                        {
                            Interlocked.Increment(ref ColorValueReadCallbackCount);
                            return ServiceResult.Good;
                        });
                }
            };

            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(options));
        }

        /// <summary>
        /// Opens the embedded test NodeSet2 resource stream.
        /// </summary>
        private static Stream OpenTestStream()
        {
            Stream stream = typeof(RuntimeNodeSetTestServer).Assembly
                .GetManifestResourceStream(kResourceName);

            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{kResourceName}' was not found.");
            }

            return stream;
        }
    }
}
