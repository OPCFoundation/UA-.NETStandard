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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Verifies explicit instance writes independently of generated model access declarations and resolution.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class ClientVariableInstanceWriteTests
    {
        /// <summary>
        /// Creates a statically read-only view over a strict session without any browse setup.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            var proxy = new FileTypeClient(
                m_session.Object, new NodeId("owner", 2), NUnitTelemetryContext.Create());
            m_encodings = 0;
            m_variable = new ClientVariable<string>(
                proxy, Namespaces.OpcUa, BrowseNames.MimeType, false,
                static (value, _) => value.TryGetValue(out string? text) && text is not null
                    ? text : throw new ServiceResultException(StatusCodes.BadTypeMismatch),
                value =>
                {
                    m_encodings++;
                    return Variant.From(value);
                });
        }

        /// <summary>
        /// The ordinary operation retains the model guard, whereas the explicit operation uses the supplied node.
        /// </summary>
        [Test]
        public async Task ExplicitInstanceWriteUsesCodecAndResolvedNodeWithoutChangingStaticAccessAsync()
        {
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_variable.WriteAsync("ordinary"));
            Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
            Assert.That(m_encodings, Is.Zero);
            m_session.VerifyNoOtherCalls();
            using var cancellation = new CancellationTokenSource();
            NodeId node = new("resolved", 4);
            m_session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), cancellation.Token))
                .Returns((RequestHeader? _, ArrayOf<WriteValue> writes, CancellationToken _) =>
                {
                    Assert.That(writes.Count, Is.EqualTo(1));
                    Assert.That(writes[0].NodeId, Is.EqualTo(node));
                    Assert.That(writes[0].AttributeId, Is.EqualTo(Attributes.Value));
                    Assert.That(writes[0].Value.WrappedValue, Is.EqualTo(Variant.From("instance")));
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.Good]
                    });
                });
            await m_variable.WriteInstanceAsync(node, "instance", cancellation.Token);
            Assert.That(m_variable.Writable, Is.False);
            Assert.That(m_encodings, Is.EqualTo(1));
            VerifyOneWrite();
        }

        /// <summary>
        /// An uncertain or bad service/operation status is never reported as an accepted instance write.
        /// </summary>
        [TestCase("uncertain", false)]
        [TestCase("denied", false)]
        [TestCase("uncertain", true)]
        [TestCase("closed", true)]
        public async Task InstanceWriteRejectsNonGoodServiceAndOperationStatusesAsync(string scenario, bool header)
        {
            StatusCode status = scenario switch
            {
                "uncertain" => StatusCodes.Uncertain,
                "denied" => StatusCodes.BadUserAccessDenied,
                "closed" => StatusCodes.BadSessionClosed,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            SetupResponse(new WriteResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = header ? status : StatusCodes.Good },
                Results = [header ? StatusCodes.Good : status]
            });
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_variable.WriteInstanceAsync(new NodeId("resolved", 4), "value"));
            Assert.That(failure!.StatusCode, Is.EqualTo(status));
            VerifyOneWrite();
        }

        /// <summary>
        /// Missing responses and malformed result or diagnostic cardinalities are explicit errors.
        /// </summary>
        [TestCase("missing-response")]
        [TestCase("missing-result")]
        [TestCase("extra-result")]
        [TestCase("extra-diagnostic")]
        public async Task InstanceWriteRejectsMalformedResponsesAsync(string shape)
        {
            var response = new WriteResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [StatusCodes.Good]
            };
            switch (shape)
            {
                case "missing-response":
                    response = null!;
                    break;
                case "missing-result":
                    response.Results = [];
                    break;
                case "extra-result":
                    response.Results = [StatusCodes.Good, StatusCodes.Good];
                    break;
                case "extra-diagnostic":
                    response.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
                    break;
            }
            SetupResponse(response);
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_variable.WriteInstanceAsync(new NodeId("resolved", 4), "value"));
            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)(shape == "extra-diagnostic"
                ? StatusCodes.BadUnexpectedError : StatusCodes.BadDecodingError)));
            VerifyOneWrite();
        }

        /// <summary>
        /// Generated response headers normalize null assignments before the write helper receives them.
        /// </summary>
        [Test]
        public async Task InstanceWriteAcceptsNormalizedResponseHeaderAsync()
        {
            var response = new WriteResponse
            {
                ResponseHeader = null!,
                Results = [StatusCodes.Good]
            };
            ResponseHeader header = response.ResponseHeader
                ?? throw new AssertionException("The generated response did not normalize its header.");
            Assert.That(header.ServiceResult, Is.EqualTo((StatusCode)StatusCodes.Good));
            SetupResponse(response);

            await m_variable.WriteInstanceAsync(new NodeId("resolved", 4), "value");

            Assert.That(m_encodings, Is.EqualTo(1));
            Assert.That(m_variable.Writable, Is.False);
            VerifyOneWrite();
        }

        /// <summary>
        /// Invalid nodes and cancellation stop before codec invocation or network activity.
        /// </summary>
        [Test]
        public async Task InstanceWriteRejectsNullNodeAndCancellationBeforeEncodingAsync()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await m_variable.WriteInstanceAsync(NodeId.Null, "value"));
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await m_variable.WriteInstanceAsync(
                    new NodeId("resolved", 4), "value", new CancellationToken(true)));
            Assert.That(m_encodings, Is.Zero);
            m_session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Cancellation raised during encoding prevents the service dispatch.
        /// </summary>
        [Test]
        public async Task InstanceWriteHonorsCancellationAfterEncodingAsync()
        {
            using var cancellation = new CancellationTokenSource();
            var proxy = new FileTypeClient(
                m_session.Object, new NodeId("owner", 2), NUnitTelemetryContext.Create());
            var variable = new ClientVariable<string>(
                proxy, Namespaces.OpcUa, BrowseNames.MimeType, false,
                static (value, _) => value.TryGetValue(out string? text) && text is not null
                    ? text : throw new ServiceResultException(StatusCodes.BadTypeMismatch),
                value =>
                {
                    m_encodings++;
                    cancellation.Cancel();
                    return Variant.From(value);
                });
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await variable.WriteInstanceAsync(
                    new NodeId("resolved", 4), "value", cancellation.Token));
            Assert.That(m_encodings, Is.EqualTo(1));
            m_session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Supplies a single response without permitting any resolution request.
        /// </summary>
        private void SetupResponse(WriteResponse response)
        {
            m_session.Setup(session => session.WriteAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
        }

        /// <summary>
        /// Proves that no browse, read, retry or other session operation was used.
        /// </summary>
        private void VerifyOneWrite()
        {
            m_session.Verify(session => session.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_session.VerifyNoOtherCalls();
        }

        /// <summary>The strict caller-owned session.</summary>
        private Mock<ISessionClient> m_session = null!;

        /// <summary>The statically read-only variable view.</summary>
        private ClientVariable<string> m_variable = null!;

        /// <summary>The exact codec invocation count.</summary>
        private int m_encodings;
    }
}
