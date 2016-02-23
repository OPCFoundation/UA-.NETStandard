/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;

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
