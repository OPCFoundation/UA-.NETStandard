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

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Input arguments for a single
    /// <c>PubSubKeyServiceType.GetSecurityKeys</c> call.
    /// </summary>
    /// <remarks>
    /// Implements the input-argument set defined by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.2">
    /// Part 14 §8.3.2 GetSecurityKeys</see>. The struct is value-typed
    /// so that callers can build it without allocating.
    /// </remarks>
    /// <param name="SecurityGroupId">
    /// SecurityGroupId of the group whose keys are requested. Must be
    /// non-empty; the SKS rejects empty identifiers with
    /// <c>BadInvalidArgument</c>.
    /// </param>
    /// <param name="StartingTokenId">
    /// SKS-assigned token id from which to start the response. A value
    /// of <c>0</c> means "the current token id"; any other value
    /// requests history starting at that explicit token id.
    /// </param>
    /// <param name="RequestedKeyCount">
    /// Number of keys requested. <c>1</c> returns only the current
    /// (or specified) key; larger values return up to that many
    /// future keys.
    /// </param>
    public readonly record struct SksKeyRequest(
        string SecurityGroupId,
        uint StartingTokenId,
        uint RequestedKeyCount);
}
