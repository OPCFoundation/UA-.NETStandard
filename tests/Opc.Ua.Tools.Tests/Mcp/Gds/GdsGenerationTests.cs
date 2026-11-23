// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.IO;
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
    /// Proves old trust-list handles cannot cross a real ManagedSession's in-place identity replacement.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsGenerationTests
    {
        [Test]
        public async Task TrustListReadAndCleanupNeverDispatchTheOldHandleToAnInPlaceRecreatedSessionAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("trust"), ObjectTypeIds.TrustListType);
            NodeId originalId = fixture.Connection.Managed.SessionId;
            fixture.Remote.OnCall = request =>
            {
                Assert.That(request.MethodId, Is.EqualTo(fixture.Remote.Model(MethodIds.TrustListType_OpenWithMasks)));
                fixture.Connection.Recreate("new-session");
                return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = [Variant.From(41u)] };
            };

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCertificateTools>()
                .DownloadTrustListAsync(
                    fixture.Remote.Id("trust").ToString(), "forbidden.bin", sessionName: "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("sessionId").GetString(), Is.EqualTo(originalId.ToString()));
            Assert.That(json.GetProperty("readStatusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadSessionIdInvalid.Code));
            Assert.That(json.GetProperty("closeStatusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadSessionIdInvalid.Code));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Connection.Requests.OfType<CallRequest>().Count(), Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(fixture.Root!, "forbidden.bin")), Is.False);
        }

        [Test]
        public async Task UnmanagedRegisteredSessionIsRefusedInsteadOfClaimingGenerationSafeDispatchAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            await fixture.Manager.RegisterExistingSessionAsync(
                "replacement", fixture.Remote.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await fixture.Services.GetRequiredService<GdsDirectoryTools>().GetApplicationAsync(
                fixture.Remote.Id("directory").ToString(), fixture.Remote.Id("app").ToString(), "replacement")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }
    }
}
#endif
