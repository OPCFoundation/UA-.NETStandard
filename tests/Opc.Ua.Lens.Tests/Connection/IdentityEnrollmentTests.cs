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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Tests.Samples;
using CertificateValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;
using CertificateValidationResult = Opc.Ua.CertificateValidationResult;

namespace UaLens.Tests.Connection
{
    [TestFixture]
    public sealed class IdentityEnrollmentTests
    {
        [Test]
        public async Task PreparationCreatesAMatchingLocalCsrWithoutGdsOrStoreMutations()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentReview review = await fixture.PrepareAsync().ConfigureAwait(false);

                Assert.That(review.SourceId, Is.EqualTo(fixture.Source.Id));
                Assert.That(review.Subject, Is.EqualTo(fixture.Key.Subject));
                Assert.That(review.ApplicationUri, Is.EqualTo(EnrollmentFixture.RegisteredApplicationUri));
                Assert.That(review.ExpiresAt, Is.EqualTo(fixture.Clock.Provider.GetUtcNow().AddMinutes(5)));
                Assert.That(fixture.Submissions, Is.Zero);
                Assert.That(fixture.Adoptions, Is.Zero);
                IdentityEnrollmentResult result = await fixture.Enrollment.RequestAsync(
                    review, null, CancellationToken.None).ConfigureAwait(false);
                var request = CertificateRequest.LoadSigningRequest(
                    fixture.Csr.ToArray(), HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default,
                    RSASignaturePadding.Pkcs1);
                using RSA requested = request.PublicKey.GetRSAPublicKey() ??
                    throw new AssertionException("The CSR did not carry an RSA public key.");
                using RSA original = fixture.Key.GetRSAPublicKey()!;
                Assert.That(requested.ExportSubjectPublicKeyInfo(), Is.EqualTo(original.ExportSubjectPublicKeyInfo()));
                Assert.That(result.Thumbprint, Is.EqualTo(fixture.Issued.Thumbprint));
                Assert.That(fixture.Submissions, Is.EqualTo(1));
                Assert.That(fixture.Adoptions, Is.Zero);
                fixture.VerifyBorrowedOwners();
            }
        }

        [Test]
        public async Task AdoptionWritesTheReviewedKeyOnceClearsItsPasswordAndRetainsTheOldCertificate()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentResult result = await fixture.RequestAsync().ConfigureAwait(false);

                CertificateIdentityReference adopted = await fixture.Enrollment.AdoptAsync(
                    result, CancellationToken.None).ConfigureAwait(false);

                Assert.That(adopted.SourceId, Is.EqualTo(fixture.Reference.SourceId));
                Assert.That(adopted.PasswordSourceId, Is.EqualTo(fixture.Reference.PasswordSourceId));
                Assert.That(adopted.Thumbprint, Is.EqualTo(fixture.Issued.Thumbprint));
                Assert.That(adopted.SubjectName, Is.EqualTo(fixture.Key.Subject));
                Assert.That(fixture.Adoptions, Is.EqualTo(1));
                Assert.That(fixture.Added, Is.Not.Null);
                Assert.That(fixture.Added!.HasPrivateKey, Is.True);
                Assert.That(fixture.Added.Thumbprint, Is.EqualTo(result.Thumbprint));
                Assert.That(fixture.Key.HasPrivateKey, Is.True);
                Assert.That(fixture.Key.Thumbprint, Is.Not.EqualTo(result.Thumbprint));
                Assert.That(fixture.PasswordBuffers, Has.Count.EqualTo(1));
                Assert.That(fixture.PasswordBuffers[0], Has.All.EqualTo('\0'));
                Assert.That(fixture.Validations, Is.EqualTo(2));
                fixture.Store.Verify(store => store.Dispose(), Times.Once);
                await Assert.ThatAsync(() => fixture.Enrollment.AdoptAsync(result, CancellationToken.None),
                    Throws.InvalidOperationException).ConfigureAwait(false);
                Assert.That(fixture.Adoptions, Is.EqualTo(1));
                fixture.VerifyBorrowedOwners();
            }
        }

        [TestCase("session")]
        [TestCase("identity")]
        [TestCase("endpoint")]
        [TestCase("application")]
        [TestCase("policy")]
        [TestCase("certificate")]
        [TestCase("expired")]
        public async Task ChangedEnrollmentContextPreventsSubmission(string change)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentReview review = await fixture.PrepareAsync().ConfigureAwait(false);
                switch (change)
                {
                    case "session":
                        fixture.SessionId = new NodeId(11u);
                        break;
                    case "identity":
                        fixture.Identity = new Mock<IUserIdentity>().Object;
                        break;
                    case "endpoint":
                        fixture.Endpoint.EndpointUrl += "/changed";
                        break;
                    case "application":
                        fixture.Endpoint.Server.ApplicationUri = "urn:changed-gds";
                        break;
                    case "policy":
                        fixture.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                        break;
                    case "certificate":
                        fixture.Endpoint.ServerCertificate = ByteString.From([9]);
                        break;
                    case "expired":
                        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change));
                }

                await Assert.ThatAsync(() => fixture.Enrollment.RequestAsync(review, null, CancellationToken.None),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.Zero);
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReviewAndResultCopiesCannotReplayTheActualConsentObjects(bool resultCopy)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentReview review = await fixture.PrepareAsync().ConfigureAwait(false);
                if (resultCopy)
                {
                    IdentityEnrollmentResult result = await fixture.Enrollment.RequestAsync(
                        review, null, CancellationToken.None).ConfigureAwait(false);
                    await Assert.ThatAsync(() => fixture.Enrollment.AdoptAsync(result with { }, CancellationToken.None),
                        Throws.InvalidOperationException).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => fixture.Enrollment.RequestAsync(review with { }, null,
                        CancellationToken.None), Throws.InvalidOperationException).ConfigureAwait(false);
                }
                Assert.That(fixture.Adoptions, Is.Zero);
                Assert.That(fixture.Submissions, Is.EqualTo(resultCopy ? 1 : 0));
            }
        }

        [TestCase("private-key")]
        [TestCase("certificate-size")]
        [TestCase("issuer-count")]
        [TestCase("issuer-size")]
        [TestCase("issuer-empty")]
        public async Task MalformedEnrollmentResponsesCannotBeAdopted(string fault)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                switch (fault)
                {
                    case "private-key":
                        fixture.ReturnedPrivateKey = ByteString.From([1]);
                        break;
                    case "certificate-size":
                        fixture.ReturnedCertificate = ByteString.From(new byte[65537]);
                        break;
                    case "issuer-count":
                        fixture.ReturnedIssuers =
                            Enumerable.Repeat(ByteString.From(fixture.Issuer.RawData), 17).ToArray();
                        break;
                    case "issuer-size":
                        fixture.ReturnedIssuers = [ByteString.From(new byte[65537])];
                        break;
                    case "issuer-empty":
                        fixture.ReturnedIssuers = [ByteString.Empty];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(fault));
                }

                await Assert.ThatAsync(fixture.RequestAsync, Throws.InvalidOperationException).ConfigureAwait(false);

                Assert.That(fixture.Adoptions, Is.Zero);
                Assert.That(fixture.Validations, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RevocationOrTrustDenialPreventsRequestResultAndAdoption(bool denyAdoption)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentResult? result = denyAdoption
                    ? await fixture.RequestAsync().ConfigureAwait(false) : null;
                fixture.ValidationStatus = StatusCodes.BadCertificateRevoked;
                if (denyAdoption)
                {
                    await Assert.ThatAsync(() => fixture.Enrollment.AdoptAsync(result!, CancellationToken.None),
                        Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                            .EqualTo(StatusCodes.BadCertificateRevoked)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(fixture.RequestAsync, Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadCertificateRevoked)).ConfigureAwait(false);
                }
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [TestCase("subject")]
        [TestCase("expired")]
        [TestCase("future")]
        [TestCase("wrong-key")]
        public async Task UnmatchedIssuedCertificatesNeverReachTheDestination(string fault)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                using Certificate otherKey = CertificateBuilder.Create(fixture.Key.Subject)
                    .SetRSAKeySize(2048).CreateForRSA();
                using Certificate replacement = fixture.CreateIssued(
                    fault == "wrong-key" ? otherKey : fixture.Key,
                    fault == "subject" ? "CN=Different subject" : fixture.Key.Subject,
                    fault == "future" ? fixture.Now.AddDays(1) : fixture.Now.AddDays(-2),
                    fault == "expired" ? fixture.Now.AddDays(-1) : fixture.Now.AddDays(30));
                fixture.ReturnedCertificate = ByteString.From(replacement.RawData);

                if (fault == "wrong-key")
                {
                    await Assert.ThatAsync(fixture.RequestAsync,
                        Throws.TypeOf<NotSupportedException>().With.Message.Contains("doesn't match"))
                        .ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(fixture.RequestAsync, Throws.InvalidOperationException)
                        .ConfigureAwait(false);
                }
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [Test]
        public async Task PendingApprovalUsesTheOwnedClockDeadlineWithoutResubmitting()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                fixture.ReturnedCertificate = ByteString.Empty;
                Task<IdentityEnrollmentResult> pending = fixture.RequestAsync();
                await fixture.Clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                fixture.Clock.Advance(TimeSpan.FromSeconds(30));

                await Assert.ThatAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.EqualTo(1));
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [TestCase("source")]
        [TestCase("destination")]
        [TestCase("original-key")]
        [TestCase("public-only-store")]
        public async Task AdoptionCannotRetargetTheStoreOrOriginalIdentity(string change)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentResult result = await fixture.RequestAsync().ConfigureAwait(false);
                using Certificate other = CertificateBuilder.Create(fixture.Key.Subject)
                    .SetRSAKeySize(2048).CreateForRSA();
                switch (change)
                {
                    case "source":
                        fixture.Source.Store.StorePath = "changed-source";
                        fixture.Store.SetupGet(store => store.StorePath).Returns("changed-source");
                        break;
                    case "destination":
                        fixture.Store.SetupGet(store => store.StorePath).Returns("different-destination");
                        break;
                    case "original-key":
                        fixture.Current = other;
                        break;
                    case "public-only-store":
                        fixture.Store.SetupGet(store => store.NoPrivateKeys).Returns(true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change));
                }

                await Assert.ThatAsync(() => fixture.Enrollment.AdoptAsync(result, CancellationToken.None),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                Assert.That(fixture.Adoptions, Is.Zero);
                Assert.That(fixture.Key.HasPrivateKey, Is.True);
            }
        }

        [Test]
        public async Task AProviderCannotSubstituteAnotherKeyBeforeCreatingTheCsr()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                using Certificate other = CertificateBuilder.Create("CN=Unselected user")
                    .SetRSAKeySize(2048).CreateForRSA();
                fixture.Current = other;

                await Assert.ThatAsync(fixture.PrepareAsync, Throws.TypeOf<ConnectionIdentityException>()
                    .With.Property(nameof(ConnectionIdentityException.Failure))
                    .EqualTo(ConnectionIdentityFailure.Incompatible)).ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.Zero);
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ApplicationAndHardwareKeyPurposesAreNotSoftwareUserEnrollment(bool hardware)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                var source = new ConfiguredCertificateSource(fixture.Source.Id, "Different purpose",
                    fixture.Source.Store, fixture.Certificates.Object, fixture.Source.PasswordSources,
                    hardware ? CryptoPurpose.UserIdentityKey : CryptoPurpose.ApplicationInstanceKey,
                    cryptoProviderName: hardware ? "ConfiguredHardware" : null);
                var enrollment = new GdsIdentityEnrollment(source, fixture.Gds.Object,
                    new NodeId(1u), new NodeId(2u), new NodeId(3u),
                    fixture.Validator.Object, () => fixture.Store.Object);
                await using (enrollment.ConfigureAwait(false))
                {
                    await Assert.ThatAsync(() => enrollment.PrepareAsync(
                        fixture.Reference, fixture.Policy, CancellationToken.None),
                        Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
                }
                Assert.That(fixture.Submissions, Is.Zero);
                Assert.That(fixture.Adoptions, Is.Zero);
                fixture.Certificates.Verify(provider => provider.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
                    It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [Test]
        public async Task AStalledSubmissionIsCoveredByTheWholeRequestDeadline()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int submissions = 0;
                fixture.Gds.Setup(gds => gds.StartSigningRequestAsync(
                        It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<ByteString>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (NodeId _, NodeId _, NodeId _, ByteString _, CancellationToken token) =>
                    {
                        submissions++;
                        entered.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                        throw new AssertionException(
                            "The stalled signing request should only complete by cancellation.");
                    });
                Task<IdentityEnrollmentResult> pending = fixture.RequestAsync();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                fixture.Clock.Advance(TimeSpan.FromSeconds(30));

                await Assert.ThatAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(submissions, Is.EqualTo(1));
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [Test]
        public async Task LateKeyAcquisitionCannotPrepareForAChangedGds()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<Certificate?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Certificates.Setup(provider => provider.GetPrivateKeyCertificateAsync(
                        It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
                        It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        entered.SetResult();
                        return new ValueTask<Certificate?>(release.Task);
                    });
                Task<IdentityEnrollmentReview> pending = fixture.PrepareAsync();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                fixture.Endpoint.EndpointUrl += "/different";
                release.SetResult(fixture.Key.AddRef());

                await Assert.ThatAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.Zero);
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [Test]
        public async Task StandardPendingStatusPollsTheSameRequestWithoutAnotherSubmission()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                int polls = 0;
                fixture.Gds.Setup(gds => gds.FinishRequestAsync(
                        It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId _, NodeId _, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return ++polls == 1
                            ? ValueTask.FromException<(ByteString, ByteString, ArrayOf<ByteString>)>(
                                new ServiceResultException(StatusCodes.BadNothingToDo))
                            : ValueTask.FromResult((
                                fixture.ReturnedCertificate, ByteString.Empty, fixture.ReturnedIssuers));
                    });
                Task<IdentityEnrollmentResult> pending = fixture.RequestAsync();
                await fixture.Clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));

                IdentityEnrollmentResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                Assert.That(result.Thumbprint, Is.EqualTo(fixture.Issued.Thumbprint));
                Assert.That(polls, Is.EqualTo(2));
                Assert.That(fixture.Submissions, Is.EqualTo(1));
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [Test]
        public async Task UserCertificatesWithoutTheRegisteredApplicationUriNeedAnotherEnrollmentAdapter()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                using Certificate unrelated = CertificateBuilder.Create(fixture.Key.Subject)
                    .SetRSAKeySize(2048).CreateForRSA();
                fixture.Current = unrelated;
                CertificateIdentityReference reference = fixture.Reference with { Thumbprint = unrelated.Thumbprint };

                await Assert.ThatAsync(() => fixture.Enrollment.PrepareAsync(
                    reference, fixture.Policy, CancellationToken.None),
                    Throws.InvalidOperationException.With.Message.Contains("ApplicationUri")).ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.Zero);
            }
        }

        [Test]
        public async Task ChangedRegisteredApplicationIsRejectedBeforeSendingTheCsr()
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                IdentityEnrollmentReview review = await fixture.PrepareAsync().ConfigureAwait(false);
                fixture.Gds.Setup(gds => gds.GetApplicationAsync(
                        It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId id, CancellationToken _) => ValueTask.FromResult(new ApplicationRecordDataType
                    {
                        ApplicationId = id,
                        ApplicationUri = "urn:changed-application"
                    }));

                await Assert.ThatAsync(() => fixture.Enrollment.RequestAsync(review, null, CancellationToken.None),
                    Throws.InvalidOperationException.With.Message.Contains("application changed"))
                        .ConfigureAwait(false);

                Assert.That(fixture.Submissions, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidIssuedApplicationBindingOrKeyUsageCannotBecomeAUserIdentity(bool badUsage)
        {
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                using Certificate invalid = fixture.CreateIssued(
                    fixture.Key, fixture.Key.Subject, fixture.Now.AddDays(-1), fixture.Now.AddDays(30),
                    badUsage ? EnrollmentFixture.RegisteredApplicationUri : "urn:unrequested-application", !badUsage);
                fixture.ReturnedCertificate = ByteString.From(invalid.RawData);

                if (badUsage)
                {
                    await Assert.ThatAsync(fixture.RequestAsync, Throws.TypeOf<ConnectionIdentityException>()
                        .With.Property(nameof(ConnectionIdentityException.Failure))
                        .EqualTo(ConnectionIdentityFailure.Incompatible)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(fixture.RequestAsync, Throws.InvalidOperationException
                        .With.Message.Contains("application binding")).ConfigureAwait(false);
                }
                Assert.That(fixture.Adoptions, Is.Zero);
            }
        }

        [Test]
        public async Task AdoptedIdentityCanBeReloadedFromItsPrivateOwnedStoreWithoutDeletingTheOldKey()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ualens-enrollment-" + Guid.NewGuid().ToString("N"));
            var fixture = new EnrollmentFixture();
            await using (fixture.ConfigureAwait(false))
            {
                fixture.Source.Store.StorePath = directory;
                DirectoryCertificateStore Open()
                {
                    var store = new DirectoryCertificateStore(fixture.MessageContext.Telemetry);
                    store.Open(directory, noPrivateKeys: false);
                    return store;
                }
                fixture.OpenStore = Open;
                try
                {
                    using (DirectoryCertificateStore initial = Open())
                    {
                        await initial.AddAsync(fixture.Key, "fixture-only".ToCharArray()).ConfigureAwait(false);
                    }
                    IdentityEnrollmentResult issued = await fixture.RequestAsync().ConfigureAwait(false);

                    CertificateIdentityReference adopted = await fixture.Enrollment.AdoptAsync(
                        issued, CancellationToken.None).ConfigureAwait(false);

                    using DirectoryCertificateStore readback = Open();
                    using Certificate? reloaded = await readback.LoadPrivateKeyAsync(
                        adopted.Thumbprint!, adopted.SubjectName, EnrollmentFixture.RegisteredApplicationUri,
                        NodeId.Null, "fixture-only".ToCharArray()).ConfigureAwait(false);
                    Assert.That(reloaded, Is.Not.Null);
                    Assert.That(reloaded!.HasPrivateKey, Is.True);
                    Assert.That(reloaded.Thumbprint, Is.EqualTo(issued.Thumbprint));
                    using CertificateCollection retained = await readback.EnumerateAsync().ConfigureAwait(false);
                    Assert.That(retained, Has.Count.EqualTo(2));
                    Assert.That(retained.Any(certificate => certificate.Thumbprint == fixture.Key.Thumbprint), Is.True);
                    Assert.That(retained.Any(certificate => certificate.Thumbprint == issued.Thumbprint), Is.True);
                    fixture.VerifyBorrowedOwners();
                }
                finally
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
            }
        }

        internal sealed class EnrollmentFixture : IAsyncDisposable
        {
            public EnrollmentFixture()
            {
                Clock.Advance(DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch);
                Now = Clock.Provider.GetUtcNow().UtcDateTime;
                Key = CertificateBuilder.Create("CN=UaLens enrollment fixture")
                    .SetNotBefore(Now.AddDays(-2)).SetNotAfter(Now.AddDays(30))
                    .AddExtension(new X509SubjectAltNameExtension(RegisteredApplicationUri, []))
                    .SetRSAKeySize(2048).CreateForRSA();
                Issuer = CertificateBuilder.Create("CN=UaLens local enrollment issuer")
                    .SetNotBefore(Now.AddDays(-7)).SetNotAfter(Now.AddYears(1)).SetCAConstraint()
                    .SetRSAKeySize(2048).CreateForRSA();
                Issued = CreateIssued(Key, Key.Subject, Now.AddDays(-1), Now.AddDays(60));
                Current = Key;
                ReturnedCertificate = ByteString.From(Issued.RawData);
                ReturnedIssuers = [ByteString.From(Issuer.RawData)];
                Endpoint = new EndpointDescription("opc.tcp://localhost:4840/enrollment-fixture")
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = ByteString.From(Issuer.RawData),
                    Server = new ApplicationDescription { ApplicationUri = "urn:ualens:enrollment-fixture" },
                    UserIdentityTokens = [Policy]
                };
                Session.SetupGet(value => value.Connected).Returns(() => Connected);
                Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
                Session.SetupGet(value => value.Identity).Returns(() => Identity);
                Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
                Session.SetupGet(value => value.MessageContext).Returns(MessageContext);
                Session.SetupGet(value => value.NamespaceUris).Returns(MessageContext.NamespaceUris);
                Gds.SetupGet(value => value.Session).Returns(Session.Object);
                Gds.Setup(value => value.GetApplicationAsync(ApplicationId, It.IsAny<CancellationToken>()))
                    .Returns(() => ValueTask.FromResult(new ApplicationRecordDataType
                    {
                        ApplicationId = ApplicationId,
                        ApplicationUri = RegisteredApplicationUri
                    }));
                Certificates.Setup(value => value.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
                    It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(Current?.AddRef()));
                Passwords.Setup(value => value.GetPassword(It.IsAny<CertificateIdentifier>())).Returns(() =>
                {
                    char[] password = "fixture-only".ToCharArray();
                    PasswordBuffers.Add(password);
                    return password;
                });
                Source = new ConfiguredCertificateSource("user", "Fixture user key",
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = "memory-user-store"
                    },
                    Certificates.Object, [new ConfiguredCertificatePasswordSource("password", "Fixture key password",
                        Passwords.Object)]);
                Reference = new CertificateIdentityReference
                {
                    SourceId = Source.Id,
                    PasswordSourceId = "password",
                    Thumbprint = Key.Thumbprint,
                    SubjectName = Key.Subject
                };
                Gds.Setup(value => value.StartSigningRequestAsync(
                    ApplicationId, GroupId, TypeId, It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId _, NodeId _, NodeId _, ByteString csr, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        Submissions++;
                        Csr = csr.Copy();
                        return ValueTask.FromResult(RequestId);
                    });
                Gds.Setup(value => value.FinishRequestAsync(ApplicationId, RequestId, It.IsAny<CancellationToken>()))
                    .Returns((NodeId _, NodeId _, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return ValueTask.FromResult((ReturnedCertificate, ReturnedPrivateKey, ReturnedIssuers));
                    });
                Validator.Setup(value => value.ValidateAsync(It.IsAny<CertificateCollection>(),
                        It.IsAny<TrustListIdentifier?>(), It.IsAny<CertificateValidationOptions?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((CertificateCollection chain, TrustListIdentifier? trust,
                        CertificateValidationOptions? options, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        Validations++;
                        Assert.That(trust, Is.EqualTo(TrustListIdentifier.Users));
                        Assert.That(options, Is.Not.Null);
                        Assert.That(options!.RejectSHA1SignedCertificates, Is.True);
                        Assert.That(options.RejectUnknownRevocationStatus, Is.True);
                        Assert.That(options.AutoAcceptUntrustedCertificates, Is.False);
                        Assert.That(options.AllowCertificateDownload, Is.False);
                        Assert.That(options.AcceptError!(
                            chain[0], new ServiceResult(StatusCodes.BadCertificateUntrusted)),
                            Is.False);
                        return Task.FromResult(StatusCode.IsGood(ValidationStatus)
                            ? CertificateValidationResult.Success :
                            new CertificateValidationResult(false, ValidationStatus, [], false));
                    });
                Store.SetupGet(value => value.StoreType).Returns(CertificateStoreType.Directory);
                Store.SetupGet(value => value.StorePath).Returns("memory-user-store");
                Store.SetupGet(value => value.NoPrivateKeys).Returns(false);
                Store.Setup(value => value.Dispose());
                Store.Setup(value => value.AddAsync(
                        It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                    .Returns((Certificate certificate, char[]? password, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        Adoptions++;
                        Added = certificate.AddRef();
                        Assert.That(password, Is.EqualTo("fixture-only".ToCharArray()));
                        return Task.CompletedTask;
                    });
                Enrollment = new GdsIdentityEnrollment(Source, Gds.Object, ApplicationId, GroupId, TypeId,
                    Validator.Object, () => OpenStore?.Invoke() ?? Store.Object, timeProvider: Clock.Provider);
            }

            public Certificate Key { get; }
            public Certificate Issuer { get; }
            public Certificate Issued { get; }
            public Certificate? Current { get; set; }
            public Certificate? Added { get; private set; }
            public DateTime Now { get; }
            public SampleTestClock Clock { get; } = new();

            public ServiceMessageContext MessageContext { get; } =
                ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { }));

            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
            public Mock<IGlobalDiscoveryServerClient> Gds { get; } = new(MockBehavior.Strict);
            public Mock<ICertificateProvider> Certificates { get; } = new(MockBehavior.Strict);
            public Mock<ICertificatePasswordProvider> Passwords { get; } = new(MockBehavior.Strict);
            public Mock<ICertificateValidatorEx> Validator { get; } = new(MockBehavior.Strict);
            public Mock<ICertificateStore> Store { get; } = new(MockBehavior.Strict);
            public ConfiguredCertificateSource Source { get; }
            public CertificateIdentityReference Reference { get; }

            public UserTokenPolicy Policy { get; } = new(UserTokenType.Certificate)
            {
                PolicyId = "user-certificate",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            public EndpointDescription Endpoint { get; }
            public GdsIdentityEnrollment Enrollment { get; }
            public Func<ICertificateStore>? OpenStore { get; set; }
            public bool Connected { get; set; } = true;
            public NodeId SessionId { get; set; } = new(10u);
            public IUserIdentity Identity { get; set; } = new Mock<IUserIdentity>().Object;
            public ByteString ReturnedCertificate { get; set; }
            public ByteString ReturnedPrivateKey { get; set; }
            public ArrayOf<ByteString> ReturnedIssuers { get; set; }
            public StatusCode ValidationStatus { get; set; }
            public ByteString Csr { get; private set; }
            public int Submissions { get; private set; }
            public int Adoptions { get; private set; }
            public int Validations { get; private set; }
            public List<char[]> PasswordBuffers { get; } = [];

            public Task<IdentityEnrollmentReview> PrepareAsync()
            {
                return Enrollment.PrepareAsync(Reference, Policy, CancellationToken.None);
            }

            public async Task<IdentityEnrollmentResult> RequestAsync()
            {
                IdentityEnrollmentReview review = await PrepareAsync().ConfigureAwait(false);
                return await Enrollment.RequestAsync(review, null, CancellationToken.None).ConfigureAwait(false);
            }

            public Certificate CreateIssued(
                Certificate key, string subject, DateTime before, DateTime after,
                string applicationUri = RegisteredApplicationUri, bool canSign = true)
            {
                using RSA publicKey = key.GetRSAPublicKey() ??
                    throw new AssertionException("Expected a real RSA test key.");
                ICertificateBuilder builder = CertificateBuilder.Create(subject).SetNotBefore(before).SetNotAfter(after)
                    .AddExtension(new X509SubjectAltNameExtension(applicationUri, []));
                if (!canSign)
                {
                    builder.AddExtension(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
                }
                return builder.SetIssuer(Issuer).SetRSAPublicKey(publicKey).CreateForRSA();
            }

            public void VerifyBorrowedOwners()
            {
                Session.Verify(value => value.Dispose(), Times.Never);
                Gds.Verify(value => value.DisposeAsync(), Times.Never);
            }

            public async ValueTask DisposeAsync()
            {
                await Enrollment.DisposeAsync().ConfigureAwait(false);
                Added?.Dispose();
                Issued.Dispose();
                Issuer.Dispose();
                Key.Dispose();
            }

            public const string RegisteredApplicationUri = "urn:ualens:enrollment:user-fixture";
            private static readonly NodeId ApplicationId = new("application", 2);
            private static readonly NodeId GroupId = new("user-group", 2);
            private static readonly NodeId TypeId = new("user-certificate-type", 2);
            private static readonly NodeId RequestId = new("request", 2);
        }
    }
}
