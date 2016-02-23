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
using System.Text;
using System.IO;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
using System.Security.Cryptography;

namespace Opc.Ua.Bindings
{
    public partial class TcpChannel
    {
        #region Token Handling Members
        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected TcpChannelToken CurrentToken
        {
            get { return m_currentToken; }
        }

        /// <summary>
        /// Returns the current security token.
        /// </summary>
        protected TcpChannelToken PreviousToken
        {
            get { return m_previousToken; }
        }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        protected TcpChannelToken CreateToken()
        {
            TcpChannelToken token = new TcpChannelToken();

            token.ChannelId = m_channelId;
            token.TokenId   = 0;
            token.CreatedAt = DateTime.UtcNow;
            token.Lifetime  = (int)Quotas.SecurityTokenLifetime;

            return token;
        }

        /// <summary>
        /// Activates a new token.
        /// </summary>
        protected void ActivateToken(TcpChannelToken token)
        {
            // compute the keys for the token.
            ComputeKeys(token);

            m_previousToken = m_currentToken;
            m_currentToken  = token;
        }

        /// <summary>
        /// Discards the tokens.
        /// </summary>
        protected void DiscardTokens()
        {
            m_previousToken = null;
            m_currentToken  = null;
        }
        #endregion

        #region Symmetric Cryptography Functions
        /// <summary>
        /// The byte length of the MAC (a.k.a signature) attached to each message.
        /// </summary>        
        private int SymmetricSignatureSize
        {
            get { return m_hmacHashSize; } 
        }
        
        /// <summary>
        /// The byte length the encryption blocks.
        /// </summary>  
        private int EncryptionBlockSize
        {
            get { return m_encryptionBlockSize; } 
        }

        /// <summary>
        /// Calculates the symmetric key sizes based on the current security policy.
        /// </summary>
        protected void CalculateSymmetricKeySizes()
        {
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 16;
                    m_encryptionKeySize = 16;
                    m_encryptionBlockSize = 16;
                    break;
                }

                case SecurityPolicies.Basic256:
                {
                    m_hmacHashSize = 20;
                    m_signatureKeySize = 24;
                    m_encryptionKeySize = 32;
                    m_encryptionBlockSize = 16;
                    break;
                }

                default:
                case SecurityPolicies.None:
                {
                    m_hmacHashSize = 0;
                    m_signatureKeySize = 0;
                    m_encryptionKeySize = 0;
                    m_encryptionBlockSize = 1;
                    break;
                }
            }
        }

        /// <summary>
        /// Computes the keys for a token.
        /// </summary>
        protected void ComputeKeys(TcpChannelToken token)
        {        
            if (SecurityMode == MessageSecurityMode.None)
            {
                return;
            }
            
            token.ClientSigningKey           = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, 0, m_signatureKeySize);
            token.ClientEncryptingKey        = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize, m_encryptionKeySize);
            token.ClientInitializationVector = Utils.PSHA1(token.ServerNonce, null, token.ClientNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);
            token.ServerSigningKey           = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, 0, m_signatureKeySize);
            token.ServerEncryptingKey        = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize, m_encryptionKeySize);
            token.ServerInitializationVector = Utils.PSHA1(token.ClientNonce, null, token.ServerNonce, m_signatureKeySize + m_encryptionKeySize, m_encryptionBlockSize);

            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                {
                    // create encryptors. 
                    SymmetricKeyAlgorithmProvider AesCbcProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesCbc);

                    IBuffer buffer = CryptographicBuffer.CreateFromByteArray(token.ClientEncryptingKey);
                    token.ClientEncryptor = AesCbcProvider.CreateSymmetricKey(buffer);

                    buffer = CryptographicBuffer.CreateFromByteArray(token.ServerEncryptingKey);
                    token.ServerEncryptor = AesCbcProvider.CreateSymmetricKey(buffer);

                    // create HMACs.
                    token.ServerHmac = new HMACSHA1(token.ServerSigningKey);
                    token.ClientHmac = new HMACSHA1(token.ClientSigningKey);
                    break;
                }

                default:
                case SecurityPolicies.None:             
                {
                    break;
                }
            }            
        }
        
        /// <summary>
        /// Secures the message using the security token.
        /// </summary>
        protected BufferCollection WriteSymmetricMessage(
            uint            messageType,
            uint            requestId, 
            TcpChannelToken token,
            object          messageBody,
            bool            isRequest,
            out bool        limitsExceeded)
        {   
            limitsExceeded = false;
            bool success = false;
            BufferCollection chunksToProcess = null;

            try
            {
                // calculate chunk sizes.
                int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxCipherBlocks   = maxCipherTextSize/EncryptionBlockSize;
                int maxPlainTextSize  = maxCipherBlocks*EncryptionBlockSize;
                int maxPayloadSize    = maxPlainTextSize - SymmetricSignatureSize - 1 - TcpMessageLimits.SequenceHeaderSize;        
                int headerSize        = TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;

                // write the body to stream.
                ArraySegmentStream ostrm = new ArraySegmentStream(
                    BufferManager, 
                    SendBufferSize, 
                    headerSize,
                    maxPayloadSize);
                
                // check for encodeable body.
                IEncodeable encodeable = messageBody as IEncodeable;

                if (encodeable != null)
                {                    
                    // debug code used to verify that message aborts are handled correctly.
                    // int maxMessageSize = Quotas.MessageContext.MaxMessageSize;
                    // Quotas.MessageContext.MaxMessageSize = Int32.MaxValue;
                    
                    BinaryEncoder.EncodeMessage(encodeable, ostrm, Quotas.MessageContext);

                    // Quotas.MessageContext.MaxMessageSize = maxMessageSize;
                }

                // check for raw bytes.
                ArraySegment<byte>? rawBytes = messageBody as ArraySegment<byte>?;
                
                if (rawBytes != null)
                {
                    BinaryEncoder encoder = new BinaryEncoder(ostrm, Quotas.MessageContext);
                    encoder.WriteRawBytes(rawBytes.Value.Array, rawBytes.Value.Offset, rawBytes.Value.Count);
                    encoder.Close();
                }

                chunksToProcess = ostrm.GetBuffers("WriteSymmetricMessage");   
             
                // ensure there is at least one chunk.
                if (chunksToProcess.Count == 0)
                {
                    byte[] buffer = BufferManager.TakeBuffer(SendBufferSize, "WriteSymmetricMessage");
                    chunksToProcess.Add(new ArraySegment<byte>(buffer, 0, 0));
                }

                BufferCollection chunksToSend = new BufferCollection(chunksToProcess.Capacity);

                int messageSize = 0;

                for (int ii = 0; ii < chunksToProcess.Count; ii++)
                {
                    ArraySegment<byte> chunkToProcess = chunksToProcess[ii];
                    
                    // nothing more to do if limits exceeded.
                    if (limitsExceeded)
                    {
                        BufferManager.ReturnBuffer(chunkToProcess.Array, "WriteSymmetricMessage");
                        continue;
                    }

                    MemoryStream strm = new MemoryStream(chunkToProcess.Array, 0, SendBufferSize);
                    BinaryEncoder encoder = new BinaryEncoder(strm, Quotas.MessageContext);
                    
                    // check if the message needs to be aborted.
                    if (MessageLimitsExceeded(isRequest, messageSize + chunkToProcess.Count - headerSize, ii+1))
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Abort);
                        
                        // replace the body in the chunk with an error message.
                        BinaryEncoder errorEncoder = new BinaryEncoder(
                            chunkToProcess.Array, 
                            chunkToProcess.Offset, 
                            chunkToProcess.Count, 
                            Quotas.MessageContext);
                        
                        WriteErrorMessageBody(errorEncoder, (isRequest)?StatusCodes.BadRequestTooLarge:StatusCodes.BadResponseTooLarge);
                                        
                        int size = errorEncoder.Close();
                        chunkToProcess = new ArraySegment<byte>(chunkToProcess.Array, chunkToProcess.Offset, size);

                        limitsExceeded = true;
                    }

                    // check if the message is complete.
                    else if (ii == chunksToProcess.Count-1)
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Final);
                    }

                    // more chunks to follow.
                    else
                    {
                        encoder.WriteUInt32(null, messageType | TcpMessageType.Intermediate);
                    }
                    
                    int count = 0;
                    
                    count += TcpMessageLimits.SequenceHeaderSize;
                    count += chunkToProcess.Count;
                    count += SymmetricSignatureSize;
                    
                    // calculate the padding.
                    int padding = 0;                    
                    
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // reserve one byte for the padding size.
                        count++;

                        if (count%EncryptionBlockSize != 0)
                        {
                            padding = EncryptionBlockSize - (count%EncryptionBlockSize);
                        }
                        
                        count += padding;
                    }

                    count += TcpMessageLimits.SymmetricHeaderSize;

                    encoder.WriteUInt32(null, (uint)count);
                    encoder.WriteUInt32(null, ChannelId);
                    encoder.WriteUInt32(null, token.TokenId);

                    uint sequenceNumber = GetNewSequenceNumber();
                    encoder.WriteUInt32(null, sequenceNumber);
                    
                    encoder.WriteUInt32(null, requestId);

                    // skip body.
                    strm.Seek(chunkToProcess.Count, SeekOrigin.Current);
                        
                    // update message size count.
                    messageSize += chunkToProcess.Count;

                    // write padding.
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        for (int jj = 0; jj <= padding; jj++)
                        {
                            encoder.WriteByte(null, (byte)padding);
                        }
                    }

                    if (SecurityMode != MessageSecurityMode.None)
                    {
                        // calculate and write signature.
                        byte[] signature = Sign(token, new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position), isRequest);

                        if (signature != null)
                        {
                            encoder.WriteRawBytes(signature, 0, signature.Length);
                        }
                    }
                    
                    if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        // encrypt the data.
                        ArraySegment<byte> dataToEncrypt = new ArraySegment<byte>(chunkToProcess.Array, TcpMessageLimits.SymmetricHeaderSize, encoder.Position-TcpMessageLimits.SymmetricHeaderSize);
                        Encrypt(token, dataToEncrypt, isRequest);
                    }

                    // add the header into chunk.
                    chunksToSend.Add(new ArraySegment<byte>(chunkToProcess.Array, 0, encoder.Position));
                }
           
                // ensure the buffers don't get cleaned up on exit.
                success = true;
                return chunksToSend;
            }
            finally
            {
                if (!success)
                {
                    if (chunksToProcess != null)
                    {
                        chunksToProcess.Release(BufferManager, "WriteSymmetricMessage");
                    }
                }
            }
        }      

        /// <summary>
        /// Decrypts and verifies a message chunk.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageType"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "messageSize")]
        protected ArraySegment<byte> ReadSymmetricMessage(
            ArraySegment<byte>  buffer,
            bool                isRequest,
            out TcpChannelToken token,
            out uint            requestId,
            out uint            sequenceNumber)
        {            
            BinaryDecoder decoder = new BinaryDecoder(buffer.Array, buffer.Offset, buffer.Count, Quotas.MessageContext);
                      
            uint messageType = decoder.ReadUInt32(null);
            uint messageSize = decoder.ReadUInt32(null);
            uint channelId   = decoder.ReadUInt32(null);
            uint tokenId     = decoder.ReadUInt32(null);

            // ensure the channel is valid.
            if (channelId != ChannelId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "SecureChannelId is not known. ChanneId={0}, CurrentChannelId={1}", 
                    channelId,
                    ChannelId);
            }

            // check for valid token.
            TcpChannelToken currentToken = CurrentToken;

            if (currentToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadSecureChannelClosed);
            }

            // find the token.
            if (currentToken.TokenId != tokenId && PreviousToken != null && PreviousToken.TokenId != tokenId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "TokenId is not known. ChanneId={0}, TokenId={1}, CurrentTokenId={2}, PreviousTokenId={3}", 
                    channelId, 
                    tokenId,
                    currentToken.TokenId,
                    (PreviousToken != null)?(int)PreviousToken.TokenId:-1);
            }

            token = currentToken;

            // check for a message secured with the token before it expired.
            if (PreviousToken != null && PreviousToken.TokenId == tokenId)
            {
                token = PreviousToken;
            }
            
            // check if token has expired.
            if (token.Expired)
            {
                throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown, "Token #{0} has expired. Lifetime={1:HH:mm:ss.fff}", token.TokenId, token.CreatedAt);
            }

            int headerSize = decoder.Position;

            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // decrypt the message.
                Decrypt(token, new ArraySegment<byte>(buffer.Array, buffer.Offset + headerSize, buffer.Count - headerSize), isRequest);
            }
      
            if (SecurityMode != MessageSecurityMode.None)
            {
                // extract signature.
                byte[] signature = new byte[SymmetricSignatureSize];

                for (int ii = 0; ii < SymmetricSignatureSize; ii++)
                {
                    signature[ii] = buffer.Array[buffer.Offset+buffer.Count-SymmetricSignatureSize+ii];
                }

                // verify the signature.
                if (!Verify(token, signature, new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count - SymmetricSignatureSize), isRequest))
                {
                    Utils.Trace("Could not verify signature on message.");
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the signature on the message.");
                }
             }
            
            int paddingCount = 0;
      
            if (SecurityMode == MessageSecurityMode.SignAndEncrypt)
            {
                // verify padding.
                int paddingStart = buffer.Offset + buffer.Count - SymmetricSignatureSize - 1;
                paddingCount = buffer.Array[paddingStart];

                for (int ii = paddingStart - paddingCount; ii < paddingStart; ii++)
                {
                    if (buffer.Array[ii] != paddingCount)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Could not verify the padding in the message.");
                    }
                }

                // add byte for size.
                paddingCount++;
            }

            // extract request id and sequence number.
            sequenceNumber = decoder.ReadUInt32(null);
            requestId = decoder.ReadUInt32(null);

            // return an the data contained in the message.
            int startOfBody = buffer.Offset + TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize;
            int sizeOfBody  = buffer.Count - TcpMessageLimits.SymmetricHeaderSize - TcpMessageLimits.SequenceHeaderSize - paddingCount - SymmetricSignatureSize;

            return new ArraySegment<byte>(buffer.Array, startOfBody, sizeOfBody);
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected byte[] Sign(TcpChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:           
                {
                    return null;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                {
                    return SymmetricSign(token, dataToSign, useClientKeys);
                }
            }
        }

        /// <summary>
        /// Returns the symmetric signature for the data.
        /// </summary>
        protected bool Verify(
            TcpChannelToken    token,
            byte[]             signature,
            ArraySegment<byte> dataToVerify,
            bool               useClientKeys)
        {       
            // verify signature.
            switch (SecurityPolicyUri)
            {
                case SecurityPolicies.None:
                {
                    return true;
                }

                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                {
                    return SymmetricVerify(token, signature, dataToVerify, useClientKeys);
                }

                default:
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Encrypt(TcpChannelToken token, ArraySegment<byte> dataToEncrypt, bool useClientKeys)
        {      
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None: 
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic128Rsa15:
                {
                    SymmetricEncrypt(token, dataToEncrypt, useClientKeys);
                    break;
                }
            }
        }

        /// <summary>
        /// Decrypts the data in a buffer using symmetric encryption.
        /// </summary>
        protected void Decrypt(TcpChannelToken token, ArraySegment<byte> dataToDecrypt, bool useClientKeys)
        {
            switch (SecurityPolicyUri)
            {
                default:
                case SecurityPolicies.None:  
                {
                    break;
                }

                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic128Rsa15:
                {
                    SymmetricDecrypt(token, dataToDecrypt, useClientKeys);
                    break;
                }
            }
        }


        /// <summary>
        /// Signs the message using SHA1 HMAC
        /// </summary>
        private static byte[] SymmetricSign(TcpChannelToken token, ArraySegment<byte> dataToSign, bool useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys) ? token.ClientHmac : token.ServerHmac;

            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToSign.Array, dataToSign.Offset, dataToSign.Count, false);
            byte[] signature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // return signature.
            return signature;
        }

        /// <summary>
        /// Verifies a HMAC for a message.
        /// </summary>
        private static bool SymmetricVerify(
            TcpChannelToken    token, 
            byte[]             signature,
            ArraySegment<byte> dataToVerify,
            bool               useClientKeys)
        {
            // get HMAC object.
            HMAC hmac = (useClientKeys)?token.ClientHmac:token.ServerHmac;
                                    
            // compute hash.
            MemoryStream istrm = new MemoryStream(dataToVerify.Array, dataToVerify.Offset, dataToVerify.Count, false);
            byte[] computedSignature = hmac.ComputeHash(istrm);
            istrm.Dispose();

            // compare signatures.
            for (int ii = 0; ii < signature.Length; ii++)
            {
                if (computedSignature[ii] != signature[ii])
                {
                    string messageType = new UTF8Encoding().GetString(dataToVerify.Array, dataToVerify.Offset, 4);
                    int messageLength = BitConverter.ToInt32(dataToVerify.Array, dataToVerify.Offset+4);
                    string expectedSignature = Utils.ToHexString(computedSignature);
                    string actualSignature = Utils.ToHexString(signature);

                    Utils.Trace(
                        "Could not validate signature.\r\nChannelId={0}, TokenId={1}, MessageType={2}, Length={3}\r\nExpectedSignature={4}\r\nActualSignature  ={5}",
                        token.ChannelId,                        
                        token.TokenId,
                        messageType,
                        messageLength,
                        expectedSignature,
                        actualSignature);

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Encrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricEncrypt(
            TcpChannelToken token, 
            ArraySegment<byte> dataToEncrypt,
            bool               useClientKeys)
        {
            // get the encrypting key.
            CryptographicKey encryptingKey = (useClientKeys)? token.ClientEncryptor : token.ServerEncryptor;
            IBuffer IV = (useClientKeys) ? CryptographicBuffer.CreateFromByteArray(token.ClientInitializationVector) : CryptographicBuffer.CreateFromByteArray(token.ServerInitializationVector);

            if (encryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            SymmetricKeyAlgorithmProvider AesCbcProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesCbc);
            if (dataToEncrypt.Count % AesCbcProvider.BlockLength != 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
            }

            byte[] blockToEncrypt = new byte[dataToEncrypt.Count];
            Array.ConstrainedCopy(dataToEncrypt.Array, dataToEncrypt.Offset, blockToEncrypt, 0, dataToEncrypt.Count);

            IBuffer block = CryptographicBuffer.CreateFromByteArray(blockToEncrypt);
            IBuffer encryptedBuffer = CryptographicEngine.Encrypt(encryptingKey, block, IV);
            CryptographicBuffer.CopyToByteArray(encryptedBuffer, out blockToEncrypt);

            Array.ConstrainedCopy(blockToEncrypt, 0, dataToEncrypt.Array, dataToEncrypt.Offset, dataToEncrypt.Count);
        }

        /// <summary>
        /// Decrypts a message using a symmetric algorithm.
        /// </summary>
        private static void SymmetricDecrypt(
            TcpChannelToken token, 
            ArraySegment<byte> dataToDecrypt,
            bool               useClientKeys)
        {
            // get the decrypting key.
            CryptographicKey decryptingKey = (useClientKeys) ? token.ClientEncryptor : token.ServerEncryptor;
            IBuffer IV = (useClientKeys) ? CryptographicBuffer.CreateFromByteArray(token.ClientInitializationVector) : CryptographicBuffer.CreateFromByteArray(token.ServerInitializationVector);

            if (decryptingKey == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Token missing symmetric key object.");
            }

            SymmetricKeyAlgorithmProvider AesCbcProvider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesCbc);
            if (dataToDecrypt.Count % AesCbcProvider.BlockLength != 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Input data is not an even number of encryption blocks.");
            }

            byte[] blockToDecrypt = new byte[dataToDecrypt.Count];
            Array.ConstrainedCopy(dataToDecrypt.Array, dataToDecrypt.Offset, blockToDecrypt, 0, dataToDecrypt.Count);
            
            IBuffer block = CryptographicBuffer.CreateFromByteArray(blockToDecrypt);
            IBuffer encryptedBuffer = CryptographicEngine.Decrypt(decryptingKey, block, IV);
            CryptographicBuffer.CopyToByteArray(encryptedBuffer, out blockToDecrypt);

            Array.ConstrainedCopy(blockToDecrypt, 0, dataToDecrypt.Array, dataToDecrypt.Offset, dataToDecrypt.Count);
        }
        #endregion

        #region Private Fields
        private TcpChannelToken m_currentToken;
        private TcpChannelToken m_previousToken;
        private int m_hmacHashSize;
        private int m_signatureKeySize;
        private int m_encryptionKeySize;
        private int m_encryptionBlockSize;
        #endregion
    }            
}
