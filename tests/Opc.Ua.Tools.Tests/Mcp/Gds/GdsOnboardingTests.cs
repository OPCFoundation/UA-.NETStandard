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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using OnboardingMethods = Opc.Ua.Onboarding.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies encoded ticket references, per-ticket results and qualified registrar binding.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsOnboardingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task TicketOperationsBindProtectedByteStringsAndPreserveMixedPerTicketOutcomesAsync(bool remove)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("ticket", "first-sensitive-ticket");
            fixture.Store.Seed("ticket-b", "second-sensitive-ticket");
            string[] observed = [];
            fixture.Remote.OnCall = call =>
            {
                Assert.That(call.InputArguments[0].TryGetValue(out ArrayOf<ByteString> tickets), Is.True);
                observed = [.. tickets.ToArray()!.Select(ticket => Encoding.UTF8.GetString(ticket.Span))];
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                    OutputArguments =
                        [Variant.From([StatusCodes.Good, StatusCodes.BadEntryExists])]
                };
            };

            CallToolResult result = await fixture.InvokeAsync(
                remove ? "onboarding_remove_tickets" : "onboarding_register_tickets", new JsonObject
                {
                    ["registrarNodeId"] = fixture.Remote.Id("registrar").ToString(),
                    ["input"] = new JsonObject
                    {
                        ["ticketReferences"] = new JsonArray("alias:ticket-a", "alias:ticket-b")
                    }
                }).ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(observed, Is.EqualTo(s_expectedTickets));
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            Assert.That(json.GetProperty("results").EnumerateArray().Select(item =>
                item.GetProperty("statusCodeValue").GetUInt32()),
                Is.EqualTo([StatusCodes.Good.Code, StatusCodes.BadEntryExists.Code]));
            Assert.That(json.GetRawText(), Does.Not.Contain("first-sensitive-ticket")
                .And.Not.Contain("second-sensitive-ticket"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Calls.Single().MethodId, Is.EqualTo(fixture.Remote.Model(remove
                ? OnboardingMethods.DeviceRegistrarAdminType_UnregisterTickets
                : OnboardingMethods.DeviceRegistrarAdminType_RegisterTickets)));
            Assert.That(fixture.Store.Views, Has.Count.EqualTo(2));
            Assert.That(fixture.Store.Views.All(view => view.Disposed), Is.True);
        }

        [Test]
        public async Task RegistrarInspectionReturnsQualifiedAliasBindingAndDoesNotReadTicketsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Browse(fixture.Remote.Id("registrar"), ReferenceTypeIds.HierarchicalReferences,
                [fixture.Remote.Reference(
                    fixture.Remote.Id("authorities"), ObjectTypeIds.TrustListType, "Authorities")]);

            CallToolResult result = await fixture.Services.GetRequiredService<GdsOnboardingTools>()
                .InspectRegistrarAsync(
                fixture.Remote.Id("registrar").ToString(), sessionName: "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("resource").GetString(), Is.EqualTo(fixture.RegistrarResource));
            Assert.That(json.GetProperty("resource").GetString(), Does.Contain("nsu=urn:test:remote-model"));
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Remote.Id("authorities").ToString()));
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(101)]
        public async Task TicketBatchLimitsRefuseBeforeAnyUaOrSecretProviderWorkAsync(int count)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await fixture.Services.GetRequiredService<GdsOnboardingTools>()
                .RegisterTicketsAsync(
                fixture.Remote.Id("registrar").ToString(),
                new GdsTicketInput { TicketReferences = Enumerable.Repeat("alias:ticket-a", count).ToArrayOf() },
                "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidArgument);
            Assert.That(fixture.Store.Views, Is.Empty);
            Assert.That(fixture.Remote.Operations, Is.Empty);
        }

        [Test]
        public async Task MismatchedTicketResultCountReportsCommittedPartialOutcomeWithoutInventingResultsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("ticket", "ticket-a");
            fixture.Store.Seed("ticket-b", "ticket-b");
            fixture.Remote.Reply(StatusCodes.Good, Variant.From([StatusCodes.Good]));

            CallToolResult result = await fixture.Services.GetRequiredService<GdsOnboardingTools>()
                .RegisterTicketsAsync(
                fixture.Remote.Id("registrar").ToString(),
                new GdsTicketInput { TicketReferences = ["alias:ticket-a", "alias:ticket-b"] }, "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(json.GetProperty("outcome").GetString(), Is.EqualTo("invalidPerTicketResultCount"));
            Assert.That(json.GetProperty("expectedResults").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MixedAuthorizedAndForbiddenTicketReferencesRefuseTheEntireBatchBeforeDispatchAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter(GdsTestContext.Other);
            fixture.Store.Seed("ticket", "first-sensitive-ticket");
            fixture.Store.Seed("ticket-b", "second-sensitive-ticket");

            CallToolResult result = await fixture.Services.GetRequiredService<GdsOnboardingTools>()
                .RegisterTicketsAsync(fixture.Remote.Id("registrar").ToString(),
                    new GdsTicketInput { TicketReferences = ["alias:ticket-a", "alias:ticket-b"] }, "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Store.Views, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.Views[0].Disposed, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(),
                Does.Not.Contain("first-sensitive-ticket").And.Not.Contain("second-sensitive-ticket"));
        }

        private static readonly string[] s_expectedTickets = ["first-sensitive-ticket", "second-sensitive-ticket"];
    }
}
#endif
