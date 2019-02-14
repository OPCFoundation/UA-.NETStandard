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

#ifndef _OpcUa_Crypto_H_
#define _OpcUa_Crypto_H_ 1

OPCUA_BEGIN_EXTERN_C

/* SecurityPolicies */
#define OpcUa_SecurityPolicy_None           "http://opcfoundation.org/UA/SecurityPolicy#None"
#define OpcUa_SecurityPolicy_Basic128Rsa15  "http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15"
#define OpcUa_SecurityPolicy_Basic256       "http://opcfoundation.org/UA/SecurityPolicy#Basic256"

/* a handle for crytographic objects created by the provider */
typedef OpcUa_Void* OpcUa_ProviderHandle;

#define OpcUa_Crypto_Rsa_Name                       L"RSA"                          
#define OpcUa_Crypto_Rsa_Id                         19

#define OpcUa_Crypto_Ec_Name                        L"EC"
#define OpcUa_Crypto_Ec_Id                          20

/* @brief Key Types; If Handle, Data points to an opaque key handle and Length must be interpreted as boolean. */
#define OpcUa_Crypto_KeyType_Invalid                0

#define OpcUa_Crypto_KeyType_Random                 1
#define OpcUa_Crypto_KeyType_Symmetric              2
#define OpcUa_Crypto_KeyType_Asymmetric             3
#define OpcUa_Crypto_KeyType_Any                    4
#define OpcUa_Crypto_KeyType_Rsa_Private            5
#define OpcUa_Crypto_KeyType_Rsa_Public             6
#define OpcUa_Crypto_KeyType_Ecc_Public             7
#define OpcUa_Crypto_KeyType_Ecc_Private            8

/* keys as handles instead of encodings */
#define OpcUa_Crypto_KeyType_Handle_Offset          20

#define OpcUa_Crypto_KeyType_Random_Handle          OpcUa_Crypto_KeyType_Random      + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Symmetric_Handle       OpcUa_Crypto_KeyType_Symmetric   + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Asymmetric_Handle      OpcUa_Crypto_KeyType_Asymmetric  + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Any_Handle             OpcUa_Crypto_KeyType_Asymmetric  + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Rsa_Private_Handle     OpcUa_Crypto_KeyType_Rsa_Private + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Rsa_Public_Handle      OpcUa_Crypto_KeyType_Rsa_Public  + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Ecc_Public_Handle      OpcUa_Crypto_KeyType_Ecc_Public  + OpcUa_Crypto_KeyType_Handle_Offset
#define OpcUa_Crypto_KeyType_Ecc_Private_Handle     OpcUa_Crypto_KeyType_Ecc_Private + OpcUa_Crypto_KeyType_Handle_Offset

/* @brief decide wether a keytype is a handle or not */
#define OPCUA_CRYPTO_KEY_ISHANDLE(xKey)             (xKey->Type > (OpcUa_UInt)OpcUa_Crypto_KeyType_Handle_Offset)
#define OPCUA_CRYPTO_KEY_ISNOHANDLE(xKey)           (xKey->Type < (OpcUa_UInt)OpcUa_Crypto_KeyType_Handle_Offset)

/* algorithm codes */
#define OpcUa_Crypto_Rsa_PKCS1_Name                 L"RSA-PKCS-#1-V1.5"
#define OpcUa_Crypto_Rsa_PKCS1_Id                   4

#define OpcUa_Crypto_Rsa_OAEP_Name                  L"RSA-OAEP"
#define OpcUa_Crypto_Rsa_OAEP_Id                    5

#define OpcUa_Crypto_Rsa_Alg_Name                   L"rsaEncryption"
#define OpcUa_Crypto_Rsa_Alg_Id                     6

#define OpcUa_Crypto_Ecc_Alg_Name                   L"id-ecPublicKey" 
#define OpcUa_Crypto_Ecc_Alg_Id                     408

#define OpcUa_Crypto_sha1WithRSAEncryption_Name     L"sha1WithRSAEncryption"
#define OpcUa_Crypto_sha1WithRSAEncryption_Id       65
#define OpcUa_Crypto_Sig_sha1WithRSAEncryption      OpcUa_Crypto_sha1WithRSAEncryption_Id

#define OpcUa_Crypto_sha224WithRSAEncryption_Name   L"sha224WithRSAEncryption"
#define OpcUa_Crypto_sha224WithRSAEncryption_Id     671
#define OpcUa_Crypto_Sig_sha224WithRSAEncryption    OpcUa_Crypto_sha224WithRSAEncryption_Id

#define OpcUa_Crypto_sha256WithRSAEncryption_Name   L"sha256WithRSAEncryption"
#define OpcUa_Crypto_sha256WithRSAEncryption_Id     668
#define OpcUa_Crypto_Sig_sha256WithRSAEncryption    OpcUa_Crypto_sha256WithRSAEncryption_Id

#define OpcUa_Crypto_sha384WithRSAEncryption_Name   L"sha384WithRSAEncryption"
#define OpcUa_Crypto_sha384WithRSAEncryption_Id     669
#define OpcUa_Crypto_Sig_sha384WithRSAEncryption    OpcUa_Crypto_sha384WithRSAEncryption_Id

#define OpcUa_Crypto_sha512WithRSAEncryption_Name   L"sha512WithRSAEncryption"
#define OpcUa_Crypto_sha512WithRSAEncryption_Id     670
#define OpcUa_Crypto_Sig_sha512WithRSAEncryption    OpcUa_Crypto_sha512WithRSAEncryption_Id

#define OpcUa_Crypto_sha1WithECC_Name               L"sha1WithECC"
#define OpcUa_Crypto_sha1WithECC_Id                 75

#define OpcUa_Crypto_sha224WithECC_Name             L"sha224WithECC"
#define OpcUa_Crypto_sha224WithECC_Id               771

#define OpcUa_Crypto_sha256WithECC_Name             L"sha256WithECC"
#define OpcUa_Crypto_sha256WithECC_Id               768

#define OpcUa_Crypto_sha384WithECC_Name             L"sha384WithECC"
#define OpcUa_Crypto_sha384WithECC_Id               769

#define OpcUa_Crypto_sha512WithECC_Name             L"sha512WithECC"
#define OpcUa_Crypto_sha512WithECC_Id               770
    
#define OpcUa_Crypto_hmacWithSHA1_Name              L"hmacWithSHA1"
#define OpcUa_Crypto_hmacWithSHA1_Id                163

#define OpcUa_Crypto_hmacWithSHA224_Name            L"hmacWithSHA224"
#define OpcUa_Crypto_hmacWithSHA224_Id              164

#define OpcUa_Crypto_hmacWithSHA256_Name            L"hmacWithSHA256"
#define OpcUa_Crypto_hmacWithSHA256_Id              165

#define OpcUa_Crypto_hmacWithSHA384_Name            L"hmacWithSHA384"
#define OpcUa_Crypto_hmacWithSHA384_Id              166

#define OpcUa_Crypto_hmacWithSHA512_Name            L"hmacWithSHA512"
#define OpcUa_Crypto_hmacWithSHA512_Id              167

#define OpcUa_Crypto_aes_128_ecb_Name               L"aes-128-ecb"
#define OpcUa_Crypto_aes_128_ecb_Id                 418

#define OpcUa_Crypto_aes_128_cbc_Name               L"aes-128-cbc"
#define OpcUa_Crypto_aes_128_cbc_Id                 419

#define OpcUa_Crypto_aes_256_ecb_Name               L"aes-256-ecb"
#define OpcUa_Crypto_aes_256_ecb_Id                 426

#define OpcUa_Crypto_aes_256_cbc_Name               L"aes-256-cbc"
#define OpcUa_Crypto_aes_256_cbc_Id                 427

#define OpcUa_Crypto_aes_512_ecb_Name               L"aes-512-ecb"
#define OpcUa_Crypto_aes_512_ecb_Id                 428

#define OpcUa_Crypto_aes_512_cbc_Name               L"aes-2512-cbc"
#define OpcUa_Crypto_aes_512_cbc_Id                 429

#define OpcUa_Crypto_PSHA1_Name                     L"P-SHA1"
#define OpcUa_Crypto_PSHA1_Id                       999


struct _OpcUa_SecurityKeyset;
struct OpcUa_Crypto_Key_;
struct OpcUa_Crypto_Signature_;

/** 
  @brief The Signature.
  */
struct OpcUa_Crypto_Signature_
{
    OpcUa_UInt          Algorithm;
    OpcUa_ByteString    Signature;
};
typedef struct OpcUa_Crypto_Signature_ OpcUa_Signature;

OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Signature_Initialize(OpcUa_Signature* pSignature);
OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Signature_Clear(OpcUa_Signature* pSignature);


/** 
  @brief The CryptoKey.
  */
struct OpcUa_Crypto_Key_;
typedef OpcUa_Void (*OpcUa_Key_ClearHandle)(struct OpcUa_Crypto_Key_* pKey);

struct OpcUa_Crypto_Key_
{
    OpcUa_UInt              Type;
    OpcUa_ByteString        Key;
    OpcUa_Key_ClearHandle   fpClearHandle;
};
typedef struct OpcUa_Crypto_Key_ OpcUa_Key;

OPCUA_EXPORT OpcUa_Void OpcUa_Key_Initialize(OpcUa_Key* pKey);
OPCUA_EXPORT OpcUa_Void OpcUa_Key_Clear(OpcUa_Key* pKey);

/** 
  @brief The SecurityKeyset.
  */
struct _OpcUa_SecurityKeyset
{   
    /** @brief The signing key. */
    OpcUa_Key      SigningKey;

    /** @brief The encryption key. */
    OpcUa_Key      EncryptionKey;

    /** @brief The initialization vector. */
    OpcUa_Key      InitializationVector;
};

typedef struct _OpcUa_SecurityKeyset OpcUa_SecurityKeyset;
OpcUa_Void OpcUa_SecurityKeyset_Initialize(OpcUa_SecurityKeyset* pSecurityKeyset);
OpcUa_Void OpcUa_SecurityKeyset_Clear(OpcUa_SecurityKeyset* pSecurityKeyset);

/** 
  @brief Name entry structure for an X509 certificate. Used to build the X.509 Subject/Issuer name.
*/
struct OpcUa_Crypto_NameEntry_
{
    char* key;
    char* value;
};

typedef struct OpcUa_Crypto_NameEntry_ OpcUa_Crypto_NameEntry;

/** 
  @brief Extension structure for an X509 certificate. Used to add V3 extensions for a X.509 certificate.
*/
struct OpcUa_Crypto_Extension_
{
    char* key;
    char* value;
};

typedef struct OpcUa_Crypto_Extension_ OpcUa_Crypto_Extension;
typedef OpcUa_Void* OpcUa_Certificate;

/************************************************************************************/
/* CryptoProvider Interface Definition                                              */
/************************************************************************************/

/* a structure that contains the function implementations by a particular provider. */
struct _OpcUa_CryptoProvider;

/**
  @brief 
 */
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGenerateAsymmetricKeypair)(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_UInt                      type,
    OpcUa_UInt32                    bits,
    OpcUa_Key*                      pPublicKey,
    OpcUa_Key*                      pPrivateKey);

/**
  @brief
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GenerateAsymmetricKeypair(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_UInt                      type,
    OpcUa_UInt32                    bits,
    OpcUa_Key*                      pPublicKey,
    OpcUa_Key*                      pPrivateKey);

/**
  @brief 
 */
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGetAsymmetricKeyLength)(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Key                       publicKey,
    OpcUa_UInt32*                   uBits);

/**
  @brief
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GetAsymmetricKeyLength(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Key                       publicKey,
    OpcUa_UInt32*                   uBits);

/**
  @brief Adds random data to the destination buffer.

  Function Pointer!
 
  @param pProvider        [in]  The crypto provider handle.
  @param keyLen           [in]  The desired length of the random key.
  
  @param pKey             [out] The generated random key.
 */
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGenerateKey)(
    struct _OpcUa_CryptoProvider*    pProvider,
    OpcUa_Int32                      keyLen,
    OpcUa_Key*                       pKey);

/**
  @brief Adds random data to the destination buffer.

  Abstract!
 
  @param pProvider        [in]  The crypto provider handle.
  @param keyLen           [in]  The desired length of the random key.
  
  @param pKey             [out] The generated random key.
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GenerateKey(
    struct _OpcUa_CryptoProvider*    pProvider,
    OpcUa_Int32                      keyLen,
    OpcUa_Key*                       pKey);

/** 
  @brief Generates a session key using secret input data.

  Function Pointer!

  @param pProvider        [in]  The crypto provider handle.
  @param secret           [in]  The secret information to create a random key.
  @param seed             [in]  The seed to create a random key.
  @param keyLen           [in]  The desired length of the random key.

  @param pKey             [out] The derived random key.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnDeriveKey)(  
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_ByteString              secret, /* clientnonce | servernonce, servernonce | clientnonce */
    OpcUa_ByteString              seed,
    OpcUa_Int32                   keyLen, /* output len */
    OpcUa_Key*                    pKey);

/** 
  @brief Generates a session key using secret input data.
 
  Abstract!

  @param pProvider        [in]  The crypto provider handle.
  @param secret           [in]  The secret information to create a random key.
  @param seed             [in]  The seed to create a random key.
  @param keyLen           [in]  The desired length of the random key.

  @param pKey             [out] The derived random key.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_DeriveKey(
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_ByteString              secret, 
    OpcUa_ByteString              seed,
    OpcUa_Int32                   keyLen, /* output len */
    OpcUa_Key*                    pKey);


/** 
  @brief Derives a set of symmetric keys (encryption, signature, initialization vector) from a given input.
 
  Abstract!

  @param pCryptoProvider  [in]  The crypto provider handle.
  @param clientNonce      [in]  The client nonce.
  @param serverNonce      [in]  The server nonce.
  @param keySize          [in]  The desired length of the random keys.

  @param pClientKeyset    [out] The derived client-side keyset.
  @param pServerKeyset    [out] The derived server-side keyset.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnDeriveChannelKeysets)(  
        struct _OpcUa_CryptoProvider*           pCryptoProvider,
        OpcUa_ByteString                        clientNonce,
        OpcUa_ByteString                        serverNonce,
        OpcUa_Int32                             keySize,
        OpcUa_SecurityKeyset*                   pClientKeyset,
        OpcUa_SecurityKeyset*                   pServerKeyset);

/** 
  @brief Derives a set of symmetric keys (encryption, signature, initialization vector) from a given input.
 
  Abstract!

  @param pCryptoProvider  [in]  The crypto provider handle.
  @param clientNonce      [in]  The client nonce.
  @param serverNonce      [in]  The server nonce.
  @param keySize          [in]  The desired length of the random keys.

  @param pClientKeyset    [out] The derived client-side keyset.
  @param pServerKeyset    [out] The derived server-side keyset.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_DeriveChannelKeysets(
        struct _OpcUa_CryptoProvider*           pCryptoProvider,
        OpcUa_ByteString                        clientNonce,
        OpcUa_ByteString                        serverNonce,
        OpcUa_Int32                             keySize,
        OpcUa_SecurityKeyset*                   pClientKeyset,
        OpcUa_SecurityKeyset*                   pServerKeyset);

/** 
  @brief Gets the public key from a given certificate.

  Function Pointer!

  @param pProvider               [in]  A pointer to a crypto provider.
  @param certificate             [in]  The passed in certificate.
  @param password                [in]  Password for certificate. Only used when certificate is password protected. (Optional)
  
  @param pPrivateKey             [out] The read out private key of the certificate.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGetPrivateKeyFromCert)(   
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_StringA                       certificateFileName,
    OpcUa_StringA                       password,             /* this could be optional */
    OpcUa_Key*                          pPrivateKey);

/** 
  @brief Gets the public key from a given certificate.
 
  Abstract!

  @param pProvider               [in]  A pointer to a crypto provider.
  @param certificate             [in]  The passed in certificate.
  @param password                [in]  Password for certificate. Only used when certificate is password protected. (Optional)
  
  @param pPrivateKey             [out] The read out private key of the certificate.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GetPrivateKeyFromCert(
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_StringA                       certificateFileName,
    OpcUa_StringA                       password,             /* this could be optional */
    OpcUa_Key*                          pPrivateKey);

/** 
  @brief Gets the private key from a given certificate.
 
  Function Pointer!

  @param pProvider                [in]  A pointer to a crypto provider.
  @param pCertificate             [in]  A pointer to a DER encoded ByteString representation of the certificate.
  @param password                 [in]  Password for certificate. Only used when certificate is password protected. (Optional)
  
  @param pPublicKey               [out] The read out public key of the certificate.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGetPublicKeyFromCert)(   
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_StringA                       password,             /* this could be optional */
    OpcUa_Key*                          pPublicKey);

/** 
  @brief Gets the private key from a given certificate.
 
  Abstract!

  @param pProvider                [in]  A pointer to a crypto provider.
  @param pCertificate             [in]  A pointer to a DER encoded ByteString representation of the certificate.
  @param password                 [in]  Password for certificate. Only used when certificate is password protected. (Optional)
  
  @param pPublicKey               [out] The read out public key of the certificate.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GetPublicKeyFromCert(
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_StringA                       password,             /* this could be optional */
    OpcUa_Key*                          pPublicKey);

/** 
  @brief Gets the signature from a given certificate.
 
  Function Pointer!

  @param pProvider                [in]  A pointer to a crypto provider.
  @param certificate              [in]  The passed in certificate.
  
  @param pSignature               [out] The read out signature of the certificate.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGetSignatureFromCert)(   
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_Signature*                    pSignature);

/** 
  @brief Gets the private key from a given certificate.
 
  Abstract!

  @param pProvider                [in]  A pointer to a crypto provider.
  @param certificate              [in]  The passed in certificate.
  
  @param pSignature               [out] The read out signature of the certificate.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GetSignatureFromCert(
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_Signature*                    pSignature);

/** 
  @brief Gets the signature from a given certificate.
 
  Function Pointer!

  @param pProvider                  [in]  A pointer to a crypto provider.
  @param pCertificate               [in]  The passed in certificate.
  
  @param pCertificateThumbprint     [out] The thumbprint of the certificate.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnGetCertificateThumbprint)(   
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_ByteString*                   pCertificateThumbprint);

/** 
  @brief Gets the private key from a given certificate.
 
  Abstract!

  @param pProvider                  [in]  A pointer to a crypto provider.
  @param pCertificate               [in]  The passed in certificate.
  
  @param pCertificateThumbprint     [out] The thumbprint of the certificate.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_GetCertificateThumbprint(
    struct _OpcUa_CryptoProvider*       pProvider,
    OpcUa_ByteString*                   pCertificate,
    OpcUa_ByteString*                   pCertificateThumbprint);

/** 
  @brief Creates a new X509 selfsigned certificate object.

  Function Pointer!

  @param pProvider                [in]  The crypto provider handle.
  @param serialNumber             [in]  The serial number of the desired certificate (Should be read out of the cetificate store).
  @param validFrom                [in]  The validation start time information.  
  @param validTo                  [in]  The validation end time information.  
  @param pNameEntries             [in]  Name entries for the certificate.
  @param nameEntriesCount         [in]  The count of name entries located at the address in pNameEntries.
  @param pSubjectPublicKey        [in]  The subject's public key.
  @param pExtensions              [in]  The extensions for the desired certificate.
  @param extensionsCount          [in]  The count of extension at the address in pExtensions.
  @param signatureHashAlgorithm   [in]  The hash algorithm for calculating the signature.
  @param pIssuerCertificate       [in]  The certificate for the certificate authority.
  @param pIssuerPrivateKey        [in]  The private key of the certificate authority.
  
  @param ppCertificate           [out] The new self-signed certificate.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnCreateCertificate)(   
    struct _OpcUa_CryptoProvider*       pProvider,
    const OpcUa_Int32                   serialNumber,
    OpcUa_DateTime                      validFrom,
    OpcUa_DateTime                      validTo,
    OpcUa_Crypto_NameEntry*             pNameEntries,       /* will be used for issuer and subject thus it's selfigned cert */
    OpcUa_UInt                          nameEntriesCount,   /* will be used for issuer and subject thus it's selfigned cert */
    OpcUa_Key                           pSubjectPublicKey,  /* EVP_PKEY* - type defines also public key algorithm */
    OpcUa_Crypto_Extension*             pExtensions,
    OpcUa_UInt                          extensionsCount,
    const OpcUa_UInt                    signatureHashAlgorithm, /* EVP_sha1(),... */
    OpcUa_Certificate*                  pIssuerCertificate, 
    OpcUa_Key                           pIssuerPrivateKey,  /* EVP_PKEY* - type defines also signature algorithm */                   
    OpcUa_Certificate**                 ppCertificate);     /* this has to be changed to OpcUa_Certificate** */

/** 
  @brief Creates a new X509 selfsigned certificate object.
 
  Abstract!

  @param pProvider                [in]  The crypto provider handle.
  @param serialNumber             [in]  The serial number of the desired certificate (Should be read out of the cetificate store).
  @param validFrom                [in]  The validation start time information.  
  @param validTo                  [in]  The validation end time information.  
  @param pNameEntries             [in]  Name entries for the certificate.
  @param nameEntriesCount         [in]  The count of name entries located at the address in pNameEntries.
  @param pSubjectPublicKey        [in]  The subject's public key.
  @param pExtensions              [in]  The extensions for the desired certificate.
  @param extensionsCount          [in]  The count of extension at the address in pExtensions.
  @param signatureHashAlgorithm   [in]  The hash algorithm for calculating the signature.
  @param pIssuerCertificate       [in]  The certificate for the certificate authority.
  @param pIssuerPrivateKey        [in]  The private key of the certificate authority.
  
  @param ppCertificate           [out] The new self-signed certificate.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_CreateCertificate(
    struct _OpcUa_CryptoProvider*       pProvider,
    const OpcUa_Int32                   serialNumber,
    OpcUa_DateTime                      validFrom,
    OpcUa_DateTime                      validTo,
    OpcUa_Crypto_NameEntry*             pNameEntries,       /* will be used for issuer and subject thus it's selfigned cert */
    OpcUa_UInt                          nameEntriesCount,   /* will be used for issuer and subject thus it's selfigned cert */
    OpcUa_Key                           pSubjectPublicKey,  /* EVP_PKEY* - type defines also public key algorithm */
    OpcUa_Crypto_Extension*             pExtensions,
    OpcUa_UInt                          extensionsCount,
    const OpcUa_UInt                    signatureHashAlgorithm, /* EVP_sha1(),... */
    OpcUa_Certificate*                  pIssuerCertificate, 
    OpcUa_Key                           pIssuerPrivateKey,  /* EVP_PKEY* - type defines also signature algorithm */                   
    OpcUa_Certificate**                 ppCertificate);     /* this has to be changed to OpcUa_Certificate** */

/** 
  @brief Encrypts data using (RSA)<NAME> with the public key of the appropriate key pair.

  Function Pointer!

  synchronous!

  @param pProvider         [in]  The crypto provider handle.
  @param pPlainText        [in]  The plain text to encrypt.
  @param plainTextLen      [in]  The length of the plain text to encrypt.
  @param publicKey         [in]  The public key used to encrypt the plain text.

  @param pCipherText       [out] The encrypted text.
  @param pCipherTextLen    [out] The length of the encrypted text.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnAsymmetricEncrypt)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32                    plainTextLen, 
    OpcUa_Key*                      publicKey,
    OpcUa_Byte*                     pCipherText, 
    OpcUa_UInt32*                   pCipherTextLen);

/** 
  @brief Encrypts data using (RSA)<NAME> with the public key of the appropriate key pair.
 
  Abstract!

  synchronous!

  @param pProvider         [in]  The crypto provider handle.
  @param pPlainText        [in]  The plain text to encrypt.
  @param plainTextLen      [in]  The length of the plain text to encrypt.
  @param publicKey         [in]  The public key used to encrypt the plain text.

  @param pCipherText       [out] The encrypted text.
  @param pCipherTextLen    [out] The length of the encrypted text.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_AsymmetricEncrypt(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32                    plainTextLen, 
    OpcUa_Key*                      publicKey,
    OpcUa_Byte*                     pCipherText, 
    OpcUa_UInt32*                   pCipherTextLen);

/** 
  @brief Decrypts encrypted data using <NAME>(RSA) with the private key of the appropriate key pair.

   Abstract!

  @param pProvider         [in]  The crypto provider handle.
  @param pCipherText       [in]  The cipher text to decrypt.
  @param cipherTextLen     [in]  The length of the cipher text to decrypt.
  @param privateKey        [in]  The private key used to decrypt the plain text.

  @param pPlainText        [out] The decrypted text.
  @param pPlainTextLen     [out] The length of the decrypted text.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_AsymmetricDecrypt(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32                    cipherTextLen, 
    OpcUa_Key*                      privateKey,
    OpcUa_Byte*                     pPlainText, 
    OpcUa_UInt32*                   pPlainTextLen);

/** 
  @brief Decrypts encrypted data using <NAME>(RSA) with the private key of the appropriate key pair.
 
  Function Pointer!

  synchonous!

  @param pProvider          [in]  The crypto provider handle.
  @param pCipherText        [in]  The cipher text to decrypt.
  @param cipherTextLen      [in]  The length of the cipher text to decrypt.
  @param privateKey         [in]  The private key used to decrypt the plain text.
  @param padding            [in]  The paddin scheme used for filling empty bytes after encryption.

  @param pPlainText         [out] The decrypted text.
  @param pPlainTextLen      [out] The length of the decrypted text.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnAsymmetricDecrypt)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32                    cipherTextLen, 
    OpcUa_Key*                      privateKey,
    OpcUa_Byte*                     pPlainText, 
    OpcUa_UInt32*                   pPlainTextLen);

/** 
  @brief Signs data using <NAME>(RSA) with the private key of the appropriate key pair.

  Function Pointer!

  @param pProvider         [in]  The crypto provider handle.
  @param data              [in]  The data to sign.
  @param privateKey        [in]  The private key used to sign the data.

  @param pSignature        [out] The signature of the data.
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnAsymmetricSign)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_ByteString                data,
    OpcUa_Key*                      privateKey,
    OpcUa_ByteString*               pSignature);

/** 
  @brief Signs data using <NAME>(RSA) with the private key of the appropriate key pair.

  Abstract!

  @param pProvider         [in]  The crypto provider handle.
  @param data              [in]  The data to sign.
  @param privateKey        [in]  The private key used to sign the data.

  @param pSignature        [out] The signature of the data.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_AsymmetricSign(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_ByteString                data,
    OpcUa_Key*                      privateKey,
    OpcUa_ByteString*               pSignature);

/** 
  @brief Verifies signed data using <NAME>(RSA) with the public key of the appropriate key pair.

  Function Pointer!

  @param pProvider                  [in]  The crypto provider handle.
  @param data                       [in]  The data that was signed.
  @param publicKey                  [in]  The public key used to verify the signature.
  @param pSignature                 [in]  The signature of the data that should be verified.
 */
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnAsymmetricVerify)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_ByteString                data,
    OpcUa_Key*                      publicKey,
    OpcUa_ByteString*               pSignature);

/** 
  @brief Verifies signed data using <NAME>(RSA) with the public key of the appropriate key pair.
 
  Abstract!

  @param pProvider                  [in]  The crypto provider handle.
  @param data                       [in]  The data that was signed.
  @param publicKey                  [in]  The public key used to verify the signature.
  @param pSignature                 [in]  The signature of the data that should be verified.
 */
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_AsymmetricVerify(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_ByteString                data,
    OpcUa_Key*                      publicKey,
    OpcUa_ByteString*               pSignature);

/** 
  @brief Encrypts data using Advanced Encryption Standard (AES) with the Cipher Block Chaining (CBC) mode.
 
   Function Pointer!

   keylen = blocksize => fixed sizes of 128 = 10 rounds, 192 = 12 rounds, 256 = 14 rounds
   message length = outputlength

  @param pProvider              [in]  Provider handle.
  @param pPlainText             [in]  Plain text to encrypt.
  @param plainTextLen           [in]  The length of the plain text.
  @param key                    [in]  The encryption/decryption key.
  @param pInitalVector          [in]  The initial vector.

  @param pCipherText            [out] The encrypted text.
  @param pCipherTextLen         [out] The length of the encrypted text
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnSymmetricEncrypt)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32                    plainTextLen,
    OpcUa_Key*                      key,
    OpcUa_Byte*                     pInitalVector,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32*                   pCipherTextLen);

/** 
  @brief Encrypts data using Advanced Encryption Standard (AES) with the Cipher Block Chaining (CBC) mode.
 
   Abstract!

   keylen = blocksize => fixed sizes of 128 = 10 rounds, 192 = 12 rounds, 256 = 14 rounds
   message length = outputlength

  @param pProvider              [in]  Provider handle.
  @param pPlainText             [in]  Plain text to encrypt.
  @param plainTextLen           [in]  The length of the plain text.
  @param key                    [in]  The encryption/decryption key.
  @param pInitalVector          [in]  The initial vector.

  @param pCipherText            [out] The encrypted text.
  @param pCipherTextLen         [out] The length of the encrypted text
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_SymmetricEncrypt(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32                    plainTextLen,
    OpcUa_Key*                      key,
    OpcUa_Byte*                     pInitalVector,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32*                   pCipherTextLen);

/** 
  @brief Decrypts encrypted data using Advanced Encryption Standard (AES) with the Cipher Block Chaining (CBC) mode.
 
  Function Pointer!

  synchronous!

  @param pProvider              [in]  Provider handle.
  @param pCipherText            [in]  Cipher text to decrypt.
  @param cipherTextLen          [in]  The length of the cipher text.
  @param key                    [in]  The encryption/decryption key.
  @param pInitalVector          [in]  The initial vector.

  @param pPlainText             [out] The decrypted text.
  @param pPlainTextLen          [out] The length of the decrypted text
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnSymmetricDecrypt)(  
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32                    cipherTextLen,
    OpcUa_Key*                      key,
    OpcUa_Byte*                     pInitalVector,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32*                   pPlainTextLen);

/** 
  @brief Decrypts encrypted data using Advanced Encryption Standard (AES) with the Cipher Block Chaining (CBC) mode.
 
  Abstract!

  synchronous!

  @param pProvider              [in]  Provider handle.
  @param pCipherText            [in]  Cipher text to decrypt.
  @param cipherTextLen          [in]  The length of the cipher text.
  @param key                    [in]  The encryption/decryption key.
  @param pInitalVector          [in]  The initial vector.

  @param pPlainText             [out] The decrypted text.
  @param pPlainTextLen          [out] The length of the decrypted text
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_SymmetricDecrypt(
    struct _OpcUa_CryptoProvider*   pProvider,
    OpcUa_Byte*                     pCipherText,
    OpcUa_UInt32                    cipherTextLen,
    OpcUa_Key*                      key,
    OpcUa_Byte*                     pInitalVector,
    OpcUa_Byte*                     pPlainText,
    OpcUa_UInt32*                   pPlainTextLen);

/** 
  @brief Generates s 20 Bytes Message Authentication Code (MAC) of the given input buffer and a secret key.

  Function Pointer!
  
  synchronous!

  @param pProvider        [in]  The crypto provider handle.
  @param pData            [in]  The data for the MAC generation.
  @param dataLen          [in]  The length data for the MAC generation.
  @param key              [in]  The key for the MAC generation.

  @param pSignature       [out] The resulting signature (MAC).
*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnSymmetricSign)(  
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_Byte*                   pData,
    OpcUa_UInt32                  dataLen,
    OpcUa_Key*                    key,
    OpcUa_ByteString*             pSignature);

/** 
  @brief Generates s 20 Bytes Message Authentication Code (MAC) of the given input buffer and a secret key.

  Abstract!
  
  synchronous!

  @param pProvider        [in]  The crypto provider handle.
  @param pData            [in]  The data for the MAC generation.
  @param dataLen          [in]  The length data for the MAC generation.
  @param key              [in]  The key for the MAC generation.

  @param pSignature      [out] The resulting Signature (messsage authentication code).
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_SymmetricSign(
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_Byte*                   pData,
    OpcUa_UInt32                  dataLen,
    OpcUa_Key*                     key,
    OpcUa_ByteString*             pSignature);

/** 
  @brief Generates s 20 Bytes Message Authentication Code (MAC) of the given input buffer and a secret key.

  Function Pointer!
  
  synchronous!

  @param pProvider                  [in]  The crypto provider handle.
  @param pData                      [in]  The data for the MAC generation.
  @param dataLen                    [in]  The length data for the MAC generation.
  @param key                        [in]  The key for the MAC generation.
  @param pSignature                 [in]  The resulting signature (MAC).

*/
typedef OpcUa_StatusCode (OpcUa_Crypto_PfnSymmetricVerify)(  
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_Byte*                   pData,
    OpcUa_UInt32                  dataLen,
    OpcUa_Key*                    key,
    OpcUa_ByteString*             pSignature);

/** 
  @brief Generates s 20 Bytes Message Authentication Code (MAC) of the given input buffer and a secret key.

  Abstract!
  
  synchronous!

  @param pProvider                  [in]  The crypto provider handle.
  @param pData                      [in]  The data for the MAC generation.
  @param dataLen                    [in]  The length data for the MAC generation.
  @param key                        [in]  The key for the MAC generation.
  @param pSignature                 [in]  The signature (MAC).

*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Crypto_SymmetricVerify(
    struct _OpcUa_CryptoProvider* pProvider,
    OpcUa_Byte*                   pData,
    OpcUa_UInt32                  dataLen,
    OpcUa_Key*                    key,
    OpcUa_ByteString*             pSignature);

/*============================================================================
 * The OpcUa_CryptoProvider interface.
 *===========================================================================*/

typedef struct _OpcUa_CryptoProvider
{
    OpcUa_StringA                               Name;
    OpcUa_ProviderHandle                        Handle;
    OpcUa_Int32                                 SymmetricKeyLength;
    OpcUa_UInt32                                MinimumAsymmetricKeyLength;
    OpcUa_UInt32                                MaximumAsymmetricKeyLength;
    OpcUa_UInt32                                DerivedEncryptionKeyLength;
    OpcUa_UInt32                                DerivedSignatureKeyLength;
    OpcUa_UInt32                                SignatureDataLength;
    OpcUa_UInt32                                AsymmetricSignatureAlgorithmId;
    OpcUa_UInt32                                AsymmetricEncryptionAlgorithmId;
    OpcUa_UInt32                                SymmetricSignatureAlgorithmId;
    OpcUa_UInt32                                SymmetricEncryptionAlgorithmId;

    OpcUa_Crypto_PfnGenerateKey*                GenerateKey;
    OpcUa_Crypto_PfnDeriveKey*                  DeriveKey;
    OpcUa_Crypto_PfnDeriveChannelKeysets*       DeriveChannelKeysets;
    OpcUa_Crypto_PfnGenerateAsymmetricKeypair*  GenerateAsymmetricKeypair;
    OpcUa_Crypto_PfnGetAsymmetricKeyLength*     GetAsymmetricKeyLength;
    OpcUa_Crypto_PfnCreateCertificate*          CreateCertificate;
    OpcUa_Crypto_PfnGetPrivateKeyFromCert*      GetPrivateKeyFromCert;
    OpcUa_Crypto_PfnGetPublicKeyFromCert*       GetPublicKeyFromCert;
    OpcUa_Crypto_PfnGetSignatureFromCert*       GetSignatureFromCert;
    OpcUa_Crypto_PfnGetCertificateThumbprint*   GetCertificateThumbprint;
    OpcUa_Crypto_PfnSymmetricEncrypt*           SymmetricEncrypt;
    OpcUa_Crypto_PfnSymmetricDecrypt*           SymmetricDecrypt;
    OpcUa_Crypto_PfnAsymmetricEncrypt*          AsymmetricEncrypt;
    OpcUa_Crypto_PfnAsymmetricDecrypt*          AsymmetricDecrypt;
    OpcUa_Crypto_PfnSymmetricSign*              SymmetricSign;
    OpcUa_Crypto_PfnSymmetricVerify*            SymmetricVerify;
    OpcUa_Crypto_PfnAsymmetricSign*             AsymmetricSign;
    OpcUa_Crypto_PfnAsymmetricVerify*           AsymmetricVerify;
}
OpcUa_CryptoProvider;

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Crypto_H_ */
