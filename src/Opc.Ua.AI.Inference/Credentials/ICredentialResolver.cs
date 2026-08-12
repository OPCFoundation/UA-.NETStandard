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
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// Turns the credential NAME a deployment publishes into the credential itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface exists to be the only thing in the sample that ever holds a
    /// secret value. <c>OPC UA - AI Model Management and Inference</c> clause 9.2
    /// forbids a Server from exposing credential material through any Attribute, and
    /// argues it from the fact that an address space is browsable, subscribable and
    /// historisable - a secret placed there is not exposed once, it is published,
    /// distributed and archived.
    /// </para>
    /// <para>
    /// So <c>ModelSourceType.CredentialReference</c> carries a name, a client that
    /// reads it learns which credential is in use and nothing about what it is, and
    /// the resolution from name to value happens here and nowhere else.
    /// </para>
    /// </remarks>
    public interface ICredentialResolver
    {
        /// <summary>
        /// Resolves a credential reference to the value to present, or null where the
        /// backend needs none.
        /// </summary>
        /// <param name="reference">
        /// The name published as <c>CredentialReference</c>. Never a secret.
        /// </param>
        /// <param name="ct">Cancels the resolution.</param>
        ValueTask<string?> ResolveAsync(string reference, CancellationToken ct);
    }

    /// <summary>
    /// Resolves a credential reference against a directory of files, which is how a
    /// Kubernetes Secret mounted as a volume appears to a process.
    /// </summary>
    /// <remarks>
    /// A mounted file is preferred to an environment variable for a reason worth
    /// stating: environment variables are inherited by child processes, appear in
    /// crash dumps and in process listings, and get printed by well-meaning
    /// diagnostic code. A file is read only by something that decides to open it.
    /// </remarks>
    public sealed class FileCredentialResolver : ICredentialResolver
    {
        private readonly string m_directory;

        /// <summary>
        /// Creates a resolver over the directory a Secret is mounted at.
        /// </summary>
        /// <param name="directory">The mount point.</param>
        public FileCredentialResolver(string directory)
        {
            m_directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        private static readonly System.Buffers.SearchValues<char> s_pathSeparators =
            System.Buffers.SearchValues.Create("/\\");

        /// <inheritdoc/>
        public async ValueTask<string?> ResolveAsync(string reference, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            // A reference names a key within the mount. Anything that could escape the
            // mount is refused rather than sanitised: a reference is configuration this
            // Server controls, so one containing a separator is a mistake worth
            // surfacing rather than input worth repairing.
            if (reference.AsSpan().IndexOfAny(s_pathSeparators) >= 0 ||
                reference.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A credential reference names a key, not a path.", nameof(reference));
            }

            string path = System.IO.Path.Combine(m_directory, reference);
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            string value = await System.IO.File
                .ReadAllTextAsync(path, ct)
                .ConfigureAwait(false);

            // Mounted secrets routinely carry a trailing newline from however they were
            // written. Presenting that in a header fails in a way that looks like a
            // wrong key rather than a stray byte.
            return value.Trim();
        }
    }

    /// <summary>
    /// Resolves nothing, for a backend that needs no credential at all.
    /// </summary>
    /// <remarks>
    /// This is the on-device case, and the workload-identity case: in neither does a
    /// secret exist anywhere for a future mistake to expose. Clause 9.2 prefers
    /// exactly that wherever the platform offers it.
    /// </remarks>
    public sealed class NullCredentialResolver : ICredentialResolver
    {
        /// <summary>A shared instance.</summary>
        public static NullCredentialResolver Instance { get; } = new();

        /// <inheritdoc/>
        public ValueTask<string?> ResolveAsync(string reference, CancellationToken ct)
        {
            return new ValueTask<string?>((string?)null);
        }
    }

    /// <summary>
    /// Resolves the token the hosting platform projects for the Server's own
    /// workload identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preferred where the platform offers it, because no secret is stored anywhere
    /// for a later mistake to expose. The reference names the token file to read,
    /// which is where a token is projected and not what it is.
    /// </para>
    /// <para>
    /// Every platform that implements workload identity projects the token as a
    /// file: Kubernetes mounts a projected service-account volume, and the cloud
    /// providers layered on it reach that path in their own way — Azure and AWS
    /// each name it in an environment variable, while Google names a JSON
    /// configuration that in turn carries the path. Reading the file is therefore
    /// the whole mechanism, and doing it directly keeps this assembly free of any
    /// cloud SDK - which matters here, because a Server that only runs against one
    /// vendor's identity service is not demonstrating a platform-independent one.
    /// </para>
    /// <para>
    /// A host that wants a vendor SDK in the loop - to exchange the projected token
    /// for a service-specific one, say - supplies a delegate instead of a path and
    /// keeps that dependency in its own composition root.
    /// </para>
    /// </remarks>
    public sealed class WorkloadIdentityCredentialResolver : ICredentialResolver
    {
        private readonly Func<string, CancellationToken, ValueTask<string?>> m_acquire;
        private readonly string m_audience;

        /// <summary>
        /// Creates a resolver over the token the platform projects.
        /// </summary>
        /// <param name="audience">
        /// The token file path to read. Empty defers to the reference, and then to
        /// the variables the platforms genuinely set.
        /// </param>
        public WorkloadIdentityCredentialResolver(string audience = "")
            : this(ReadProjectedTokenAsync, audience)
        {
        }

        /// <summary>
        /// Creates a resolver over a supplied acquisition delegate, which is what
        /// lets a test exercise this path without a platform, and what lets a host
        /// bring its own identity SDK without this assembly depending on one.
        /// </summary>
        /// <param name="acquire">
        /// Acquires the token for a scope or token path.
        /// </param>
        /// <param name="audience">The scope or token path to request.</param>
        public WorkloadIdentityCredentialResolver(
            Func<string, CancellationToken, ValueTask<string?>> acquire,
            string audience = "")
        {
            m_acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
            m_audience = audience ?? string.Empty;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The audience configured at construction wins; the reference is the
        /// fallback. Workload identity has no secret to name, so what
        /// <c>CredentialReference</c> means on this path is the token being asked
        /// for - and letting a deployment state that explicitly, under a name that
        /// says so, avoids a member whose meaning changes with the authentication
        /// kind.
        /// </remarks>
        public ValueTask<string?> ResolveAsync(string reference, CancellationToken ct)
        {
            string scope = !string.IsNullOrEmpty(m_audience) ? m_audience : reference;
            return m_acquire(scope ?? string.Empty, ct);
        }

        private static async ValueTask<string?> ReadProjectedTokenAsync(
            string scope,
            CancellationToken ct)
        {
            string? path = !string.IsNullOrEmpty(scope) ? scope : ResolveProjectedTokenPath();
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            string value = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            // A projected token carries whatever trailing whitespace the platform
            // wrote. Presenting that in a header fails in a way that looks like a
            // rejected identity rather than a stray byte.
            return value.Trim();
        }

        private static string? ResolveProjectedTokenPath()
        {
            for (int ii = 0; ii < s_tokenPathVariables.Length; ii++)
            {
                string? value = Environment.GetEnvironmentVariable(s_tokenPathVariables[ii]);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            string? google = ResolveGoogleExternalAccountTokenPath();
            if (!string.IsNullOrEmpty(google))
            {
                return google;
            }
            return System.IO.File.Exists(KubernetesProjectedTokenPath)
                ? KubernetesProjectedTokenPath
                : null;
        }

        /// <summary>
        /// Resolves the token path out of a Google external-account credential
        /// configuration.
        /// </summary>
        /// <remarks>
        /// Google is the platform that does not name the token in an environment
        /// variable of its own. It names a JSON configuration in
        /// <c>GOOGLE_APPLICATION_CREDENTIALS</c>, and for the
        /// <c>external_account</c> type that configuration carries the projected
        /// token's path in <c>credential_source.file</c>. Reading it here is what
        /// makes the Google path real rather than assumed; a token file invented
        /// under a Google-shaped variable name would simply never be found.
        /// On GKE the token comes from the metadata server instead, which is not a
        /// file at all — a host in that position supplies an acquisition delegate.
        /// </remarks>
        private static string? ResolveGoogleExternalAccountTokenPath()
        {
            string? configPath = Environment.GetEnvironmentVariable(
                "GOOGLE_APPLICATION_CREDENTIALS");
            if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
            {
                return null;
            }
            try
            {
                using System.IO.FileStream stream = System.IO.File.OpenRead(configPath);
                using JsonDocument document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("credential_source", out JsonElement source) ||
                    !source.TryGetProperty("file", out JsonElement file) ||
                    file.ValueKind != JsonValueKind.String)
                {
                    return null;
                }
                return file.GetString();
            }
            catch (JsonException)
            {
                // A configuration this malformed is a deployment error, but failing
                // to resolve an identity is already reported as such by the caller.
                return null;
            }
            catch (System.IO.IOException)
            {
                return null;
            }
        }

        // The path Kubernetes mounts a projected service-account token at, which is
        // the mechanism every cloud workload identity is layered on. Used only when
        // no platform variable names one.
        private const string KubernetesProjectedTokenPath =
            "/var/run/secrets/kubernetes.io/serviceaccount/token";

        // The variables the platforms genuinely set, verified against each
        // platform's own documentation rather than inferred from its name:
        // AZURE_FEDERATED_TOKEN_FILE is injected by the Azure Workload Identity
        // mutating webhook, and AWS_WEB_IDENTITY_TOKEN_FILE is the AWS SDK
        // standard that EKS IAM-roles-for-service-accounts populates. There is
        // deliberately no Google entry: Google has no such variable, and one
        // invented to look like it would never match anything.
        private static readonly string[] s_tokenPathVariables =
        [
            "AZURE_FEDERATED_TOKEN_FILE",
            "AWS_WEB_IDENTITY_TOKEN_FILE"
        ];
    }
}
