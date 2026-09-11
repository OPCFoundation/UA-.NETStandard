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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.AuditRegressions
{
    /// <summary>
    /// Regressions for the client session defects reported by the
    /// Opc.Ua.Client audit. Each test names the defect it pins down.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("AuditRegression")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionAuditRegressionTests
    {
        /// <summary>
        /// The last partial add batch used to slice
        /// <c>operationLimit - batch.Count</c> entries off the remove list
        /// without clamping to its length, so a call with more adds than
        /// removes threw ArgumentOutOfRangeException.
        /// </summary>
        [Test]
        public async Task SetTriggeringDoesNotSliceRemovesPastTheEndAsync()
        {
            const uint subscriptionId = 1u;
            const uint triggeringItemId = 1u;

            // Limit 10, 15 adds, 2 removes: the second batch has room for 5
            // removes but only 2 exist.
            var linksToAdd = new List<uint>([.. Enumerable.Repeat(1u, 15)]);
            var linksToRemove = new List<uint>([.. Enumerable.Repeat(2u, 2)]);

            using SessionMock sessionMock = SessionMock.Create();
            sessionMock.OperationLimits.MaxMonitoredItemsPerCall = 10;

            sessionMock.Channel
                .SetupSequence(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is SetTriggeringRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = Enumerable.Repeat(StatusCodes.Good, 10).ToArrayOf()
                })
                .ReturnsAsync(new SetTriggeringResponse
                {
                    AddResults = Enumerable.Repeat(StatusCodes.Good, 5).ToArrayOf(),
                    RemoveResults = Enumerable.Repeat(StatusCodes.Good, 2).ToArrayOf()
                });

            SetTriggeringResponse response = await sessionMock.SetTriggeringAsync(
                null,
                subscriptionId,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.AddResults.Count, Is.EqualTo(15));
                Assert.That(response.RemoveResults.Count, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Snapshot(out SessionState) used to build the state through the
        /// SessionOptions copy constructor, which drops every member declared
        /// on SessionState itself, and Restore(SessionState) downcast to
        /// SessionConfiguration - so the paired API could not round-trip.
        /// </summary>
        [Test]
        public void SessionStateSnapshotRoundTripsThroughRestore()
        {
            using SessionMock source = SessionMock.Create();
            source.Restore(SeedConfiguration());
            source.Snapshot(out SessionState state);

            using SessionMock target = SessionMock.Create();
            Assert.That(
                () => target.Restore(state),
                Throws.Nothing,
                "Restore(SessionState) must accept what Snapshot(out SessionState) produced");
            Assert.That(
                target.SessionId,
                Is.EqualTo(new NodeId("session", 3)),
                "the round trip must carry the session identity across");
        }

        /// <summary>
        /// Snapshot(out SessionState) must preserve the members declared on
        /// SessionState - the record copy constructor of the SessionOptions
        /// base silently dropped them.
        /// </summary>
        [Test]
        public void SessionStateSnapshotKeepsSessionScopedMembers()
        {
            using SessionMock source = SessionMock.Create();
            source.Restore(SeedConfiguration());

            source.Snapshot(out SessionState state);

            Assert.Multiple(() =>
            {
                Assert.That(state.SessionId, Is.EqualTo(new NodeId("session", 3)));
                Assert.That(
                    state.AuthenticationToken,
                    Is.EqualTo(new NodeId("token", 3)));
                Assert.That(state.ServerNonce, Is.EqualTo(ByteString.From([1, 2, 3])));
                Assert.That(state.ClientNonce, Is.EqualTo(ByteString.From([4, 5, 6])));
                Assert.That(
                    state.UserIdentityTokenPolicy,
                    Is.EqualTo(SecurityPolicies.Basic256Sha256));
            });
        }

        /// <summary>
        /// A configuration with every session-scoped member set to something
        /// other than its default, so a snapshot that silently drops them is
        /// visible.
        /// </summary>
        private static SessionConfiguration SeedConfiguration()
        {
            return new SessionConfiguration
            {
                SessionName = "audit",
                SessionId = new NodeId("session", 3),
                AuthenticationToken = new NodeId("token", 3),
                ServerNonce = ByteString.From([1, 2, 3]),
                ClientNonce = ByteString.From([4, 5, 6]),
                UserIdentityTokenPolicy = SecurityPolicies.Basic256Sha256
            };
        }

        /// <summary>
        /// Equals compared the session's ConfiguredEndpoint against the other
        /// session's channel EndpointDescription, which is never equal - so a
        /// session did not even equal a session on the same endpoint.
        /// </summary>
        [Test]
        public void EqualsComparesConfiguredEndpoints()
        {
            using SessionMock template = SessionMock.Create();

            // The clone carries the same configured endpoint instance, the same
            // session name and the same (unassigned) session id, so it must
            // compare equal.
            using Session clone = template.CloneSession(
                template.TransportChannel,
                copyEventHandlers: false);

            Assert.That(
                clone,
                Is.EqualTo(template),
                "comparing a ConfiguredEndpoint against an EndpointDescription " +
                "is never equal, so no two sessions ever compared equal");
        }

        /// <summary>
        /// The template constructor shared the template's default subscription
        /// with the clone; both sessions dispose it, so the second disposal hit
        /// an already disposed instance.
        /// </summary>
        [Test]
        public void CloneDoesNotShareTheDefaultSubscriptionWithItsTemplate()
        {
            using SessionMock template = SessionMock.Create();
            Subscription templateDefault = template.DefaultSubscription;

            using Session clone = template.CloneSession(
                template.TransportChannel,
                copyEventHandlers: false);

            Assert.That(
                clone.DefaultSubscription,
                Is.Not.SameAs(templateDefault),
                "a shared default subscription is disposed twice");
        }

        /// <summary>
        /// ReadValuesAsync(expectedTypes) reported BadTypeMismatch for every
        /// failed node because it compared the (absent) value's type before
        /// looking at the per-node read error.
        /// </summary>
        [Test]
        public async Task ReadValuesWithExpectedTypesReportsTheRealPerNodeErrorAsync()
        {
            using SessionMock sessionMock = SessionMock.Create();

            sessionMock.Channel
                .Setup(c => c.SendRequestAsync(
                    It.Is<IServiceRequest>(r => r is ReadRequest),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied)
                    ],
                    DiagnosticInfos = []
                });

            (_, ArrayOf<ServiceResult> errors) = await sessionMock
                .ReadValuesAsync(
                    new NodeId[] { new("Node", 2) }.ToArrayOf(),
                    new[] { TypeInfo.Scalars.Int32 }.ToArrayOf(),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(
                errors[0].StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "masking the read error as BadTypeMismatch hides why the read failed");
        }
    }
}
