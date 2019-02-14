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

#ifndef __OPCERROR_H
#define __OPCERROR_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

// The 'Facility' is set to the standard for COM interfaces or FACILITY_ITF (i.e. 0x004)
// The 'Code' is set in the range defined OPC Commmon for DA (i.e. 0x0400 to 0x04FF)
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
// MessageId: OPC_E_INVALIDHANDLE
//
// MessageText:
//
//  The value of the handle is invalid.
//
#define OPC_E_INVALIDHANDLE              ((HRESULT)0xC0040001L)

//
// MessageId: OPC_E_BADTYPE
//
// MessageText:
//
//  The server cannot convert the data between the specified format and/or requested data type and the canonical data type. 
//
#define OPC_E_BADTYPE                    ((HRESULT)0xC0040004L)

//
// MessageId: OPC_E_PUBLIC
//
// MessageText:
//
//  The requested operation cannot be done on a public group.
//
#define OPC_E_PUBLIC                     ((HRESULT)0xC0040005L)

//
// MessageId: OPC_E_BADRIGHTS
//
// MessageText:
//
//  The item's access rights do not allow the operation.
//
#define OPC_E_BADRIGHTS                  ((HRESULT)0xC0040006L)

//
// MessageId: OPC_E_UNKNOWNITEMID
//
// MessageText:
//
//  The item ID is not defined in the server address space or no longer exists in the server address space.
//
#define OPC_E_UNKNOWNITEMID              ((HRESULT)0xC0040007L)

//
// MessageId: OPC_E_INVALIDITEMID
//
// MessageText:
//
//  The item ID does not conform to the server's syntax.
//
#define OPC_E_INVALIDITEMID              ((HRESULT)0xC0040008L)

//
// MessageId: OPC_E_INVALIDFILTER
//
// MessageText:
//
//  The filter string was not valid.
//
#define OPC_E_INVALIDFILTER              ((HRESULT)0xC0040009L)

//
// MessageId: OPC_E_UNKNOWNPATH
//
// MessageText:
//
//  The item's access path is not known to the server.
//
#define OPC_E_UNKNOWNPATH                ((HRESULT)0xC004000AL)

//
// MessageId: OPC_E_RANGE
//
// MessageText:
//
//  The value was out of range.
//
#define OPC_E_RANGE                      ((HRESULT)0xC004000BL)

//
// MessageId: OPC_E_DUPLICATENAME
//
// MessageText:
//
//  Duplicate name not allowed.
//
#define OPC_E_DUPLICATENAME              ((HRESULT)0xC004000CL)

//
// MessageId: OPC_S_UNSUPPORTEDRATE
//
// MessageText:
//
//  The server does not support the requested data rate but will use the closest available rate.
//
#define OPC_S_UNSUPPORTEDRATE            ((HRESULT)0x0004000DL)

//
// MessageId: OPC_S_CLAMP
//
// MessageText:
//
//  A value passed to write was accepted but the output was clamped.
//
#define OPC_S_CLAMP                      ((HRESULT)0x0004000EL)

//
// MessageId: OPC_S_INUSE
//
// MessageText:
//
//  The operation cannot be performed because the object is bering referenced.
//
#define OPC_S_INUSE                      ((HRESULT)0x0004000FL)

//
// MessageId: OPC_E_INVALIDCONFIGFILE
//
// MessageText:
//
//  The server's configuration file is an invalid format.
//
#define OPC_E_INVALIDCONFIGFILE          ((HRESULT)0xC0040010L)

//
// MessageId: OPC_E_NOTFOUND
//
// MessageText:
//
//  The requested object (e.g. a public group) was not found.
//
#define OPC_E_NOTFOUND                   ((HRESULT)0xC0040011L)

//
// MessageId: OPC_E_INVALID_PID
//
// MessageText:
//
//  The specified property ID is not valid for the item.
//
#define OPC_E_INVALID_PID                ((HRESULT)0xC0040203L)

//
// MessageId: OPC_E_DEADBANDNOTSET
//
// MessageText:
//
//  The item deadband has not been set for this item.
//
#define OPC_E_DEADBANDNOTSET             ((HRESULT)0xC0040400L)

//
// MessageId: OPC_E_DEADBANDNOTSUPPORTED
//
// MessageText:
//
//  The item does not support deadband.
//
#define OPC_E_DEADBANDNOTSUPPORTED       ((HRESULT)0xC0040401L)

//
// MessageId: OPC_E_NOBUFFERING
//
// MessageText:
//
//  The server does not support buffering of data items that are collected at a faster rate than the group update rate.
//
#define OPC_E_NOBUFFERING                ((HRESULT)0xC0040402L)

//
// MessageId: OPC_E_INVALIDCONTINUATIONPOINT
//
// MessageText:
//
//  The continuation point is not valid.
//
#define OPC_E_INVALIDCONTINUATIONPOINT   ((HRESULT)0xC0040403L)

//
// MessageId: OPC_S_DATAQUEUEOVERFLOW
//
// MessageText:
//
//  Not every detected change has been returned since the server's buffer reached its limit and had to purge out the oldest data.
//
#define OPC_S_DATAQUEUEOVERFLOW          ((HRESULT)0x00040404L)

//
// MessageId: OPC_E_RATENOTSET
//
// MessageText:
//
//  There is no sampling rate set for the specified item.  
//
#define OPC_E_RATENOTSET                 ((HRESULT)0xC0040405L)

//
// MessageId: OPC_E_NOTSUPPORTED
//
// MessageText:
//
//  The server does not support writing of quality and/or timestamp.
//
#define OPC_E_NOTSUPPORTED               ((HRESULT)0xC0040406L)

//
// MessageId: OPCCPX_E_TYPE_CHANGED
//
// MessageText:
//
//  The dictionary and/or type description for the item has changed.
//
#define OPCCPX_E_TYPE_CHANGED            ((HRESULT)0xC0040407L)

//
// MessageId: OPCCPX_E_FILTER_DUPLICATE
//
// MessageText:
//
//  A data filter item with the specified name already exists. 
//
#define OPCCPX_E_FILTER_DUPLICATE        ((HRESULT)0xC0040408L)

//
// MessageId: OPCCPX_E_FILTER_INVALID
//
// MessageText:
//
//  The data filter value does not conform to the server's syntax.
//
#define OPCCPX_E_FILTER_INVALID          ((HRESULT)0xC0040409L)

//
// MessageId: OPCCPX_E_FILTER_ERROR
//
// MessageText:
//
//  An error occurred when the filter value was applied to the source data.
//
#define OPCCPX_E_FILTER_ERROR            ((HRESULT)0xC004040AL)

//
// MessageId: OPCCPX_S_FILTER_NO_DATA
//
// MessageText:
//
//  The item value is empty because the data filter has excluded all fields.
//
#define OPCCPX_S_FILTER_NO_DATA          ((HRESULT)0x0004040BL)

#endif // ifndef __OPCERROR_H
