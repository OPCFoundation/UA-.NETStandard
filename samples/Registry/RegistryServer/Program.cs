/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Registry.Samples
{
    /// <summary>
    /// Standalone durable registry executable.
    /// </summary>
    public static class RegistryServerProgram
    {
        /// <summary>
        /// Runs until interrupted, or for a bounded --once/--self-test workflow.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--help") >= 0)
                {
                    Console.WriteLine(
                        "RegistryServer --user <name> [--password-env REGISTRY_PASSWORD]\n" +
                        "  [--endpoint <opc.tcp URL>] [--state <directory>] [--pki <directory>]\n" +
                        "  [--schema] [--auto-accept] [--once] [--seconds <seconds>] [--self-test]\n" +
                        "Hosts separate durable Endpoint and Media roots, and optionally Schema.\n" +
                        "--once stops after 30 seconds by default; --self-test runs a TCP client and stops.");
                    return 0;
                }
                var options = new RegistrySampleServerOptions();
                string passwordEnvironment = "REGISTRY_PASSWORD";
                bool once = false;
                bool selfTest = false;
                int seconds = 30;
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--endpoint":
                            options.EndpointUrl = args[++i];
                            break;
                        case "--user":
                            options.UserName = args[++i];
                            break;
                        case "--password-env":
                            passwordEnvironment = args[++i];
                            break;
                        case "--state":
                            options.StateDirectory = args[++i];
                            break;
                        case "--pki":
                            options.PkiDirectory = args[++i];
                            break;
                        case "--schema":
                            options.EnableSchema = true;
                            break;
                        case "--auto-accept":
                            options.AutoAccept = true;
                            break;
                        case "--once":
                            once = true;
                            break;
                        case "--self-test":
                            selfTest = true;
                            break;
                        case "--seconds":
                            seconds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        default:
                            throw new ArgumentException("Unknown option: " + args[i]);
                    }
                }
                options.Password = Environment.GetEnvironmentVariable(passwordEnvironment) ??
                    throw new ArgumentException("Set the password environment variable: " + passwordEnvironment);
                if (seconds <= 0)
                {
                    throw new ArgumentException("The bounded duration must be positive.");
                }
                if (options.AutoAccept)
                {
                    Console.Error.WriteLine("Development only: accepting untrusted client certificates.");
                }
                using var shutdown = new CancellationTokenSource();
                if (once || selfTest)
                {
                    shutdown.CancelAfter(TimeSpan.FromSeconds(seconds));
                }
                ConsoleCancelEventHandler cancel = (_, e) =>
                {
                    e.Cancel = true;
                    shutdown.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    RegistrySampleServer server = await RegistrySampleServer.StartAsync(options, shutdown.Token)
                        .ConfigureAwait(false);
                    await using (server.ConfigureAwait(false))
                    {
                        Console.WriteLine("Registry ready: " + options.EndpointUrl);
                        if (selfTest)
                        {
                            RegistryDemoResult result = await RegistryDemo.ConnectAndRunAsync(options.EndpointUrl,
                                options.UserName, options.Password, Path.Combine(options.StateDirectory, "self-test-pki"),
                                options.AutoAccept, options.EnableSchema, cancellationToken: shutdown.Token)
                                .ConfigureAwait(false);
                            Console.WriteLine($"Self-test succeeded: epoch={result.Epoch}, String parts={result.SnapshotParts}.");
                        }
                        else
                        {
                            try
                            {
                                await Task.Delay(once ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan,
                                    shutdown.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                            {
                                // Ctrl+C is a successful, orderly shutdown.
                            }
                        }
                    }
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
