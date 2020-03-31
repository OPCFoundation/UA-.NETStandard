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

#ifndef __OPCAEDEF_H
#define __OPCAEDEF_H



// OPC Alarm & Event Component Category Description
#define OPC_EVENTSERVER_CAT_DESC L"OPC Alarm & Event Server Version 1.0"


//****************************************************
// OPC Quality flags 
//
// Masks for extracting quality subfields
// (note 'status' mask also includes 'Quality' bits)
//
#define    OPC_QUALITY_MASK            0xC0
#define    OPC_STATUS_MASK             0xFC
#define    OPC_LIMIT_MASK              0x03

// Values for QUALITY_MASK bit field
//
#define    OPC_QUALITY_BAD             0x00
#define    OPC_QUALITY_UNCERTAIN       0x40
#define    OPC_QUALITY_GOOD            0xC0

// STATUS_MASK Values for Quality = BAD
//
#define    OPC_QUALITY_CONFIG_ERROR    0x04
#define    OPC_QUALITY_NOT_CONNECTED   0x08
#define    OPC_QUALITY_DEVICE_FAILURE  0x0c
#define    OPC_QUALITY_SENSOR_FAILURE  0x10
#define    OPC_QUALITY_LAST_KNOWN      0x14
#define    OPC_QUALITY_COMM_FAILURE    0x18
#define    OPC_QUALITY_OUT_OF_SERVICE  0x1C

// STATUS_MASK Values for Quality = UNCERTAIN
//
#define    OPC_QUALITY_LAST_USABLE     0x44
#define    OPC_QUALITY_SENSOR_CAL      0x50
#define    OPC_QUALITY_EGU_EXCEEDED    0x54
#define    OPC_QUALITY_SUB_NORMAL      0x58

// STATUS_MASK Values for Quality = GOOD
//
#define    OPC_QUALITY_LOCAL_OVERRIDE  0xD8


// State bit masks
#define OPC_CONDITION_ENABLED			0x0001
#define OPC_CONDITION_ACTIVE			0x0002
#define OPC_CONDITION_ACKED				0x0004


// bit masks for m_wChangeMask
#define OPC_CHANGE_ACTIVE_STATE			0x0001
#define OPC_CHANGE_ACK_STATE			0x0002
#define OPC_CHANGE_ENABLE_STATE			0x0004
#define OPC_CHANGE_QUALITY				0x0008
#define OPC_CHANGE_SEVERITY				0x0010
#define OPC_CHANGE_SUBCONDITION			0x0020
#define OPC_CHANGE_MESSAGE				0x0040
#define OPC_CHANGE_ATTRIBUTE			0x0080


// dwEventType
#define OPC_SIMPLE_EVENT				0x0001
#define OPC_TRACKING_EVENT				0x0002
#define OPC_CONDITION_EVENT				0x0004

#define OPC_ALL_EVENTS	(OPC_SIMPLE_EVENT | OPC_TRACKING_EVENT | OPC_CONDITION_EVENT )


// QueryAvailableFilters() bit masks
#define OPC_FILTER_BY_EVENT				0x0001
#define OPC_FILTER_BY_CATEGORY			0x0002
#define OPC_FILTER_BY_SEVERITY			0x0004
#define OPC_FILTER_BY_AREA				0x0008
#define OPC_FILTER_BY_SOURCE			0x0010


#endif
