// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
