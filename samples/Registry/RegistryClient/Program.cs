using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Registry.Samples
{
    /// <summary>Native registry client executable; credentials come from a named environment variable.</summary>
    public static class RegistryClientProgram
    {
        /// <summary>Runs one bounded workflow. Returns a nonzero exit code on service or typed domain failure.</summary>
        public static async Task<int> Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--help") >= 0)
                {
                    Console.WriteLine(
                        "RegistryClient --endpoint <opc.tcp URL> --user <name> [--password-env REGISTRY_PASSWORD]\n" +
                        "  [--pki <directory>] [--auto-accept] [--schema] [--verify-only] [--once] [--timeout <seconds>]\n" +
                        "One authenticated SignAndEncrypt native workflow; no broker or PubSub runtime is started.");
                    return 0;
                }
                string endpoint = "opc.tcp://localhost:62555/Registry";
                string user = string.Empty;
                string passwordEnvironment = "REGISTRY_PASSWORD";
                string pki = Path.Combine("registry-data", "client-pki");
                bool autoAccept = false;
                bool schema = false;
                bool verify = false;
                int seconds = 60;
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--endpoint":
                            endpoint = args[++i];
                            break;
                        case "--user":
                            user = args[++i];
                            break;
                        case "--password-env":
                            passwordEnvironment = args[++i];
                            break;
                        case "--pki":
                            pki = args[++i];
                            break;
                        case "--auto-accept":
                            autoAccept = true;
                            break;
                        case "--schema":
                            schema = true;
                            break;
                        case "--verify-only":
                            verify = true;
                            break;
                        case "--once":
                            break;
                        case "--timeout":
                            seconds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        default:
                            throw new ArgumentException("Unknown option: " + args[i]);
                    }
                }
                string password = Environment.GetEnvironmentVariable(passwordEnvironment) ??
                    throw new ArgumentException("Set the password environment variable: " + passwordEnvironment);
                if (string.IsNullOrWhiteSpace(user) || password.Length == 0 || seconds <= 0)
                {
                    throw new ArgumentException("A username, nonempty password and positive timeout are required.");
                }
                if (autoAccept)
                {
                    Console.Error.WriteLine("Development only: accepting untrusted server certificates.");
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                RegistryDemoResult result = await RegistryDemo.ConnectAndRunAsync(
                    endpoint, user, password, pki, autoAccept, schema, verify, timeout.Token).ConfigureAwait(false);
                Console.WriteLine($"Native demo succeeded: {result.Protocol}, topic={result.Topic}, " +
                    $"schema={result.SchemaReference}, epoch={result.Epoch}, String parts={result.SnapshotParts}, " +
                    $"String length={result.Description.Length}, local base content type={result.ResolvedContentType}.");
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
