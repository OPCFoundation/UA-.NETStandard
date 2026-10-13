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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Verifies generation binding across actual generic-tool classification and mutation dispatch.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class McpGenericGenerationTests
    {
        /// <summary>
        /// Reproduces the old unscoped sequence: a valid ordinary preflight can write into the replacement session.
        /// </summary>
        [Test]
        public async Task UnscopedManagedPreflightDemonstratesTheOriginalRetargetingRace()
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            fixture.PauseAfterClassifying = new NodeId(100u, 1);
            OpcUaSessionManager.SessionInfo registration = fixture.Manager.GetSessionInfoOrThrow();
            Task<WriteResponse> pending = UnscopedWriteAsync();
            try
            {
                await fixture.ClassificationReached.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(fixture.ClassifiedNamespace, Is.EqualTo(McpGenericGenerationTestContext.OrdinaryNamespace));
                fixture.RecreateAsProtectedModel();
            }
            finally
            {
                fixture.ReleaseClassification();
            }
            _ = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Mutations, Has.Count.EqualTo(1));
            Assert.That(fixture.Mutations[0], Is.TypeOf<WriteRequest>());
            Assert.That(fixture.Mutations[0].RequestHeader.AuthenticationToken,
                Is.EqualTo(NodeId.Parse("s=replacement-authentication")));

            async Task<WriteResponse> UnscopedWriteAsync()
            {
                ArrayOf<WriteValue> writes =
                [
                    new WriteValue
                    {
                        NodeId = new NodeId(100u, 1),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(17))
                    }
                ];
                await fixture.Manager.UaOperationGuard.AuthorizeWritesAsync(registration, writes).ConfigureAwait(false);
                return await registration.Session.WriteAsync(null, writes, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The service view preserves registered authority and bounds a read preflight and subsequent mutation.
        /// </summary>
        [Test]
        public async Task OperationViewPreservesRegisteredMetadataAndBindsReadPreflight()
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            OpcUaSessionManager.SessionInfo registration = fixture.Manager.GetSessionInfoOrThrow();
            await using McpMutationSession operation = await McpMutationSession.CaptureAsync(
                fixture.Manager, registration, CancellationToken.None).ConfigureAwait(false);
            Assert.That(operation.Access.Name, Is.EqualTo(McpGenericGenerationTestContext.RegistrationName));
            Assert.That(operation.Access.AuthType, Is.EqualTo("UserName"));
            Assert.That(operation.Access.ConnectedAt, Is.EqualTo(registration.ConnectedAt));
            Assert.That(operation.Access.Endpoint.EndpointUrl, Is.EqualTo(registration.Endpoint.EndpointUrl));
            Assert.That(operation.Access.OwnsSession, Is.False);
            Assert.That(operation.Access.Session, Is.Not.SameAs(fixture.Managed));

            ReadResponse read = await operation.Access.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = new NodeId(100u, 1), AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(17));
            fixture.RecreateAsProtectedModel();
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.Access.Session.WriteAsync(
                    null,
                    [new WriteValue { NodeId = new NodeId(100u, 1), Value = new DataValue(Variant.From(18)) }],
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Mutations, Is.Empty);
        }

        /// <summary>
        /// Replacing a host name withdraws an old registration without authorizing either connection by accident.
        /// </summary>
        [Test]
        public async Task NamedRegistrationReplacementInvalidatesThePreflight()
        {
            await using McpGenericGenerationTestContext original =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            await using McpGenericGenerationTestContext replacement =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            original.PauseAfterClassifying = new NodeId(100u, 1);
            Task<string> pending = InvokeAsync(original.Manager, "WriteValue");
            try
            {
                await original.ClassificationReached.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                _ = await original.Manager.RegisterExistingSessionAsync(
                    McpGenericGenerationTestContext.RegistrationName, replacement.Managed, "Certificate")
                    .ConfigureAwait(false);
            }
            finally
            {
                original.ReleaseClassification();
            }
            string json = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("BadSessionIdInvalid"));
            Assert.That(original.Mutations, Is.Empty);
            Assert.That(replacement.Mutations, Is.Empty);
            Assert.That(original.Managed.Connected, Is.True);
            Assert.That(original.Manager.GetSessionOrThrow(), Is.SameAs(replacement.Managed));
        }

        /// <summary>
        /// An ordinary preflight result cannot authorize a mutation on a recreated protected namespace.
        /// </summary>
        [TestCase("Call")]
        [TestCase("CallMethod")]
        [TestCase("Write")]
        [TestCase("WriteValue")]
        [TestCase("HistoryUpdate")]
        [TestCase("AddNodes")]
        [TestCase("DeleteNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteReferences")]
        public async Task RecoveryAfterClassificationNeverDispatchesToReplacement(string operation)
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            fixture.PauseAfterClassifying = FinalTarget(operation);
            Session original = fixture.Inner;
            Task<string> pending = InvokeAsync(fixture.Manager, operation);
            try
            {
                await fixture.ClassificationReached.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(fixture.ClassifiedNamespace, Is.EqualTo(McpGenericGenerationTestContext.OrdinaryNamespace));
                Assert.That(fixture.Mutations, Is.Empty);
                fixture.RecreateAsProtectedModel();
            }
            finally
            {
                fixture.ReleaseClassification();
            }
            string json = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Inner, Is.SameAs(original));
            Assert.That(fixture.Inner.NamespaceUris.GetString(1),
                Is.EqualTo(McpGenericGenerationTestContext.ProtectedNamespace));
            Assert.That(fixture.Mutations, Is.Empty,
                "The replacement must receive no mutation authorized using the old namespace/model classification.");
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(),
                Is.EqualTo("BadSessionIdInvalid"));
        }

        /// <summary>
        /// Recovery after the tool's own last check still cannot retarget a mutation queued at managed dispatch.
        /// </summary>
        [TestCase("Call")]
        [TestCase("CallMethod")]
        [TestCase("Write")]
        [TestCase("WriteValue")]
        [TestCase("HistoryUpdate")]
        [TestCase("AddNodes")]
        [TestCase("DeleteNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteReferences")]
        public async Task RecoveryWhileMutationWaitsForDispatchNeverRetargets(string operation)
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            fixture.PauseAfterClassifying = FinalTarget(operation);
            Task<string> pending = InvokeAsync(fixture.Manager, operation);
            try
            {
                await fixture.ClassificationReached.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await fixture.PauseMutationDispatchAsync().ConfigureAwait(false);
                fixture.ReleaseClassification();
                await fixture.MutationDispatchWaiting.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(fixture.Managed.SessionId, Is.EqualTo(McpGenericGenerationTestContext.OriginalSessionId));
                fixture.RecreateAsProtectedModel();
            }
            finally
            {
                fixture.ReleaseClassification();
                fixture.ReleaseMutationDispatch();
            }
            string json = await pending.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(fixture.Mutations, Is.Empty);
            using var document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("error").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("statusCode").GetString(), Is.EqualTo("BadSessionIdInvalid"));
        }

        /// <summary>
        /// Same-generation operations retain their established request, result and registration behavior.
        /// </summary>
        [TestCase("Call")]
        [TestCase("CallMethod")]
        [TestCase("Write")]
        [TestCase("WriteValue")]
        [TestCase("HistoryUpdate")]
        [TestCase("AddNodes")]
        [TestCase("DeleteNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteReferences")]
        public async Task SameGenerationPreservesMutationResultsAndBorrowedOwner(string operation)
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            string json = await InvokeAsync(fixture.Manager, operation).ConfigureAwait(false);
            AssertSuccessfulResult(json, operation);
            Assert.That(fixture.Mutations, Has.Count.EqualTo(1));
            Assert.That(fixture.Mutations[0].RequestHeader.AuthenticationToken,
                Is.EqualTo(McpGenericGenerationTestContext.OriginalAuthentication));
            Assert.That(fixture.Managed.SessionId, Is.EqualTo(McpGenericGenerationTestContext.OriginalSessionId));
            Assert.That(fixture.Managed.Connected, Is.True);
            Assert.That(fixture.Manager.GetSessionInfoOrThrow().Name,
                Is.EqualTo(McpGenericGenerationTestContext.RegistrationName));
            Assert.That(fixture.Manager.GetSessionOrThrow(), Is.SameAs(fixture.Managed));
        }

        /// <summary>
        /// A known acknowledged result is not discarded when cancellation and recreation follow its effect.
        /// </summary>
        [TestCase("Call")]
        [TestCase("CallMethod")]
        [TestCase("Write")]
        [TestCase("WriteValue")]
        [TestCase("HistoryUpdate")]
        [TestCase("AddNodes")]
        [TestCase("DeleteNodes")]
        [TestCase("AddReferences")]
        [TestCase("DeleteReferences")]
        public async Task AcknowledgedMutationSurvivesLateCancellationAndRecovery(string operation)
        {
            await using McpGenericGenerationTestContext fixture =
                await McpGenericGenerationTestContext.CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.MutationAcknowledged = () =>
            {
                fixture.RecreateAsProtectedModel();
                cancellation.Cancel();
            };
            string json = await InvokeAsync(fixture.Manager, operation, cancellation.Token).ConfigureAwait(false);
            AssertSuccessfulResult(json, operation);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(fixture.Mutations, Has.Count.EqualTo(1));
            Assert.That(fixture.Mutations[0].RequestHeader.AuthenticationToken,
                Is.EqualTo(McpGenericGenerationTestContext.OriginalAuthentication));
        }

        /// <summary>
        /// Invokes the actual public tool entry point with one ordinary target.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static Task<string> InvokeAsync(
            OpcUaSessionManager manager, string operation, CancellationToken ct = default)
        {
            const string source = "ns=1;i=100";
            const string target = "ns=1;i=101";
            return operation switch
            {
                "Call" => MethodServiceTools.CallAsync(manager, source, target, ["input"], ct: ct),
                "CallMethod" => ConvenienceTools.CallMethodAsync(manager, source, target, ["input"], ct: ct),
                "Write" => AttributeServiceTools.WriteAsync(manager, [source], ["17"], ct: ct),
                "WriteValue" => ConvenienceTools.WriteValueAsync(manager, source, "17", "Int32", ct: ct),
                "HistoryUpdate" => AttributeServiceTools.HistoryUpdateAsync(
                    manager, source, ["2026-01-01T00:00:00Z"], ["17"], ct: ct),
                "AddNodes" => NodeManagementServiceTools.AddNodesAsync(
                    manager, source, "i=35", "1:Created", "Object", ct: ct),
                "DeleteNodes" => NodeManagementServiceTools.DeleteNodesAsync(manager, [source], ct: ct),
                "AddReferences" => NodeManagementServiceTools.AddReferencesAsync(
                    manager, source, "i=35", target, ct: ct),
                "DeleteReferences" => NodeManagementServiceTools.DeleteReferencesAsync(
                    manager, source, "i=35", target, ct: ct),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        /// <summary>
        /// Identifies the last target classified by each public tool.
        /// </summary>
        private static NodeId FinalTarget(string operation)
        {
            return new NodeId(operation is "Call" or "CallMethod" or "AddReferences" or "DeleteReferences"
                ? 101u : 100u, 1);
        }

        /// <summary>
        /// Pins caller-visible values and non-default status results instead of merely checking for JSON.
        /// </summary>
        private static void AssertSuccessfulResult(string json, string operation)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement result = document.RootElement;
            Assert.That(result.TryGetProperty("error", out _), Is.False);
            switch (operation)
            {
                case "Call":
                    Assert.That(result.GetProperty("statusCode").GetString(),
                        Is.EqualTo("GoodCompletesAsynchronously"));
                    Assert.That(result.GetProperty("outputArguments")[0].GetInt32(), Is.EqualTo(17));
                    break;
                case "CallMethod":
                    Assert.That(result.GetProperty("outputArguments")[0].GetInt32(), Is.EqualTo(17));
                    break;
                case "Write":
                    Assert.That(result.GetProperty("results")[0].GetString(), Is.EqualTo("GoodClamped"));
                    break;
                case "WriteValue":
                    Assert.That(result.GetProperty("statusCode").GetString(), Is.EqualTo("GoodClamped"));
                    break;
                case "HistoryUpdate":
                    Assert.That(result.GetProperty("results")[0].GetProperty("operationResults")[0].GetString(),
                        Is.EqualTo("BadNoEntryExists"));
                    break;
                case "AddNodes":
                    Assert.That(result.GetProperty("results")[0].GetProperty("addedNodeId").GetString(),
                        Is.EqualTo("ns=1;i=201"));
                    break;
                default:
                    Assert.That(result.GetProperty("results")[0].GetString(), Is.EqualTo("Good"));
                    break;
            }
        }
    }
}
#endif
