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

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Applies Part 14 reference operations in the application's configuration transaction.
    /// Authorization of the calling Session and privileged elements is the host's responsibility.
    /// </summary>
    public interface IPubSubConfigurationImportApplication
    {
        /// <summary>
        /// Imports only referenced elements, processing removals first.
        /// Persistence is the commit point; an exception after persistence does not imply rollback.
        /// </summary>
        ValueTask<PubSubConfigurationImportResult> ImportConfigurationAsync(
            PubSubConfiguration2DataType configuration,
            ArrayOf<PubSubConfigurationRefDataType> references,
            bool requireCompleteUpdate,
            bool allowSecurityConfiguration = true,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Results in input-reference order, with assigned values and optional address-space objects.
    /// </summary>
    public sealed record PubSubConfigurationImportResult(
        bool ChangesApplied,
        ArrayOf<StatusCode> ReferenceResults,
        ArrayOf<PubSubConfigurationValueDataType> ConfigurationValues,
        ArrayOf<NodeId> ConfigurationObjects);
}
