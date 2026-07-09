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
using System.Globalization;
using System.IO;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Creates Kubernetes API clients and resolves Kubernetes namespace settings.
    /// </summary>
    internal static class KubernetesApiClientFactory
    {
        /// <summary>
        /// Creates the API client that matches the current Kubernetes runtime environment.
        /// </summary>
        /// <param name="options">The Kubernetes server options used to locate in-cluster credentials.</param>
        /// <returns>
        /// An in-cluster HTTP API client when Kubernetes credentials are available; otherwise, a no-op client.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public static IKubernetesApiClient Create(KubernetesServerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            string? host = options.ApiServerHost ?? Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            string? portText = options.ApiServerPort?.ToString(CultureInfo.InvariantCulture) ??
                Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
            int port = int.TryParse(portText, out int parsed) ? parsed : 443;

            if (string.IsNullOrEmpty(host) || !File.Exists(options.TokenPath))
            {
                return new NotInClusterKubernetesApiClient();
            }

            string namespaceName = options.Namespace ?? ReadTrimmed(options.NamespacePath) ?? "default";
            string token = ReadTrimmed(options.TokenPath) ?? string.Empty;
            return new KubernetesHttpApiClient(
                host,
                port,
                namespaceName,
                token,
                options.CertificateAuthorityPath,
                options.TokenPath);
        }

        /// <summary>
        /// Resolves the Kubernetes namespace from options or the mounted namespace file.
        /// </summary>
        /// <param name="options">The Kubernetes server options that may specify the namespace.</param>
        /// <param name="client">The Kubernetes API client associated with the options.</param>
        /// <returns>The configured namespace, mounted namespace, or Kubernetes default namespace.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public static string ResolveNamespace(KubernetesServerOptions options, IKubernetesApiClient client)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            return options.Namespace ?? ReadTrimmed(options.NamespacePath) ?? "default";
        }

        private static string? ReadTrimmed(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return File.ReadAllText(path).Trim();
        }
    }
}
