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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// The runtime-neutral seam a projection binding runtime uses to open a live
    /// channel for a compiled, executable form. It is deliberately narrower than
    /// <see cref="IWotBinderRegistry"/>: consumers that only drive live value
    /// exchange (for example an OPC UA target-mapping runtime wired onto a
    /// materialized NodeSet) depend on this interface instead of the full
    /// registry so they cannot accidentally reach into Prepare/Activate/Deactivate
    /// lifecycle concerns that belong to the materialization coordinator.
    /// </summary>
    public interface IWotBindingChannelFactory
    {
        /// <summary>
        /// Opens a live channel for an executable compiled form using the
        /// implementation's credential provider, codecs and safety bounds.
        /// </summary>
        /// <param name="form">The compiled, executable form to activate.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="form"/> is <c>null</c>.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// No executor is registered for the form's binding.
        /// </exception>
        ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, CancellationToken cancellationToken = default);
    }
}
