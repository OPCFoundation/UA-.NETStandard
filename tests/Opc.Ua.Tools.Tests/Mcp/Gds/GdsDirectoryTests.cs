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
using Opc.Ua.Gds;
using Opc.Ua.Mcp;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies model-aware directory binding and actual server cursor and status semantics.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsDirectoryTests
    {
        [Test]
        public async Task QueryApplicationsPreservesActualCursorEpochAndTypedRecordsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            DateTimeUtc epoch = fixture.Clock.GetUtcNow().UtcDateTime;
            fixture.Remote.Reply(StatusCodes.GoodResultsMayBeIncomplete,
                Variant.From(epoch), Variant.From(918u),
                Variant.FromStructure(
                [
                    new ApplicationDescription
                    {
                        ApplicationUri = "urn:actual:result",
                        ApplicationName = new LocalizedText("de", "Dienst"),
                        ApplicationType = ApplicationType.ClientAndServer,
                        DiscoveryUrls = ["opc.tcp://example.invalid:4840"]
                    }
                ]));

            CallToolResult result = await Tools(fixture).QueryApplicationsAsync(
                fixture.Remote.Id("directory").ToString(), new GdsQueryInput
                {
                    StartingRecordId = 917,
                    MaxRecords = 1,
                    ApplicationName = "filter-name",
                    ApplicationUri = "urn:filter",
                    ProductUri = "urn:product",
                    ApplicationType = 2,
                    ServerCapabilities = ["DA"]
                }, "selected").ConfigureAwait(false);

            fixture.Remote.AssertCall(fixture.Remote.Id("directory"), GdsMethods.DirectoryType_QueryApplications,
                Variant.From(917u), Variant.From(1u), Variant.From("filter-name"), Variant.From("urn:filter"),
                Variant.From(2u), Variant.From("urn:product"), Variant.From(["DA"]));
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("nextRecordId").GetUInt32(), Is.EqualTo(918));
            Assert.That(json.GetProperty("lastCounterResetTime").GetDateTime(), Is.EqualTo((DateTime)epoch));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            JsonElement application = json.GetProperty("applications").GetProperty("items")[0];
            Assert.That(application.GetProperty("ApplicationUri").GetString(), Is.EqualTo("urn:actual:result"));
            Assert.That(application.GetProperty("ApplicationName").GetProperty("Locale").GetString(), Is.EqualTo("de"));
        }

        [Test]
        public async Task QueryServersKeepsServerRecordIdsAndResetEpochWithoutFabricatingACursorAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            DateTimeUtc epoch = fixture.Clock.GetUtcNow().UtcDateTime;
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(epoch),
                Variant.FromStructure(
                [
                    new ServerOnNetwork { RecordId = 817, ServerName = "Server A", ServerCapabilities = ["DA"] },
                    new ServerOnNetwork { RecordId = 920, ServerName = "Server B", ServerCapabilities = ["HD"] }
                ]));

            CallToolResult result = await Tools(fixture).QueryServersAsync(fixture.Remote.Id("directory").ToString(),
                new GdsQueryInput { StartingRecordId = 816, MaxRecords = 2 }, "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("servers").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("RecordId").GetUInt32()), Is.EqualTo(new uint[] { 817, 920 }));
            Assert.That(json.GetProperty("lastCounterResetTime").GetDateTime(), Is.EqualTo((DateTime)epoch));
            Assert.That(json.TryGetProperty("nextRecordId", out _), Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task FindAndGetPreserveFullApplicationRecordsAndNamesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var record = new ApplicationRecordDataType
            {
                ApplicationId = fixture.Remote.Id("record"),
                ApplicationUri = "urn:record",
                ApplicationNames = [new LocalizedText("fr", "Capteur")],
                DiscoveryUrls = ["opc.tcp://example.invalid:4840"],
                ServerCapabilities = ["DA", "LDS"]
            };
            fixture.Remote.Reply(StatusCodes.Good, Variant.FromStructure([record]));
            CallToolResult found = await Tools(fixture).FindApplicationsAsync(
                fixture.Remote.Id("directory").ToString(), "urn:record", sessionName: "selected").ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good, Variant.FromStructure(record));
            CallToolResult read = await Tools(fixture).GetApplicationAsync(
                fixture.Remote.Id("directory").ToString(), record.ApplicationId.ToString(), "selected")
                .ConfigureAwait(false);

            JsonElement first = RemoteModelTestSession.Payload(found)
                .GetProperty("applications").GetProperty("items")[0];
            JsonElement second = RemoteModelTestSession.Payload(read).GetProperty("application");
            Assert.That(first.GetRawText(), Is.EqualTo(second.GetRawText()));
            Assert.That(first.GetProperty("ApplicationNames")[0].GetProperty("Text").GetString(),
                Is.EqualTo("Capteur"));
            Assert.That(first.GetProperty("ServerCapabilities").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(s_serverCapabilities));
            Assert.That(fixture.Remote.Calls.Select(call => call.MethodId),
                Is.EqualTo(
                [
                    fixture.Remote.Model(GdsMethods.DirectoryType_FindApplications),
                    fixture.Remote.Model(GdsMethods.DirectoryType_GetApplication)
                ]));
        }

        [Test]
        public async Task UpdateAndUnregisterRemainSeparateExplicitOperationsWithExactApplicationIdAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Reply(StatusCodes.Good);
            string applicationId = fixture.Remote.Id("app").ToString();

            CallToolResult updated = await Tools(fixture).UpdateApplicationAsync(
                fixture.Remote.Id("directory").ToString(), new GdsApplicationInput
                {
                    ApplicationId = applicationId,
                    ApplicationUri = "urn:updated",
                    ProductUri = "urn:product",
                    ApplicationNames = [new GdsApplicationName { Locale = "en", Text = "Updated" }]
                }, "selected").ConfigureAwait(false);
            Assert.That(updated.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls[0].InputArguments[0].TryGetStructure<ApplicationRecordDataType>(
                fixture.Remote.Context, out ApplicationRecordDataType? application), Is.True);
            Assert.That(application!.ApplicationId.ToString(), Is.EqualTo(applicationId));
            Assert.That(application.DiscoveryUrls.IsEmpty, Is.True, "Update supplies an explicit empty replacement.");

            CallToolResult removed = await Tools(fixture).UnregisterApplicationAsync(
                fixture.Remote.Id("directory").ToString(), applicationId, "selected").ConfigureAwait(false);

            Assert.That(removed.IsError, Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Remote.Calls[1].MethodId,
                Is.EqualTo(fixture.Remote.Model(GdsMethods.DirectoryType_UnregisterApplication)));
            Assert.That(fixture.Remote.Calls[1].InputArguments[0], Is.EqualTo(Variant.From(fixture.Remote.Id("app"))));
        }

        [Test]
        public async Task RenamedSubtypeDirectoryUsesActualRootAndReleasesBrowseContinuationAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            NodeId root = fixture.Remote.Id("renamed");
            NodeId vendorType = fixture.Remote.Id("vendor-directory-type");
            fixture.Remote.Type(root, vendorType);
            fixture.Subtype(vendorType, Gds.ObjectTypeIds.DirectoryType);
            fixture.Remote.Browse(root, ReferenceTypeIds.HierarchicalReferences,
                [fixture.Remote.Reference(fixture.Remote.Id("first"), ObjectTypeIds.FolderType),
                    fixture.Remote.Reference(fixture.Remote.Id("second"), ObjectTypeIds.FolderType)],
                continuation: ByteString.From([41]));

            CallToolResult result = await Tools(fixture).InspectDirectoryAsync(
                root.ToString(), maxResults: 1, sessionName: "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Remote.Id("first").ToString()));
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(fixture.Remote.Continuations.Single().Release, Is.True);
            Assert.That(fixture.Remote.Continuations.Single().Point, Is.EqualTo(ByteString.From([41])));
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task UnencryptedNamedSessionIsRefusedBeforeAnyModelOrMethodDispatchAsync(MessageSecurityMode mode)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(mode: mode).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await Tools(fixture).UnregisterApplicationAsync(
                fixture.Remote.Id("directory").ToString(), fixture.Remote.Id("app").ToString(), "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadSecurityModeInsufficient);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        [TestCase(0u)]
        [TestCase(501u)]
        public async Task InvalidWirePageLimitIsRejectedBeforeRemoteQueryAsync(uint maximum)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await Tools(fixture).QueryApplicationsAsync(
                fixture.Remote.Id("directory").ToString(), new GdsQueryInput { MaxRecords = maximum }, "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidArgument);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        /// <summary>
        /// Gets the domain tool through its real DI registration.
        /// </summary>
        private static GdsDirectoryTools Tools(GdsTestContext fixture)
        {
            return fixture.Services.GetRequiredService<GdsDirectoryTools>();
        }

        private static readonly string[] s_serverCapabilities = ["DA", "LDS"];
    }
}
#endif
