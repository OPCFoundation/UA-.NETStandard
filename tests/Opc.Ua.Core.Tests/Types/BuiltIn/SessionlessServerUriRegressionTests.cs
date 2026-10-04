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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    [TestFixture]
    [Category("BuiltIn")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionlessServerUriRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DefaultContextRoundTripPreservesLocalExpandedNodeId(bool json)
        {
            var source = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var destination = ServiceMessageContext.Create(NUnitTelemetryContext.Create());

            SessionLessServiceMessage decoded = RoundTrip(json, source, destination);

            Assert.That(decoded.Message, Is.TypeOf<BrowseResponse>());
            var response = (BrowseResponse)decoded.Message;
            Assert.That(response.Results[0].References[0].NodeId, Is.EqualTo(new ExpandedNodeId(42)));
            Assert.That(response.Results[0].References[0].TypeDefinition.ServerIndex, Is.Zero);
            Assert.That(decoded.ServerUris.Count, Is.EqualTo(1));
            Assert.That(decoded.ServerUris.GetString(0), Is.Empty);
            Assert.That(destination.ServerUris.Count, Is.Zero, "Decoding must not invent a local application URI.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemoteServerUriRoundTripPreservesReservedLocalIndex(bool json)
        {
            var source = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            source.ServerUris = new StringTable(["urn:source", "urn:remote"]);
            var destination = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            destination.ServerUris = new StringTable(["urn:destination", "urn:unrelated", "urn:remote"]);

            SessionLessServiceMessage decoded = RoundTrip(json, source, destination, 1);

            Assert.That(decoded.Message, Is.TypeOf<BrowseResponse>());
            var response = (BrowseResponse)decoded.Message;
            Assert.That(
                response.Results[0].References[0].NodeId,
                Is.EqualTo(new ExpandedNodeId(42).WithServerIndex(2)));
            Assert.That(response.Results[0].References[0].TypeDefinition.ServerIndex, Is.Zero);
            Assert.That(decoded.ServerUris.Count, Is.EqualTo(2));
            Assert.That(decoded.ServerUris.GetString(0), Is.EqualTo("urn:destination"));
            Assert.That(decoded.ServerUris.GetString(1), Is.EqualTo("urn:remote"));
            Assert.That(destination.ServerUris.Count, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemoteServerUrisRequireLocalContextUri(bool json)
        {
            var source = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            source.ServerUris = new StringTable(["urn:source", "urn:remote"]);
            var destination = ServiceMessageContext.Create(NUnitTelemetryContext.Create());

            Assert.That(
                () => RoundTrip(json, source, destination, 1),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError)
                    .And.Message.EqualTo("The decoder context has no local server URI."));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyRemoteServerUriIsRejected(bool json)
        {
            var source = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            source.ServerUris = new StringTable(["urn:source", string.Empty]);
            var destination = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            destination.ServerUris.Append("urn:destination");

            Assert.That(
                () => RoundTrip(json, source, destination),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError)
                    .And.Message.EqualTo("ServerUris contains an empty URI."));
        }

        private static SessionLessServiceMessage RoundTrip(
            bool json,
            ServiceMessageContext source,
            IServiceMessageContext destination,
            uint serverIndex = 0)
        {
            var message = new SessionLessServiceMessage
            {
                NamespaceUris = source.NamespaceUris,
                ServerUris = source.ServerUris,
                Message = new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(42).WithServerIndex(serverIndex),
                                    TypeDefinition = new ExpandedNodeId(58)
                                }
                            ]
                        }
                    ]
                }
            };
            var decoded = new SessionLessServiceMessage();
            if (json)
            {
                using var encoder = new JsonEncoder(source, JsonEncoderOptions.Verbose);
                message.Encode(encoder);
                using var decoder = new JsonDecoder(encoder.CloseAndReturnText(), destination);
                decoded.Decode(decoder);
            }
            else
            {
                using var encoder = new BinaryEncoder(source);
                message.Encode(encoder);
                using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer(), destination);
                decoded.Decode(decoder);
            }
            return decoded;
        }
    }
}
