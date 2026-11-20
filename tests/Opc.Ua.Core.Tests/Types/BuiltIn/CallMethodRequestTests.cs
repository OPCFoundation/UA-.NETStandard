/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

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
            Assert.That(request.InputArguments.IsNull, Is.False);
            Assert.That(request.InputArguments.Count, Is.Zero);
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
            var context = ServiceMessageContext.Create(telemetry);
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
            Assert.That(decodedRequest.InputArguments.IsNull, Is.False);
            Assert.That(decodedRequest.InputArguments.Count, Is.Zero);
        }

        /// <summary>
        /// Verify that InputArguments with values are correctly encoded and decoded.
        /// </summary>
        [Test]
        public void InputArgumentsWithValuesEncodeDecode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.Create(telemetry);
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

            Assert.That(decodedRequest.InputArguments.IsNull, Is.False);
            Assert.That(decodedRequest.InputArguments.Count, Is.EqualTo(2));
            Assert.That(decodedRequest.InputArguments[0].GetInt32(), Is.EqualTo(42));
            Assert.That(decodedRequest.InputArguments[1].GetString(), Is.EqualTo("test"));
        }
    }
}
