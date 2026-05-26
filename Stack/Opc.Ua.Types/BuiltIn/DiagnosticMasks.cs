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

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The DiagnosticsMasks enumeration.
    /// </summary>
    [Flags]
    public enum DiagnosticsMasks : uint
    {
        /// <summary>
        /// ServiceSymbolicId = 0,
        /// </summary>
        None = 0,

        /// <summary>
        /// ServiceSymbolicId = 1,
        /// </summary>
        ServiceSymbolicId = 1,

        /// <summary>
        /// ServiceLocalizedText = 2,
        /// </summary>
        ServiceLocalizedText = 2,

        /// <summary>
        /// ServiceSymbolicIdAndText = 3,
        /// </summary>
        ServiceSymbolicIdAndText = ServiceSymbolicId | ServiceLocalizedText,

        /// <summary>
        /// ServiceAdditionalInfo = 4,
        /// </summary>
        ServiceAdditionalInfo = 4,

        /// <summary>
        /// ServiceInnerStatusCode = 8,
        /// </summary>
        ServiceInnerStatusCode = 8,

        /// <summary>
        /// ServiceNoInnerStatus = 15,
        /// </summary>
        ServiceNoInnerStatus = ServiceSymbolicIdAndText |
            ServiceAdditionalInfo |
            ServiceInnerStatusCode,

        /// <summary>
        /// ServiceInnerDiagnostics = 16,
        /// </summary>
        ServiceInnerDiagnostics = 16,

        /// <summary>
        /// ServiceAll = 31,
        /// </summary>
        ServiceAll = ServiceNoInnerStatus | ServiceInnerDiagnostics,

        /// <summary>
        /// OperationSymbolicId = 32,
        /// </summary>
        OperationSymbolicId = 32,

        /// <summary>
        /// SymbolicId = 33,
        /// </summary>
        SymbolicId = ServiceSymbolicId | OperationSymbolicId,

        /// <summary>
        /// OperationLocalizedText = 64,
        /// </summary>
        OperationLocalizedText = 64,

        /// <summary>
        /// LocalizedText = 66,
        /// </summary>
        LocalizedText = ServiceLocalizedText | OperationLocalizedText,

        /// <summary>
        /// OperationSymbolicIdAndText = 96,
        /// </summary>
        OperationSymbolicIdAndText = OperationSymbolicId | OperationLocalizedText,

        /// <summary>
        /// SymbolicIdAndText = 99,
        /// </summary>
        SymbolicIdAndText = ServiceSymbolicIdAndText | OperationSymbolicIdAndText,

        /// <summary>
        /// OperationAdditionalInfo = 128,
        /// </summary>
        OperationAdditionalInfo = 128,

        /// <summary>
        /// AdditionalInfo = 132,
        /// </summary>
        AdditionalInfo = ServiceAdditionalInfo | OperationAdditionalInfo,

        /// <summary>
        /// OperationNoInnerStatus = 224,
        /// </summary>
        OperationNoInnerStatus = OperationSymbolicIdAndText | OperationAdditionalInfo,

        /// <summary>
        /// NoInnerStatus = 239,
        /// </summary>
        NoInnerStatus = ServiceNoInnerStatus | OperationNoInnerStatus,

        /// <summary>
        /// OperationInnerStatusCode = 256,
        /// </summary>
        OperationInnerStatusCode = 256,

        /// <summary>
        /// InnerStatusCode = 264,
        /// </summary>
        InnerStatusCode = ServiceInnerStatusCode | OperationInnerStatusCode,

        /// <summary>
        /// OperationInnerDiagnostics = 512,
        /// </summary>
        OperationInnerDiagnostics = 512,

        /// <summary>
        /// InnerDiagnostics = 528,
        /// </summary>
        InnerDiagnostics = ServiceInnerDiagnostics | OperationInnerDiagnostics,

        /// <summary>
        /// OperationAll = 992,
        /// </summary>
        OperationAll = OperationNoInnerStatus |
            OperationInnerStatusCode |
            OperationInnerDiagnostics,

        /// <summary>
        /// All = 1023
        /// </summary>
        All = ServiceAll | OperationAll,

        /// <summary>
        /// UserPermissionAdditionalInfo = 0x80000000,
        /// </summary>
        /// <remarks>
        /// Mask for internal use only.
        /// </remarks>
        UserPermissionAdditionalInfo = 0x80000000
    }
}
