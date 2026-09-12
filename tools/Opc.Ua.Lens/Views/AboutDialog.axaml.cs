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

using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Views;

/// <summary>
/// Modal About dialog showing the UaLens product name, runtime assembly
/// version, copyright notice, the OPC Foundation MIT 1.00 license body,
/// and a "Visit website" shortcut to the upstream GitHub repo.
/// Triggered from the Help → About… menu entry on <see cref="MainWindow"/>.
/// </summary>
internal sealed partial class AboutDialog : Window
{
    private const string s_websiteUrl = "https://github.com/OPCFoundation/UA-.NETStandard";

    /// <summary>
    /// Verbatim copy of the OPC Foundation MIT 1.00 license body that
    /// appears at the head of every UaLens source file, with the leading
    /// <c>" * "</c> comment prefixes stripped so the text reads cleanly
    /// in a multi-line <see cref="TextBox"/>.
    /// </summary>
    private const string s_licenseText =
        "Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.\r\n" +
        "\r\n" +
        "OPC Foundation MIT License 1.00\r\n" +
        "\r\n" +
        "Permission is hereby granted, free of charge, to any person\r\n" +
        "obtaining a copy of this software and associated documentation\r\n" +
        "files (the \"Software\"), to deal in the Software without\r\n" +
        "restriction, including without limitation the rights to use,\r\n" +
        "copy, modify, merge, publish, distribute, sublicense, and/or sell\r\n" +
        "copies of the Software, and to permit persons to whom the\r\n" +
        "Software is furnished to do so, subject to the following\r\n" +
        "conditions:\r\n" +
        "\r\n" +
        "The above copyright notice and this permission notice shall be\r\n" +
        "included in all copies or substantial portions of the Software.\r\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND,\r\n" +
        "EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES\r\n" +
        "OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND\r\n" +
        "NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT\r\n" +
        "HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,\r\n" +
        "WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING\r\n" +
        "FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR\r\n" +
        "OTHER DEALINGS IN THE SOFTWARE.\r\n" +
        "\r\n" +
        "The complete license agreement can be found here:\r\n" +
        "http://opcfoundation.org/License/MIT/1.00/";

    public AboutDialog()
    {
        InitializeComponent();

        var versionLabel = this.RequiredControl<TextBlock>("VersionLabel");
        var licenseBox = this.RequiredControl<TextBox>("LicenseBox");
        var websiteBtn = this.RequiredControl<Button>("WebsiteButton");
        var closeBtn = this.RequiredControl<Button>("CloseButton");

        string version = typeof(AboutDialog).Assembly.GetName().Version?.ToString() ?? "(unknown)";
        versionLabel.Text = $"Version {version}";

        licenseBox.Text = s_licenseText;

        websiteBtn.Click += (_, _) => OpenBrowser(s_websiteUrl);
        closeBtn.Click += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Launch the user's default browser at <paramref name="url"/>.
    /// Uses the OS shell so http(s) URLs are routed to the registered
    /// browser on Windows, Linux, and macOS.  Best-effort: failures are
    /// swallowed so a missing browser doesn't crash the About dialog.
    /// </summary>
    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch browser for {url}: {ex}");
        }
    }
}
