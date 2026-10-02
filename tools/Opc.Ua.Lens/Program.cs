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
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace UaLens;

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
            return await SmokeTest.RunAsync(endpoint).ConfigureAwait(false);
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

        ServiceProvider services = new ServiceCollection().AddUaLens().BuildServiceProvider();
        try
        {
            // No await is reached on this branch until Avalonia has finished. The
            // desktop therefore stays on the process's original STA thread.
            return BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            await services.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    private static bool Has(string[] args, string flag)
        => Array.Exists(args, argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));

    private static string? Option(string[] args, string name)
    {
        int index = Array.FindIndex(
            args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
