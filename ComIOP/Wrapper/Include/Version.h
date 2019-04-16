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

/*  Copyright © OPC Foundation 2019.               All rights reserved.     */
/****************************************************************************/
 
#define SDK_COPYRIGHT_NOTICE "Copyright (c) 2004-2019 OPC Foundation, Inc"

//----------------------------------------------------------------------------
//	Product name and version.
//
#define SDK_MAJOR     1
#define SDK_MINOR     01
#define SDK_BUILD     333
#define SDK_REVISION  0
#define SDK_PATCH     0

// Convert numbers to strings:
#define chSTR(x) #x
#define chSTR2(x) chSTR(x)

#define SDK_VERSION SDK_MAJOR,SDK_MINOR,SDK_BUILD,SDK_PATCH
#define SDK_VERSION_STR chSTR2(SDK_MAJOR) "." chSTR2(SDK_MINOR)  "." chSTR2(SDK_BUILD) "." chSTR2(SDK_REVISION)

#define SDK_FILEVERSION SDK_MAJOR,SDK_MINOR,SDK_BUILD,SDK_PATCH
#define SDK_FILEVERSION_STR chSTR2(SDK_MAJOR) "." chSTR2(SDK_MINOR)  "." chSTR2(SDK_BUILD) "." chSTR2(SDK_PATCH)
//----------------------------------------------------------------------------

//----------------------------------------------------------------------------
//	OpcUaComServerHost 
//
#define COMHOST_FILEVERSION SDK_FILEVERSION
#define COMHOST_FILEVERSION_STR SDK_FILEVERSION_STR
//----------------------------------------------------------------------------

//----------------------------------------------------------------------------
//	StackTest
//
#define STACKTEST_FILEVERSION SDK_FILEVERSION
#define STACKTEST_FILEVERSION_STR SDK_FILEVERSION_STR
//----------------------------------------------------------------------------
