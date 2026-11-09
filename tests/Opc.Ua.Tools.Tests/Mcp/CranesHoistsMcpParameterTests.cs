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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Exercises the public tool boundary's input validation, cancellation and shared-session error behavior.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class CranesHoistsMcpParameterTests
    {
        /// <summary>
        /// Every configurable output budget is validated before the session accessor can be used.
        /// </summary>
        [TestCase("list", 1023)]
        [TestCase("list", 1048577)]
        [TestCase("system", 1023)]
        [TestCase("system", 1048577)]
        [TestCase("element", 1023)]
        [TestCase("element", 1048577)]
        [TestCase("values", 1023)]
        [TestCase("values", 1048577)]
        [TestCase("events", 1023)]
        [TestCase("events", 1048577)]
        public async Task InvalidResponseBudgetsFailBeforeSessionAccess(string tool, int bytes)
        {
            CallToolResult response = await (tool switch
            {
                "list" => CranesHoistsTools.ListSystemsAsync(null!, maximumResponseBytes: bytes),
                "system" => CranesHoistsTools.ReadSystemAsync(null!, "i=1", maximumResponseBytes: bytes),
                "element" => CranesHoistsTools.ReadElementAsync(null!, "i=1", maximumResponseBytes: bytes),
                "values" => CranesHoistsTools.ObserveValuesAsync(null!, Json("[\"i=1\"]"),
                    maximumResponseBytes: bytes),
                _ => CranesHoistsTools.ObserveEventsAsync(null!, "i=1", maximumResponseBytes: bytes)
            }).ConfigureAwait(false);
            AssertError(response, "BadOutOfRange");
        }

        /// <summary>
        /// Inventory count limits cannot be used to request unbounded traversal.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(101)]
        public async Task InvalidInventoryCountsFailBeforeSessionAccess(int maximum)
        {
            AssertError(await CranesHoistsTools.ListSystemsAsync(null!, maximumResults: maximum)
                .ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Both observation tools reuse the typed client's hard duration/notification limits.
        /// </summary>
        [TestCase(true, 0, 50)]
        [TestCase(true, 61, 50)]
        [TestCase(true, 5, 0)]
        [TestCase(true, 5, 101)]
        [TestCase(false, 0, 50)]
        [TestCase(false, 61, 50)]
        [TestCase(false, 5, 0)]
        [TestCase(false, 5, 101)]
        public async Task ObservationBoundsFailBeforeResourceAllocation(bool events, int seconds, int count)
        {
            AssertError(await (events
                ? CranesHoistsTools.ObserveEventsAsync(null!, "i=2253",
                    durationSeconds: seconds, maximumNotifications: count)
                : CranesHoistsTools.ObserveValuesAsync(null!, Json("[\"i=1\"]"),
                    durationSeconds: seconds, maximumNotifications: count)).ConfigureAwait(false),
                "BadOutOfRange");
        }

        /// <summary>
        /// Malformed arrays, null nodes and equivalent duplicate identities are rejected without subscriptions.
        /// </summary>
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("[42]")]
        [TestCase("[null]")]
        [TestCase("[\"i=0\"]")]
        [TestCase("[\"i=1\",\"ns=0;i=1\"]")]
        public async Task InvalidNodeArraysFailBeforeSessionAccess(string nodes)
        {
            AssertError(await CranesHoistsTools.ObserveValuesAsync(null!, Json(nodes)).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// The array's cardinality cap is applied before parsing or allocating remote resources.
        /// </summary>
        [Test]
        public async Task MoreThanSixtyFourNodeIdsIsRejected()
        {
            var nodes = new JsonArray();
            for (int i = 1; i <= 65; i++)
            {
                nodes.Add("i=" + i);
            }
            AssertError(await CranesHoistsTools.ObserveValuesAsync(null!, Json(nodes.ToJsonString()))
                .ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Empty and null node identifiers are not implicit selectors for another node.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("i=0")]
        public async Task EmptyNodeSelectorsFailBeforeSessionAccess(string node)
        {
            AssertError(await CranesHoistsTools.ReadElementAsync(null!, node).ConfigureAwait(false),
                "BadInvalidArgument");
            AssertError(await CranesHoistsTools.ListSystemsAsync(null!, rootNodeId: node).ConfigureAwait(false),
                "BadInvalidArgument");
        }

        /// <summary>
        /// Excessively large NodeIds cannot expand request or response envelopes.
        /// </summary>
        [Test]
        public async Task OversizedNodeSelectorsFailBeforeSessionAccess()
        {
            AssertError(await CranesHoistsTools.ReadElementAsync(null!, "s=" + new string('x', 1024))
                .ConfigureAwait(false), "BadOutOfRange");
        }

        /// <summary>
        /// Configuration parses only exact named request selectors, never numeric enum values or active state.
        /// </summary>
        [TestCase("")]
        [TestCase("13")]
        [TestCase("speedLimitDirPlusSetpoint")]
        [TestCase("SpeedLimitDirPlus")]
        [TestCase("SpeedLimitDirPlusSetpoint,SpeedLimitDirMinusSetpoint")]
        public async Task UnknownConfigurationSelectorsFailBeforeSessionAccess(string signal)
        {
            AssertError(await CranesHoistsTools.ConfigureLimitAsync(null!, "i=1", signal, Json("25"),
                confirmConfiguration: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Boolean configuration requests do not silently coerce numbers, strings or null.
        /// </summary>
        [TestCase("0")]
        [TestCase("1")]
        [TestCase("\"true\"")]
        [TestCase("null")]
        public async Task EnableRequestsRequireNativeBooleans(string value)
        {
            AssertError(await CranesHoistsTools.ConfigureLimitAsync(null!, "i=1", "SpeedLimitEnabledDirPlus",
                Json(value), confirmConfiguration: true).ConfigureAwait(false), "BadInvalidArgument");
        }

        /// <summary>
        /// Consent is checked before parsing or acquiring any device/session resources.
        /// </summary>
        [Test]
        public async Task DefaultConfigurationConsentIsDenied()
        {
            AssertError(await CranesHoistsTools.ConfigureLimitAsync(null!, string.Empty, string.Empty, default)
                .ConfigureAwait(false), "BadUserAccessDenied");
        }

        /// <summary>
        /// Pre-cancelled operations keep their original token and never acquire the accessor.
        /// </summary>
        [TestCase("list")]
        [TestCase("system")]
        [TestCase("element")]
        [TestCase("values")]
        [TestCase("events")]
        [TestCase("configure")]
        public async Task PreCancellationIsNotConvertedToAFalseSuccess(string tool)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException error = (await Assert.CatchAsync<OperationCanceledException>(async () =>
                await Invoke(tool, null!, cancellation.Token).ConfigureAwait(false)))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        /// <summary>
        /// Each valid workflow reports shared-session failure rather than an empty successful result.
        /// </summary>
        [TestCase("list")]
        [TestCase("system")]
        [TestCase("element")]
        [TestCase("values")]
        [TestCase("events")]
        [TestCase("configure")]
        public async Task UnconnectedWorkflowsReturnExplicitSessionErrors(string tool)
        {
            await using CranesHoistsMcpHarness harness = await CranesHoistsMcpHarness.CreateAsync()
                .ConfigureAwait(false);
            AssertError(await Invoke(tool, harness.Accessor, CancellationToken.None).ConfigureAwait(false),
                "BadInvalidState");
        }

        /// <summary>
        /// Missing model discovery uses the typed client's explicit absence semantics without browsing or writing.
        /// </summary>
        [Test]
        public async Task MissingModelIsExplicitAndDoesNotUseRawServices()
        {
            Mock<ISession> session = Session();
            await using (CranesHoistsMcpHarness harness =
                await CranesHoistsMcpHarness.CreateAsync(session.Object).ConfigureAwait(false))
            {
                CallToolResult result = await harness.CallAsync("craneshoists_list_systems").ConfigureAwait(false);
                Assert.That(result.IsError, Is.False);
                JsonElement json = CranesHoistsMcpHarness.Json(result);
                Assert.That(json.GetProperty("modelPresent").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("systems").GetArrayLength(), Is.Zero);
                var direct = new CranesHoistsClientAccessor(harness.Manager);
                Assert.That(direct.CreateClient().Session, Is.SameAs(session.Object));
                Assert.Throws<ArgumentOutOfRangeException>(() => new CranesHoistsClientAccessor(
                    harness.Manager, new CranesHoistsClientOptions { MaximumNodes = 0 }));
            }
            session.Verify(value => value.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.WriteAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.Dispose(), Times.Never);
            session.Verify(value => value.DisposeAsync(), Times.Never);
        }

        /// <summary>
        /// Named/ambiguous sessions use the shared session manager, not a package-local connection registry.
        /// </summary>
        [Test]
        public async Task UnknownAndAmbiguousSessionNamesAreExplicitErrors()
        {
            Mock<ISession> session = Session();
            await using CranesHoistsMcpHarness harness =
                await CranesHoistsMcpHarness.CreateAsync(session.Object).ConfigureAwait(false);
            AssertError(await harness.CallAsync("craneshoists_list_systems",
                new JsonObject { ["sessionName"] = "missing" }).ConfigureAwait(false), "BadInvalidState");
            await harness.Manager.RegisterExistingSessionAsync("second", session.Object, "Anonymous")
                .ConfigureAwait(false);
            AssertError(await harness.CallAsync("craneshoists_list_systems").ConfigureAwait(false), "BadInvalidState");
            CallToolResult selected = await harness.CallAsync("craneshoists_list_systems",
                new JsonObject { ["sessionName"] = "crane" }).ConfigureAwait(false);
            Assert.That(selected.IsError, Is.False);
            Assert.That(CranesHoistsMcpHarness.Json(selected).GetProperty("modelPresent").GetBoolean(), Is.False);
        }

        /// <summary>
        /// The core request filter sees the new tools' real required arguments before the generated binder.
        /// </summary>
        [Test]
        public async Task MissingRequiredArgumentsProduceActionableMcpErrors()
        {
            await using CranesHoistsMcpHarness harness = await CranesHoistsMcpHarness.CreateAsync()
                .ConfigureAwait(false);
            CallToolResult result = await harness.CallAsync("craneshoists_configure_limit").ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            string message = result.Content.OfType<TextContentBlock>().Single().Text;
            Assert.That(message, Does.Contain("elementNodeId").And.Contain("signal").And.Contain("value"));
            Assert.That(message, Does.Contain("missing required"));
        }

        /// <summary>
        /// Selects one public workflow using valid arguments, preserving its cancellation token.
        /// </summary>
        private static Task<CallToolResult> Invoke(
            string tool, CranesHoistsClientAccessor accessor, CancellationToken cancellationToken)
        {
            return tool switch
            {
                "list" => CranesHoistsTools.ListSystemsAsync(accessor, ct: cancellationToken),
                "system" => CranesHoistsTools.ReadSystemAsync(accessor, "i=1", ct: cancellationToken),
                "element" => CranesHoistsTools.ReadElementAsync(accessor, "i=1", ct: cancellationToken),
                "values" => CranesHoistsTools.ObserveValuesAsync(accessor, Json("[\"i=1\"]"), ct: cancellationToken),
                "events" => CranesHoistsTools.ObserveEventsAsync(accessor, "i=1", ct: cancellationToken),
                _ => CranesHoistsTools.ConfigureLimitAsync(accessor, "i=1", "SpeedLimitDirPlusSetpoint", Json("25"),
                    confirmConfiguration: true, ct: cancellationToken)
            };
        }

        /// <summary>
        /// Creates only the connected-session boundary needed for model-absence behavior.
        /// </summary>
        private static Mock<ISession> Session()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(
                new ConfiguredEndpoint(null, new EndpointDescription(), EndpointConfiguration.Create()));
            session.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
            return session;
        }

        /// <summary>
        /// Parses a test input without sharing a disposed JsonDocument.
        /// </summary>
        private static JsonElement Json(string value)
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Requires both the MCP failure flag and a precise OPC UA error category.
        /// </summary>
        private static void AssertError(CallToolResult result, string code)
        {
            Assert.That(result.IsError, Is.True);
            JsonElement json = CranesHoistsMcpHarness.Json(result);
            Assert.That(json.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("statusCode").GetString(), Does.StartWith(code));
        }
    }
}
#endif
