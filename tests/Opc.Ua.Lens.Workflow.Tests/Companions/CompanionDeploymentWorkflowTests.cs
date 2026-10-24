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
    [TestFixture]
    public sealed partial class CompanionDeploymentWorkflowTests
    {

        [Test]
        [Platform("Win,Linux")]
        [Category("LensDesktopMainline")]
        [NonParallelizable]
        public async Task CompiledDeploymentBindingsTrackOperationSelectionAndFreshConfirmationAsync()
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var host = new ObserveTestHost();
                await using (host.ConfigureAwait(true))
                {
                    var policy = new Mock<ICompanionDeploymentPolicy>(MockBehavior.Strict);
                    CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext(
                        policy.Object, host.Telemetry);
                    ConfigurePolicy(policy, context);
                    AddAlternateSelections(context);
                    var document = new CompanionPlugin(host.Host, context.Workspace);
                    await using (document.ConfigureAwait(true))
                    {
                        await context.Workspace.BindAsync(context.Session.Object).ConfigureAwait(true);
                        document.IsOffline = false;
                        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                        var view = new CompanionView { DataContext = document };
                        DesktopInteraction.Owner.Content = view;
                        view.GetLogicalDescendants().OfType<Expander>().Single().IsExpanded = true;
                        DesktopInteraction.Owner.UpdateLayout();
                        ComboBox operations = view.GetLogicalDescendants().OfType<ComboBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Guided operation");
                        ComboBox providers = view.GetLogicalDescendants().OfType<ComboBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Companion model");
                        ListBox targets = view.GetLogicalDescendants().OfType<ListBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Companion instances");
                        CheckBox confirmation = view.GetLogicalDescendants().OfType<CheckBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Confirm deployment operation");
                        TextBox input = view.GetLogicalDescendants().OfType<TextBox>().Single(control =>
                            AutomationProperties.GetName(control) == "Operation input or destination");
                        Button run = view.GetLogicalDescendants().OfType<Button>().Single(control =>
                            ReferenceEquals(control.Command, document.RunTaskCommand));
                        Assert.That(operations.SelectedItem, Is.SameAs(context.Operation));
                        Assert.That(confirmation.IsVisible, Is.True);
                        operations.SelectedItem = document.Operations[1];
                        Assert.That(document.SelectedOperation?.Id, Is.EqualTo("refresh"));
                        Assert.That(document.IsDeploymentOperation, Is.False);
                        Assert.That(confirmation.IsVisible, Is.False);
                        operations.SelectedItem = context.Operation;
                        Assert.That(document.IsDeploymentOperation, Is.True);
                        Assert.That(confirmation.IsVisible, Is.True);
                        input.Text = CompanionDeploymentTestData.Input;
                        Assert.That(document.OperationInput, Is.EqualTo("recipe=7"));
                        confirmation.IsChecked = true;
                        Assert.That(document.ConfirmDeployment, Is.True);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);

                        await document.PrepareTaskCommand.ExecuteAsync(null).ConfigureAwait(true);

                        Assert.That(document.PreparedOperation?.DeploymentGrant?.RuleId, Is.EqualTo("plant-rule"));
                        Assert.That(confirmation.IsChecked, Is.False);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);
                        confirmation.IsChecked = true;
                        Assert.That(document.ConfirmDeployment, Is.True);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.True);
                        Assert.That(run.IsEffectivelyEnabled, Is.True);
                        input.Text = "recipe=9";
                        Assert.That(document.PreparedOperation, Is.Null);
                        Assert.That(confirmation.IsChecked, Is.False);
                        Assert.That(document.RunTaskCommand.CanExecute(null), Is.False);
                        Assert.That(run.IsEffectivelyEnabled, Is.False);
                        targets.SelectedItem = document.Targets[1];
                        CompanionTarget selected = document.SelectedTarget ??
                            throw new AssertionException("The target selection binding did not update.");
                        Assert.That(selected.NodeId, Is.EqualTo(new NodeId(4321u, 2)));
                        Assert.That(document.SelectedOperation, Is.Null);
                        Assert.That(confirmation.IsVisible, Is.False);
                        providers.SelectedItem = null;
                        Assert.That(document.SelectedProvider, Is.Null);
                        Assert.That(document.Targets, Is.Empty);
                        context.VerifyExecutionCount(0);
                        VerifyAuthorizationCount(policy, 1);
                    }
                }
            }).ConfigureAwait(false);
        }
    }
}
