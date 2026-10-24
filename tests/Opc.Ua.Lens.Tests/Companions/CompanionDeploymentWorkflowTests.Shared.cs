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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

namespace UaLens.Tests.Companions
{
    public sealed partial class CompanionDeploymentWorkflowTests
    {

        private static void AddAlternateSelections(CompanionPreparedOperationTestContext context)
        {
            context.Targets = [context.Target, context.Target with { NodeId = new NodeId(4321u, 2) }];
            context.Inspection = context.Inspection with
            {
                Operations = [context.Operation, new("refresh", "Refresh", CompanionOperationSafety.ReadOnly)]
            };
        }

        private static async Task InitializeDocumentAsync(
            CompanionPlugin document, CompanionPreparedOperationTestContext context)
        {
            await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(false);
            document.IsOffline = false;
            await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(false);
            await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(false);
            document.OperationInput = CompanionDeploymentTestData.Input;
        }

        private static void ChangeDocumentSelection(CompanionPlugin document, string change)
        {
            switch (change)
            {
                case "input":
                    document.OperationInput = "recipe=9";
                    break;
                case "operation":
                    document.SelectedOperation = document.Operations[1];
                    break;
                case "target":
                    document.SelectedTarget = document.Targets[1];
                    break;
                case "provider":
                    document.SelectedProvider = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        private static JsonElement DocumentState()
        {
            return JsonSerializer.SerializeToElement(
                new CompanionDocumentState(1, "sample", "nsu=urn:ualens:test;i=1234"),
                CompanionJsonContext.Default.CompanionDocumentState);
        }

        private static bool HasExpectedTypedInputs(CompanionOperationDraft draft)
        {
            ArrayOf<CompanionValue> inputs = draft.Inputs;
            return inputs.Count == 5 &&
                inputs[0].Name == "label" &&
                inputs[0].Value.TryGetValue(out string label) &&
                label == "unreviewed-label" &&
                inputs[2].Name == "count" &&
                inputs[2].Value.TryGetValue(out uint count) &&
                count == 19u;
        }

        private static void ConfigurePolicy(
            Mock<ICompanionDeploymentPolicy> policy, CompanionPreparedOperationTestContext context)
        {
            var configured = new ConfiguredCompanionDeploymentPolicy(
                [CompanionDeploymentTestData.CreateRule(context)], context.Clock.Object);
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                .Returns((CompanionContext borrowed, CompanionOperationDraft draft, CancellationToken token) =>
                    configured.AuthorizeAsync(borrowed, draft, token));
        }

        private static void ReturnGrant(
            Mock<ICompanionDeploymentPolicy> policy, CompanionDeploymentGrant? grant)
        {
            policy.Setup(value => value.AuthorizeAsync(
                    It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(grant!));
        }

        private static CompanionDeploymentGrant? FaultyGrant(
            CompanionPreparedOperationTestContext context, DateTimeOffset maximumExpiry, string fault)
        {
            var grant = new CompanionDeploymentGrant("plant-rule", "revision-7", maximumExpiry);
            return fault switch
            {
                "rule" => grant with { RuleId = "another-rule" },
                "rule-case" => grant with { RuleId = "Plant-rule" },
                "revision" => grant with { Revision = "revision-8" },
                "revision-case" => grant with { Revision = "Revision-7" },
                "null" => null,
                "expired" => grant with { ExpiresAt = context.UtcNow.AddTicks(-1) },
                "deadline" => grant with { ExpiresAt = context.UtcNow },
                "overlong" => grant with { ExpiresAt = maximumExpiry.AddTicks(1) },
                "empty-rule" => grant with { RuleId = string.Empty },
                "invalid-revision" => grant with { Revision = "../revision" },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };
        }

        private static async Task AssertConsumedAsync(
            CompanionPreparedOperationTestContext context, CompanionOperationDraft draft)
        {
            await Assert.ThatAsync(
                () => context.Workspace.ExecuteAsync(draft, true, confirmDeployment: true),
                Throws.InvalidOperationException.With.Message.Contains("Prepare")).ConfigureAwait(false);
        }

        private static void VerifyAuthorizationCount(Mock<ICompanionDeploymentPolicy> policy, int count)
        {
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<CompanionContext>(), It.IsAny<CompanionOperationDraft>(), It.IsAny<CancellationToken>()),
                Times.Exactly(count));
        }

        private static void AssertResult(CompanionOperationResult result)
        {
            Assert.That(result.Summary, Is.EqualTo("Operation complete."));
            Assert.That(result.Values, Has.Count.EqualTo(1));
            Assert.That(result.Values[0].Name, Is.EqualTo("Processed"));
            Assert.That(result.Values[0].Value.TryGetValue(out uint processed), Is.True);
            Assert.That(processed, Is.EqualTo(17u));
        }
    }
}
