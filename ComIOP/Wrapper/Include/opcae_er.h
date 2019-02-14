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

#ifndef __OPCAE_ER_H
#define __OPCAE_ER_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

// The 'Facility' is set to the standard for COM interfaces or FACILITY_ITF (i.e. 0x004)
// The 'Code' is set in the range defined OPC Commmon for AE (i.e. 0x0200 to 0x02FF)

//
//  Values are 32 bit values layed out as follows:
//
//   3 3 2 2 2 2 2 2 2 2 2 2 1 1 1 1 1 1 1 1 1 1
//   1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0
//  +---+-+-+-----------------------+-------------------------------+
//  |Sev|C|R|     Facility          |               Code            |
//  +---+-+-+-----------------------+-------------------------------+
//
//  where
//
//      Sev - is the severity code
//
//          00 - Success
//          01 - Informational
//          10 - Warning
//          11 - Error
//
//      C - is the Customer code flag
//
//      R - is a reserved bit
//
//      Facility - is the facility code
//
//      Code - is the facility's status code
//
//
// Define the facility codes
//


//
// Define the severity codes
//


//
// MessageId: OPC_S_ALREADYACKED
//
// MessageText:
//
//  The condition has already been acknowleged.
//
#define OPC_S_ALREADYACKED               ((HRESULT)0x00040200L)

//
// MessageId: OPC_S_INVALIDBUFFERTIME
//
// MessageText:
//
//  The buffer time parameter was invalid.
//
#define OPC_S_INVALIDBUFFERTIME          ((HRESULT)0x00040201L)

//
// MessageId: OPC_S_INVALIDMAXSIZE
//
// MessageText:
//
//  The max size parameter was invalid.
//
#define OPC_S_INVALIDMAXSIZE             ((HRESULT)0x00040202L)

//
// MessageId: OPC_S_INVALIDKEEPALIVETIME
//
// MessageText:
//
//  The KeepAliveTime parameter was invalid.
//
#define OPC_S_INVALIDKEEPALIVETIME       ((HRESULT)0x00040203L)

//
// MessageId: OPC_E_INVALIDBRANCHNAME
//
// MessageText:
//
//  The string was not recognized as an area name.
//
#define OPC_E_INVALIDBRANCHNAME          ((HRESULT)0xC0040203L)

//
// MessageId: OPC_E_INVALIDTIME
//
// MessageText:
//
//  The time does not match the latest active time.
//
#define OPC_E_INVALIDTIME                ((HRESULT)0xC0040204L)

//
// MessageId: OPC_E_BUSY
//
// MessageText:
//
//  A refresh is currently in progress.
//
#define OPC_E_BUSY                       ((HRESULT)0xC0040205L)

//
// MessageId: OPC_E_NOINFO
//
// MessageText:
//
//  Information is not available.
//
#define OPC_E_NOINFO                     ((HRESULT)0xC0040206L)

#endif // __OPCAE_ER_H
