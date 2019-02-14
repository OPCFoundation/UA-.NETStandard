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

#ifndef _OpcCmdError_H_
#define _OpcCmdError_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000


// The 'Facility' is set to the standard for COM interfaces or FACILITY_ITF (i.e. 0x004)
// The 'Code' is set in the range defined OPC Commmon for DX (i.e. 0x0700 to 0x07FF)
// Note that for backward compatibility not all existing codes use this range.

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
// MessageId: OPCCMD_E_INVALIDBRANCH
//
// MessageText:
//
//  The Target ID specified in the request is not a valid branch.
//
#define OPCCMD_E_INVALIDBRANCH           ((HRESULT)0xC0040900L)

//
// MessageId: OPCCMD_E_INVALIDNAMESPACE
//
// MessageText:
//
//  The specified namespace is not a valid namespace for this server.
//
#define OPCCMD_E_INVALIDNAMESPACE        ((HRESULT)0xC0040901L)

//
// MessageId: OPCCMD_E_INVALIDCOMMANDNAME
//
// MessageText:
//
//  The specified command name is not valid for this server.
//
#define OPCCMD_E_INVALIDCOMMANDNAME      ((HRESULT)0xC0040902L)

//
// MessageId: OPCCMD_E_BUSY
//
// MessageText:
//
//  The server is currently not able to process this command.
//
#define OPCCMD_E_BUSY                    ((HRESULT)0xC0040903L)

//
// MessageId: OPCCMD_E_EVENTFILTER_NOTSUPPORTED
//
// MessageText:
//
//  The server does not support filtering of events.
//
#define OPCCMD_E_EVENTFILTER_NOTSUPPORTED ((HRESULT)0xC0040904L)

//
// MessageId: OPCCMD_E_NO_SUCH_COMMAND
//
// MessageText:
//
//  A command with the specified UUID is neither executing nor completed (and still stored in the cache).
//
#define OPCCMD_E_NO_SUCH_COMMAND         ((HRESULT)0xC0040905L)

//
// MessageId: OPCCMD_E_ALREADY_CONNECTED
//
// MessageText:
//
//  A client is already connected for the specified Invoke UUID.
//
#define OPCCMD_E_ALREADY_CONNECTED       ((HRESULT)0xC0040906L)

//
// MessageId: OPCCMD_E_NOT_CONNECTED
//
// MessageText:
//
//  No Callback ID is currently connected for the specified command.
//
#define OPCCMD_E_NOT_CONNECTED           ((HRESULT)0xC0040907L)

//
// MessageId: OPCCMD_E_CONTROL_NOTSUPPORTED
//
// MessageText:
//
//  The server does not support the specified control for this command.
//
#define OPCCMD_E_CONTROL_NOTSUPPORTED    ((HRESULT)0xC0040908L)

//
// MessageId: OPCCMD_E_INVALID_CONTROL
//
// MessageText:
//
//  The specified control is not possible in the current state of execution.
//
#define OPCCMD_E_INVALID_CONTROL         ((HRESULT)0xC0040909L)

#endif // ifndef _OpcCmdError_H_
