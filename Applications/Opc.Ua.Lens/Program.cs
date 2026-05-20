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
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace UaLens;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return SmokeTest.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--testtree", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return TreeNavTest.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-ka", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            int wIdx = Array.FindIndex(args, a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));
            int wait = wIdx >= 0 && wIdx + 1 < args.Length && int.TryParse(args[wIdx + 1], out int w) ? w : 12;
            return KeepAliveProbe.RunAsync(endpoint, wait).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-workers", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return WorkerCountProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-attrs", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return AttributesProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-dots", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return DotsProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-lines", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return LinesProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-variant", StringComparison.OrdinalIgnoreCase)))
        {
            return VariantProbe.Run();
        }

        if (args.Any(a => string.Equals(a, "--probe-scottplot", StringComparison.OrdinalIgnoreCase)))
        {
            return ScottPlotProbe.Run();
        }

        if (args.Any(a => string.Equals(a, "--probe-cert-trust", StringComparison.OrdinalIgnoreCase)))
        {
            return CertTrustProbe.RunAsync().GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-channel-drop", StringComparison.OrdinalIgnoreCase)))
        {
            return ChannelDropProbe.Run();
        }

        if (args.Any(a => string.Equals(a, "--probe-adapter-race", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return AdapterRaceProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-rates", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return RatesProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        if (args.Any(a => string.Equals(a, "--probe-events", StringComparison.OrdinalIgnoreCase)))
        {
            int idx = Array.FindIndex(args, a => string.Equals(a, "--endpoint", StringComparison.OrdinalIgnoreCase));
            string endpoint = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
            return EventsProbe.RunAsync(endpoint).GetAwaiter().GetResult();
        }

        // Start the resource-monitor host before Avalonia so the
        // CPU / memory pane has data on first paint.  Disposed as the
        // process exits — Avalonia's StartWithClassicDesktopLifetime returns
        // synchronously after the main window closes.  CA2000: the host is
        // disposed in the finally block below; the analyzer can't see the
        // GetResult-vs-finally lifetime.
#pragma warning disable CA2000
        var resourceHost = UaLens.Diagnostics.ResourceMonitorHost
            .StartAsync().GetAwaiter().GetResult();
#pragma warning restore CA2000
        UaLens.Views.MainWindow.PendingResourceMonitor = resourceHost;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            resourceHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();
}
