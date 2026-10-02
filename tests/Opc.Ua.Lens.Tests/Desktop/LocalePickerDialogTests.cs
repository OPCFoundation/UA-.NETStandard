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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class LocalePickerDialogTests
{
    [Test]
    public Task EditingLocalesPreservesOrderAndAppliesOnlyTheFinalSnapshot()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            context.Session.SetupGet(session => session.PreferredLocales)
                .Returns(new ArrayOf<string>(s_withEmpty));
            ArrayOf<string> applied = default;
            context.Session.Setup(session => session.ChangePreferredLocalesAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Callback((ArrayOf<string> locales, CancellationToken _) => applied = locales)
                .Returns(Task.CompletedTask);
            await context.ConnectAsync().ConfigureAwait(true);
            var dialog = new LocalePickerDialog(context.Desktop.Connection);
            dialog.Show(DesktopInteraction.Owner);
            ListBox list = DesktopInteraction.Control<ListBox>(dialog, "LocaleList");
            TextBox input = DesktopInteraction.Control<TextBox>(dialog, "NewLocaleBox");
            Assert.That(list.Items, Is.EqualTo(s_initial));
            input.Text = "  ";
            Click(dialog, "AddButton");
            input.Text = "en-US";
            Click(dialog, "AddButton");
            Assert.That(list.Items, Is.EqualTo(s_initial));
            input.Text = "  fr-FR  ";
            Click(dialog, "AddButton");
            Assert.That(input.Text, Is.Empty);
            Assert.That(list.SelectedIndex, Is.EqualTo(2));
            Click(dialog, "DownButton");
            Assert.That(list.SelectedIndex, Is.EqualTo(2));
            Click(dialog, "UpButton");
            Assert.That(list.Items, Is.EqualTo(s_moved));
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Click(dialog, "DownButton");
            Assert.That(list.Items, Is.EqualTo(s_added));
            list.SelectedIndex = 0;
            Click(dialog, "UpButton");
            Assert.That(list.Items, Is.EqualTo(s_added));
            list.SelectedIndex = 1;
            Click(dialog, "RemoveButton");
            Assert.That(list.Items, Is.EqualTo(s_final));
            list.SelectedIndex = -1;
            Click(dialog, "RemoveButton");
            Click(dialog, "DownButton");
            Assert.That(list.Items, Is.EqualTo(s_final));
            Assert.That(applied.IsNull, Is.True);
            Click(dialog, "ApplyButton");
            Assert.That(dialog.IsVisible, Is.False);
            Assert.That(applied.ToArray(), Is.EqualTo(s_final));
            context.Session.Verify(session => session.ChangePreferredLocalesAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [Test]
    public Task FailedApplyLeavesTheDialogOpenAndRetainsTheUsersEdits()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            context.Session.SetupGet(session => session.PreferredLocales).Returns(new ArrayOf<string>(s_initial));
            context.Session.Setup(session => session.ChangePreferredLocalesAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadUserAccessDenied, "locale request rejected"));
            await context.ConnectAsync().ConfigureAwait(true);
            var dialog = new LocalePickerDialog(context.Desktop.Connection);
            dialog.Show(DesktopInteraction.Owner);
            await DesktopInteraction.ChangedAsync(dialog,
                () => dialog.Title?.Contains("apply failed", StringComparison.Ordinal) == true, () =>
                {
                    Click(dialog, "ApplyButton");
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
            Assert.That(dialog.IsVisible, Is.True);
            Assert.That(dialog.Title, Does.Contain("locale request rejected"));
            Assert.That(DesktopInteraction.Control<ListBox>(dialog, "LocaleList").Items, Is.EqualTo(s_initial));
            Click(dialog, "CancelButton");
            Assert.That(dialog.IsVisible, Is.False);
            context.Session.Verify(session => session.ChangePreferredLocalesAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [TestCase("en-US")]
    [TestCase("de-DE")]
    public Task DisconnectedDialogUsesTheUiCultureAndClosesWithoutAPhantomSession(string culture)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            CultureInfo original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var dialog = new LocalePickerDialog(context.Connection);
                dialog.Show(DesktopInteraction.Owner);
                ListBox list = DesktopInteraction.Control<ListBox>(dialog, "LocaleList");
                Assert.That(list.Items, Is.EqualTo(culture == "en-US" ? s_english : s_german));
                while (list.Items.Count > 0)
                {
                    list.SelectedIndex = 0;
                    Click(dialog, "RemoveButton");
                }
                Assert.That(list.Items, Is.Empty);
                Click(dialog, "ApplyButton");
                Assert.That(dialog.IsVisible, Is.False);
                Assert.That(context.Connection.CurrentSession, Is.Null);
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        });
    }

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }

    private static readonly string[] s_initial = ["en-US", "de-DE"];
    private static readonly string[] s_withEmpty = ["en-US", string.Empty, "de-DE"];
    private static readonly string[] s_moved = ["en-US", "fr-FR", "de-DE"];
    private static readonly string[] s_added = ["en-US", "de-DE", "fr-FR"];
    private static readonly string[] s_final = ["en-US", "fr-FR"];
    private static readonly string[] s_english = ["en-US"];
    private static readonly string[] s_german = ["de-DE", "en-US"];
}
