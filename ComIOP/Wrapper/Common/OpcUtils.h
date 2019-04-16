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

#ifndef _OpcUtils_H_
#define _OpcUtils_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "opccomn.h"

#define OPCUTILS_API

#include "OpcDefs.h"
#include "COpcString.h"
#include "COpcFile.h"
#include "OpcMatch.h"
#include "COpcCriticalSection.h"
#include "COpcArray.h"
#include "COpcList.h"
#include "COpcMap.h"
#include "COpcSortedArray.h"
#include "COpcText.h"
#include "COpcTextReader.h"
#include "COpcThread.h"
#include "OpcCategory.h"
#include "OpcRegistry.h"
#include "OpcXmlType.h"
#include "COpcXmlAnyType.h"
#include "COpcXmlElement.h"
#include "COpcXmlDocument.h"
#include "COpcVariant.h"
#include "COpcComObject.h"
#include "COpcClassFactory.h"
#include "COpcComModule.h"
#include "COpcCommon.h"
#include "COpcConnectionPoint.h"
#include "COpcCPContainer.h"
#include "COpcEnumCPs.h"
#include "COpcEnumString.h"
#include "COpcEnumUnknown.h"
#include "COpcSecurity.h"
#include "COpcThreadPool.h"
#include "COpcBrowseElement.h"

#endif // _OpcUtils_H_
