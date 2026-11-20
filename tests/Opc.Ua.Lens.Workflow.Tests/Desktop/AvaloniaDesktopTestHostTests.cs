// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NUnit.Framework;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[Category("LensDesktopWorkflow")]
[NonParallelizable]
public sealed class AvaloniaDesktopTestHostTests
{
    [Test]
    public async Task RunAsyncUsesOneStaDispatcherAcrossCalls()
    {
        int thread = 0;
        Application? application = null;
        IApplicationLifetime? lifetime = null;
        await AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            thread = Environment.CurrentManagedThreadId;
            application = Application.Current;
            lifetime = application!.ApplicationLifetime;
            if (OperatingSystem.IsWindows())
            {
                Assert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
            }
            Assert.That(application, Is.TypeOf<Application>());
            Assert.That(DesktopInteraction.Owner.IsVisible, Is.True);
            var posted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(posted.SetResult);
            await posted.Task.ConfigureAwait(true);
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread));
            Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True);
        }).ConfigureAwait(false);
        await AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(thread));
            Assert.That(Application.Current, Is.SameAs(application));
            Assert.That(Application.Current!.ApplicationLifetime,
                Is.SameAs(lifetime).And.InstanceOf<IClassicDesktopStyleApplicationLifetime>());
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task RunAsyncPropagatesFaultAndRestoresDesktopOwner()
    {
        var failure = new InvalidOperationException("owned-body-failure");
        Window? child = null;
        Window? owner = null;
        Exception? observed = null;
        try
        {
            await AvaloniaDesktopTestHost.RunAsync(() =>
            {
                owner = DesktopInteraction.Owner;
                child = new Window { ShowInTaskbar = false };
                child.Show(owner);
                Application.Current!.Resources["OwnedTestMarker"] = "temporary";
                throw failure;
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException error)
        {
            observed = error;
        }
        Assert.That(observed, Is.SameAs(failure));
        await AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Assert.That(child!.IsVisible, Is.False);
            Assert.That(owner!.IsVisible, Is.False);
            Assert.That(DesktopInteraction.Owner, Is.Not.SameAs(owner));
            Assert.That(Application.Current!.Resources.ContainsKey("OwnedTestMarker"), Is.False);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task RunAsyncSerializesBodiesWhileAwaitedWorkKeepsPumping()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondEntered = false;
        Task first = AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(true);
            Assert.That(secondEntered, Is.False);
        });
        Task? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            second = AvaloniaDesktopTestHost.RunAsync(() =>
            {
                secondEntered = true;
                Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True);
                return Task.CompletedTask;
            });
            release.SetResult();
            await Task.WhenAll(first, second).ConfigureAwait(false);
            Assert.That(secondEntered, Is.True);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second ?? Task.CompletedTask).ConfigureAwait(false);
        }
    }
}
