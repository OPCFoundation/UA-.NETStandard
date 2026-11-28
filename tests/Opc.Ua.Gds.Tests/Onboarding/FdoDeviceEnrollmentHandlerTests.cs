// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using global::Fdo.Enrollment;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Fdo;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the device adapter's validation-to-installation gate without the FDO transport.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class FdoDeviceEnrollmentHandlerTests
    {
        /// <summary>
        /// Validation is separate from installation, and accepted evidence installs exactly once.
        /// </summary>
        [Test]
        public async Task ValidEnrollmentInstallsOnceAndRejectsDuplicateCalls()
        {
            Mock<Func<ByteString, CancellationToken, ValueTask>> validate = CreateCallback();
            Mock<Func<ByteString, CancellationToken, ValueTask>> install = CreateCallback();
            IFdoDeviceEnrollmentHandler handler = CreateHandler(validate.Object, install.Object);
            FdoDeviceEnrollment enrollment = CreateEnrollment();

            await handler.ValidateCertificateAsync(enrollment, default).ConfigureAwait(false);
            install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            await AssertRejectedAsync(() => handler.ValidateCertificateAsync(enrollment, default))
                .ConfigureAwait(false);
            await handler.InstallAsync(CreateEnrollment(), default).ConfigureAwait(false);
            await AssertRejectedAsync(() => handler.InstallAsync(enrollment, default)).ConfigureAwait(false);

            validate.Verify(callback => callback(s_leafBytes, default), Times.Once);
            validate.VerifyNoOtherCalls();
            install.Verify(callback => callback(s_leafBytes, default), Times.Once);
            install.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The installer is never called before validation.
        /// </summary>
        [Test]
        public async Task InstallationBeforeValidationIsRejected()
        {
            Mock<Func<ByteString, CancellationToken, ValueTask>> install = CreateCallback();
            IFdoDeviceEnrollmentHandler handler = CreateHandler(CreateCallback().Object, install.Object);

            await AssertRejectedAsync(() => handler.InstallAsync(CreateEnrollment(), default)).ConfigureAwait(false);
            install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Each changed field independently invalidates installation without consuming the approved evidence.
        /// </summary>
        [TestCase(nameof(FdoDeviceEnrollment.RunId))]
        [TestCase(nameof(FdoDeviceEnrollment.DeviceGuid))]
        [TestCase(nameof(FdoDeviceEnrollment.CertificateSigningRequest))]
        [TestCase(nameof(FdoDeviceEnrollment.Certificate))]
        public async Task InstallationRejectsChangedEvidence(string changedField)
        {
            Mock<Func<ByteString, CancellationToken, ValueTask>> install = CreateCallback();
            IFdoDeviceEnrollmentHandler handler = CreateHandler(CreateCallback().Object, install.Object);
            FdoDeviceEnrollment enrollment = CreateEnrollment();
            await handler.ValidateCertificateAsync(enrollment, default).ConfigureAwait(false);

            await AssertRejectedAsync(() => handler.InstallAsync(CreateEnrollment(changedField), default))
                .ConfigureAwait(false);
            install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            await handler.InstallAsync(enrollment, default).ConfigureAwait(false);
            install.Verify(callback => callback(s_leafBytes, default), Times.Once);
            install.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A host validation error or cancellation from inside the callback cannot authorize installation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsuccessfulValidationDoesNotAuthorizeInstallation(bool cancel)
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("Certificate validation failed.");
            Mock<Func<ByteString, CancellationToken, ValueTask>> validate = CreateCallback();
            validate.Setup(callback => callback(It.IsAny<ByteString>(), cancellation.Token))
                .Returns((ByteString _, CancellationToken token) =>
                {
                    if (!cancel)
                    {
                        return ValueTask.FromException(failure);
                    }
                    cancellation.Cancel();
                    return ValueTask.FromCanceled(token);
                });
            Mock<Func<ByteString, CancellationToken, ValueTask>> install = CreateCallback();
            IFdoDeviceEnrollmentHandler handler = CreateHandler(validate.Object, install.Object);
            FdoDeviceEnrollment enrollment = CreateEnrollment();

            if (cancel)
            {
                OperationCanceledException error = await Assert.CatchAsync<OperationCanceledException>(
                    () => handler.ValidateCertificateAsync(enrollment, cancellation.Token).AsTask());
                Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            else
            {
                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => handler.ValidateCertificateAsync(enrollment, cancellation.Token).AsTask());
                Assert.That(error, Is.SameAs(failure));
            }
            validate.Verify(callback => callback(s_leafBytes, cancellation.Token), Times.Once);
            await AssertRejectedAsync(() => handler.InstallAsync(enrollment, default)).ConfigureAwait(false);
            install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// In-flight validation cannot be repeated or used to install, and in-flight installation cannot be repeated.
        /// </summary>
        [Test]
        public async Task ConcurrentCallsDoNotBypassTheEnrollmentGate()
        {
            var validationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var installationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<Func<ByteString, CancellationToken, ValueTask>> validate = CreateCallback();
            Mock<Func<ByteString, CancellationToken, ValueTask>> install = CreateCallback();
            validate.SetupSequence(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(validationGate.Task))
                .Returns(ValueTask.CompletedTask);
            install.SetupSequence(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(installationGate.Task))
                .Returns(ValueTask.CompletedTask);
            IFdoDeviceEnrollmentHandler handler = CreateHandler(validate.Object, install.Object);
            FdoDeviceEnrollment enrollment = CreateEnrollment();

            Task validation = handler.ValidateCertificateAsync(enrollment, default).AsTask();
            try
            {
                validate.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()),
                    Times.Once);
                await AssertRejectedAsync(() => handler.ValidateCertificateAsync(enrollment, default))
                    .ConfigureAwait(false);
                await AssertRejectedAsync(() => handler.InstallAsync(enrollment, default)).ConfigureAwait(false);
                install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                validationGate.SetResult();
                await validation.ConfigureAwait(false);
            }

            Task installation = handler.InstallAsync(enrollment, default).AsTask();
            try
            {
                await AssertRejectedAsync(() => handler.InstallAsync(enrollment, default)).ConfigureAwait(false);
            }
            finally
            {
                installationGate.SetResult();
                await installation.ConfigureAwait(false);
            }
            validate.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
            install.Verify(callback => callback(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private static FdoDeviceEnrollmentHandler CreateHandler(
            Func<ByteString, CancellationToken, ValueTask> validate,
            Func<ByteString, CancellationToken, ValueTask> install)
        {
            return new FdoDeviceEnrollmentHandler(
                ByteString.From(s_deviceGuid.Span), ByteString.From(s_csr.Span), validate, install);
        }

        private static Mock<Func<ByteString, CancellationToken, ValueTask>> CreateCallback()
        {
            var callback = new Mock<Func<ByteString, CancellationToken, ValueTask>>();
            callback.Setup(invoke => invoke(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return callback;
        }

        private static FdoDeviceEnrollment CreateEnrollment(string? changedField = null)
        {
            // Fdo creates enrollment evidence internally. Construct only that evidence here to avoid a TO2 exchange.
            return (FdoDeviceEnrollment)s_enrollmentConstructor.Invoke(
            [
                changedField == nameof(FdoDeviceEnrollment.RunId) ? s_otherRunId : s_runId,
                changedField == nameof(FdoDeviceEnrollment.DeviceGuid) ? s_otherDeviceGuid : s_deviceGuid,
                changedField == nameof(FdoDeviceEnrollment.CertificateSigningRequest) ? s_otherEvidence : s_csr,
                changedField == nameof(FdoDeviceEnrollment.Certificate) ? s_otherEvidence : s_leaf,
                null,
                false,
                null,
                false
            ]);
        }

        private static async Task AssertRejectedAsync(Func<ValueTask> call)
        {
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(() => call().AsTask());
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        private static readonly ConstructorInfo s_enrollmentConstructor =
            typeof(FdoDeviceEnrollment).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(Guid), typeof(ReadOnlyMemory<byte>), typeof(ReadOnlyMemory<byte>),
                    typeof(ReadOnlyMemory<byte>), typeof(FdoCaCertificates), typeof(bool),
                    typeof(FdoCsrAttributes), typeof(bool)],
                modifiers: null) ?? throw new InvalidOperationException("FDO evidence constructor not found.");
        private static readonly Guid s_runId = new("11111111-2222-3333-4444-555555555555");
        private static readonly Guid s_otherRunId = new("22222222-2222-3333-4444-555555555555");
        private static readonly ReadOnlyMemory<byte> s_deviceGuid = new byte[16];
        private static readonly ReadOnlyMemory<byte> s_otherDeviceGuid =
            new byte[16] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly ReadOnlyMemory<byte> s_csr = new byte[] { 1, 2 };
        private static readonly ReadOnlyMemory<byte> s_leaf = new byte[] { 3, 4 };
        private static readonly ReadOnlyMemory<byte> s_otherEvidence = new byte[] { 5, 6 };
        private static readonly ByteString s_leafBytes = ByteString.From(s_leaf.Span);
    }
}
#endif
