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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Renders a stage camera view and returns an encoded image plus the
    /// metadata a Vision <c>ClipEndpointType</c> needs (dimensions, format,
    /// timestamp). The Vision server takes a dependency on this interface
    /// so it does not have to know that any specific rendering technology
    /// is involved; a host with no rendering support at all can bind a
    /// no-op implementation that always returns
    /// <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must be safe to call concurrently from many callers;
    /// they may serialize access to a graphics device internally.
    /// </remarks>
    public interface ISceneCameraCaptureProvider
    {
        /// <summary>
        /// Describes the graphics backend the provider will use to fulfill
        /// requests. Cheap to read: probed once and cached at construction.
        /// When <see cref="SceneCameraCaptureBackend.IsAvailable"/> is
        /// <c>false</c> every call to
        /// <see cref="CaptureAsync"/> returns
        /// <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
        /// </summary>
        SceneCameraCaptureBackend Backend { get; }

        /// <summary>
        /// Captures the requested camera view and returns an encoded image,
        /// or a <see cref="SceneCameraCaptureResult"/> whose
        /// <see cref="SceneCameraCaptureResult.Status"/> describes why no
        /// image was produced. The implementation never throws for
        /// input-driven failures (missing stage, missing prim, no backend,
        /// blank frame) - those become status codes instead - and never
        /// returns a blank frame as if it succeeded. Cancellation surfaces
        /// as <see cref="System.OperationCanceledException"/>.
        /// </summary>
        ValueTask<SceneCameraCaptureResult> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken);
    }
}
