/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// The DiagnosticsMasks enumeration.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1717:OnlyFlagsEnumsShouldHavePluralNames"), Flags]
    public enum DiagnosticsMasks
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
        /// ServiceAdditionalInfo = 4,
        /// </summary>
        ServiceAdditionalInfo = 4,

        /// <summary>
        /// ServiceInnerStatusCode = 8,
        /// </summary>
        ServiceInnerStatusCode = 8,

        /// <summary>
        /// ServiceInnerDiagnostics = 16,
        /// </summary>
        ServiceInnerDiagnostics = 16,

        /// <summary>
        /// ServiceSymbolicIdAndText = 3,
        /// </summary>
        ServiceSymbolicIdAndText = 3,

        /// <summary>
        /// ServiceNoInnerStatus = 15,
        /// </summary>
        ServiceNoInnerStatus = 15,

        /// <summary>
        /// ServiceAll = 31,
        /// </summary>
        ServiceAll = 31,

        /// <summary>
        /// OperationSymbolicId = 32,
        /// </summary>
        OperationSymbolicId = 32,

        /// <summary>
        /// OperationLocalizedText = 64,
        /// </summary>
        OperationLocalizedText = 64,

        /// <summary>
        /// OperationAdditionalInfo = 128,
        /// </summary>
        OperationAdditionalInfo = 128,

        /// <summary>
        /// OperationInnerStatusCode = 256,
        /// </summary>
        OperationInnerStatusCode = 256,

        /// <summary>
        /// OperationInnerDiagnostics = 512,
        /// </summary>
        OperationInnerDiagnostics = 512,

        /// <summary>
        /// OperationSymbolicIdAndText = 96,
        /// </summary>
        OperationSymbolicIdAndText = 96,

        /// <summary>
        /// OperationNoInnerStatus = 224,
        /// </summary>
        OperationNoInnerStatus = 224,

        /// <summary>
        /// OperationAll = 992,
        /// </summary>
        OperationAll = 992,

        /// <summary>
        /// SymbolicId = 33,
        /// </summary>
        SymbolicId = 33,

        /// <summary>
        /// LocalizedText = 66,
        /// </summary>
        LocalizedText = 66,

        /// <summary>
        /// AdditionalInfo = 132,
        /// </summary>
        AdditionalInfo = 132,

        /// <summary>
        /// InnerStatusCode = 264,
        /// </summary>
        InnerStatusCode = 264,

        /// <summary>
        /// InnerDiagnostics = 528,
        /// </summary>
        InnerDiagnostics = 528,

        /// <summary>
        /// SymbolicIdAndText = 99,
        /// </summary>
        SymbolicIdAndText = 99,

        /// <summary>
        /// NoInnerStatus = 239,
        /// </summary>
        NoInnerStatus = 239,

        /// <summary>
        /// All = 1023
        /// </summary>
        All = 1023
    }
}
