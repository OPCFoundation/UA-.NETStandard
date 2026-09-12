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
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens;

internal sealed partial class App : Application
{
    public App()
        : this(null)
    {
    }

    public App(IServiceProvider? services)
    {
        m_services = services;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Themes.ThemeManager.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IServiceProvider services = m_services
                ?? throw new InvalidOperationException("Desktop startup requires the owned UaLens service container.");
            MainViewModel viewModel = services.GetRequiredService<MainViewModel>();
            AppearancePreferences appearance = services.GetRequiredService<AppearancePreferences>();
            var window = new MainWindow(viewModel, appearance);
            window.Opened += async (_, _) => await StartOptionalMonitoringAsync(viewModel).ConfigureAwait(true);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartOptionalMonitoringAsync(MainViewModel viewModel)
    {
        try
        {
            await viewModel.StartResourceMonitoringAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException error) when (error.CancellationToken.IsCancellationRequested)
        {
            if (!viewModel.Workspace.IsClosing)
            {
                viewModel.ResourceStatus = "Resource monitoring startup was cancelled.";
            }
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Monitoring is optional, but its startup failure remains visible.
            viewModel.ResourceStatus = $"Resource monitoring unavailable: {error.Message}";
        }
    }

    private readonly IServiceProvider? m_services;
}
