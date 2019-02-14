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

#define IDS_CONNECT_E_NOCONNECTION         0x101B

// OPC DA error codes.
#define IDS_OPC_E_INVALIDHANDLE             0x1001
#define IDS_OPC_E_BADTYPE                   0x1002
#define IDS_OPC_E_PUBLIC                    0x1003
#define IDS_OPC_E_BADRIGHTS                 0x1004
#define IDS_OPC_E_UNKNOWNITEMID             0x1005
#define IDS_OPC_E_INVALIDITEMID             0x1006
#define IDS_OPC_E_INVALIDFILTER             0x1007
#define IDS_OPC_E_UNKNOWNPATH               0x1008
#define IDS_OPC_E_RANGE                     0x1009
#define IDS_OPC_E_DUPLICATENAME             0x100A
#define IDS_OPC_E_INVALIDCONFIGFILE         0x100B
#define IDS_OPC_E_NOTFOUND                  0x100C
#define IDS_OPC_E_INVALID_PID               0x100D
#define IDS_OPC_E_DEADBANDNOTSET 	        0x100E
#define IDS_OPC_E_DEADBANDNOTSUPPORTED      0x100F
#define IDS_OPC_E_RATENOTSET	    	    0x1010
#define IDS_OPC_E_NOBUFFERING	    	    0x1011
#define IDS_OPC_E_STALEVALUE	    	    0x1012
#define IDS_OPC_E_INVALIDCONTINUATIONPOINT  0x1013
#define IDS_OPC_S_UNSUPPORTEDRATE           0x1014
#define IDS_OPC_S_CLAMP                     0x1015
#define IDS_OPC_S_INUSE                     0x1016
#define IDS_OPC_S_DATAQUEUEOVERFLOW	        0x1017
#define IDS_OPC_S_QUALITY		    	    0x1018
#define IDS_OPC_S_TIME	    	            0x1019
#define IDS_OPC_S_TIMEQUALITY	    	    0x101A

#define IDS_OPCDX_E_UNKNOWN_ITEM_NAME       0x0800
#define IDS_OPCDX_E_UNKNOWN_ITEM_PATH       0x0801
#define IDS_OPCDX_E_INVALID_ITEM            0x0802
#define IDS_OPCDX_E_NOACCESS                0x0803
#define IDS_OPCDX_E_RANGE                   0x0804
#define IDS_OPCDX_S_CLAMP                   0x0805
#define IDS_OPCDX_E_CONFIGPERSIST           0x0806
#define IDS_OPCDX_E_INVALID_BROWSEPATH      0x0807
#define IDS_OPCDX_E_INVALIDNAME             0x0808
#define IDS_OPCDX_E_DUPLICATENAME           0x0809
#define IDS_OPCDX_E_INVALID_DXCONNECTION    0x080A
#define IDS_OPCDX_E_INVALID_OVERRIDEATTEMPT 0x080B
#define IDS_OPCDX_E_INVALID_SERVERTYPE      0x080C
#define IDS_OPCDX_E_INVALID_TARGET          0x080D
#define IDS_OPCDX_E_INVALID_SERVER_URL      0x080E
#define IDS_OPCDX_E_INVALID_VERSION         0x080F
#define IDS_OPCDX_E_NOT_ACCESSIBLE          0x0810
#define IDS_OPCDX_E_OVERRIDE_BADTYPE        0x0811
#define IDS_OPCDX_E_OVERRIDE_OUTOFRANGE     0x0812
#define IDS_OPCDX_E_PERSISTING              0x0813
#define IDS_OPCDX_E_QUEUESIZE_OUTOFRANGE    0x0814
#define IDS_OPCDX_E_SUBSTITUTE_BADTYPE      0x0815
#define IDS_OPCDX_E_SUBSTITUTE_OUTOFRANGE   0x0816
#define IDS_OPCDX_E_TARGET_FAULT            0x0817
#define IDS_OPCDX_E_TARGET_NOTCONNECTED     0x0818
#define IDS_OPCDX_E_NO_WRITESATTEMPTED      0x0819
#define IDS_OPCDX_E_SOURCE_FAULT            0x081A
#define IDS_OPCDX_E_SOURCE_NOTCONNECTED     0x081B
#define IDS_OPCDX_E_TARGETITEM_CONNECTED    0x081C
#define IDS_OPCDX_E_CONNECTIONS_EXIST       0x081D
#define IDS_OPCDX_E_TOO_MANY_CONNECTIONS    0x081E

// Next default values for new objects
// 
#ifdef APSTUDIO_INVOKED
#ifndef APSTUDIO_READONLY_SYMBOLS
#define _APS_NEXT_RESOURCE_VALUE        102
#define _APS_NEXT_COMMAND_VALUE         40001
#define _APS_NEXT_CONTROL_VALUE         1000
#define _APS_NEXT_SYMED_VALUE           101
#endif
#endif
