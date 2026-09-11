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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Administration;

[TestFixture]
public sealed class CertificateStoreOperationsTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SuccessfulListingCannotEraseFailedDelete(bool throws)
    {
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        store.Setup(value => value.DeleteAsync(CertStoreKind.Trusted, "missing", It.IsAny<CancellationToken>()))
            .Returns(() => throws
                ? Task.FromException<bool>(new InvalidOperationException("permission denied"))
                : Task.FromResult(false));
        store.Setup(value => value.ListAsync(CertStoreKind.Trusted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<X509Certificate2>.Empty);
        var operations = new CertificateStoreOperations(store.Object);

        await operations.DeleteAsync(CertStoreKind.Trusted, "missing").ConfigureAwait(false);
        CertificateOperationResult? failure = operations.LastResult;
        await operations.ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(false);

        Assert.That(operations.LastResult, Is.SameAs(failure));
        Assert.That(failure!.Operation, Is.EqualTo("Delete"));
        Assert.That(failure.Store, Is.EqualTo(CertStoreKind.Trusted));
        Assert.That(failure.Attempted, Is.EqualTo(1));
        Assert.That(failure.Succeeded, Is.Zero);
        Assert.That(failure.Failed, Is.EqualTo(1));
        Assert.That(failure.Detail, Does.Contain(throws ? "permission denied" : "did not complete"));
        Assert.That(operations.LoadingStatus, Is.EqualTo("Trusted: 0 certificate(s)."));
        operations.AcknowledgeResult();
        Assert.That(operations.LastResult, Is.Null);
    }

    [Test]
    public async Task BulkClearContinuesAfterFailureAndPreservesPartialResult()
    {
        using X509Certificate2 first = CreateCertificate("first", false);
        using X509Certificate2 second = CreateCertificate("second", false);
        using X509Certificate2 third = CreateCertificate("third", false);
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        int listings = 0;
        store.Setup(value => value.ListAsync(CertStoreKind.Rejected, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                listings++;
                return listings == 1
                    ? (ArrayOf<X509Certificate2>)[Copy(first), Copy(second), Copy(third)]
                    : [Copy(second)];
            });
        var deleted = new List<string>();
        store.Setup(value => value.DeleteAsync(
                CertStoreKind.Rejected, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<CertStoreKind, string, CancellationToken>((_, thumbprint, _) => deleted.Add(thumbprint))
            .Returns<CertStoreKind, string, CancellationToken>((_, thumbprint, _) =>
                thumbprint == second.Thumbprint
                    ? Task.FromException<bool>(new InvalidOperationException("file is read-only"))
                    : Task.FromResult(true));
        var operations = new CertificateStoreOperations(store.Object);

        await operations.DeleteAllAsync(CertStoreKind.Rejected, expiredOnly: false).ConfigureAwait(false);

        Assert.That(deleted, Is.EqualTo(new[] { first.Thumbprint, second.Thumbprint, third.Thumbprint }));
        Assert.That(operations.LastResult!.Attempted, Is.EqualTo(3));
        Assert.That(operations.LastResult.Succeeded, Is.EqualTo(2));
        Assert.That(operations.LastResult.Failed, Is.EqualTo(1));
        Assert.That(operations.LastResult.Operation, Is.EqualTo("Clear all"));
        Assert.That(operations.LastResult.Store, Is.EqualTo(CertStoreKind.Rejected));
        Assert.That(operations.LastResult.Detail, Does.Contain(second.Thumbprint).And.Contain("file is read-only"));
        Assert.That(operations.Rejected, Has.Count.EqualTo(1));
        Assert.That(operations.Rejected[0].Thumbprint, Is.EqualTo(second.Thumbprint));
        Assert.That(listings, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DelayedRefreshLeavesOutcomeVisibleAndBusyUntilFinished(bool refreshFails)
    {
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        var listing = new TaskCompletionSource<ArrayOf<X509Certificate2>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        store.Setup(value => value.DeleteAsync(CertStoreKind.Issuer, "expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(value => value.ListAsync(CertStoreKind.Issuer, It.IsAny<CancellationToken>()))
            .Returns(listing.Task);
        var operations = new CertificateStoreOperations(store.Object);

        Task deletion = operations.DeleteAsync(CertStoreKind.Issuer, "expired");
        CertificateOperationResult? failure = operations.LastResult;
        Assert.That(deletion.IsCompleted, Is.False);
        Assert.That(operations.IsBusy, Is.True);
        Assert.That(failure!.Failed, Is.EqualTo(1));
        await operations.DeleteAsync(CertStoreKind.Issuer, "another").ConfigureAwait(false);
        Assert.That(operations.LastResult, Is.SameAs(failure));
        if (refreshFails)
        {
            listing.SetException(new InvalidOperationException("enumeration denied"));
        }
        else
        {
            listing.SetResult([]);
        }
        await deletion.ConfigureAwait(false);

        Assert.That(operations.LastResult, Is.SameAs(failure));
        Assert.That(operations.IsBusy, Is.False);
        Assert.That(operations.LoadingStatus, Does.Contain(refreshFails ? "enumeration denied" : "0 certificate(s)"));
        store.Verify(value => value.DeleteAsync(
            CertStoreKind.Issuer, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TrustFailureSurvivesBothSourceAndDestinationRefreshes()
    {
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        store.Setup(value => value.TrustRejectedAsync("rejected", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(value => value.ListAsync(It.IsAny<CertStoreKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<X509Certificate2>.Empty);
        var operations = new CertificateStoreOperations(store.Object);

        await operations.TrustAsync("rejected").ConfigureAwait(false);

        Assert.That(operations.LastResult!.Failed, Is.EqualTo(1));
        Assert.That(operations.LastResult.Summary, Does.Contain("Rejected").And.Contain("Trusted"));
        store.Verify(value => value.ListAsync(CertStoreKind.Rejected, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(value => value.ListAsync(CertStoreKind.Trusted, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteExpiredCountsOnlyMatchingTargetsAndNextOperationReplacesResult()
    {
        using X509Certificate2 expired = CreateCertificate("expired", true);
        using X509Certificate2 current = CreateCertificate("current", false);
        string currentThumbprint = current.Thumbprint;
        var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
        store.Setup(value => value.ListAsync(CertStoreKind.Trusted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (ArrayOf<X509Certificate2>)[Copy(expired), Copy(current)]);
        store.Setup(value => value.DeleteAsync(
                CertStoreKind.Trusted, expired.Thumbprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(value => value.AddAsync(
                CertStoreKind.Trusted,
                It.Is<X509Certificate2>(certificate => certificate.Thumbprint == currentThumbprint),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var time = new Mock<TimeProvider>();
        time.Setup(value => value.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var operations = new CertificateStoreOperations(store.Object, time.Object);

        await operations.DeleteAllAsync(CertStoreKind.Trusted, expiredOnly: true).ConfigureAwait(false);

        Assert.That(operations.LastResult!.Operation, Is.EqualTo("Delete expired"));
        Assert.That(operations.LastResult.Attempted, Is.EqualTo(1));
        Assert.That(operations.LastResult.Failed, Is.EqualTo(1));
        await operations.AddAsync(CertStoreKind.Trusted, current).ConfigureAwait(false);
        Assert.That(operations.LastResult.Operation, Is.EqualTo("Add"));
        Assert.That(operations.LastResult.Attempted, Is.EqualTo(1));
        Assert.That(operations.LastResult.Succeeded, Is.EqualTo(1));
        Assert.That(operations.LastResult.Failed, Is.Zero);
        store.Verify(value => value.DeleteAsync(
            CertStoreKind.Trusted, current.Thumbprint, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static X509Certificate2 CreateCertificate(string name, bool expired)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(expired ? 2001 : 2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static X509Certificate2 Copy(X509Certificate2 certificate)
        => X509CertificateLoader.LoadCertificate(certificate.RawData);
}
