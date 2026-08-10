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
using System.Threading;
using System.Threading.Tasks;

namespace AiModelManagement.Bridge
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
    /// Resolves an Entra ID access token through the ambient workload identity.
    /// </summary>
    /// <remarks>
    /// Preferred where the platform offers it, because no secret is stored anywhere
    /// for a later mistake to expose. The reference names the scope to request, which
    /// is what a token is requested FOR and not what it is.
    /// </remarks>
    public sealed class WorkloadIdentityCredentialResolver : ICredentialResolver
    {
        private readonly Azure.Core.TokenCredential m_credential;
        private readonly string m_audience;

        /// <summary>
        /// Creates a resolver over the ambient platform identity.
        /// </summary>
        public WorkloadIdentityCredentialResolver(string audience = "")
            : this(new Azure.Identity.DefaultAzureCredential(), audience)
        {
        }

        /// <summary>
        /// Creates a resolver over a supplied credential, which is what lets a test
        /// exercise this path without a cloud.
        /// </summary>
        /// <param name="credential">The credential to request tokens from.</param>
        public WorkloadIdentityCredentialResolver(
            Azure.Core.TokenCredential credential,
            string audience = "")
        {
            m_credential = credential ?? throw new ArgumentNullException(nameof(credential));
            m_audience = audience ?? string.Empty;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The audience configured at construction wins; the reference is the
        /// fallback. Workload identity has no secret to name, so what
        /// <c>CredentialReference</c> means on this path is the scope being asked
        /// for - and letting a deployment state that explicitly, under a name that
        /// says so, avoids a member whose meaning changes with the authentication
        /// kind.
        /// </remarks>
        public async ValueTask<string?> ResolveAsync(string reference, CancellationToken ct)
        {
            string scope = !string.IsNullOrEmpty(m_audience) ? m_audience : reference;

            if (string.IsNullOrEmpty(scope))
            {
                return null;
            }

            Azure.Core.AccessToken token = await m_credential
                .GetTokenAsync(new Azure.Core.TokenRequestContext([scope]), ct)
                .ConfigureAwait(false);
            return token.Token;
        }
    }
}
