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
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Client.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.UserManagement
{
    /// <summary>
    /// Exercises password confinement through the real typed caller and service transport.
    /// </summary>
    [TestFixture]
    public sealed class UserManagementConfidentialDispatchTests
    {
        /// <summary>
        /// Neither an absent channel policy nor signing protects password method arguments.
        /// </summary>
        [TestCase(MessageSecurityMode.None, "AddUser")]
        [TestCase(MessageSecurityMode.None, "ModifyUser")]
        [TestCase(MessageSecurityMode.None, "ModifyUserEmpty")]
        [TestCase(MessageSecurityMode.None, "ChangePassword")]
        [TestCase(MessageSecurityMode.Sign, "AddUser")]
        [TestCase(MessageSecurityMode.Sign, "ModifyUser")]
        [TestCase(MessageSecurityMode.Sign, "ModifyUserEmpty")]
        [TestCase(MessageSecurityMode.Sign, "ChangePassword")]
        [Category("SecurityRegression")]
        public async Task SecurityF02PasswordCallsRequireConfidentialDispatch(
            MessageSecurityMode securityMode,
            string operation)
        {
            await using var fixture = new SessionRequestContextTestContext(securityMode: securityMode);
            SetSuccessfulCalls(fixture);
            var client = new UserManagementClient(fixture.ManagedSession);

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await InvokeAsync(client, operation).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Supported managed and ordinary sessions send the exact arguments on the captured encrypted channel.
        /// </summary>
        [TestCase(false, "AddUser")]
        [TestCase(false, "ModifyUser")]
        [TestCase(false, "ModifyUserEmpty")]
        [TestCase(false, "ChangePassword")]
        [TestCase(true, "AddUser")]
        [TestCase(true, "ModifyUser")]
        [TestCase(true, "ModifyUserEmpty")]
        [TestCase(true, "ChangePassword")]
        [Category("SecurityRegression")]
        public async Task SecurityF02EncryptedPasswordCallsUseCapturedTransport(bool managed, string operation)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            var client = new UserManagementClient(managed ? fixture.ManagedSession : fixture.InnerSession);

            await InvokeAsync(client, operation).ConfigureAwait(false);

            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            var request = (CallRequest)fixture.Requests[0];
            Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(NodeId.Parse("s=original-token")));
            Assert.That(request.MethodsToCall.Count, Is.EqualTo(1));
            Assert.That(request.MethodsToCall[0].ObjectId, Is.EqualTo(new NodeId(Objects.UserManagement)));
            Assert.That(request.MethodsToCall[0].InputArguments, Is.EqualTo(ExpectedArguments(operation)));
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A null password is an edit, not a secret; even custom sessions retain that workflow.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02NonPasswordEditsAndRemovalPreserveCustomSessionDispatch()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext).Returns(
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                });
            var client = new UserManagementClient(session.Object);

            await client.ModifyUserAsync(
                "operator",
                userConfiguration: UserConfigurationMask.Disabled,
                description: "disabled account").ConfigureAwait(false);
            await client.RemoveUserAsync("operator").ConfigureAwait(false);

            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(),
                It.Is<ArrayOf<CallMethodRequest>>(methods =>
                    methods.Count == 1 &&
                    methods[0].InputArguments.Equals(NonPasswordArguments())),
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(),
                It.Is<ArrayOf<CallMethodRequest>>(methods =>
                    methods.Count == 1 &&
                    methods[0].InputArguments.Equals(ArrayOf.Wrapped(Variant.From("operator")))),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An arbitrary session cannot claim confidentiality without fixed dispatch support.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02CustomSessionCannotDispatchPasswords()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext).Returns(
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            var client = new UserManagementClient(session.Object);

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ChangePasswordAsync("old-marker", "new-marker").ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An ordinary borrowed session's security metadata cannot hide a signing-only physical channel.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02BorrowedSessionCannotHideSigningTransport()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });
            await using SessionRequestContext borrowed =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var client = new UserManagementClient(borrowed.Session);

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ChangePasswordAsync("old-marker", "new-marker").ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Promoting an old borrowed session must not capture the replacement session, even if it is encrypted.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02ConfidentialCaptureCannotRetargetExpiredBorrowedSession()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            await using SessionRequestContext borrowed =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var client = new UserManagementClient(borrowed.Session);
            fixture.Recreate();

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ChangePasswordAsync("old-marker", "new-marker").ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(borrowed.Invalidated.IsCompleted, Is.True);
        }

        /// <summary>
        /// A fresh confidential scope does not close or invalidate the borrowed session that supplied it.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02EncryptedBorrowedSessionPreservesOwnerAndArguments()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            await using SessionRequestContext borrowed =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var client = new UserManagementClient(borrowed.Session);

            await client.ChangePasswordAsync("old-marker", "new-marker").ConfigureAwait(false);

            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            var request = (CallRequest)fixture.Requests[0];
            Assert.That(request.MethodsToCall[0].InputArguments, Is.EqualTo(ExpectedArguments("ChangePassword")));
            Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(NodeId.Parse("s=original-token")));
            Assert.That(borrowed.Invalidated.IsCompleted, Is.False);
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Namespace changes cannot be hidden by promoting an older borrowed session to a fresh capture.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF02BorrowedNamespaceChangeCannotRetargetPasswords()
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            await using SessionRequestContext borrowed =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            var client = new UserManagementClient(borrowed.Session);
            fixture.InnerSession.NamespaceUris.Update(
                ["http://opcfoundation.org/UA/", "urn:request-context:replacement"]);

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ChangePasswordAsync("old-marker", "new-marker").ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// The security decision precedes a queued send, which must not follow identity or transport replacement.
        /// </summary>
        [Test]
        [Combinatorial]
        [Category("SecurityRegression")]
        public async Task SecurityF02ReplacementCannotReceivePasswords(
            [Values("Recreate", "Transport", "Downgrade")] string replacementKind,
            [Values("AddUser", "ModifyUser", "ModifyUserEmpty", "ChangePassword")] string passwordOperation)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            SetSuccessfulCalls(fixture);
            var client = new UserManagementClient(fixture.ManagedSession);
            EndpointDescription endpoint = fixture.Channel.Object.EndpointDescription;
            Task<AsyncReaderWriterLock.Releaser>? writer = null;
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(() =>
            {
                writer ??= fixture.ServiceGate.WriterLockAsync().AsTask();
                return endpoint;
            });
            var replacement = new Mock<ITransportChannel>();
            replacement.SetupGet(channel => channel.MessageContext).Returns(fixture.MessageContext);
            replacement.SetupGet(channel => channel.EndpointDescription).Returns(new EndpointDescription
            {
                EndpointUrl = endpoint.EndpointUrl,
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            Task operation = InvokeAsync(client, passwordOperation).AsTask();
            Assert.That(writer, Is.Not.Null, "The caller must classify the actual dispatch transport.");
            using (await writer!.ConfigureAwait(false))
            {
                Assert.That(operation.IsCompleted, Is.False);
                switch (replacementKind)
                {
                    case "Recreate":
                        fixture.Recreate();
                        break;
                    case "Transport":
                        fixture.ReplaceTransport(replacement.Object);
                        break;
                    default:
                        endpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                }
            }

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await operation.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(replacementKind == "Downgrade"
                ? StatusCodes.BadSecurityModeInsufficient
                : StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
            replacement.Verify(channel => channel.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Scripts successful method calls without bypassing generated service dispatch.
        /// </summary>
        private static void SetSuccessfulCalls(SessionRequestContextTestContext fixture)
        {
            fixture.Handler = static (_, _) => new ValueTask<IServiceResponse>(new CallResponse
            {
                Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
            });
        }

        /// <summary>
        /// Invokes each real password-bearing entry point, including the empty-password boundary.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="operation"/> is not a supported password-bearing operation.
        /// </exception>
        private static ValueTask InvokeAsync(UserManagementClient client, string operation)
        {
            return operation switch
            {
                "AddUser" => client.AddUserAsync(
                    "operator", "new-marker", UserConfigurationMask.Disabled, "account-marker"),
                "ModifyUser" => client.ModifyUserAsync(
                    "operator", "new-marker", UserConfigurationMask.Disabled, "account-marker"),
                "ModifyUserEmpty" => client.ModifyUserAsync("operator", string.Empty),
                "ChangePassword" => client.ChangePasswordAsync("old-marker", "new-marker"),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Gets the complete wire arguments expected for each public mutation.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="operation"/> is not a supported password-bearing operation.
        /// </exception>
        private static ArrayOf<Variant> ExpectedArguments(string operation)
        {
            return operation switch
            {
                "AddUser" =>
                [
                    Variant.From("operator"), Variant.From("new-marker"),
                    Variant.From((uint)UserConfigurationMask.Disabled), Variant.From("account-marker")
                ],
                "ModifyUser" =>
                [
                    Variant.From("operator"), Variant.From(true), Variant.From("new-marker"),
                    Variant.From(true), Variant.From((uint)UserConfigurationMask.Disabled),
                    Variant.From(true), Variant.From("account-marker")
                ],
                "ModifyUserEmpty" =>
                [
                    Variant.From("operator"), Variant.From(true), Variant.From(string.Empty),
                    Variant.From(false), Variant.From(0u), Variant.From(false), Variant.From(string.Empty)
                ],
                "ChangePassword" => [Variant.From("old-marker"), Variant.From("new-marker")],
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Gets the exact arguments of the metadata-only edit.
        /// </summary>
        private static ArrayOf<Variant> NonPasswordArguments()
        {
            return
            [
                Variant.From("operator"), Variant.From(false), Variant.From(string.Empty),
                Variant.From(true), Variant.From((uint)UserConfigurationMask.Disabled),
                Variant.From(true), Variant.From("disabled account")
            ];
        }
    }
}
