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

#ifndef _OpcUa_SecureChannel_H_
#define _OpcUa_SecureChannel_H_ 1

OPCUA_BEGIN_EXTERN_C

#include <opcua_types.h>
#include <opcua_crypto.h>
#include <opcua_pki.h>

#define OPCUA_SECURECHANNEL_THREADSAFE      OPCUA_CONFIG_YES
#define OPCUA_SECURECHANNEL_DEBUG_MUTEX     OPCUA_CONFIG_NO

#if OPCUA_SECURECHANNEL_THREADSAFE
#if OPCUA_SECURECHANNEL_DEBUG_MUTEX
#define OPCUA_SECURECHANNEL_LOCK(xSecureChannel)\
{ \
    OPCUA_P_MUTEX_LOCK(xSecureChannel->hSyncAccess); \
    xSecureChannel->iSyncAccessLevel++; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_INFO, "OPCUA_SECURECHANNEL_LOCK: %x --> %i!\n", xSecureChannel, xSecureChannel->iSyncAccessLevel); \
}

#define OPCUA_SECURECHANNEL_UNLOCK(xSecureChannel)\
{ \
    if(xSecureChannel->iSyncAccessLevel <= 0) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_ERROR, "OPCUA_SECURECHANNEL_UNLOCK: INVALID LOCK STATE!\n"); \
        __asm int 3 \
    } \
    xSecureChannel->iSyncAccessLevel--; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_INFO, "OPCUA_SECURECHANNEL_UNLOCK: %x <-- %i!\n", xSecureChannel, xSecureChannel->iSyncAccessLevel); \
    OPCUA_P_MUTEX_UNLOCK(xSecureChannel->hSyncAccess); \
}

#define OPCUA_SECURECHANNEL_LOCK_WRITE(xSecureChannel)\
{ \
    OPCUA_P_MUTEX_LOCK(xSecureChannel->hWriteMutex); \
    xSecureChannel->iWriteMutexLevel++; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_INFO, "OPCUA_SECURECHANNEL_LOCK_WRITE:   --> %i!\n", xSecureChannel->iWriteMutexLevel); \
}

#define OPCUA_SECURECHANNEL_UNLOCK_WRITE(xSecureChannel)\
{ \
    if(xSecureChannel->iWriteMutexLevel <= 0) \
    { \
        OpcUa_Trace(OPCUA_TRACE_LEVEL_ERROR, "OPCUA_SECURECHANNEL_UNLOCK_WRITE: INVALID LOCK STATE!\n"); \
        __asm int 3 \
    } \
    xSecureChannel->iWriteMutexLevel--; \
    OpcUa_Trace(OPCUA_TRACE_LEVEL_INFO, "OPCUA_SECURECHANNEL_UNLOCK_WRITE: <-- %i!\n", xSecureChannel->iWriteMutexLevel); \
    OPCUA_P_MUTEX_UNLOCK(xSecureChannel->hWriteMutex); \
}
#else /* OPCUA_SECURECHANNEL_DEBUG_MUTEX */
# define OPCUA_SECURECHANNEL_LOCK(xSecureChannel)    OPCUA_P_MUTEX_LOCK(xSecureChannel->hSyncAccess)
# define OPCUA_SECURECHANNEL_UNLOCK(xSecureChannel)  OPCUA_P_MUTEX_UNLOCK(xSecureChannel->hSyncAccess)
# define OPCUA_SECURECHANNEL_LOCK_WRITE(xSecureChannel)    OPCUA_P_MUTEX_LOCK(xSecureChannel->hWriteMutex)
# define OPCUA_SECURECHANNEL_UNLOCK_WRITE(xSecureChannel)  OPCUA_P_MUTEX_UNLOCK(xSecureChannel->hWriteMutex)
#endif /* OPCUA_SECURECHANNEL_DEBUG_MUTEX */
#else /* OPCUA_SECURECHANNEL_THREADSAFE */
# define OPCUA_SECURECHANNEL_LOCK(xSecureChannel)
# define OPCUA_SECURECHANNEL_UNLOCK(xSecureChannel)
# define OPCUA_SECURECHANNEL_LOCK_WRITE(xSecureChannel)
# define OPCUA_SECURECHANNEL_UNLOCK_WRITE(xSecureChannel)
#endif /* OPCUA_SECURECHANNEL_THREADSAFE */


/** @brief Value of an invalid secure channel id. */
#define OPCUA_SECURECHANNEL_ID_INVALID                  0

/** @brief Starting number of the sequence numeration. */
#define OPCUA_SECURECHANNEL_STARTING_SEQUENCE_NUMBER    50

struct _OpcUa_SecureStream;
struct _OpcUa_SecureChannel;
struct _OpcUa_InputStream;

/**
 * @brief Opens a securechannel object.
 *
 * @param pSecureChannel        [in] The securechannel that should be opened
 * @param hTransportConnection  [in] A handle to the associated transport connection
 * @param channelSecurityToken  [in] A previously created ChannelSecurityToken
 * @param messageSecurityMode   [in] The MessageSecurityMode that should be supported be the securechannel
 * @param clientCertificate     [in] The client certificate.
 * @param serverCertificate     [in] The server certificate.
 * @param pReceivingKeyset      [in] The symmetric keyset applied to incoming data.
 * @param pSendingKeyset        [in] The symmetric keyset applied to outgoing data.
 * @param pCryptoProvider       [in] The associated cryptographic provider.
 */
OpcUa_StatusCode OpcUa_SecureChannel_Open(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_Handle                    hTransportConnection,
    OpcUa_ChannelSecurityToken      channelSecurityToken,
    OpcUa_MessageSecurityMode       messageSecurityMode,
    OpcUa_ByteString*               clientCertificate,
    OpcUa_ByteString*               serverCertificate,
    struct _OpcUa_SecurityKeyset*   pReceivingKeyset,
    struct _OpcUa_SecurityKeyset*   pSendingKeyset,
    OpcUa_CryptoProvider*           pCryptoProvider);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnOpen)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_Handle                    a_hTransportConnection,
    OpcUa_ChannelSecurityToken      channelSecurityToken,
    OpcUa_MessageSecurityMode       messageSecurityMode,
    OpcUa_ByteString*               clientCertificate,
    OpcUa_ByteString*               serverCertificate,
    struct _OpcUa_SecurityKeyset*   pReceivingKeyset,
    struct _OpcUa_SecurityKeyset*   pSendingKeyset,
    OpcUa_CryptoProvider*           pCryptoProvider);

/**
 * @brief Opens a securechannel object.
 *
 * @param pSecureChannel        [in] The securechannel that should be opened
 * @param hTransportConnection  [in] A handle to the associated transport connection
 * @param channelSecurityToken  [in] A previously created ChannelSecurityToken
 * @param messageSecurityMode   [in] The MessageSecurityMode that should be supported be the securechannel
 * @param clientCertificate     [in] The client certificate.
 * @param serverCertificate     [in] The server certificate.
 * @param pReceivingKeyset      [in] The client's symmetric keyset.
 * @param pSendingKeyset        [in] The server's symmetric keyset.
 * @param pCryptoProvider       [in] The associated cryptographic provider.
 */
OpcUa_StatusCode OpcUa_SecureChannel_Renew(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_Handle                    hTransportConnection,
    OpcUa_ChannelSecurityToken      channelSecurityToken,
    OpcUa_MessageSecurityMode       messageSecurityMode,
    OpcUa_ByteString*               clientCertificate,
    OpcUa_ByteString*               serverCertificate,
    struct _OpcUa_SecurityKeyset*   pReceivingKeyset,
    struct _OpcUa_SecurityKeyset*   pSendingKeyset,
    OpcUa_CryptoProvider*           pCryptoProvider);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnRenew)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_Handle                    hTransportConnection,
    OpcUa_ChannelSecurityToken      channelSecurityToken,
    OpcUa_MessageSecurityMode       messageSecurityMode,
    OpcUa_ByteString*               clientCertificate,
    OpcUa_ByteString*               serverCertificate,
    struct _OpcUa_SecurityKeyset*   pReceivingKeyset,
    struct _OpcUa_SecurityKeyset*   pSendingKeyset,
    OpcUa_CryptoProvider*           pCryptoProvider);


/**
 * @brief Closes a securechannel object.
 *
 * @param pSecureChannel        [in] The securechannel to close.
 */
OpcUa_StatusCode OpcUa_SecureChannel_Close(
    struct _OpcUa_SecureChannel*    pSecureChannel);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnClose)(
    struct _OpcUa_SecureChannel*    pSecureChannel);

/**
 * @brief Generates a channelSecurityToken.
 *
 * @param pSecureChannel        [in] The securechannel.
 * @param uTokenLifeTime        [in] The lifetime of the token.
 * @param ppSecurityToken      [out] The created token.
 */
OpcUa_StatusCode OpcUa_SecureChannel_GenerateSecurityToken(
    struct _OpcUa_SecureChannel*   pSecureChannel,
    OpcUa_UInt32                   uTokenLifeTime,
    OpcUa_ChannelSecurityToken**   ppSecurityToken);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnGenerateSecurityToken)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32                    uTokenLifeTime,
    OpcUa_ChannelSecurityToken**    ppSecurityToken);

/**
 * @brief Renews a channelSecurityToken.
 *
 * @param pSecureChannel        [in] The securechannel at which the security token was created.
 * @param pSecurityToken        [in] The security that shall be renewed.
 * @param uTokenLifeTime        [in] The next intervals lifetime in milliseconds.
 * @param ppSecurityToken      [out] The renewed security token.
 */
OpcUa_StatusCode OpcUa_SecureChannel_RenewSecurityToken(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_ChannelSecurityToken*     pSecurityToken,
    OpcUa_UInt32                    uTokenLifeTime,
    OpcUa_ChannelSecurityToken**    ppSecurityToken);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnRenewSecurityToken)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_ChannelSecurityToken*     pSecurityToken,
    OpcUa_UInt32                    uTokenLifeTime,
    OpcUa_ChannelSecurityToken**    ppSecurityToken);

/**
 * @brief Creates a reference to a security set identified by the token id and consisting of keysets and crypto provider.
 *
 * @param pSecureChannel        [in] The securechannel.
 * @param uTokenId              [in] .
 * @param ppReceivingKeyset    [out] .
 * @param ppSendingKeyset      [out] .
 * @param ppCryptoProvider     [out] .
 */
OpcUa_StatusCode OpcUa_SecureChannel_GetSecuritySet(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32                    uTokenId,
    OpcUa_SecurityKeyset**          ppReceivingKeyset,
    OpcUa_SecurityKeyset**          ppSendingKeyset,
    OpcUa_CryptoProvider**          ppCryptoProvider);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnGetSecuritySet)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32                    uTokenId,
    OpcUa_SecurityKeyset**          ppReceivingKeyset,
    OpcUa_SecurityKeyset**          ppSendingKeyset,
    OpcUa_CryptoProvider**          ppCryptoProvider);

/**
 * @brief Creates a reference to the current security set consisting of keysets and crypto provider.
 *
 * @param pSecureChannel        [in] The securechannel.
 * @param puTokenId             [in] .
 * @param ppReceivingKeyset    [out] .
 * @param ppSendingKeyset      [out] .
 * @param ppCryptoProvider     [out] .
 */
OpcUa_StatusCode OpcUa_SecureChannel_GetCurrentSecuritySet(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32*                   puTokenId,
    OpcUa_SecurityKeyset**          ppReceivingKeyset,
    OpcUa_SecurityKeyset**          ppSendingKeyset,
    OpcUa_CryptoProvider**          ppCryptoProvider);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnGetCurrentSecuritySet)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32*                   puTokenId,
    OpcUa_SecurityKeyset**          ppReceivingKeyset,
    OpcUa_SecurityKeyset**          ppSendingKeyset,
    OpcUa_CryptoProvider**          ppCryptoProvider);

/**
 * @brief Release a reference to a security keyset created by GetSecuritySet.
 *
 * @param pSecureChannel  [in] The securechannel which holds the security set.
 * @param uTokenId        [in] The token id of the security set, that should be released.
 */
OpcUa_StatusCode OpcUa_SecureChannel_ReleaseSecuritySet(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32                    uTokenId);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnReleaseSecuritySet)(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    OpcUa_UInt32                    uTokenId);

/**
 * @brief Get (and create) a new sequence number for this channel.
 *
 * @param pSecureChannel        [in] The securechannel.
 *
 * @return The new sequence number.
 */
OpcUa_UInt32 OpcUa_SecureChannel_GetSequenceNumber(
    struct _OpcUa_SecureChannel*    pSecureChannel);

typedef OpcUa_UInt32 (OpcUa_SecureChannel_PfnGetSequenceNumber)(
    struct _OpcUa_SecureChannel*    pSecureChannel);

typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnLockFunction)(
    struct _OpcUa_SecureChannel*    pSecureChannel);
/**
 * @brief Check if the secure channel is open or not.
 *
 * @param pSecureChannel        [in] The securechannel.
 *
 * @return OpcUa_False if the Channel is closed, OpcUa_True else.
 */
OpcUa_Boolean OpcUa_SecureChannel_IsOpen(
    struct _OpcUa_SecureChannel*    pSecureChannel);

typedef OpcUa_Boolean (OpcUa_SecureChannel_PfnIsOpen)(
    struct _OpcUa_SecureChannel*    pSecureChannel);

struct _OpcUa_SecureListener_ChannelManager;
typedef OpcUa_StatusCode (OpcUa_SecureChannel_PfnRelease)(
    struct _OpcUa_SecureListener_ChannelManager* pManager,
    struct _OpcUa_SecureChannel**   pSecureChannel);

/**
 * @brief Derive the keysets from the given nonces based on the crypto configuration.
 *
 * @param eSecurityMode         [in] The security mode used for the connection.
 * @param pCryptoProvider       [in] The used crypto provider.
 * @param pClientNonce          [in] The nonce created by the client.
 * @param pServerNonce          [in] The nonce created by the server.
 * @param ppClientKeyset       [out] The created client keyset.
 * @param ppServerKeyset       [out] The created server keyset.
 */
OpcUa_StatusCode OpcUa_SecureChannel_DeriveKeys(
    OpcUa_MessageSecurityMode   eSecurityMode,
    OpcUa_CryptoProvider*       pCryptoProvider,
    OpcUa_ByteString*           pClientNonce,
    OpcUa_ByteString*           pServerNonce,
    OpcUa_SecurityKeyset**      ppClientKeyset,
    OpcUa_SecurityKeyset**      ppServerKeyset);

/**
 * @brief Set the given stream as active stream (not yet completely received) at the given secure channel.
 *
 * @param pSecureChannel        [in] The securechannel.
 * @param pSecureIStream       [out] The securechannel that should be opened.
 */
OpcUa_StatusCode OpcUa_SecureChannel_SetPendingInputStream(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    struct _OpcUa_InputStream*      pSecureIStream);

/**
 * @brief Returns the stream, that is currently being received from the secure channel object.
 *
 * @param pSecureChannel        [in] The securechannel.
 * @param ppSecureIStream      [out] The securechannel that should be opened.
 */
OpcUa_StatusCode OpcUa_SecureChannel_GetPendingInputStream(
    struct _OpcUa_SecureChannel*    pSecureChannel,
    struct _OpcUa_InputStream**     ppSecureIStream);

/**
 * @brief SecureChannel states.
 */
typedef enum _OpcUa_SecureChannelState
{
    /** @brief Error state. */
    OpcUa_SecureChannelState_Unknown,
    /** @brief SecureChannel is opened. */
    OpcUa_SecureChannelState_Opened,
    /** @brief The securechannel was closed.  */
    OpcUa_SecureChannelState_Closed
} OpcUa_SecureChannelState;

/**
 * @brief The securechannel structure.
 */
struct _OpcUa_SecureChannel
{
    /** @brief An opaque handle that contains data specific to the securechannel implementation. */
    OpcUa_Handle                                    Handle;
    /** @brief The channel identifier. */
    OpcUa_UInt32                                    SecureChannelId;

    /** functions **/

    /** @brief Opens the securechannel. */
    OpcUa_SecureChannel_PfnOpen*                    Open;
    /** @brief Renews the securechannel. */
    OpcUa_SecureChannel_PfnRenew*                   Renew;
    /** @brief Closes the securechannel. */
    OpcUa_SecureChannel_PfnClose*                   Close;
    /** @brief Generates a securitytoken. */
    OpcUa_SecureChannel_PfnGenerateSecurityToken*   GenerateSecurityToken;
    /** @brief Renews a securitytoken. */
    OpcUa_SecureChannel_PfnRenewSecurityToken*      RenewSecurityToken;
    /** @brief Get a security set identified by token id. */
    OpcUa_SecureChannel_PfnGetSecuritySet*          GetSecuritySet;
    /** @brief Get the most current security set. */
    OpcUa_SecureChannel_PfnGetCurrentSecuritySet*   GetCurrentSecuritySet;
    /** @brief Release a security set. */
    OpcUa_SecureChannel_PfnReleaseSecuritySet*      ReleaseSecuritySet;
    /** @brief Get (and create) a new sequence number for this channel. */
    OpcUa_SecureChannel_PfnGetSequenceNumber*       GetSequenceNumber;
    /** @brief Get (and create) a new sequence number for this channel. */
    OpcUa_SecureChannel_PfnIsOpen*                  IsOpen;
    OpcUa_SecureChannel_PfnLockFunction*            LockWriteMutex;
    OpcUa_SecureChannel_PfnLockFunction*            UnlockWriteMutex;

    /** internals **/

#if OPCUA_SECURECHANNEL_THREADSAFE
    /** @brief The mutex used to synchronize access to the securechannel object. */
    OpcUa_Mutex                                     hSyncAccess;
    OpcUa_Mutex                                     hWriteMutex;
#if OPCUA_SECURECHANNEL_DEBUG_MUTEX
    OpcUa_Int32                                     iSyncAccessLevel;
    OpcUa_Int32                                     iWriteMutexLevel;
#endif /* OPCUA_SECURECHANNEL_DEBUG_MUTEX */
#endif /* OPCUA_SECURECHANNEL_THREADSAFE */

    /** @brief An opaque handle that represents the associated TransportConnection. */
    OpcUa_Handle                                    TransportConnection;
    /** @brief The currently active input stream for this channel. */
    struct _OpcUa_InputStream*                      pPendingSecureIStream;
    /** @brief The sequence number of the last chunk sent. */
    OpcUa_UInt32                                    uLastSequenceNumberReceived;
    /** @brief The sequence number of the last chunk received. */
    OpcUa_UInt32                                    uLastSequenceNumberSent;
    /** @brief Current number of active outputstreams. */
    OpcUa_UInt32                                    uNumberOfOutputStreams;
    /** @brief Maximum number of chunks per message. */
    OpcUa_UInt32                                    nMaxBuffersPerMessage;
    /** @brief SecureChannel state. */
    OpcUa_SecureChannelState                        State;
    /** @brief The Message Security Mode. */
    OpcUa_MessageSecurityMode                       MessageSecurityMode;
    /** @brief The security policy for the channel (cannot be changed once it is set). */
    OpcUa_String                                    SecurityPolicyUri;
    /** @brief Whether the channel can only be used for discovery services. */
    OpcUa_Boolean                                   DiscoveryOnly;
    /** @brief Client Instance Certificate. */
    OpcUa_ByteString                                ClientCertificate;
    /** @brief Server Instance Certificate. */
    OpcUa_ByteString                                ServerCertificate;

    /** security sets **/

    /** @brief If true, the current security set can be used, else, the previous is to be preferred. */
    OpcUa_Boolean                                   bCurrentTokenActive;

    /** @brief Identifier used for the next token. */
    OpcUa_UInt32                                    NextTokenId;

    /** @brief Security function appropriate to selected security policy. */
    OpcUa_CryptoProvider*                           pCurrentCryptoProvider;
    /** @brief Keys and initial vector for client-side security functions. */
    OpcUa_SecurityKeyset*                           pCurrentReceivingKeyset;
    /** @brief Keys and initial vector for server-side security functions. */
    OpcUa_SecurityKeyset*                           pCurrentSendingKeyset;
    /** @brief Identifier for the securechannel and its current security set. */
    OpcUa_ChannelSecurityToken                      CurrentChannelSecurityToken;

    /** @brief Security function appropriate to selected security policy. */
    OpcUa_CryptoProvider*                           pPreviousCryptoProvider;
    /** @brief Keys and initial vector for client-side security functions. */
    OpcUa_SecurityKeyset*                           pPreviousReceivingKeyset;
    /** @brief Keys and initial vector for server-side security functions. */
    OpcUa_SecurityKeyset*                           pPreviousSendingKeyset;
    /** @brief Identifier for the securechannel and its previous security set. */
    OpcUa_ChannelSecurityToken                      PreviousChannelSecurityToken;
    /** @brief Counter set according to the watchdog interval and the token lifetime. */
    OpcUa_UInt32                                    uExpirationCounter;
    /** @brief Counter set according to the watchdog interval and the token lifetime. */
    OpcUa_UInt32                                    uOverlapCounter;
    /** @brief Counter set according how many references of this object are in use. */
    OpcUa_UInt32                                    uRefCount;
    OpcUa_SecureChannel_PfnRelease*                 ReleaseMethod;
    struct _OpcUa_SecureListener_ChannelManager*    ReleaseParam;

    /** Storage for delayed OSC messages **/
    OpcUa_Void*                                     pOSCOutputStream;
    OpcUa_Void*                                     pOSCMessage;
    OpcUa_EncodeableType*                           pOSCMessageType;
};
typedef struct _OpcUa_SecureChannel OpcUa_SecureChannel;

OPCUA_END_EXTERN_C

#endif
