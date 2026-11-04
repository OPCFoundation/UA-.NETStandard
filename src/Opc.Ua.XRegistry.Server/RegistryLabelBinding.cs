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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Binds inherited String-label Methods to a native registry's single commit path.
    /// </summary>
    public static class RegistryLabelBinding
    {
        /// <summary>
        /// Adds the label Methods before publication and binds them to authorized metadata mutations.
        /// </summary>
        public static void Bind(
            AttributesState attributes,
            ISystemContext context,
            RegistryNativeHost host,
            string xid,
            Func<ISystemContext, RegistryAccessKind, ServiceResult> authorize)
        {
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }
            attributes.AddAddAttribute(context).AddRemoveAttribute(context);
            attributes.AddAttribute!.OnCallMethod2Async = (caller, _, _, input, _, ct) =>
                ChangeAsync(caller, input, remove: false, ct);
            attributes.RemoveAttribute!.OnCallMethod2Async = (caller, _, _, input, _, ct) =>
                ChangeAsync(caller, input, remove: true, ct);
            XRegistryProjectionEngine.LinkMethodArguments(attributes, context);

            async ValueTask<ServiceResult> ChangeAsync(
                ISystemContext caller, ArrayOf<Variant> input, bool remove, CancellationToken ct)
            {
                ServiceResult access = authorize(caller, RegistryAccessKind.Write);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                int epochIndex = remove ? 1 : 2;
                if (input.Count != epochIndex + 1 || !input[0].TryGetValue(out string key) ||
                    !input[epochIndex].TryGetValue(out uint expectedEpoch))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                string? value = null;
                if (!remove && !input[1].TryGetValue(out value))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                RegistryMutationResultDataType result = await host.LabelAsync(xid, key, value, expectedEpoch, ct)
                    .ConfigureAwait(false);
                return result.Issues.Count == 0
                    ? new ServiceResult(result.StatusCode)
                    : new ServiceResult(result.StatusCode, new LocalizedText(result.Issues[0].Detail));
            }
        }
    }
}
