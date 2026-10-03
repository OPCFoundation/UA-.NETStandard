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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua.Interop.LegacyPeer
{
    /// <summary>
    /// Entry point of the 1.5.x interop peer.
    /// </summary>
    /// <remarks>
    /// <code>
    /// server --port &lt;port&gt; --pki &lt;dir&gt; [--kind interop|reference]
    ///        [--autoaccept true|false] [--ecc true|false] [--init-only true]
    ///     Starts a 1.5.x server, prints "LEGACY-SERVER-READY &lt;url&gt;" and
    ///     runs until standard input is closed or a line "stop" is read.
    ///     'interop' (default) is a small server with a known address space;
    ///     'reference' is the 1.5.x Quickstarts reference server with all of
    ///     its default node managers.
    /// client --url &lt;url&gt; --pki &lt;dir&gt; [--policy &lt;uri&gt;] [--mode &lt;mode&gt;]
    ///        [--user &lt;name&gt; --password &lt;password&gt;] [--checks a,b,c]
    ///        [--autoaccept true|false] [--ecc true|false]
    ///        [--expect-connect-error &lt;StatusCode name,...&gt;]
    ///        [--token-lifetime &lt;ms&gt;] [--token-test-seconds &lt;s&gt;]
    ///        [--timeout-seconds &lt;s&gt;]
    ///     Runs the client checks against a server that exposes the
    ///     Quickstarts reference server address space. Prints one
    ///     "RESULT {json}" line per check and exits with 0 when every
    ///     check passed.
    /// With --init-only true, server and client create their certificates
    /// and stores, print "LEGACY-PKI-READY &lt;application uri&gt;" and exit, so a test can
    /// set up trust lists before the peer connects.
    /// </code>
    /// </remarks>
    public static class Program
    {
        public const int ExitSuccess = 0;
        public const int ExitChecksFailed = 1;
        public const int ExitUsage = 2;
        public const int ExitFatal = 3;

        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                return Usage("missing command");
            }

            PeerOptions options;
            try
            {
                options = PeerOptions.Parse(args);
            }
            catch (ArgumentException e)
            {
                return Usage(e.Message);
            }

            try
            {
                switch (args[0])
                {
                    case "server":
                        return await LegacyServerHost.RunAsync(options).ConfigureAwait(false);
                    case "client":
                        return await LegacyClientChecks.RunAsync(options).ConfigureAwait(false);
                    default:
                        return Usage("unknown command " + args[0]);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("FATAL " + e);
                return ExitFatal;
            }
        }

        private static int Usage(string error)
        {
            Console.Error.WriteLine("error: " + error);
            Console.Error.WriteLine(
                "usage: server --port <port> --pki <dir> [--kind interop|reference] " +
                "[--autoaccept true|false] [--ecc true|false]");
            Console.Error.WriteLine(
                "       client --url <url> --pki <dir> [--policy <uri>] [--mode <mode>] " +
                "[--user <name> --password <password>] [--checks a,b,c] [--autoaccept true|false] " +
                "[--ecc true|false] [--expect-connect-error <code>] [--token-lifetime <ms>] " +
                "[--token-test-seconds <s>]");
            return ExitUsage;
        }
    }

    /// <summary>
    /// The --name value options of the peer.
    /// </summary>
    public sealed class PeerOptions
    {
        private readonly Dictionary<string, string> m_options;

        private PeerOptions(Dictionary<string, string> options)
        {
            m_options = options;
        }

        public static PeerOptions Parse(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int ii = 1; ii < args.Length; ii++)
            {
                string name = args[ii];
                if (!name.StartsWith("--", StringComparison.Ordinal) || ii + 1 >= args.Length)
                {
                    throw new ArgumentException("expected --name value, got " + name);
                }
                options[name.Substring(2)] = args[++ii];
            }
            return new PeerOptions(options);
        }

        /// <summary>
        /// The PKI root of the peer.
        /// </summary>
        public string PkiRoot => Get("pki");

        /// <summary>
        /// Whether untrusted peer certificates are accepted (default true).
        /// </summary>
        public bool AutoAccept => GetBool("autoaccept", true);

        /// <summary>
        /// Whether the peer uses ECC application certificates and policies in
        /// addition to RSA (default false).
        /// </summary>
        public bool Ecc => GetBool("ecc", false);

        public string Get(string name, string defaultValue = null)
        {
            if (m_options.TryGetValue(name, out string value))
            {
                return value;
            }
            return defaultValue ?? throw new ArgumentException("missing --" + name);
        }

        public bool GetBool(string name, bool defaultValue)
        {
            return m_options.TryGetValue(name, out string value)
                ? bool.Parse(value)
                : defaultValue;
        }

        public int GetInt(string name, int defaultValue)
        {
            return m_options.TryGetValue(name, out string value)
                ? int.Parse(value, CultureInfo.InvariantCulture)
                : defaultValue;
        }
    }

    /// <summary>
    /// Certificate configuration of the legacy peer. The stores live below
    /// the PKI root in the default 1.5 layout: own, issuer, trusted and
    /// rejected, each with certs, private and crl folders. The tests rely
    /// on this layout to provision certificates and trust lists before the
    /// peer starts.
    /// </summary>
    public static class LegacyPki
    {
        /// <summary>
        /// The application certificates: RSA only, or RSA and the ECC curves
        /// 1.5.x supports.
        /// </summary>
        public static CertificateIdentifierCollection ApplicationCertificates(
            string subjectName,
            string pkiRoot,
            bool ecc)
        {
            if (ecc)
            {
                // The default set puts the certificates into the PKI root
                // itself; keep them in 'own' like the RSA-only set.
                CertificateIdentifierCollection certificates =
                    Configuration.ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        subjectName,
                        CertificateStoreType.Directory,
                        pkiRoot);
                foreach (CertificateIdentifier certificate in certificates)
                {
                    certificate.StorePath = Path.Combine(pkiRoot, "own");
                }
                return certificates;
            }
            return new CertificateIdentifierCollection
            {
                new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiRoot, "own"),
                    SubjectName = subjectName,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }
            };
        }
    }
}
