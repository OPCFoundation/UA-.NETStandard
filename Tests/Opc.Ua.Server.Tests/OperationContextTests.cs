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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("OperationContext")]
    [Parallelizable]
    public class OperationContextTests
    {
        [Test]
        public void ConstructorWithRequestHeaderThrowsWhenRequestHeaderNull()
        {
            using var lifetime = new RequestLifetime();
            Assert.That(
                () => new OperationContext(null!, null, RequestType.Read, lifetime),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithRequestHeaderSetsProperties()
        {
            var header = new RequestHeader
            {
                RequestHandle = 7,
                ReturnDiagnostics = (uint)DiagnosticsMasks.All,
                AuditEntryId = "audit-1",
                TimeoutHint = 0
            };
            using var lifetime = new RequestLifetime();

            var context = new OperationContext(header, null, RequestType.Write, lifetime);

            Assert.That(context.RequestType, Is.EqualTo(RequestType.Write));
            Assert.That(context.ClientHandle, Is.EqualTo(7u));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.All));
            Assert.That(context.AuditEntryId, Is.EqualTo("audit-1"));
            Assert.That(context.Session, Is.Null);
            Assert.That(context.ChannelContext, Is.Null);
            Assert.That(context.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(context.RequestId, Is.GreaterThan(0u));
            Assert.That(context.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void ConstructorWithRequestHeaderSetsDeadlineWhenTimeoutHintProvided()
        {
            var header = new RequestHeader
            {
                RequestHandle = 1,
                TimeoutHint = 5000
            };
            using var lifetime = new RequestLifetime();

            var before = DateTime.UtcNow;
            var context = new OperationContext(header, null, RequestType.Read, lifetime);
            var after = DateTime.UtcNow;

            Assert.That(context.OperationDeadline, Is.GreaterThan(before));
            Assert.That(context.OperationDeadline, Is.LessThanOrEqualTo(after.AddMilliseconds(5000)));
        }

        [Test]
        public void ConstructorWithRequestHeaderAcceptsIdentity()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            var mockIdentity = new Mock<IUserIdentity>();

            var context = new OperationContext(header, null, RequestType.Read, lifetime, mockIdentity.Object);

            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
        }

        [Test]
        public void ConstructorWithSessionThrowsWhenRequestHeaderNull()
        {
            var mockSession = new Mock<ISession>();
            using var lifetime = new RequestLifetime();
            Assert.That(
                () => new OperationContext(null!, null!, RequestType.Read, lifetime, mockSession.Object),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithSessionThrowsWhenSessionNull()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            Assert.That(
                () => new OperationContext(header, null!, RequestType.Read, lifetime, (ISession)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithSessionSetsPropertiesFromSession()
        {
            var mockSession = new Mock<ISession>();
            var mockIdentity = new Mock<IUserIdentity>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(mockIdentity.Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(new[] { "en-US", "de-DE" });
            mockSession.Setup(s => s.Id).Returns(new NodeId(42));

            var header = new RequestHeader
            {
                RequestHandle = 5,
                ReturnDiagnostics = (uint)DiagnosticsMasks.ServiceSymbolicId,
                AuditEntryId = "audit-2",
                TimeoutHint = 3000
            };
            using var lifetime = new RequestLifetime();

            var context = new OperationContext(header, null!, RequestType.Browse, lifetime, mockSession.Object);

            Assert.That(context.Session, Is.SameAs(mockSession.Object));
            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(context.PreferredLocales.ToArray(), Is.EqualTo(new[] { "en-US", "de-DE" }));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));
            Assert.That(context.RequestType, Is.EqualTo(RequestType.Browse));
            Assert.That(context.SessionId, Is.EqualTo(new NodeId(42)));
            Assert.That(context.OperationDeadline, Is.Not.EqualTo(DateTime.MaxValue));
        }

        [Test]
        public void ConstructorWithSessionDiagnosticsMaskSetsProperties()
        {
            var mockSession = new Mock<ISession>();
            var mockIdentity = new Mock<IUserIdentity>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(mockIdentity.Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(new[] { "fr-FR" });

            var context = new OperationContext(mockSession.Object, DiagnosticsMasks.OperationAll);

            Assert.That(context.Session, Is.SameAs(mockSession.Object));
            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.OperationAll));
            Assert.That(context.RequestType, Is.EqualTo(RequestType.Unknown));
            Assert.That(context.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(context.ChannelContext, Is.Null);
        }

        [Test]
        public void ConstructorWithSessionDiagnosticsMaskThrowsWhenSessionNull()
        {
            Assert.That(
                () => new OperationContext((ISession)null!, DiagnosticsMasks.All),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithMonitoredItemThrowsWhenNull()
        {
            Assert.That(
                () => new OperationContext((IMonitoredItem)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithMonitoredItemSetsPropertiesFromSession()
        {
            var mockSession = new Mock<ISession>();
            var mockIdentity = new Mock<IUserIdentity>();
            mockSession.Setup(s => s.Identity).Returns(mockIdentity.Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(new[] { "ja-JP" });
            mockSession.Setup(s => s.Id).Returns(new NodeId(99));

            var mockItem = new Mock<IMonitoredItem>();
            mockItem.Setup(m => m.Session).Returns(mockSession.Object);
            mockItem.Setup(m => m.EffectiveIdentity).Returns((IUserIdentity)null!);

            var context = new OperationContext(mockItem.Object);

            Assert.That(context.Session, Is.SameAs(mockSession.Object));
            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(context.PreferredLocales.ToArray(), Is.EqualTo(new[] { "ja-JP" }));
            Assert.That(context.SessionId, Is.EqualTo(new NodeId(99)));
            Assert.That(context.RequestType, Is.EqualTo(RequestType.Unknown));
            Assert.That(context.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.SymbolicId));
        }

        [Test]
        public void ConstructorWithMonitoredItemUsesEffectiveIdentityWhenNoSession()
        {
            var mockIdentity = new Mock<IUserIdentity>();
            var mockItem = new Mock<IMonitoredItem>();
            mockItem.Setup(m => m.Session).Returns((ISession)null!);
            mockItem.Setup(m => m.EffectiveIdentity).Returns(mockIdentity.Object);

            var context = new OperationContext(mockItem.Object);

            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(context.Session, Is.Null);
            Assert.That(context.SessionId.IsNull, Is.True);
        }

        [Test]
        public void SecurityPolicyUriReturnsNullWhenNoChannelContext()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();

            var context = new OperationContext(header, null, RequestType.Read, lifetime);

            Assert.That(context.SecurityPolicyUri, Is.Null);
        }

        [Test]
        public void OperationStatusReflectsRequestLifetimeStatus()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            var context = new OperationContext(header, null, RequestType.Read, lifetime);

            Assert.That(context.OperationStatus, Is.EqualTo(StatusCodes.Good));

            lifetime.TryCancel(StatusCodes.BadTimeout);

            Assert.That(context.OperationStatus, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public void CancellationTokenReflectsRequestLifetime()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            var context = new OperationContext(header, null, RequestType.Read, lifetime);

            Assert.That(context.CancellationToken.IsCancellationRequested, Is.False);

            lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient);

            Assert.That(context.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void StringTableIsNotNull()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            var context = new OperationContext(header, null, RequestType.Read, lifetime);

            Assert.That(context.StringTable, Is.Not.Null);
        }

        [Test]
        public void RequestLifetimePropertyReturnsConfiguredLifetime()
        {
            var header = new RequestHeader { RequestHandle = 1, TimeoutHint = 0 };
            using var lifetime = new RequestLifetime();
            var context = new OperationContext(header, null, RequestType.Read, lifetime);

            Assert.That(context.RequestLifetime, Is.SameAs(lifetime));
        }
    }
}
