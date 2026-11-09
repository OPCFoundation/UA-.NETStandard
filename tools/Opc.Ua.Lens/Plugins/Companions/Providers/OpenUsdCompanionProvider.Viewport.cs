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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Creates a read-only viewport source after explicit user activation.
    /// </summary>
    internal interface IOpenUsdViewportSourceFactory
    {
        /// <summary>
        /// Captures the reviewed primary-session representation and its verified asset closure.
        /// </summary>
        Task<OpenUsdViewportSource> OpenViewportAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken);
    }

    internal sealed partial class OpenUsdCompanionProvider
    {
        /// <inheritdoc/>
        public async Task<OpenUsdViewportSource> OpenViewportAsync(
            CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
        {
            ValidateTarget(context, target);
            using CancellationTokenSource bounded =
                CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
            var evidence = new CompanionOperationDraft(target,
                new CompanionOperation("viewport", "Start viewport", CompanionOperationSafety.ReadOnly),
                null, context.Session, DateTimeOffset.MaxValue);
            IOpenUsdCompanionReader reader = m_createReader(context, CreateOptions());
            bool transferred = false;
            try
            {
                OpenUsdConnector.RepresentationInfo representation = await FindAsync(
                    reader, context, target.NodeId, bounded.Token).ConfigureAwait(false);
                ByteString digest = OpenUsdWorkflowMetadata.Digest(context, representation);
                if (representation.Components.Any(static component => component.Enabled &&
                    (component.Dynamic || IsRemote(component))))
                {
                    throw new NotSupportedException(
                        "The embedded preview requires a fixed local closure; " +
                        "dynamic and remote components are refused.");
                }
                ArrayOf<OpenUsdConnector.BindingInfo> bindings =
                    representation.Bindings.Any(static binding => binding.Enabled &&
                        binding.Intent == OpenUsdIntentProfile.UaToUsdTelemetry)
                    ? OpenUsdWorkflowTasks.Bindings(representation, "read-bindings") : [];
                CellCompanionSupport.CheckCount(bindings.Count, 128, "Viewport telemetry bindings");
                foreach (OpenUsdConnector.BindingInfo binding in bindings)
                {
                    if (binding.TimeSampled || binding.PropertyName is "points" or "faceVertexCounts" or
                        "faceVertexIndices" or "normals" or "subdivisionScheme" or "extent" ||
                        binding.PropertyName!.Contains("inputs:", StringComparison.Ordinal) ||
                        binding.PropertyName.Contains("info:", StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            "Live preview bindings cannot alter geometry topology, asset paths or shader inputs.");
                    }
                }
                ArrayOf<OpenUsdVerifiedAsset> assets = await ReadVerifiedAssetsAsync(
                    reader, representation, context, bounded.Token).ConfigureAwait(false);
                OpenUsdViewportScenePolicy.Validate(representation.RootLayerIdentifier!, assets);
                ByteString current = OpenUsdWorkflowMetadata.Digest(context, await FindAsync(
                    reader, context, target.NodeId, bounded.Token).ConfigureAwait(false));
                if (!evidence.MatchesSession(context.Session) ||
                    !CryptographicOperations.FixedTimeEquals(current.Span, digest.Span))
                {
                    throw new InvalidOperationException(
                        "The OpenUSD representation changed during capture. Start again.");
                }
                bounded.Token.ThrowIfCancellationRequested();
                var source = new OpenUsdViewportSource(context, target, evidence, digest,
                    representation.RootLayerIdentifier!, representation.PrimPath!, bindings, assets,
                    reader, m_createWorkflowSource(context));
                transferred = true;
                return source;
            }
            finally
            {
                if (!transferred)
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
