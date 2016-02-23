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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua
{
	/// <summary>
    /// An abstract interface to certficate stores.
    /// </summary>
	public interface ICertificateStore : IDisposable
    {
        /// <summary>
        /// Opens the store at the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <remarks>
        /// The syntax depends on the store implementation.
        /// </remarks>
		Task Open(string location);

        /// <summary>
        /// Closes the store.
        /// </summary>
		void Close();

		/// <summary>
		/// Enumerates the certificates in the store.
		/// </summary>
        Task<X509Certificate2Collection> Enumerate();

        /// <summary>
        /// Adds a certificate to the store.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        Task Add(X509Certificate2 certificate);

        /// <summary>
        /// Deletes a certificate from the store.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>True if the certificate exists.</returns>
        Task<bool> Delete(string thumbprint);

        /// <summary>
        /// Finds the certificate with the specified thumprint.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>The matching certificate</returns>
        Task<X509Certificate2Collection> FindByThumbprint(string thumbprint);
        
		/// <summary>
		/// Whether the store supports access control.
		/// </summary>
		bool SupportsAccessControl { get; }

        /// <summary>
        /// Returns the access rules that are currently applied to the store.
        /// </summary>
        /// <returns>The list of access rules.</returns>
		IList<ApplicationAccessRule> GetAccessRules();

        /// <summary>
        /// Sets the access rules that are currently applied to the store.
        /// </summary>
        /// <param name="rules">The rules.</param>
        /// <param name="replaceExisting">if set to <c>true</c> the existing access rules are replaced.</param>
        void SetAccessRules(IList<ApplicationAccessRule> rules, bool replaceExisting);   
        
		/// <summary>
		/// Whether the store supports access control on certificates.
		/// </summary>
		bool SupportsCertificateAccessControl { get; }  
		/// Whether the store supports private keys.
		/// </summary>
        bool SupportsPrivateKeys { get; }

        /// <summary>
        /// Returns the file containing the private key for the specified certificate.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>The full file path. Null if the certificate does not exist or the private key does not exist.</returns>
        string GetPrivateKeyFilePath(string thumbprint);
        
        /// <summary>
        /// Returns the access rules that are currently applied to the certficate's private key.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>The access rules.</returns>
        IList<ApplicationAccessRule> GetAccessRules(string thumbprint);

        /// <summary>
        /// Sets the access rules that are currently applied to the certficate's private key.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <param name="rules">The rules.</param>
        /// <param name="replaceExisting">if set to <c>true</c> the existing access rules are replaced.</param>
        void SetAccessRules(string thumbprint, IList<ApplicationAccessRule> rules, bool replaceExisting);

        /// <summary>
        /// Whether the store supports CRLs.
        /// </summary>
        bool SupportsCRLs { get; }

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate);
       
    };
}
