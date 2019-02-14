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

#ifndef _OpcUa_DateTime_H_
#define _OpcUa_DateTime_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * Functions for OpcUa_DateTime
 *===========================================================================*/

/**
  @brief Returns the UTC time in OpcUa_DateTime format.

  @return the UTC time
*/
#define OPCUA_P_DATETIME_UTCNOW OpcUa_ProxyStub_g_PlatformLayerCalltable->UtcNow

/**
  @brief Convert a string to a date-time

  @return OpcUa_BadInvalidArgument if a_pchDateTimeString is null
  @return OpcUa_BadInvalidArgument if the string is incorrectly formatted
  @return OpcUa_Bad for other failures
  @return OpcUa_Good on success

  @param szDateTimeString  [in] String to convert
  @param pDateTime        [out] Location to store the date-time
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_DateTime_GetDateTimeFromString( OpcUa_CharA*    szDateTimeString,
                                                                    OpcUa_DateTime* pDateTime);

/**
  @brief Convert a date-time to a string

  @return OpcUa_BadInvalidArgument if buffer is null
  @return OpcUa_BadInvalidArgument if the buffer is too short
  @return OpcUa_Good on success

  @param DateTime   [in] Date-time to convert.
  @param pchBuffer  [bi] Byte buffer to store the result (at last 25 bytes long).
  @param uLength    [in] Length of the given buffer in bytes (at least 25).
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_DateTime_GetStringFromDateTime( OpcUa_DateTime  DateTime, 
                                                                    OpcUa_CharA*    pchBuffer,
                                                                    OpcUa_UInt32    uLength);

/*============================================================================
 * Functions for OpcUa_TimeVal
 *===========================================================================*/


/**
  @brief Get the time in OpcUa_TimeVal format

  @return OpcUa_BadInvalidArgument if pValue is null
  @return OpcUa_Good on success

  @param pValue     [out]   Location of an OpcUa_TimeVal to store the time of day
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_DateTime_GetTimeOfDay(OpcUa_TimeVal* pValue);


OPCUA_END_EXTERN_C

#endif /* _OpcUa_DateTime_H_ */
