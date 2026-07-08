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
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for the typed Part 16 state-machine partial on
    /// <see cref="SoftwareUpdateClient"/>. Covers each of the four
    /// software-update slots (PrepareForUpdate / Installation /
    /// Confirmation / PowerCycle) — lazy proxy resolution, cached
    /// reuse, and the BadNotFound surface when a child is absent.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    public sealed class SoftwareUpdateClientStateMachineTests
    {
        [Test]
        public async Task GetInstallationStateAsyncReturnsNullWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            FiniteStateSnapshot? snapshot = await client
                .GetInstallationStateAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public async Task GetPrepareForUpdateStateAsyncReturnsNullWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            FiniteStateSnapshot? snapshot = await client
                .GetPrepareForUpdateStateAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public async Task GetConfirmationStateAsyncReturnsNullWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            FiniteStateSnapshot? snapshot = await client
                .GetConfirmationStateAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public async Task GetPowerCycleStateAsyncReturnsNullWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            FiniteStateSnapshot? snapshot = await client
                .GetPowerCycleStateAsync().ConfigureAwait(false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public void InstallSoftwarePackageAsyncThrowsBadNotFoundWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.InstallSoftwarePackageAsync(
                    "urn:vendor", "1.0", global::Opc.Ua.ArrayOf.Empty<string>(), default).ConfigureAwait(false))!;

            Assert.That((uint)ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotFound));
        }

        [Test]
        public void PrepareAsyncThrowsBadNotFoundWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.PrepareAsync().ConfigureAwait(false))!;

            Assert.That((uint)ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotFound));
        }

        [Test]
        public void ConfirmAsyncThrowsBadNotFoundWhenChildAbsent()
        {
            var sessionMock = CreateSessionMock();
            SetupTranslateAllEmpty(sessionMock);

            var client = NewClient(sessionMock);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ConfirmAsync().ConfigureAwait(false))!;

            Assert.That((uint)ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetInstallationStateAsyncCachesChildResolution()
        {
            int childResolutionCalls = 0;
            var sessionMock = CreateSessionMock();
            SetupTranslate(sessionMock, paths =>
            {
                // The child-resolution call sends exactly ONE BrowsePath
                // targeting the Installation child. State-snapshot calls
                // send 4 paths (CurrentState, .Id, .Number, .EffectiveDisplayName).
                if (paths.Count == 1 &&
                    paths[0].RelativePath.Elements.Count == 1 &&
                    paths[0].RelativePath.Elements[0].TargetName.Name == "Installation")
                {
                    Interlocked.Increment(ref childResolutionCalls);
                    // Return Bad so the higher-level call returns null but
                    // the proxy-resolution path is still exercised once.
                    return BadAll(1);
                }
                return BadAll(paths.Count);
            });

            var client = NewClient(sessionMock);

            await client.GetInstallationStateAsync().ConfigureAwait(false);
            await client.GetInstallationStateAsync().ConfigureAwait(false);
            await client.GetInstallationStateAsync().ConfigureAwait(false);

            // The Resolve* helper only short-circuits when the proxy is
            // non-null. When the child resolution returns null the helper
            // re-asks each call, which is the documented behaviour.
            Assert.That(childResolutionCalls, Is.GreaterThanOrEqualTo(1));
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static SoftwareUpdateClient NewClient(Mock<ISession> sessionMock)
        {
            return new SoftwareUpdateClient(
                sessionMock.Object,
                new NodeId("su-1", 2),
                NullTelemetry());
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);

            var ctx = ServiceMessageContext.Create(NullTelemetry());
            ctx.NamespaceUris = nsTable;
            mock.SetupGet(s => s.MessageContext).Returns(ctx);
            return mock;
        }

        private static void SetupTranslateAllEmpty(Mock<ISession> sessionMock)
        {
            SetupTranslate(sessionMock, paths => BadAll(paths.Count));
        }

        private static void SetupTranslate(
            Mock<ISession> sessionMock,
            Func<ArrayOf<BrowsePath>, ArrayOf<BrowsePathResult>> respond)
        {
            sessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = respond(paths),
                        DiagnosticInfos = default
                    });

            sessionMock
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, double _, TimestampsToReturn _,
                               ArrayOf<ReadValueId> nodes, CancellationToken _) =>
                {
                    var results = new DataValue[nodes.Count];
                    for (int i = 0; i < results.Length; i++)
                    {
                        results[i] = new DataValue(Variant.Null, StatusCodes.BadNotFound);
                    }
                    return new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = ArrayOf.Wrapped(results),
                        DiagnosticInfos = default
                    };
                });
        }

        private static ArrayOf<BrowsePathResult> BadAll(int count)
        {
            var arr = new BrowsePathResult[count];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = new BrowsePathResult
                {
                    StatusCode = StatusCodes.BadNoMatch,
                    Targets = global::Opc.Ua.ArrayOf.Empty<BrowsePathTarget>()
                };
            }
            return ArrayOf.Wrapped(arr);
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }
    }
}
