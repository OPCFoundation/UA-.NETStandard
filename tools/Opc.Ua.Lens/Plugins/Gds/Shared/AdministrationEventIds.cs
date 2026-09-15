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

namespace Opc.Ua;

internal static partial class UaLensEventIds
{
    /// <summary>
    /// Administration and discovery tools reserve the source-generated
    /// logging event-id block 3000-5999. Each owned tool takes a base
    /// below and offsets its per-message ids from it. Keep these ranges
    /// non-overlapping when adding messages.
    /// </summary>
    public const int CertificateManagerBase = 3000;

    public const int FileSystemBase = 3050;

    public const int FileSystemNodeBase = 3090;

    public const int GdsDiscoveryBase = 3100;

    public const int GdsManagementBase = 3200;

    public const int GdsPushBase = 3300;

    public const int GdsSessionBase = 3400;

    public const int RoleManagementBase = 3450;

    public const int UserManagementBase = 3500;
}
