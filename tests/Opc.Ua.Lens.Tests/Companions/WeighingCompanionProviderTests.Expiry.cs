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
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Scales = Opc.Ua.Scales;

namespace UaLens.Tests.Companions
{
    public sealed partial class WeighingCompanionProviderTests
    {
        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        public async Task GrantExpiryIsEnforcedDuringPreparationAsync(long ticksFromExpiry)
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            DateTimeOffset deadline = session.Grant.ExpiresAt;
            session.UtcNow = deadline.AddTicks(ticksFromExpiry);

            if (ticksFromExpiry < 0)
            {
                CompanionTaskInput prepared = await provider.PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
                await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
                Assert.That(session.Services.Calls[0].ObjectId, Is.EqualTo(target.NodeId));
            }
            else
            {
                await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                        session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("expired"))
                    .ConfigureAwait(false);
                Assert.That(session.Services.Calls.IsEmpty, Is.True);
            }
            Assert.That(session.Grant.ExpiresAt, Is.EqualTo(deadline));
        }

        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        public async Task GrantExpiryIsEnforcedDuringDispatchAsync(long ticksFromExpiry)
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            DateTimeOffset deadline = session.Grant.ExpiresAt;
            session.UtcNow = deadline.AddTicks(ticksFromExpiry);

            if (ticksFromExpiry < 0)
            {
                CompanionOperationResult result = await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
                Assert.That(result.Summary, Does.Contain("accepted"));
                session.Policy.Verify(policy => policy.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), target, "set-tare", It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
            else
            {
                await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                        session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("expired"))
                    .ConfigureAwait(false);
                Assert.That(session.Services.Calls.IsEmpty, Is.True);
                session.Policy.Verify(policy => policy.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), target, "set-tare", It.IsAny<CancellationToken>()), Times.Once);
            }
            Assert.That(session.Grant.ExpiresAt, Is.EqualTo(deadline));
        }

        [Test]
        public async Task GrantExpiryDuringPreparationReadsPreventsReturningAnAuthorizedPreparationAsync()
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            NodeId weight = session.Resolve(target.NodeId, Scales.BrowseNames.CurrentWeight);
            session.Services.ReadHandler = (read, _) =>
            {
                if (read.NodeId == weight && read.AttributeId == Attributes.Value)
                {
                    session.UtcNow = session.Grant.ExpiresAt;
                }
                return session.Services.Read(read).WithSourceTimestamp(session.Timestamp);
            };

            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("expired")).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            session.Policy.Verify(policy => policy.AuthorizeAsync(
                It.IsAny<CompanionContext>(), target, "set-tare", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GrantExpiryImmediatelyBeforeSendPreventsMethodDispatchAsync()
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            var progress = new Mock<IProgress<CompanionTaskProgress>>(MockBehavior.Strict);
            progress.Setup(value => value.Report(It.IsAny<CompanionTaskProgress>()))
                .Callback(() => session.UtcNow = session.Grant.ExpiresAt);

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, progress.Object, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("expired")).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            session.Policy.Verify(policy => policy.AuthorizeAsync(
                It.IsAny<CompanionContext>(), target, "set-tare", It.IsAny<CancellationToken>()), Times.Exactly(2));
            session.Transport.Verify(value => value.Dispose(), Times.Never);
        }

        [Test]
        public async Task ChangedGrantDeadlineCannotRenewAnExistingPreparationAsync()
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            WeighingSimulationGrant original = session.Grant;
            session.Grant = original with { ExpiresAt = original.ExpiresAt.AddMinutes(5) };

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("grant changed"))
                .ConfigureAwait(false);

            Assert.That(session.Grant.RunId, Is.EqualTo(original.RunId));
            Assert.That(session.Grant.Revision, Is.EqualTo(original.Revision));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ReauthorizationReturningAnExpiredGrantCannotReachTheServerAsync()
        {
            (WeighingTestSession session, CompanionTarget target, WeighingCompanionProvider provider) = ExpiryCase();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            session.Grant = session.Grant with { ExpiresAt = session.UtcNow };

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("expired")).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            session.Policy.Verify(policy => policy.AuthorizeAsync(
                It.IsAny<CompanionContext>(), target, "set-tare", It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        private static (WeighingTestSession, CompanionTarget, WeighingCompanionProvider) ExpiryCase()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            return (session, target, session.Provider());
        }
    }
}
