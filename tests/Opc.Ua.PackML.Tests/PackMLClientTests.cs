// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
