/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#pragma once

/** 
 * @brief Creates a certificate signed by a certificate authority.
    
 * @param sStorePath                [in]     The full path to the store to place the certificate in. 
 * @param sApplicationName          [in]     The name of the application.
 * @param uNoOfHostNames            [in]     The number of host names.
 * @param pHostNames                [in]     A list for host names for the machine.
 * @param uKeyType                  [in]     The type of key. 0 chooses default. Only OpcUa_Crypto_Rsa_Id is supported now.
 * @param uKeySize                  [in]     The size of key in bits (1024 or 2048)
 * @param uLifetimeInMonths         [in]     The lifetime of the certificate in months.
 * @param bIsCA						[in]     Whether a CA certificate should be created.
 * @param bReuseKey					[in]     Whether the key pair used by an existing certificate should be resued.
 * @param eFileFormat               [in]     The format of the private key file.
 * @param pIssuerPublicKey          [in]     The DER encoded issuer certificate (NULL for self-signed certificates).
 * @param pIssuerPrivateKey         [in]     The issuer's private key (NULL for self-signed certificates).
 * @param sPassword                 [in]     The password used to protect the key file.
 * @param pPublicKey                [in\out] The DER encoded certificate (must be provided by caller if bReuseKey = TRUE)
 * @param pPublicKeyFilePath        [out]    The full path to the file containing the key.
 * @param pPrivateKey               [in\out] The private key (must be provided by caller if bReuseKey = TRUE and eFileFormat = PFX).
 * @param pPrivateKeyFilePath       [out]    The full path to the file containing the key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Certificate_Create(
    OpcUa_StringA      a_sStorePath,
    OpcUa_StringA      a_sApplicationName,
    OpcUa_StringA      a_sApplicationUri,
    OpcUa_StringA      a_sOrganization,
    OpcUa_StringA      a_sSubjectName,
    OpcUa_UInt32       a_uNoOfDomainNames,
    OpcUa_StringA*     a_pDomainNames,
    OpcUa_UInt32       a_uKeyType,
    OpcUa_UInt32       a_uKeySize,
    OpcUa_UInt32       a_uLifetimeInMonths,
    OpcUa_Boolean      a_bIsCA,
    OpcUa_Boolean      a_bReuseKey,
    OpcUa_P_FileFormat a_eFileFormat,
    OpcUa_ByteString*  a_pIssuerCertificate,   
    OpcUa_Key*         a_pIssuerPrivateKey,
    OpcUa_StringA      a_sPassword,
    OpcUa_ByteString*  a_pCertificate,   
    OpcUa_StringA*     a_pCertificateFilePath,  
    OpcUa_Key*         a_pPrivateKey,
    OpcUa_StringA*     a_pPrivateKeyFilePath);

/** 
 * @brief Revokes a certificate signed by a certificate authority.
    
 * @param sStorePath                [in]     The full path to the store to place the certificate in. 
 * @param pCertificate              [in]     The DER encoded certificate to revoke.
 * @param pIssuerPrivateKey         [in]     The PKCS#12 encoded issuer private key file. 
 * @param sIssuerPassword           [in]     The password for the issuer key file. 
 * @param bUnrevoke					[in]     Whether to unrevoke the certificate. 
 * @param pCrlFilePath				[out]    The full path to the CRL file created.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Certificate_Revoke(
    OpcUa_StringA     a_sStorePath,
    OpcUa_ByteString* a_pCertificate,   
    OpcUa_ByteString* a_pIssuerPrivateKey,
    OpcUa_StringA     a_sIssuerPassword,
    OpcUa_Boolean     a_bUnrevoke,
    OpcUa_StringA*    a_pCrlFilePath);

/** 
 * @brief Install a certificate into the store (converts format or changes password if it already is installed).
    
 * @param sStorePath                [in]     The full path to the store to place the certificate in. 
 * @param pCertificate              [in]     The DER encoded certificate to convert.
 * @param pIssuerPrivateKey         [in]     The PKCS#12 or PEM encoded issuer private key file. 
 * @param sInputPassword            [in]     The password in the input private key.
 * @param eInputFormat              [in]     The format of the input file.
 * @param sOutputPassword           [in]     The password for the output private key.
 * @param eOutputFormat             [in]     The format of the output file.
 * @param pPublicKeyFilePath        [out]    The full path to the file containing the key.
 * @param pPrivateKeyFilePath       [out]    The full path to the file containing the key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Certificate_Install(
    OpcUa_StringA      a_sStorePath,
    OpcUa_ByteString*  a_pCertificate,   
    OpcUa_ByteString*  a_pPrivateKey,
    OpcUa_StringA      a_sInputPassword,
    OpcUa_P_FileFormat a_eInputFormat,
    OpcUa_StringA      a_sOutputPassword,
    OpcUa_P_FileFormat a_eOutputFormat,
    OpcUa_StringA*     a_pCertificateFilePath,  
    OpcUa_StringA*     a_pPrivateKeyFilePath);

/** 
 * @brief Gets the thumbprint for a certificate.
 *
 * @param pCertificate  [in]  The X509 certificate encoded as a DER blob.
 * @param pThumbprint   [out] The SHA1 thumbprint encoded as a hexadecimal string.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_GetThumbprint(
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA*    a_pThumbprint);

/** 
 * @brief Gets the common name for a certificate.
 *
 * @param pCertificate  [in]  The X509 certificate encoded as a DER blob.
 * @param pThumbprint   [out] The common name field from the certificate subject name.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_GetCommonName(
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA*    a_pCommonName);

/** 
 * @brief Saves a certificate in a store.
 *
 * @param sFriendlyName [in]  A human readable name for the certificate.
 * @param pCertificate  [in]  The X509 certificate encoded as a DER blob.
 * @param pFilePath     [out] The full path to the file containing the key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_SavePublicKeyInStore(
    OpcUa_StringA     a_sStorePath,
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA*    a_pFilePath);

/** 
 * @brief Writes the private key to a file.
 *
 * @param sStorePath      [in]  The full path to the store to place the certificate in. 
 * @param eFileFormat     [in]  The format of the file to save (PEM or PKCK#12).
 * @param sPassword       [in]  The password to use to protect the file.
 * @param pCertificate    [in]  The public key encoded as a DER blob.
 * @param pPrivateKey     [in]  The private key.
 * @param pFilePath       [out] The full path to the file containing the key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_SavePrivateKeyInStore(
    OpcUa_StringA      a_sStorePath,
    OpcUa_P_FileFormat a_eFileFormat,
    OpcUa_StringA      a_sPassword,    
    OpcUa_ByteString*  a_pCertificate,   
    OpcUa_Key*         a_pPrivateKey,
    OpcUa_StringA*     a_pFilePath);


/** 
 * @brief Reads the public key file to the Windows certificate store.
 *
 * @param pContext         [in/out] The context used to continue a previous operation.
 * @param bUseMachineStore [in]     If true use the machine store; otherwise use the current user store.
 * @param sStoreName       [in]     The name of Windows certificate store.
 * @param sCommonName      [in]     The common name of the certificate (ignored if null).
 * @param sThumbprint      [in]     The SHA1 thumbprint of the certificate (ignored if null).
 * @param pCertificate     [out]    The certificate encoded as a DER blob.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_FindCertificateInWindowsStore(
    OpcUa_Handle*     a_pContext,    
    OpcUa_Boolean     a_bUseMachineStore,
    OpcUa_StringA     a_sStoreName,
    OpcUa_StringA     a_sCommonName,
    OpcUa_StringA     a_sThumbprint,
    OpcUa_ByteString* a_pCertificate);

/** 
 * @brief Reads the public key file to the certificate store.
 *
 * @param pContext       [in/out] The context used to continue a previous operation.
 * @param sStorePath,    [in]     The full path to the certificate store.
 * @param bHasPrivateKey [in]     Whether a private key is required.
 * @param sPassword      [in]     The password used to access the private key.
 * @param sCommonName    [in]     The common name of the certificate (ignored if null).
 * @param sThumbprint    [in]     The SHA1 thumbprint of the certificate (ignored if null).
 * @param pCertificate   [out]    The certificate encoded as a DER blob.
 * @param pPrivateKey    [out]    The private key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_FindCertificateInStore(
    OpcUa_Handle*     a_pContext,    
    OpcUa_StringA     a_sStorePath,
    OpcUa_Boolean     a_bHasPrivateKey,
    OpcUa_StringA     a_sPassword,
    OpcUa_StringA     a_sCommonName,
    OpcUa_StringA     a_sThumbprint,
    OpcUa_ByteString* a_pCertificate,
    OpcUa_Key*        a_pPrivateKey);

/** 
 * @brief Frees the context previous returned.
 *
 * @param pContext [in/out] The context used to continue a find previous operation.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_FreeFindContext(
    OpcUa_Handle* a_pContext);

/** 
 * @brief Exports the private key from a Windows store to an OpenSSL store.
 *
 * @param bUseMachineStore [in]  If true use the machine store; otherwise use the current user store.
 * @param sStoreName       [in]  The name of Windows certificate store.
 * @param pCertificate     [in]  The certificate to export.
 * @param sPassword        [in]  The password to use to protect the file.
 * @param sTargetStorePath [in]  The full path to the target OpenSSL store.
 * @param pPrivateKey      [out] The private key.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_ExportPrivateKeyFromWindowsStore(
    OpcUa_Boolean     a_bUseMachineStore,
    OpcUa_StringA     a_sStoreName,
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA     a_sPassword,
    OpcUa_StringA     a_sTargetStorePath,
    OpcUa_Key*        a_pPrivateKey);

/** 
 * @brief Imports the certificate to a Windows store.
 *
 * @param pCertificate     [in]  The certificate to import.
 * @param bUseMachineStore [in]  If true use the machine store; otherwise use the current user store.
 * @param sStoreName       [in]  The name of Windows certificate store.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_ImportToWindowsStore(
    OpcUa_ByteString* a_pCertificate,
    OpcUa_Boolean     a_bUseMachineStore,
    OpcUa_StringA     a_sStoreName);

/** 
 * @brief Imports the private key from an OpenSSL store to a Windows store.
 *
 * @param sSourceStorePath [in]  The full path to the source OpenSSL store.
 * @param pCertificate     [in]  The certificate to import.
 * @param sPassword        [in]  The password to use to protect the file.
 * @param bUseMachineStore [in]  If true use the machine store; otherwise use the current user store.
 * @param sStoreName       [in]  The name of Windows certificate store.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_ImportPrivateKeyToWindowsStore(
    OpcUa_StringA     a_sSourceStorePath,
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA     a_sPassword,
    OpcUa_Boolean     a_bUseMachineStore,
    OpcUa_StringA     a_sStoreName);

/** 
 * @brief Extracts the specified information from the certificate..
 *
 * @param pCertificate      [in]   The certificate to process.
 * @param psNameEntries     [out]  All of the entries in the subject name.
 * @param puNoOfNameEntries [out]  The number of the entries in the subject name.
 * @param psCommonName      [out]  The common name.
 * @param psThumbprint      [out]  The thumbprint.
 * @param psApplicationUri  [out]  The application uri.
 * @param psDomains         [out]  The domains.
 * @param puNoOfDomains     [out]  The number of domains.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_GetInfo(
    OpcUa_ByteString* a_pCertificate,
    OpcUa_StringA**   a_psNameEntries,
    OpcUa_UInt32*     a_puNoOfNameEntries,
    OpcUa_StringA*    a_psCommonName,
    OpcUa_StringA*    a_psThumbprint,
    OpcUa_StringA*    a_psApplicationUri,
    OpcUa_StringA**   a_psDomains,
    OpcUa_UInt32*     a_puNoOfDomains);

/** 
 * @brief Looks up the domain names for the IP address.
 *
 * @param sAddress    [in]  The IP address
 * @param pDomainName [out] The domain name.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_LookupDomainName(
    OpcUa_StringA  a_sAddress,
    OpcUa_StringA* a_pDomainName);

/** 
 * @brief Looks up the names and IP address for the localhost.
 *
 * @param pHostNames     [out] The hostnames.
 * @param pNoOfHostNames [out] The number of hostnames.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_Certificate_LookupLocalhostNames(
    OpcUa_StringA** a_pHostNames,
    OpcUa_UInt32*   a_pNoOfHostNames);


/** 
 * @brief Loads a private key from a file.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OpcUa_StatusCode OpcUa_Certificate_LoadPrivateKeyFromFile(
    OpcUa_StringA      a_sFilePath,
    OpcUa_P_FileFormat a_eFileFormat,
    OpcUa_StringA      a_sPassword,    
    OpcUa_ByteString*  a_pCertificate,   
    OpcUa_Key*         a_pPrivateKey);

/** 
 * @brief Loads a key from a file.
 *
 * @return Status code; @see opcua_statuscodes.h
 */
OpcUa_StatusCode OpcUa_ReadFile(
    OpcUa_StringA     a_sFilePath,
    OpcUa_ByteString* a_pBuffer);