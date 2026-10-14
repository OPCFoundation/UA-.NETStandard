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

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Protects size, generation and optional-metadata boundaries discovered during the test-gap review.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsBoundaryTests
    {
        [Test]
        public async Task OversizedGrantedRolesRetainCommittedRequestAndUsableProtectedReferenceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Services.GetRequiredService<GdsMcpOptions>().MaxResponseBytes = 4096;
            var roles = Enumerable.Range(0, 200)
                .Select(index => new NodeId(new string('r', 100) + index, fixture.Remote.InstanceNamespace))
                .ToArrayOf();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From("issued-id"), Variant.From(ByteString.From([17, 19])),
                Variant.From("public-thumbprint"), Variant.From(SecurityPolicies.Basic256Sha256), Variant.From(roles));

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCredentialTools>()
                .FinishKeyRequestAsync(
                fixture.Remote.Id("key-service").ToString(), fixture.Remote.Id("request").ToString(), "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True,
                "Omitted details must be explicit, not mistaken for a complete result.");
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(json.GetProperty("detailsOmitted").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(fixture.Remote.Id("request").ToString()));
            Assert.That(json.GetProperty("credentialId").GetString(), Is.EqualTo("issued-id"));
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("grantedRoleCount").GetInt32(), Is.EqualTo(200));
            string reference = json.GetProperty("credential").GetProperty("reference").GetString()!;
            using ISecret secret = await fixture.Secrets.ResolveAsync(
                reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(secret.Bytes.ToArray(), Is.EqualTo(new byte[] { 17, 19 }));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json.GetRawText()), Is.LessThanOrEqualTo(4096));
        }

        [Test]
        public async Task RecreatedServerSessionCannotFinishAnOldTokenRequestEvenWithSameRegistryEntryAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var requestId = Guid.NewGuid();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(default(ByteString)), Variant.From((Uuid)requestId));
            GdsCredentialTools tools = fixture.Services.GetRequiredService<GdsCredentialTools>();
            CallToolResult started = await tools.StartTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), "urn:resource", "jwt", sessionName: "selected")
                .ConfigureAwait(false);
            Assert.That(started.IsError, Is.False);
            OpcUaSessionManager.SessionInfo registration = fixture.Manager.GetSessionInfoOrThrow("selected");
            fixture.Connection.Recreate("recreated-session");

            CallToolResult result = await tools.FinishTokenRequestAsync(
                fixture.Remote.Id("auth-service").ToString(), requestId.ToString(),
                new GdsTokenFinishInput(), "selected")
                .ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSessionInfoOrThrow("selected"), Is.SameAs(registration));
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        [Test]
        public async Task PushDiagnosticsPreserveTargetAndStatusWhileOmittingProviderMessageTextAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            NodeId diagnostics = fixture.Remote.Id("diagnostics");
            fixture.Remote.Child(fixture.Remote.Id("configuration"), new QualifiedName("TransactionDiagnostics"),
                diagnostics);
            fixture.Remote.Property(diagnostics, new QualifiedName("Errors"),
                Variant.FromStructure(
                [
                    new TransactionErrorType
                    {
                        TargetId = fixture.Remote.Id("group"), Error = StatusCodes.BadCertificateInvalid,
                        Message = new LocalizedText("provider leaked private-key-password")
                    }
                ]));

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCertificateTools>().InspectPushAsync(
                fixture.Remote.Id("configuration").ToString(), sessionName: "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("transactionErrors")[0].GetProperty("targetId").GetString(),
                Is.EqualTo(fixture.Remote.Id("group").ToString()));
            Assert.That(json.GetProperty("transactionErrors")[0].GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadCertificateInvalid.Code));
            Assert.That(json.GetProperty("transactionErrorCount").GetInt32(), Is.EqualTo(1));
            Assert.That(json.GetRawText(), Does.Not.Contain("private-key-password"));
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(121)]
        public void OperationDeadlineRejectsZeroAndBeyondTwoMinutes(int seconds)
        {
            var options = new GdsMcpOptions { OperationTimeout = TimeSpan.FromSeconds(seconds) };

            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo(nameof(GdsMcpOptions.OperationTimeout)));
        }

        [Test]
        public async Task SessionGrantUsesActualRegistrationNameRatherThanCallerSuppliedCasingAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Services.GetRequiredService<GdsDirectoryTools>()
                .UnregisterApplicationAsync(fixture.Remote.Id("directory").ToString(),
                    fixture.Remote.Id("app").ToString(), "SeLeCtEd").ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("sessionName").GetString(),
                Is.EqualTo("selected"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls[0].MethodId,
                Is.EqualTo(fixture.Remote.Model(Gds.MethodIds.DirectoryType_UnregisterApplication)));
        }

        [Test]
        public async Task UngrantedRegistrationCannotBorrowTheSameManagedSessionThroughAnotherNameAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            await fixture.Manager.RegisterExistingSessionAsync("ungranted", fixture.Connection.Managed, "Anonymous")
                .ConfigureAwait(false);

            CallToolResult result = await fixture.Services.GetRequiredService<GdsDirectoryTools>()
                .UnregisterApplicationAsync(fixture.Remote.Id("directory").ToString(),
                    fixture.Remote.Id("app").ToString(), "ungranted").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Operations, Is.Empty);
            Assert.That(fixture.Connection.Requests, Is.Empty);
        }
    }
}
#endif
