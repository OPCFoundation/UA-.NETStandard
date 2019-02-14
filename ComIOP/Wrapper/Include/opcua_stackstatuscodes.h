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

#ifndef _OpcUa_StackStatusCodes_H_
#define _OpcUa_StackStatusCodes_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * Begin of status codes internal to the stack.
 *===========================================================================*/
#define OpcUa_StartOfStackStatusCodes 0x81000000

/*============================================================================
 * The message signature is invalid.
 *===========================================================================*/
#define OpcUa_BadSignatureInvalid 0x81010000

/*============================================================================
 * The extensible parameter provided is not a valid for the service.
 *===========================================================================*/
#define OpcUa_BadExtensibleParameterInvalid 0x81040000

/*============================================================================
 * The extensible parameter provided is valid but the server does not support it.
 *===========================================================================*/
#define OpcUa_BadExtensibleParameterUnsupported 0x81050000

/*============================================================================
 * The hostname could not be resolved.
 *===========================================================================*/
#define OpcUa_BadHostUnknown 0x81060000

/*============================================================================
 * Too many posts were made to a semaphore.
 *===========================================================================*/
#define OpcUa_BadTooManyPosts 0x81070000

/*============================================================================
 * The security configuration is not valid.
 *===========================================================================*/
#define OpcUa_BadSecurityConfig 0x81080000

/*============================================================================
 * Invalid file name specified.
 *===========================================================================*/
#define OpcUa_BadFileNotFound 0x81090000

OPCUA_END_EXTERN_C

#endif /* _OpcUa_StackStatusCodes_H_ */
