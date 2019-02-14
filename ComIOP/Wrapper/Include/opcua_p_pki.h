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

#ifndef _OpcUa_P_PKI_H_
#define _OpcUa_P_PKI_H_ 1

OPCUA_BEGIN_EXTERN_C

/**
  @brief The supported PKIs.
*/
typedef enum _OpcUa_P_PKI_Types
{
    OpcUa_Invalid_PKI   = 0,
    OpcUa_NO_PKI        = 1,
    OpcUa_Override      = 2,
    OpcUa_OpenSSL_PKI   = 3,
    OpcUa_Win32_PKI     = 4
} OpcUa_P_PKI_Types;

/* PKI flags */
#define OpcUa_PKI_CheckRevocationStatus 8

/* WIN32 PKI specific flag */
#define WIN32_PKI_USERSTORE 1
#define WIN32_PKI_MACHINESTORE 2
#define WIN32_PKI_SERVICESSTORE 4

/**
  @brief The openssl pki config.
  */
struct _OpcUa_P_OpenSSL_CertificateStore_Config
{
    /*! @brief used PKI type. */
    OpcUa_P_PKI_Types   PkiType;

    /*! @brief The trusted certificate store location. */
    OpcUa_StringA       TrustedCertificateStorePath;

    /*! @brief The issuer certificate store location. */
    OpcUa_StringA       IssuerCertificateStorePath;

    /*! @brief PKI-specific flags. */
    OpcUa_UInt32        Flags;

    /*! @brief External PKIProvider IF to override default implementation. Checked when Configuration name is "Override" */
    OpcUa_Void*         Override;
};
typedef struct _OpcUa_P_OpenSSL_CertificateStore_Config OpcUa_P_OpenSSL_CertificateStore_Config;


/**
  @brief The certificate und key format enumeration.
*/
typedef enum _OpcUa_P_FileFormat
{
    OpcUa_Crypto_Encoding_Invalid   = 0,
    OpcUa_Crypto_Encoding_DER       = 1,
    OpcUa_Crypto_Encoding_PEM       = 2,
    OpcUa_Crypto_Encoding_PKCS12    = 3
}
OpcUa_P_FileFormat;

/**
  @brief Loads a X.509 certificate from the specified file.
  */
OpcUa_StatusCode OpcUa_P_OpenSSL_X509_LoadFromFile(
    OpcUa_StringA               fileName,
    OpcUa_P_FileFormat          fileFormat,
    OpcUa_StringA               sPassword,      /* optional: just for OpcUa_PKCS12 */
    OpcUa_ByteString*           pCertificate);

/**
  @brief Loads a RSA private key from the specified file.
  */
OpcUa_StatusCode OpcUa_P_OpenSSL_RSA_LoadPrivateKeyFromFile(
    OpcUa_StringA           privateKeyFile,
    OpcUa_P_FileFormat      fileFormat,
    OpcUa_StringA           password,
    OpcUa_ByteString*       pPrivateKey);

OPCUA_END_EXTERN_C

#endif
