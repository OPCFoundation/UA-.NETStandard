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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Extended unit tests for Session: HasExpired, IsSecureChannelValid, UpdateLocaleIds,
    /// continuation points, activation, and ValidateRequest edge cases.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public class SessionExtendedTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_server = m_fixture.Server;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        private async Task<(RequestHeader requestHeader, SecureChannelContext secureChannelContext, ISession session)>
            CreateAndActivateAsync(string sessionName)
        {
            (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                await m_server.CreateAndActivateSessionAsync(sessionName).ConfigureAwait(false);

            ISession session = m_server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken);
            Assert.That(session, Is.Not.Null, $"Session '{sessionName}' should exist after create/activate.");

            return (requestHeader, secureChannelContext, session);
        }


        [Test]
        public async Task HasExpiredReturnsFalseForFreshlyActivatedSessionAsync()
        {
            (_, _, ISession session) = await CreateAndActivateAsync("HasExpiredFresh").ConfigureAwait(false);

            Assert.That(session.HasExpired, Is.False,
                "A session that was just activated should not be expired.");
        }



        [Test]
        public async Task IsSecureChannelValidReturnsTrueForCurrentChannelIdAsync()
        {
            (_, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("IsChannelValidTrue").ConfigureAwait(false);

            Assert.That(session.IsSecureChannelValid(channelCtx.SecureChannelId), Is.True);
        }

        [Test]
        public async Task IsSecureChannelValidReturnsFalseForDifferentChannelIdAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("IsChannelValidFalse").ConfigureAwait(false);

            Assert.That(session.IsSecureChannelValid("totally-wrong-channel-id"), Is.False);
        }

        [Test]
        public async Task IsSecureChannelValidReturnsFalseForEmptyStringAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("IsChannelValidEmpty").ConfigureAwait(false);

            Assert.That(session.IsSecureChannelValid(string.Empty), Is.False);
        }



        [Test]
        public async Task ActivatedIsTrueAfterCreateAndActivateSessionAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("ActivatedFlag").ConfigureAwait(false);

            Assert.That(session.Activated, Is.True);
        }



        [Test]
        public async Task UpdateLocaleIdsReturnsTrueWhenLocalesChangeAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("UpdateLocales").ConfigureAwait(false);

            bool changed = session.UpdateLocaleIds(["en-US", "de-DE"]);

            Assert.That(changed, Is.True);
        }

        [Test]
        public async Task UpdateLocaleIdsReturnsFalseWhenSameLocalesAppliedTwiceAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("UpdateLocalesSame").ConfigureAwait(false);

            session.UpdateLocaleIds(["en-US"]);
            bool changedAgain = session.UpdateLocaleIds(["en-US"]);

            Assert.That(changedAgain, Is.False);
        }

        [Test]
        public async Task PreferredLocalesUpdatedAfterUpdateLocaleIdsAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("UpdateLocalesPreferred").ConfigureAwait(false);

            session.UpdateLocaleIds(["fr-FR", "it-IT"]);

            Assert.That(session.PreferredLocales, Is.Not.Null);
            Assert.That(session.PreferredLocales, Does.Contain("fr-FR"));
            Assert.That(session.PreferredLocales, Does.Contain("it-IT"));
        }

        [Test]
        public async Task UpdateLocaleIdsWithEmptyArrayReturnsExpectedResultAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("UpdateLocalesEmpty").ConfigureAwait(false);

            session.UpdateLocaleIds(["en-US"]);

            // Clearing to empty array should indicate a change
            bool changed = session.UpdateLocaleIds([]);

            Assert.That(changed, Is.True);
        }



        [Test]
        public async Task SaveAndRestoreContinuationPointPreservesThePointAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("SaveRestoreContinuation").ConfigureAwait(false);

            using var continuationPoint = new ContinuationPoint
            {
                Id = Guid.NewGuid()
            };

            session.SaveContinuationPoint(continuationPoint);

            byte[] idBytes = continuationPoint.Id.ToByteArray();
            ContinuationPoint restored = session.RestoreContinuationPoint(idBytes.ToByteString());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Id, Is.EqualTo(continuationPoint.Id));
        }

        [Test]
        public async Task RestoreContinuationPointReturnsNullForUnknownIdAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreUnknownContinuation").ConfigureAwait(false);

            byte[] unknownId = Guid.NewGuid().ToByteArray();
            ContinuationPoint result = session.RestoreContinuationPoint(unknownId.ToByteString());

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task RestoreContinuationPointReturnsNullForShortByteStringAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreShortContinuation").ConfigureAwait(false);

            // Continuation point IDs must be 16 bytes (Guid); shorter input returns null
            byte[] shortId = [1, 2, 3];
            ContinuationPoint result = session.RestoreContinuationPoint(shortId.ToByteString());

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task RestoreContinuationPointRemovesItFromSessionAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreRemovesContinuation").ConfigureAwait(false);

            using var cp = new ContinuationPoint { Id = Guid.NewGuid() };
            session.SaveContinuationPoint(cp);
            byte[] idBytes = cp.Id.ToByteArray();

            // First restore retrieves it
            ContinuationPoint first = session.RestoreContinuationPoint(idBytes.ToByteString());
            Assert.That(first, Is.Not.Null);

            // Second restore returns null because it was removed on first restore
            ContinuationPoint second = session.RestoreContinuationPoint(idBytes.ToByteString());
            Assert.That(second, Is.Null);
        }

        [Test]
        public async Task SaveContinuationPointThrowsForNullAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("SaveNullContinuation").ConfigureAwait(false);

            Assert.That(
                () => session.SaveContinuationPoint(continuationPoint: null),
                Throws.TypeOf<ArgumentNullException>());
        }



        [Test]
        public async Task SaveAndRestoreHistoryContinuationPointPreservesValueAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("SaveRestoreHistory").ConfigureAwait(false);

            Guid id = Guid.NewGuid();
            object value = new object();
            session.SaveHistoryContinuationPoint(id, value);

            byte[] idBytes = id.ToByteArray();
            object restored = session.RestoreHistoryContinuationPoint(idBytes.ToByteString());

            Assert.That(restored, Is.SameAs(value));
        }

        [Test]
        public async Task RestoreHistoryContinuationPointReturnsNullForUnknownIdAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreUnknownHistory").ConfigureAwait(false);

            byte[] unknownId = Guid.NewGuid().ToByteArray();
            object result = session.RestoreHistoryContinuationPoint(unknownId.ToByteString());

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task RestoreHistoryContinuationPointReturnsNullForShortByteStringAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreShortHistory").ConfigureAwait(false);

            // Must be 16 bytes (Guid size); shorter returns null
            byte[] shortBytes = [0xAB, 0xCD];
            object result = session.RestoreHistoryContinuationPoint(shortBytes.ToByteString());

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task RestoreHistoryContinuationPointRemovesItFromSessionAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("RestoreRemovesHistory").ConfigureAwait(false);

            Guid id = Guid.NewGuid();
            session.SaveHistoryContinuationPoint(id, new object());
            byte[] idBytes = id.ToByteArray();

            // First restore retrieves
            object first = session.RestoreHistoryContinuationPoint(idBytes.ToByteString());
            Assert.That(first, Is.Not.Null);

            // Second restore returns null
            object second = session.RestoreHistoryContinuationPoint(idBytes.ToByteString());
            Assert.That(second, Is.Null);
        }

        [Test]
        public async Task SaveHistoryContinuationPointThrowsForNullValueAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("SaveNullHistory").ConfigureAwait(false);

            Assert.That(
                () => session.SaveHistoryContinuationPoint(Guid.NewGuid(), continuationPoint: null),
                Throws.TypeOf<ArgumentNullException>());
        }



        [Test]
        public async Task LastContactTickCountIsPopulatedAfterActivationAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("LastContactTick").ConfigureAwait(false);

            Assert.That(session.LastContactTickCount, Is.GreaterThan(0L));
        }

        [Test]
        public async Task ClientLastContactTimeIsCloseToNowAfterActivationAsync()
        {
            DateTime before = DateTime.UtcNow.AddSeconds(-1);

            (_, _, ISession session) =
                await CreateAndActivateAsync("ClientLastContactTime").ConfigureAwait(false);

            DateTime after = DateTime.UtcNow.AddSeconds(1);

            Assert.That(session.ClientLastContactTime, Is.GreaterThan(before));
            Assert.That(session.ClientLastContactTime, Is.LessThan(after));
        }



        [Test]
        public async Task ValidateRequestThrowsBadSecureChannelIdInvalidForWrongChannelAsync()
        {
            (RequestHeader requestHeader, _, ISession session) =
                await CreateAndActivateAsync("ValidateRequestWrongChannel").ConfigureAwait(false);

            var badChannelContext = new SecureChannelContext(
                "wrong-channel-id",
                new EndpointDescription(),
                RequestEncoding.Binary);

            Assert.That(
                () => session.ValidateRequest(requestHeader, badChannelContext, RequestType.Read),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("Code")
                    .EqualTo(StatusCodes.BadSecureChannelIdInvalid));
        }

        [Test]
        public async Task ValidateRequestThrowsArgumentNullExceptionForNullRequestHeaderAsync()
        {
            (_, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("ValidateRequestNullHeader").ConfigureAwait(false);

            Assert.That(
                () => session.ValidateRequest(requestHeader: null, channelCtx, RequestType.Read),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task ValidateRequestIncrementsWriteCountDiagnosticsAsync()
        {
            (RequestHeader requestHeader, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("ValidateRequestWriteCount").ConfigureAwait(false);

            uint before = session.SessionDiagnostics.WriteCount.TotalCount;

            session.ValidateRequest(requestHeader, channelCtx, RequestType.Write);

            Assert.That(session.SessionDiagnostics.WriteCount.TotalCount,
                Is.GreaterThan(before));
        }

        [Test]
        public async Task ValidateRequestIncrementsBrowseCountDiagnosticsAsync()
        {
            (RequestHeader requestHeader, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("ValidateRequestBrowseCount").ConfigureAwait(false);

            uint before = session.SessionDiagnostics.BrowseCount.TotalCount;

            session.ValidateRequest(requestHeader, channelCtx, RequestType.Browse);

            Assert.That(session.SessionDiagnostics.BrowseCount.TotalCount,
                Is.GreaterThan(before));
        }

        [Test]
        public async Task ValidateRequestIncrementsTranslateBrowsePathsCountDiagnosticsAsync()
        {
            (RequestHeader requestHeader, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("ValidateRequestTranslateCount").ConfigureAwait(false);

            uint before = session.SessionDiagnostics.TranslateBrowsePathsToNodeIdsCount.TotalCount;

            session.ValidateRequest(requestHeader, channelCtx, RequestType.TranslateBrowsePathsToNodeIds);

            Assert.That(session.SessionDiagnostics.TranslateBrowsePathsToNodeIdsCount.TotalCount,
                Is.GreaterThan(before));
        }

        [Test]
        public async Task ValidateRequestIncrementsTotalRequestCountForAllTypesAsync()
        {
            (RequestHeader requestHeader, SecureChannelContext channelCtx, ISession session) =
                await CreateAndActivateAsync("ValidateRequestTotalCount").ConfigureAwait(false);

            uint before = session.SessionDiagnostics.TotalRequestCount.TotalCount;

            session.ValidateRequest(requestHeader, channelCtx, RequestType.Read);

            Assert.That(session.SessionDiagnostics.TotalRequestCount.TotalCount,
                Is.EqualTo(before + 1));
        }



        [Test]
        public async Task SessionDiagnosticsSessionNameMatchesProvidedNameAsync()
        {
            const string sessionName = "DiagnosticsNameTest";
            (_, _, ISession session) = await CreateAndActivateAsync(sessionName).ConfigureAwait(false);

            Assert.That(session.SessionDiagnostics.SessionName, Is.EqualTo(sessionName));
        }

        [Test]
        public async Task SessionDiagnosticsActualSessionTimeoutIsPositiveAsync()
        {
            (_, _, ISession session) =
                await CreateAndActivateAsync("DiagnosticsTimeout").ConfigureAwait(false);

            Assert.That(session.SessionDiagnostics.ActualSessionTimeout, Is.GreaterThan(0.0));
        }

    }
}
