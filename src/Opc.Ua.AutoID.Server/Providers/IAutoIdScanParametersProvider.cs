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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Optional provider capability for the runtime parameters of OPC 30010 6.1.3.1.
    /// </summary>
    /// <remarks>
    /// Supported code names and their indexes remain stable for the provider lifetime.
    /// Writes occur only while idle. A successful write commits the complete configuration;
    /// a rejected or cancelled write must leave the previous configuration intact.
    /// Providers own persistence and the encoding of scan data in the selected format.
    /// </remarks>
    public interface IAutoIdScanParametersProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Gets the supported code names, indexed by the CodeTypes variable.
        /// </summary>
        ArrayOf<string> SupportedCodeTypes { get; }

        /// <summary>
        /// Reads the initial configuration before the reader is published.
        /// </summary>
        ValueTask<AutoIdScanParameters> ReadParametersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits a validated configuration, or throws without changing the existing configuration.
        /// </summary>
        ValueTask WriteParametersAsync(
            AutoIdScanParameters parameters,
            CancellationToken cancellationToken = default);
    }

}
