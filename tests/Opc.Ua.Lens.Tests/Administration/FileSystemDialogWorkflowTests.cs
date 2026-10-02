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
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using UaLens.Plugins.FileSystem;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class FileSystemDialogWorkflowTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(true, true, true)]
    public Task FilterAcceptReturnsIndependentSelectionAndFallsBackForAnEmptyChoice(bool fs, bool dir, bool file)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var initial = new FileSystemRootFilter(false, true, false);
            var dialog = new FilterDialog(initial);
            Task<FileSystemRootFilter?> prompt = dialog.ShowDialog<FileSystemRootFilter?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<CheckBox>(dialog, "AcceptFileSystemBox").IsChecked = fs;
                DesktopInteraction.Control<CheckBox>(dialog, "AcceptDirectoryBox").IsChecked = dir;
                DesktopInteraction.Control<CheckBox>(dialog, "AcceptFileBox").IsChecked = file;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                FileSystemRootFilter? result = await prompt.ConfigureAwait(true);
                Assert.That(result, Is.EqualTo(new FileSystemRootFilter(fs || (!dir && !file), dir, file)));
                Assert.That(dialog.Result, Is.SameAs(result));
                Assert.That(result!.AcceptsAnything, Is.True);
                Assert.That(initial, Is.EqualTo(new FileSystemRootFilter(false, true, false)));
                Assert.That(result, Is.Not.SameAs(initial));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CancelFilterDoesNotPublishEditedCriteria()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var initial = new FileSystemRootFilter(true, true, false);
            var dialog = new FilterDialog(initial);
            Task<FileSystemRootFilter?> prompt = dialog.ShowDialog<FileSystemRootFilter?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<CheckBox>(dialog, "AcceptFileBox").IsChecked = true;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(dialog.Result, Is.Null);
                Assert.That(initial.AllowFile, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("  Product reports  ", true, "Product reports")]
    [TestCase("  ", true, null)]
    [TestCase("Original", true, "Original")]
    [TestCase("New name", false, null)]
    public Task NamePromptReturnsTrimmedIntentOnlyOnAccept(string text, bool accept, string? expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new NameInputDialog("Rename production file", "File name:", "Original");
            Task<string?> prompt = dialog.ShowDialog<string?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Assert.That(dialog.Title, Is.EqualTo("Rename production file"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "PromptLabel").Text,
                    Is.EqualTo("File name:"));
                TextBox input = DesktopInteraction.Control<TextBox>(dialog, "NameBox");
                Assert.That(input.Text, Is.EqualTo("Original"));
                input.Text = text;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(
                    dialog, accept ? "OkButton" : "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.EqualTo(expected));
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }
}
