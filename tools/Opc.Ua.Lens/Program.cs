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
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using UaLens.Connection;

namespace UaLens
{
    internal static class Program
    {
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            string endpoint = Option(args, "--endpoint")
                ?? "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            if (Has(args, "--smoke"))
            {
                string nodeId = Option(args, "--node")
                    ?? "ns=2;s=Scalar_Simulation_UInt32";
                return await SmokeTest.RunAsync(endpoint, nodeId).ConfigureAwait(false);
            }
            if (Has(args, "--testtree"))
            {
                return await TreeNavTest.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-ka"))
            {
                int wait = int.TryParse(
                    Option(args, "--wait"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    ? seconds
                    : 12;
                return await KeepAliveProbe.RunAsync(endpoint, wait).ConfigureAwait(false);
            }
            if (Has(args, "--probe-workers"))
            {
                return await WorkerCountProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-attrs"))
            {
                return await AttributesProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-dots"))
            {
                return await DotsProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-lines"))
            {
                return await LinesProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-variant"))
            {
                return VariantProbe.Run();
            }
            if (Has(args, "--probe-scottplot"))
            {
                return ScottPlotProbe.Run();
            }
            if (Has(args, "--probe-cert-trust"))
            {
                return await CertTrustProbe.RunAsync().ConfigureAwait(false);
            }
            if (Has(args, "--probe-channel-drop"))
            {
                return ChannelDropProbe.Run();
            }
            if (Has(args, "--probe-adapter-race"))
            {
                return await AdapterRaceProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-rates"))
            {
                return await RatesProbe.RunAsync(endpoint).ConfigureAwait(false);
            }
            if (Has(args, "--probe-events"))
            {
                return await EventsProbe.RunAsync(endpoint).ConfigureAwait(false);
            }

            bool nativeViewportSmoke = Has(args, "--smoke-native-viewport");
            bool offlineDesktopSmoke = Has(args, "--smoke-test") || nativeViewportSmoke;
            string? nativeArtifactDirectory = null;
            if (nativeViewportSmoke)
            {
                string? directory = Option(args, "--smoke-artifact-directory");
                if (string.IsNullOrWhiteSpace(directory))
                {
                    Console.Error.WriteLine("--smoke-native-viewport requires --smoke-artifact-directory.");
                    return 2;
                }
                nativeArtifactDirectory = Path.GetFullPath(directory);
            }
            EnvironmentConnectionConfiguration? automaticConnection = null;
            try
            {
                if (!offlineDesktopSmoke && !EnvironmentConnectionConfiguration.TryCreateFromEnvironment(
                    out automaticConnection,
                    out string? configurationError))
                {
                    Console.Error.WriteLine(configurationError);
                    return 2;
                }

                DesktopSmokeTest? desktopSmoke = offlineDesktopSmoke ? new DesktopSmokeTest(nativeArtifactDirectory) : null;
                var serviceCollection = new ServiceCollection();
                bool allowSoftwareGl = Has(args, "--allow-software-gl");
                bool x11Vulkan = Has(args, "--x11-vulkan");
                if (allowSoftwareGl || x11Vulkan)
                {
                    serviceCollection.AddSingleton(CreateX11PlatformOptions(allowSoftwareGl, x11Vulkan));
                }
                if (automaticConnection is not null)
                {
                    serviceCollection.AddSingleton(automaticConnection);
                }
                ServiceProvider services = serviceCollection.AddUaLens().BuildServiceProvider();
                int exitCode;
                try
                {
                    // No await is reached on this branch until Avalonia has finished. The
                    // desktop therefore stays on the process's original STA thread.
                    exitCode = BuildAvaloniaApp(services, desktopSmoke).StartWithClassicDesktopLifetime(args);
                    if (desktopSmoke is not null)
                    {
                        await desktopSmoke.Completion.ConfigureAwait(false);
                    }
                }
                finally
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
                if (desktopSmoke is not null && exitCode == 0)
                {
                    // The artifact runner requires both this marker and exit 0. Never
                    // report success before the desktop and service container drain.
                    Console.WriteLine("UALENS_DESKTOP_SMOKE_PASS");
                    if (nativeViewportSmoke)
                    {
                        Console.WriteLine(desktopSmoke.NativeViewportProof);
                        Console.WriteLine("UALENS_NATIVE_VIEWPORT_SMOKE_PASS");
                    }
                }
                return exitCode;
            }
            finally
            {
                automaticConnection?.Dispose();
            }
        }

        public static AppBuilder BuildAvaloniaApp(IServiceProvider services, DesktopSmokeTest? desktopSmoke = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            AppBuilder builder = AppBuilder.Configure(() => new App(services, desktopSmoke))
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
            X11PlatformOptions? x11 = services.GetService<X11PlatformOptions>();
            return x11 is null ? builder : builder.With(x11);
        }

        /// <summary>
        /// Creates independent X11 options from explicit host rendering choices.
        /// </summary>
        internal static X11PlatformOptions CreateX11PlatformOptions(
            bool allowSoftwareGl, bool x11Vulkan = false)
        {
            var options = new X11PlatformOptions();
            if (x11Vulkan)
            {
                options.RenderingMode = [X11RenderingMode.Vulkan];
            }
            if (allowSoftwareGl)
            {
                options.GlxRendererBlacklist.Remove("llvmpipe");
            }
            return options;
        }

        private static bool Has(string[] args, string flag)
        {
            return Array.Exists(args, argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));
        }

        private static string? Option(string[] args, string name)
        {
            int index = Array.FindIndex(
                args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
