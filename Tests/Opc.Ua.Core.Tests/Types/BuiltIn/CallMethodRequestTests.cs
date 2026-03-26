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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the CallMethodRequest class.
    /// </summary>
    [TestFixture]
    [Category("CallMethodRequest")]
    [SetCulture("en-us")]
    [Parallelizable]
    public class CallMethodRequestTests
    {
        /// <summary>
        /// Verify that InputArguments is never null after construction.
        /// </summary>
        [Test]
        public void InputArgumentsInitializedAfterConstruction()
        {
            var request = new CallMethodRequest();
            Assert.IsNotNull(request.InputArguments);
            Assert.AreEqual(0, request.InputArguments.Count);
        }

        /// <summary>
        /// Verify that InputArguments is initialized to empty collection after decode
        /// when the wire format indicates no arguments.
        /// This tests the fix for calling methods with only output parameters (no input parameters).
        /// </summary>
        [Test]
        public void InputArgumentsInitializedAfterDecode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var originalRequest = new CallMethodRequest
            {
                ObjectId = new NodeId(1000),
                MethodId = new NodeId(2000),
                InputArguments = [] // Empty collection
            };

            // Encode
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
            {
                originalRequest.Encode(encoder);
            }

            // Decode
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context);
            var decodedRequest = new CallMethodRequest();
            decodedRequest.Decode(decoder);

            // InputArguments should not be null
            Assert.IsNotNull(decodedRequest.InputArguments);
            Assert.AreEqual(0, decodedRequest.InputArguments.Count);
        }

        /// <summary>
        /// Verify that InputArguments with values are correctly encoded and decoded.
        /// </summary>
        [Test]
        public void InputArgumentsWithValuesEncodeDecode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new ServiceMessageContext(telemetry);
            var originalRequest = new CallMethodRequest
            {
                ObjectId = new NodeId(1000),
                MethodId = new NodeId(2000),
                InputArguments = [new Variant(42), new Variant("test")]
            };

            // Encode
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
            {
                originalRequest.Encode(encoder);
            }

            // Decode
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, context);
            var decodedRequest = new CallMethodRequest();
            decodedRequest.Decode(decoder);

            Assert.IsNotNull(decodedRequest.InputArguments);
            Assert.AreEqual(2, decodedRequest.InputArguments.Count);
            Assert.AreEqual(42, decodedRequest.InputArguments[0].Value);
            Assert.AreEqual("test", decodedRequest.InputArguments[1].Value);
        }
    }
}
