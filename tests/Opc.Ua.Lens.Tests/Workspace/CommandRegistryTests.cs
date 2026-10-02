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
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
public sealed class CommandRegistryTests
{
    [Test]
    public void RegisterPublishesANewSnapshotWithoutMutatingAnExistingView()
    {
        var registry = new CommandRegistry([Descriptor("document.rename", "F2")]);
        ArrayOf<CommandDescriptor> original = registry.All;
        int notifications = 0;
        registry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CommandRegistry.All))
            {
                notifications++;
            }
        };

        registry.Register([Descriptor("workspace.save", "Ctrl+S")]);

        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(original[0].Id, Is.EqualTo("document.rename"));
        Assert.That(registry.All.Count, Is.EqualTo(2));
        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void DuplicateIdentifiersRejectTheWholeRegistrationBatch()
    {
        CommandDescriptor original = Descriptor("document.rename", "F2");
        var registry = new CommandRegistry([original]);
        ArrayOf<CommandDescriptor> before = registry.All;

        Assert.That(
            () => registry.Register(
                [Descriptor("document.close", "Ctrl+W"), Descriptor("document.rename", "F3")]),
            Throws.TypeOf<ArgumentException>());

        Assert.That(registry.All, Is.EqualTo(before));
        Assert.That(registry.TryGet("document.close", out _), Is.False);
        Assert.That(registry.TryGet("document.rename", out CommandDescriptor? retained), Is.True);
        Assert.That(retained, Is.SameAs(original));
    }

    [TestCase("F2", "f2")]
    [TestCase("Ctrl+Shift+M", " shift + control + m ")]
    [TestCase("Ctrl+Alt+R", "Alt+R+Ctrl")]
    public void ShortcutValidationDetectsCollisionsRegardlessOfCaseAndModifierOrder(
        string first,
        string second)
    {
        Assert.That(
            () => new CommandRegistry([Descriptor("first", first), Descriptor("second", second)]),
            Throws.TypeOf<ArgumentException>());
    }

    [TestCase("Ctrl+A")]
    [TestCase("Control+V")]
    [TestCase("Ctrl+C")]
    [TestCase("Ctrl+X")]
    [TestCase("Ctrl+Z")]
    [TestCase("Ctrl+Y")]
    public void TextEditingShortcutsCannotBeRegisteredGlobally(string shortcut)
    {
        Assert.That(
            () => new CommandRegistry([Descriptor("invalid", shortcut)]),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void FocusInAnEditorPreservesDocumentAndTextEditingKeys()
    {
        var rename = Descriptor("document.rename", "F2");
        var save = new CommandDescriptor(
            "workspace.save", CommandScope.Application, "Save workspace", "Ctrl+S",
            new RelayCommand(() => { }), allowInTextInput: true);
        var registry = new CommandRegistry([rename, save]);

        Assert.That(registry.TryResolveShortcut("F2", isTextInput: true, out _), Is.False);
        Assert.That(registry.TryResolveShortcut("Ctrl+A", isTextInput: true, out _), Is.False);
        Assert.That(registry.TryResolveShortcut("Ctrl+V", isTextInput: true, out _), Is.False);
        Assert.That(registry.TryResolveShortcut("Ctrl+S", isTextInput: true, out CommandDescriptor? command), Is.True);
        Assert.That(command, Is.SameAs(save));
        Assert.That(registry.TryResolveShortcut("F2", isTextInput: false, out command), Is.True);
        Assert.That(command, Is.SameAs(rename));
    }

    [Test]
    public async Task AvailabilityGatesBothShortcutResolutionAndTheExecutableCommand()
    {
        bool hasDocument = false;
        int executions = 0;
        var descriptor = new CommandDescriptor(
            "document.close", CommandScope.Document, "Close", "Ctrl+W",
            new RelayCommand(() => executions++), () => hasDocument);
        var registry = new CommandRegistry([descriptor]);

        Assert.That(registry.TryResolveShortcut("Ctrl+W", isTextInput: false, out _), Is.False);
        Assert.That(descriptor.Command.CanExecute(null), Is.False);
        await descriptor.ExecuteAsync().ConfigureAwait(false);
        await descriptor.Command.ExecuteAsync(null).ConfigureAwait(false);
        Assert.That(executions, Is.Zero);

        hasDocument = true;
        registry.Refresh();
        Assert.That(
            registry.TryResolveShortcut("Ctrl+W", isTextInput: false, out CommandDescriptor? resolved), Is.True);
        Assert.That(resolved, Is.SameAs(descriptor));
        Assert.That(descriptor.Command.CanExecute(null), Is.True);
        await descriptor.ExecuteAsync().ConfigureAwait(false);
        Assert.That(executions, Is.EqualTo(1));
    }

    [Test]
    public async Task BusyCommandCannotBeDispatchedAgainAndCompletesOnlyAfterItsAction()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;
        var descriptor = new CommandDescriptor(
            "connection.connect", CommandScope.Connection, "Connect", "Ctrl+N",
            new AsyncRelayCommand(async () =>
            {
                executions++;
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            }));
        var registry = new CommandRegistry([descriptor]);

        Task executing = descriptor.ExecuteAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(descriptor.IsBusy, Is.True);
            Assert.That(executing.IsCompleted, Is.False);
            Assert.That(registry.TryResolveShortcut("Ctrl+N", isTextInput: false, out _), Is.False);
            await descriptor.ExecuteAsync().ConfigureAwait(false);
            Assert.That(executions, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
        }
        await executing.ConfigureAwait(false);

        Assert.That(descriptor.IsBusy, Is.False);
        Assert.That(descriptor.IsApplicable, Is.True);
    }

    [Test]
    public async Task CancellationReachesTheOwnedAsynchronousCommand()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        var descriptor = new CommandDescriptor(
            "connection.connect", CommandScope.Connection, "Connect", null,
            new AsyncRelayCommand(async token =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                finally
                {
                    cancelled = token.IsCancellationRequested;
                }
            }));
        Task executing = descriptor.ExecuteAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        await Assert.ThatAsync(
            () => executing, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(cancelled, Is.True);
        Assert.That(descriptor.IsBusy, Is.False);
    }

    [Test]
    public async Task CommandFailuresReachTheCallerInsteadOfBecomingSuccessfulResults()
    {
        var descriptor = new CommandDescriptor(
            "workspace.save", CommandScope.Application, "Save", null,
            new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("Write failed"))));

        await Assert.ThatAsync(
            () => descriptor.ExecuteAsync(),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Write failed")).ConfigureAwait(false);
        Assert.That(descriptor.IsBusy, Is.False);
    }

    private static CommandDescriptor Descriptor(string id, string shortcut)
        => new(id, CommandScope.Document, id, shortcut, new RelayCommand(() => { }));
}
