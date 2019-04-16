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

#ifndef _OpcUa_Credentials_H_
#define _OpcUa_Credentials_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * OpcUa_UserNameCredential
 *
 * A username/password credential.
 *
 * Name     - the name of the user.
 * Password - the password (could be hashed). 
 *===========================================================================*/
typedef struct _OpcUa_UserNameCredential
{
    OpcUa_String Name;
    OpcUa_String Password;
}
OpcUa_UserNameCredential;

/*============================================================================
 * OpcUa_X509Credentials
 *
 * An X509 certificate credential.
 *
 * Subject       - the subject of the certificate.
 * Thumbprint    - the thumbprint of the certificate.
 * Password      - the password required to access the private key.
 * StoreLocation - the location of the certificate store.
 * StoreName     - the name of the certificate store.
 *===========================================================================*/
typedef struct _OpcUa_X509Credential
{
    OpcUa_String Subject;
    OpcUa_String Thumbprint;
    OpcUa_String Password;
    OpcUa_String StoreLocation;
    OpcUa_String StoreName;
}
OpcUa_X509Credential;

/*============================================================================
 * OpcUa_ActualCredential
 *
 * An actually used credential.
 *===========================================================================*/
typedef struct _OpcUa_ActualCredential
{
    OpcUa_ByteString*               pClientCertificate;
    OpcUa_ByteString*               pClientPrivateKey;
    OpcUa_ByteString*               pServerCertificate;
    OpcUa_Void*                     pkiConfig;
    OpcUa_String*                   pRequestedSecurityPolicyUri;
    OpcUa_Int32                     nRequestedLifetime;
    OpcUa_MessageSecurityMode       messageSecurityMode;
}
OpcUa_ActualCredential;

/*============================================================================
 * OpcUa_CredentialType
 *===========================================================================*/
typedef enum _OpcUa_CredentialType
{
    OpcUa_CredentialType_UserName = 0x1,
    OpcUa_CredentialType_X509     = 0x2,
    OpcUa_CredentialType_Actual   = 0x4
}
OpcUa_CredentialType;

/*============================================================================
 * OpcUa_ClientCredential
 *
 * A union of all possible credential types.
 *
 * Type     - the type of credential.
 * UserName - a username/password credential.
 * X509     - an X509 certificate credential.
 *===========================================================================*/
typedef struct _OpcUa_ClientCredential
{
    OpcUa_CredentialType     Type;
    
    union
    {
    OpcUa_UserNameCredential UserName;
    OpcUa_X509Credential     X509;
    OpcUa_ActualCredential   TheActuallyUsedCredential;
    }
    Credential;
}
OpcUa_ClientCredential;

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Credentials_H_ */
