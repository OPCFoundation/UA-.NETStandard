/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.PackML.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.PackML.Tests
{
    /// <summary>
    /// Tests how the PackML client handles browse-path status codes.
    /// </summary>
    [TestFixture]
    [Category("PackML")]
    public sealed class PackMLClientTests
    {
        /// <summary>
        /// Preserves translation errors instead of treating them as missing fields or methods.
        /// </summary>
        /// <param name="invalidBrowseName">Whether the server returned BadBrowseNameInvalid.</param>
        /// <param name="read">Whether to read a field instead of calling an optional method.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TranslationErrorsPreserveServerStatus(bool invalidBrowseName, bool read)
        {
            StatusCode status = invalidBrowseName
                ? StatusCodes.BadBrowseNameInvalid
                : StatusCodes.BadNodeIdUnknown;
            PackMLClient client = CreateClient(status);
            ushort namespaceIndex = checked((ushort)client.Session.NamespaceUris.GetIndex(Namespaces.PackML));
            var unit = new NodeId("Unit", namespaceIndex);

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (read)
                {
                    await client.ReadAsync(unit, PackMLFields.Status.CurMachSpeed);
                }
                else
                {
                    await client.RemoteCommandAsync(unit, []);
                }
            }))!;

            Assert.That(error.StatusCode, Is.EqualTo(status));
        }

        /// <summary>
        /// Reports a missing field without attempting a Read when translation returns BadNoMatch.
        /// </summary>
        [Test]
        public async Task NoMatchReportsAnAbsentField()
        {
            PackMLClient client = CreateClient(StatusCodes.BadNoMatch);
            ushort namespaceIndex = checked((ushort)client.Session.NamespaceUris.GetIndex(Namespaces.PackML));
            var unit = new NodeId("Unit", namespaceIndex);

            PackMLValue<float> field = await client.ReadAsync(unit, PackMLFields.Status.CurMachSpeed);

            Assert.That(field.NodeId.IsNull, Is.True);
            Assert.That(field.IsPresent, Is.False);
            Assert.That(field.HasValue, Is.False);
        }

        /// <summary>
        /// Propagates a later translation error in a batch even when an earlier field is absent.
        /// </summary>
        /// <param name="invalidBrowseName">Whether the server returned BadBrowseNameInvalid.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BatchTranslationErrorIsNotHiddenByEarlierAbsentField(bool invalidBrowseName)
        {
            StatusCode status = invalidBrowseName
                ? StatusCodes.BadBrowseNameInvalid
                : StatusCodes.BadNodeIdUnknown;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.PackML);
            ushort namespaceIndex = checked((ushort)namespaceUris.GetIndex(Namespaces.PackML));
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris = namespaceUris;
            int requestCount = 0;
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                {
                    requestCount = paths.Count;
                    var results = new BrowsePathResult[paths.Count];
                    for (int i = 0; i < results.Length; i++)
                    {
                        results[i] = new BrowsePathResult
                        {
                            StatusCode = i == 1 ? status : StatusCodes.BadNoMatch
                        };
                    }
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf(),
                        DiagnosticInfos = default
                    };
                });
            var client = new PackMLClient(session.Object, telemetry);

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadStatusAsync(new NodeId("Unit", namespaceIndex))))!;

            Assert.That(requestCount, Is.GreaterThan(1));
            Assert.That(error.StatusCode, Is.EqualTo(status));
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Creates a session that returns one browse-path status.
        /// </summary>
        private static PackMLClient CreateClient(StatusCode status)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.PackML);
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris = namespaceUris;
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowsePathResult { StatusCode = status }],
                    DiagnosticInfos = default
                });
            return new PackMLClient(session.Object, telemetry);
        }
    }
}
