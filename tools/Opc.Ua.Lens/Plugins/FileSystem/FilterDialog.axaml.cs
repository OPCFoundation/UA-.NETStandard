/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Three-checkbox modal that the FileSystem plug-in opens behind the
/// "Filter…" toolbar button. Returns the user's selection as a
/// <see cref="FileSystemRootFilter"/>, which the plug-in subsequently
/// hands to <see cref="FileSystemPlugin.PickRootAsync"/> to scope the
/// next address-space pick. Cancelling returns <c>null</c>.
/// </summary>
internal sealed partial class FilterDialog : Window
{
    /// <summary>The user's last-confirmed selection; <c>null</c> on cancel.</summary>
    public FileSystemRootFilter? Result { get; private set; }

    public FilterDialog(FileSystemRootFilter initial)
    {
        InitializeComponent();
        var fileSystem = this.RequiredControl<CheckBox>("AcceptFileSystemBox");
        var directory = this.RequiredControl<CheckBox>("AcceptDirectoryBox");
        var file = this.RequiredControl<CheckBox>("AcceptFileBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        fileSystem.IsChecked = initial.AllowFileSystem;
        directory.IsChecked = initial.AllowDirectory;
        file.IsChecked = initial.AllowFile;

        ok.Click += (_, _) =>
        {
            // At least one box must be ticked — otherwise the picker
            // would accept nothing and the OK button would be greyed
            // out forever. Falling back to "FileSystem only" matches
            // the default opening filter.
            bool allowFs = fileSystem.IsChecked == true;
            bool allowDir = directory.IsChecked == true;
            bool allowFile = file.IsChecked == true;
            if (!allowFs && !allowDir && !allowFile)
            {
                allowFs = true;
            }
            Result = new FileSystemRootFilter(allowFs, allowDir, allowFile);
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
