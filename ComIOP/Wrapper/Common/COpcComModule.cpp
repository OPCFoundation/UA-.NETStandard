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

#include "StdAfx.h"
#include "COpcComModule.h"
#include "COpcTextReader.h"
#include "OpcRegistry.h"
#include "COpcSecurity.h"
#include "COpcList.h"
#include "COpcMap.h"
#include "COpcXmlDocument.h"

//==============================================================================
// Static Data

static COpcComModule* g_pModule = NULL;

//==============================================================================
// Local Declarations

#define OPCREG_CONFIG_INFO             _T("ConfigInfo")
#define OPCREG_SELFREG_INFO            _T("SelfRegInfo")
#define OPCREG_VENDOR_NAME             _T("VendorName")
#define OPCREG_APPLICATION_NAME        _T("ApplicationName")
#define OPCREG_APPLICATION_DESCRIPTION _T("ApplicationDescription")
#define OPCREG_APPID                   _T("AppID")
#define OPCREG_CLASS_NAME              _T("ClassName")
#define OPCREG_CLASS_DESCRIPTION       _T("ClassDescription")
#define OPCREG_CLASS_VERSION           _T("ClassVersion")
#define OPCREG_CLSID                   _T("CLSID")
#define OPCREG_ALLOW_EVERYONE_ACCESS   _T("AllowEveryoneAccess")
#define OPCREG_SERVER_WRAPPER          _T("ServerWrapper")

//==============================================================================
// ServiceMain

static void WINAPI ServiceMain(DWORD dwArgc, LPTSTR* lpszArgv)
{
    OPC_ASSERT(g_pModule != NULL);

    g_pModule->ServiceMain(dwArgc, lpszArgv);
}

//==============================================================================
// EventHandler

static void WINAPI EventHandler(DWORD fdwControl)
{
    OPC_ASSERT(g_pModule != NULL);

    g_pModule->EventHandler(fdwControl);
}

//==============================================================================
// ExitProcess

void COpcComModule::ExitProcess(DWORD dwExitCode)
{
    OPC_ASSERT(g_pModule != NULL);

    g_pModule->m_cServiceStatus.dwWin32ExitCode = dwExitCode;

    PostThreadMessage(g_pModule->m_dwThread, WM_QUIT, 0, 0);
}

//==============================================================================
// OpcWinMain

OPCUTILS_API DWORD OpcWinMain(
    COpcComModule* pModule,
    HINSTANCE      hInstance,  
    LPTSTR         lpCmdLine
)
{
    OPC_ASSERT(pModule != NULL);

    g_pModule = pModule;

    DWORD dwResult = g_pModule->WinMain(hInstance, lpCmdLine);

    g_pModule = NULL;

    return dwResult;
}

//==============================================================================
// OpcExtractSelfRegInfo

HRESULT OpcExtractSelfRegInfo(IXMLDOMElement* ipRoot, IXMLDOMElement** ippInfo)
{
	BSTR bstrName = NULL;
	IXMLDOMElement* ipElement = NULL;

	*ippInfo = NULL;

	HRESULT hResult = S_OK;

	TRY
	{
		hResult = ipRoot->get_firstChild((IXMLDOMNode**)&ipElement);

		if (FAILED(hResult))
		{
			THROW();
		}

		hResult = ipElement->get_nodeName(&bstrName);

		if (FAILED(hResult))
		{
			THROW();
		}

		if (((COpcString)bstrName) == OPCREG_SELFREG_INFO)
		{
			hResult = ipRoot->removeChild(ipElement, (IXMLDOMNode**)ippInfo);

			if (FAILED(hResult))
			{
				THROW();
			}
		}
	}
	CATCH_FINALLY
	{
		if (bstrName != NULL)  SysFreeString(bstrName);
		if (ipElement != NULL) ipElement->Release();
	}
	
	return hResult;
}

//==============================================================================
// OpcInsertSelfRegInfo

HRESULT OpcInsertSelfRegInfo(IXMLDOMElement* ipRoot, IXMLDOMElement* ipInfo)
{
	IXMLDOMElement* ipElement = NULL;
	IXMLDOMElement* ipResult  = NULL;

	HRESULT hResult = S_OK;

	TRY
	{
		VARIANT_BOOL bHasChildren = VARIANT_FALSE;

		hResult = ipRoot->hasChildNodes(&bHasChildren);
		
		if (FAILED(hResult))
		{
			THROW();
		}

		if (bHasChildren == VARIANT_TRUE)
		{
			hResult = ipRoot->get_firstChild((IXMLDOMNode**)&ipElement);

			if (FAILED(hResult))
			{
				THROW();
			}

			VARIANT vRefChild;
			vRefChild.vt      = VT_UNKNOWN;
			vRefChild.punkVal = ipElement;

			hResult = ipRoot->insertBefore(ipInfo, vRefChild, (IXMLDOMNode**)&ipResult);

			if (FAILED(hResult))
			{
				THROW();
			}
		}
		else
		{
			hResult = ipRoot->appendChild(ipInfo, (IXMLDOMNode**)&ipResult);

			if (FAILED(hResult))
			{
				THROW();
			}
		}
	}
	CATCH_FINALLY
	{
		if (ipElement != NULL) ipElement->Release();
		if (ipResult != NULL)  ipResult->Release();
	}
	
	return hResult;
}

//==============================================================================
// COpcComModule

// Constructor
COpcComModule::COpcComModule(               
	const COpcString&          cVendorName,
    const COpcString&          cApplicationName,
    const COpcString&          cApplicationDescription,
    const GUID&                cAppID,
    const TOpcClassTableEntry* pClasses,
    const TClassCategories*    pCategories,
	bool                       bIsWrapper
)
:
    m_cAppID(cAppID)
{
    m_cVendorName             = cVendorName;
	m_cApplicationName        = cApplicationName;
	m_cApplicationDescription = cApplicationDescription;

    m_pClasses        = (TOpcClassTableEntry*)pClasses;
    m_pCategories     = (TClassCategories*)pCategories;
    m_bRegFromFile    = false;

	m_bWrapper        = bIsWrapper;
	m_pWrappedClasses = NULL;

    m_hModule         = NULL;
    m_dwThread        = NULL;
    m_hServiceStatus  = NULL;

    memset(&m_cServiceStatus, 0, sizeof(m_cServiceStatus));
}

// Destructor
COpcComModule::~COpcComModule()
{
    if (m_bRegFromFile)
    {
        FreeClassTable(m_pClasses);
        OpcFree(m_pCategories);
        CoUninitialize();
    }
}


// GetConfigParam
bool COpcComModule::GetConfigParam(const COpcString& cName, COpcString& cValue)
{
	return m_cParameters.Lookup(cName, cValue);
}

// WinMain
DWORD COpcComModule::WinMain(
    HINSTANCE hInstance, 
    LPTSTR    lpCmdLine
)
{
	m_hModule = hInstance;

	// need to extract the executable name from the command line.
	WCHAR* pchCommandLine = GetCommandLine();
	BOOL isQuoted = FALSE;

	if (*pchCommandLine == '"')
	{
		isQuoted = TRUE;
		pchCommandLine++;
	}

	while (*pchCommandLine != '\0')
	{
		// check for quotes.
		if (isQuoted)
		{
			if (*pchCommandLine == '"')
			{
				pchCommandLine++;
				break;
			}
		}

		// check for whitespace.
		else
		{
			if (iswspace(*pchCommandLine))
			{
				pchCommandLine++;
				break;
			}
		}

		pchCommandLine++;
	}

	// skip trailing whitespace.
	while (iswspace(*pchCommandLine))
	{
		pchCommandLine++;
	}

    // parse command line arguments.
    COpcText cText;
    COpcTextReader cReader(pchCommandLine);

    // skip until first command flag - if any.
    cText.SetType(COpcText::Delimited);
    cText.SetDelims(L"-/");

    if (cReader.GetNext(cText))
    {
        // read first command flag.
        cText.SetType(COpcText::NonWhitespace);
        cText.SetEofDelim();

        if (cReader.GetNext(cText))
        {
            COpcString cFlags = ((COpcString&)cText).ToLower();

            // register module as local server.
            if (cFlags== _T("regserver")) return RegisterServer(false);
        
            // register module as service.
            if (cFlags== _T("service")) return RegisterServer(true);
        
            // unregister module.
            if (cFlags == _T("unregserver")) return UnregisterServer();
        }
    }

    // check for service key in registry.
    LPTSTR tsValue;

    bool bService = OpcRegGetValue(
        HKEY_CLASSES_ROOT, 
        _T("AppID\\") + (COpcString)m_cAppID, 
        _T("LocalService"),
        &tsValue);

	
	// enter the main execution loop when running as an EXE server.
    if (!bService)
	{
		Run();
	}

	// start the service main thread when running as a service.
	else
	{
        OpcFree(tsValue);

        // initialize status structure.
        m_cServiceStatus.dwServiceType             = SERVICE_WIN32_OWN_PROCESS;
        m_cServiceStatus.dwCurrentState            = SERVICE_STOPPED;
        m_cServiceStatus.dwControlsAccepted        = SERVICE_ACCEPT_STOP;
        m_cServiceStatus.dwWin32ExitCode           = 0;
        m_cServiceStatus.dwServiceSpecificExitCode = 0;
        m_cServiceStatus.dwCheckPoint              = 0;
        m_cServiceStatus.dwWaitHint                = 0;

        SERVICE_TABLE_ENTRY* pServices = new SERVICE_TABLE_ENTRY[2];

        pServices[0].lpServiceName = (LPTSTR)(LPCTSTR)m_cApplicationName;
        pServices[0].lpServiceProc = ::ServiceMain;
        pServices[1].lpServiceName = NULL;
        pServices[1].lpServiceProc = NULL;

        // notify the server control manager.
        if (!StartServiceCtrlDispatcher(pServices))
        {
            delete [] pServices;
            return -1;
        }

        delete [] pServices;
    }

    return m_cServiceStatus.dwWin32ExitCode;
}

// ServiceMain
void COpcComModule::ServiceMain(DWORD dwArgc, LPTSTR* lpszArgv)
{
    // register handler for service control events.
    m_hServiceStatus = RegisterServiceCtrlHandler(m_cApplicationName, ::EventHandler);

    if (m_hServiceStatus == NULL)
    {
        return;
    }

    // update service state.
    m_cServiceStatus.dwCurrentState = SERVICE_START_PENDING;
	SetServiceStatus(m_hServiceStatus, &m_cServiceStatus);

    m_cServiceStatus.dwWin32ExitCode = S_OK;
    m_cServiceStatus.dwCheckPoint    = 0;
    m_cServiceStatus.dwWaitHint      = 0;

    // enter the main execution loop.
    Run();

    // update service state.
    m_cServiceStatus.dwCurrentState = SERVICE_STOPPED;
    SetServiceStatus(m_hServiceStatus, &m_cServiceStatus);
}

// EventHandler
void COpcComModule::EventHandler(DWORD fdwControl)
{
	switch (fdwControl)
	{
	    case SERVICE_CONTROL_STOP:
        {
            // update service state.
            m_cServiceStatus.dwCurrentState = SERVICE_STOP_PENDING;
	        SetServiceStatus(m_hServiceStatus, &m_cServiceStatus);

            // post quit message.
            PostThreadMessage(m_dwThread, WM_QUIT, 0, 0);
		    break;
        }

        default:
        {
            // do nothing.
            break;
        }
	}
}

// Run
void COpcComModule::Run()
{
    HRESULT hResult = S_OK;

    // record service control thread.
	m_dwThread = GetCurrentThreadId();

    // intialize thread as free threaded service.
	hResult = CoInitializeEx(NULL, COINIT_MULTITHREADED);

    if (FAILED(hResult))
    {
        return;
    }

	// create a NULL DACL which will allow access to everyone.
	COpcSecurity cSecurity;

    hResult = cSecurity.InitializeFromThreadToken();

    if (FAILED(hResult))
    {
        return;
    }

    // initialize security.
	hResult = CoInitializeSecurity(
	    cSecurity,
	    -1,
	    NULL,
	    NULL,
	    RPC_C_AUTHN_LEVEL_PKT,
	    RPC_C_IMP_LEVEL_IMPERSONATE,
	    NULL,
	    EOAC_NONE,
	    NULL);

    if (FAILED(hResult))
    {
		// security may have already been initialized when registering classes from config file.
		if (hResult != RPC_E_TOO_LATE)
		{
	        return;
		}
    }

    DWORD pdwRegister[256];

    // register class objects.
    hResult = OpcRegisterClassObjects(
        m_pClasses, 
        CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER, 
        REGCLS_MULTIPLEUSE,
        pdwRegister);

    if (FAILED(hResult))
    {
        return;
    }
	
	// check if a wrapper process.
	if (m_bWrapper)
	{	
		// load classes from registry.
		m_pWrappedClasses = GetClassesFromRegistry(m_pClasses);

		// register class objects for wrapped classes.
		if (m_pWrappedClasses != NULL)
		{
			OpcRegisterClassObjects(
				m_pWrappedClasses, 
				CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER, 
				REGCLS_MULTIPLEUSE,
				pdwRegister);
		}
	}

    // update service state.
    m_cServiceStatus.dwCurrentState = SERVICE_RUNNING;

	if (m_hServiceStatus != NULL)
	{
		SetServiceStatus(m_hServiceStatus, &m_cServiceStatus);
	}

    TRY
    {
        // enter message loop.
	    MSG cMsg;
	    while (GetMessage(&cMsg, 0, 0, 0)) DispatchMessage(&cMsg);
    }
    CATCH
    {
    }
	
	// revoke wrapped classes.
	if (m_pWrappedClasses != NULL)
	{
		OpcRevokeClassObjects(m_pWrappedClasses, pdwRegister);
	}

    // unregister class objects.
    OpcRevokeClassObjects(m_pClasses, pdwRegister);

    // unregister com libraries.
	CoUninitialize();
}

// RegisterServer
HRESULT COpcComModule::RegisterServer(bool bService)
{
	COpcString cValue;
	bool bEveryone = GetConfigParam(OPCREG_ALLOW_EVERYONE_ACCESS, cValue);

	return OpcRegisterServer(
        m_hModule, 
		m_cVendorName,
		m_cApplicationName,
		m_cApplicationDescription,
		m_cAppID,
		bService,
        m_pClasses, 
        m_pCategories,
		bEveryone);
}

// UnregisterServer
HRESULT COpcComModule::UnregisterServer()
{
    return OpcUnregisterServer(
		m_hModule,
		m_cVendorName,
		m_cApplicationName,
		m_cAppID,
        m_pClasses, 
        m_pCategories);
}

// RegisterFromFiles
HRESULT COpcComModule::RegisterFromFiles(HINSTANCE hModule)
{
    CoInitializeEx(NULL, COINIT_MULTITHREADED);
	
    // create a NULL DACL which will allow access to everyone.
	COpcSecurity cSecurity;

    HRESULT hResult = cSecurity.InitializeFromThreadToken();

    if (FAILED(hResult))
    {
        return hResult;
    }

    // initialize security.
	hResult = CoInitializeSecurity(
	    cSecurity,
	    -1,
	    NULL,
	    NULL,
	    RPC_C_AUTHN_LEVEL_PKT,
	    RPC_C_IMP_LEVEL_IMPERSONATE,
	    NULL,
	    EOAC_NONE,
	    NULL);

    if (FAILED(hResult))
    {
       return hResult;
    }

	// =================
	// TJF - skip GetClassesFromFiles and just use the hard-coded configuration
	// defined by macros 'OPC_CLASS_TABLE_ENTRY' & 'OPC_CATEGORY_TABLE_ENTRY'.  Not clear
	// at this point how to support multiple CoClasses from XML file
	m_bRegFromFile = false;
	return S_OK;
	// =================


    COpcString    cVendorName;
    COpcString    cApplicationName;
    COpcString    cApplicationDescription;
    GUID          cAppID;

    // get classes from config file(s).
    TOpcClassTableEntry* pClassesFromFile = GetClassesFromFiles(
		hModule, 
		m_pClasses,
		cVendorName,
		cApplicationName,
		cApplicationDescription,
		cAppID
	);

	// check for error reading from file - use defaults.
	if (pClassesFromFile == NULL)
	{
		m_bRegFromFile = false;
		return S_OK;
	}

	m_cVendorName             = cVendorName;
	m_cApplicationName        = cApplicationName;
	m_cApplicationDescription = cApplicationDescription;
	m_cAppID                  = cAppID;
	
	// check if the server wraps other servers.
	COpcString cWrapper;

	if (GetConfigParam(OPCREG_SERVER_WRAPPER, cWrapper))
	{
		m_bWrapper = (cWrapper.ToLower() == _T("true"));
	}

    // replace the class and category tables with the auto generated ones.
    for (UINT uClassCount = 0; pClassesFromFile[uClassCount].tsClassName != NULL; uClassCount++);            
    for (UINT uCategoryCount = 0; m_pCategories[uCategoryCount].pClsid != NULL; uCategoryCount++);
  
    TClassCategories* pCategoriesFromFile = OpcArrayAlloc(TClassCategories, uClassCount*uCategoryCount+1);
    memset(pCategoriesFromFile, 0, (uClassCount*uCategoryCount+1)*sizeof(TClassCategories));

    UINT uIndex = 0;

    for (UINT ii = 0; pClassesFromFile[ii].tsClassName != NULL; ii++)
    {
        for (UINT jj = 0; m_pCategories[jj].pClsid != NULL; jj++)
        {
            pCategoriesFromFile[uIndex].pClsid    = pClassesFromFile[ii].pClsid;
            pCategoriesFromFile[uIndex].pCategory = m_pCategories[jj].pCategory;
            uIndex++;
        }
    }

    m_pClasses     = pClassesFromFile;
    m_pCategories  = pCategoriesFromFile;
    m_bRegFromFile = true;

    return S_OK;
}

// InsertSelfRegInfo
HRESULT COpcComModule::InsertSelfRegInfo(
	COpcXmlElement&            cRoot,
	const TOpcClassTableEntry* pClasses
)
{
	BSTR bstrName = NULL;
	BSTR bstrNamespace = NULL;

	IXMLDOMElement*  ipInfo = NULL;
	IXMLDOMDocument* ipDocument = NULL;

	HRESULT hResult = S_OK;

	TRY
	{
		IXMLDOMElement* ipRoot = cRoot;

		HRESULT hResult = ipRoot->get_ownerDocument(&ipDocument);

		if (FAILED(hResult))
		{
			THROW();
		}

		VARIANT vNodeType;
		vNodeType.vt   = VT_I4;
		vNodeType.lVal = NODE_ELEMENT;

		BSTR bstrName = SysAllocString((COpcString)OPCREG_SELFREG_INFO);
		BSTR bstrNamespace = SysAllocString((COpcString)OPCXML_NS_OPC);

        hResult = ipDocument->createNode(vNodeType, bstrName, bstrNamespace, (IXMLDOMNode**)&ipInfo);

        if (FAILED(hResult))
        {
			THROW();
        }

		COpcXmlElement cElement = ipInfo;
        
		// save the default information.
		WRITE_ELEMENT(OPCREG_VENDOR_NAME,             m_cVendorName);
		WRITE_ELEMENT(OPCREG_APPLICATION_NAME,        m_cApplicationName);
		WRITE_ELEMENT(OPCREG_APPLICATION_DESCRIPTION, m_cApplicationDescription);
		WRITE_ELEMENT(OPCREG_APPID,                   m_cAppID);
		WRITE_ELEMENT(OPCREG_CLASS_NAME,              (COpcString)pClasses[0].tsClassName);
		WRITE_ELEMENT(OPCREG_CLASS_DESCRIPTION,       (COpcString)pClasses[0].tsClassDescription);
		WRITE_ELEMENT(OPCREG_CLASS_VERSION,           (COpcString)pClasses[0].tsClassVersion);
		WRITE_ELEMENT(OPCREG_CLSID,                   *(pClasses[0].pClsid));

		OpcInsertSelfRegInfo(ipRoot, ipInfo);
	}
	CATCH_FINALLY
	{
		SysFreeString(bstrName);
		SysFreeString(bstrNamespace);

		if (ipInfo != NULL) ipInfo->Release();
		if (ipDocument != NULL) ipDocument->Release();
	}
	
	return hResult;
}

// GetClassesFromFiles
TOpcClassTableEntry* COpcComModule::GetClassesFromFiles(
    HINSTANCE                  hModule,
    const TOpcClassTableEntry* pClasses,
	COpcString&                cVendorName,
	COpcString&                cApplicationName,
	COpcString&                cApplicationDescription,
	GUID&                      cAppID
)
{
    // lookup the module path.
    TCHAR tsPath[MAX_PATH+1];
    memset(tsPath, 0, sizeof(tsPath));

    GetModuleFileName(hModule, tsPath, MAX_PATH);

    // split into components.
    TCHAR tsDrive[_MAX_DRIVE];
    TCHAR tsDir[_MAX_DIR];
    TCHAR tsName[_MAX_FNAME];
    TCHAR tsExtention[_MAX_EXT];

    _tsplitpath(tsPath, tsDrive, tsDir, tsName, tsExtention);

	// construct config file parh.
	COpcString cConfigFile;

	cConfigFile += tsDrive;
    cConfigFile += tsDir;
    cConfigFile += tsName; 
    cConfigFile += OPCREG_CONFIG_FILE_SUFFIX;

	// load the XML document.
	COpcXmlDocument cDocument;

    if (!cDocument.Load(cConfigFile))
    {
		return NULL;
	}

	COpcXmlElement cRoot = cDocument.GetRoot();
	
	// nothing more to do document is not valid.
	if (cRoot == NULL)
	{
		return NULL;
	}

	// populate the configuration parameters table.
	cRoot.GetAttributes(m_cParameters);

	// find the registration element.
	COpcXmlElement cElement = cRoot.GetChild(OPCREG_SELFREG_INFO);

	if (cElement == NULL)
	{
		// insert default self registration info.
		HRESULT hResult = InsertSelfRegInfo(cRoot, pClasses);

		// persist the configuration with the self registration info.
		if (SUCCEEDED(hResult))
		{
			cDocument.Save();
		};
	}

	// nothing more to do if self reg info was not found.
	if (cElement == NULL)
	{
		return NULL;
	}

	// read registration information from the XML document.
	COpcString cClassName;
	COpcString cClassDescription;
	COpcString cClassVersion;
	CLSID      cClsid = GUID_NULL;

	READ_ELEMENT(OPCREG_VENDOR_NAME,             cVendorName);
	READ_ELEMENT(OPCREG_APPLICATION_NAME,        cApplicationName);
	READ_ELEMENT(OPCREG_APPLICATION_DESCRIPTION, cApplicationDescription);
	READ_ELEMENT(OPCREG_APPID,                   cAppID);
	READ_ELEMENT(OPCREG_CLASS_NAME,              cClassName);
	READ_ELEMENT(OPCREG_CLASS_DESCRIPTION,       cClassDescription);
	READ_ELEMENT(OPCREG_CLASS_VERSION,           cClassVersion);
	READ_ELEMENT(OPCREG_CLSID,                   cClsid);

	// check that all require information has been provided.
	if (
		   cVendorName.IsEmpty()      || 
		   cAppID == GUID_NULL        || 
		   cClassName.IsEmpty()       || 
		   cClsid == GUID_NULL
	   )
	{
		return NULL;
	}

	// default application name as class name.
	if (cApplicationName.IsEmpty())
	{
		cApplicationName = cClassName;
	}

	// default application description as class description.
	if (cApplicationDescription.IsEmpty())
	{
		cApplicationDescription = cClassDescription;
	}

	// re-allocate class table dynamically,
    TOpcClassTableEntry* pClassesFromFile = OpcArrayAlloc(TOpcClassTableEntry, 2);
    memset(pClassesFromFile, 0, sizeof(TOpcClassTableEntry)*2);
 
    pClassesFromFile[0].tsClassName         = OpcStrDup((LPCTSTR)cClassName);
    pClassesFromFile[0].tsClassDescription  = OpcStrDup((LPCTSTR)cClassDescription);
    pClassesFromFile[0].tsClassVersion      = OpcStrDup((LPCTSTR)cClassVersion);
    pClassesFromFile[0].pClsid              = (CLSID*)OpcAlloc(sizeof(CLSID));
    *((CLSID*)(pClassesFromFile[0].pClsid)) = cClsid;
    pClassesFromFile[0].pfnCreateInstance   = pClasses[0].pfnCreateInstance;

    return pClassesFromFile;
}

// 899A3076-F94E-4695-9DF8-0ED25B02BDBA
static const GUID CATID_PseudoComServers = {0x899A3076, 0xF94E, 0x4695, { 0x9D, 0xF8, 0x0E, 0xD2, 0x5B, 0x02, 0xBD, 0xBA } }; 

// GetClassesFromRegistry map
TOpcClassTableEntry* COpcComModule::GetClassesFromRegistry(
    const TOpcClassTableEntry* pClasses
)
{
	// read registration information from the XML document.
	COpcString cClassName;
	COpcString cClassDescription;
	COpcString cClassVersion;
	CLSID      cClsid = GUID_NULL;

	CreatorsMap creatorsByCategory;
	HRESULT hResult = GetHostCreatorsByCategory (pClasses, creatorsByCategory);
	if (FAILED(hResult))
	{
		return NULL;
	}

	COpcList<CLSID> cClsids;

	hResult = OpcEnumServersInCategory(NULL, CATID_PseudoComServers, &cClsids);

	if (FAILED(hResult))
	{
		return NULL;
	}

	UINT uCount = cClsids.GetCount();

	if (uCount == 0)
	{
		return NULL;
	}

	// re-allocate class table dynamically,
    TOpcClassTableEntry* pClassesFromFile = OpcArrayAlloc(TOpcClassTableEntry, uCount+1);
    memset(pClassesFromFile, 0, sizeof(TOpcClassTableEntry)*(uCount+1));

    OPC_POS pos = cClsids.GetHeadPosition();

    for (UINT ii = 0; pos != NULL; ii++)
    {
        CLSID cClsid = cClsids.GetNext(pos);
		PfnOpcCreateInstance* pCreator = NULL;

		COpcString cSubKey(_T("CLSID\\"));
		cSubKey += (COpcString)cClsid;
		cSubKey += _T("\\ProgID");

		LPTSTR tsProgID = NULL;
		bool bResult = OpcRegGetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, &tsProgID);

		if (bResult)
		{
			pCreator = FindHostCreatorByCategory(creatorsByCategory, cClsid);

			pClassesFromFile[ii].tsClassName         = OpcStrDup(tsProgID);
			pClassesFromFile[ii].tsClassDescription  = OpcStrDup(tsProgID);
			pClassesFromFile[ii].tsClassVersion      = NULL;
			pClassesFromFile[ii].pClsid              = (CLSID*)OpcAlloc(sizeof(CLSID));
			*((CLSID*)(pClassesFromFile[ii].pClsid)) = cClsid;
			if (pCreator)
				pClassesFromFile[ii].pfnCreateInstance = pCreator;
			else
				pClassesFromFile[ii].pfnCreateInstance   = pClasses[0].pfnCreateInstance;

			OpcFree(tsProgID);
		}
	}

    return pClassesFromFile;
}

// FreeClassTable
void COpcComModule::FreeClassTable(TOpcClassTableEntry* ppClassesFromFile)
{
    for (UINT ii = 0; ppClassesFromFile[ii].tsClassName != NULL; ii++)
    {
        OpcFree((LPTSTR)ppClassesFromFile[ii].tsClassName);
        OpcFree((LPTSTR)ppClassesFromFile[ii].tsClassDescription);
        OpcFree((LPTSTR)ppClassesFromFile[ii].tsClassVersion);
        OpcFree((CLSID*)ppClassesFromFile[ii].pClsid);
    }

    OpcFree(ppClassesFromFile);
}


//==============================================================================
// OpcServerRegInfo

HRESULT OpcServerRegInfo(
	const COpcString& cSrcFile,
	COpcString&       cVendorName,
	COpcString&       cApplicationName,
	COpcString&       cApplicationDescription,
	GUID&             cAppID,
	COpcString&       cClassName,
	COpcString&       cClassDescription,
	COpcString&       cClassVersion,
	GUID&             cClsid
)
{
	HRESULT hResult = S_OK;

	TRY
	{
		// load the configuration file.
		COpcXmlDocument cConfigDocument;

		if (!cConfigDocument.Load(cSrcFile))
		{
			THROW_(hResult, E_FAIL);
		}

		// find the existing self registration element.
		COpcXmlElement cElement = cConfigDocument.GetRoot().GetChild(OPCREG_SELFREG_INFO);

		if (cElement == NULL)
		{
			THROW_(hResult, E_FAIL);
		}

		READ_ELEMENT(OPCREG_VENDOR_NAME,             cVendorName);
		READ_ELEMENT(OPCREG_APPLICATION_NAME,        cApplicationName);
		READ_ELEMENT(OPCREG_APPLICATION_DESCRIPTION, cApplicationDescription);
		READ_ELEMENT(OPCREG_APPID,                   cAppID);
		READ_ELEMENT(OPCREG_CLASS_NAME,              cClassName);
		READ_ELEMENT(OPCREG_CLASS_DESCRIPTION,       cClassDescription);
		READ_ELEMENT(OPCREG_CLASS_VERSION,           cClassVersion);
		READ_ELEMENT(OPCREG_CLSID,                   cClsid);
	}
	CATCH
	{
	}

	return hResult;
}

//==============================================================================
// OpcCopyServer

HRESULT OpcCopyServer(
	const COpcString& cSrcPath, 
	const COpcString& cDstPath,
	const COpcString& cVendorName,
	const COpcString& cApplicationName,
	const COpcString& cApplicationDescription,
	const GUID&       cAppID,
	const COpcString& cClassName,
	const COpcString& cClassDescription,
	const COpcString& cClassVersion,
	const GUID&       cClsid
) 
{
	// parse the source path.
	COpcString cSrcBase = cSrcPath;
	
	int iIndex = cSrcBase.ReverseFind(OPCREG_CONFIG_FILE_SUFFIX);

	if (iIndex == -1)
	{
		return E_INVALIDARG;
	}

	cSrcBase = cSrcBase.SubStr(0, iIndex);

	// parse the destination path.
	COpcString cDstBase = cDstPath;
	
	iIndex = cDstBase.ReverseFind(OPCREG_CONFIG_FILE_SUFFIX);

	if (iIndex != -1)
	{
		cDstBase = cDstBase.SubStr(0, iIndex);
	}

	HRESULT hResult = S_OK;

	TRY
	{
		// load the configuration file.
		COpcString cConfigFile;

		cConfigFile += cSrcBase;
		cConfigFile += OPCREG_CONFIG_FILE_SUFFIX;

		// parse the document.
		COpcXmlDocument cConfigDocument;

		if (!cConfigDocument.Load(cConfigFile))
		{
			THROW_(hResult, E_FAIL);
		}

		// find the existing self registration element.
		COpcXmlElement cElement = cConfigDocument.GetRoot().GetChild(OPCREG_SELFREG_INFO);

		if (cElement == NULL)
		{
			cElement = cConfigDocument.GetRoot().AddChild(OPCREG_SELFREG_INFO);
		}

		WRITE_ELEMENT(OPCREG_VENDOR_NAME,             cVendorName);
		WRITE_ELEMENT(OPCREG_APPLICATION_NAME,        cApplicationName);
		WRITE_ELEMENT(OPCREG_APPLICATION_DESCRIPTION, cApplicationDescription);
		WRITE_ELEMENT(OPCREG_APPID,                   cAppID);
		WRITE_ELEMENT(OPCREG_CLASS_NAME,              cClassName);
		WRITE_ELEMENT(OPCREG_CLASS_DESCRIPTION,       cClassDescription);
		WRITE_ELEMENT(OPCREG_CLASS_VERSION,           cClassVersion);
		WRITE_ELEMENT(OPCREG_CLSID,                   cClsid);

		// save the document.
		cConfigFile  = cDstBase;
		cConfigFile += OPCREG_CONFIG_FILE_SUFFIX;

		if (!cConfigDocument.Save(cConfigFile))
		{
			THROW_(hResult, E_FAIL);
		}

		// copy the COM server executable binary file.
		CopyFile((COpcString)(cSrcBase + _T(".exe")), (COpcString)(cDstBase + _T(".exe")), FALSE);
	}
	CATCH
	{
	}

	return hResult;
}

// OpcGetModuleName
COpcString OpcGetModuleName()
{
    COpcString cConfigPath;

    // get executable path.
    TCHAR tsPath[MAX_PATH+1];
    memset(tsPath, 0, sizeof(tsPath));

	DWORD dwResult = GetModuleFileName(NULL, tsPath, MAX_PATH);

    if (dwResult == 0)
    {
        return cConfigPath;
    }

    cConfigPath = tsPath;

    // remove directory.
    int iIndex = cConfigPath.ReverseFind(_T("\\"));
    
    if (iIndex != -1)
    {
        cConfigPath = cConfigPath.SubStr(iIndex+1);
    }

    // remove extension.
    iIndex = cConfigPath.ReverseFind(_T("."));
    
    if (iIndex != -1)
    {
        cConfigPath = cConfigPath.SubStr(0, iIndex);
    }

    return cConfigPath;
}

// OpcGetModulePath
COpcString OpcGetModulePath()
{
    COpcString cConfigPath;

    // get executable path.
    TCHAR tsPath[MAX_PATH+1];
    memset(tsPath, 0, sizeof(tsPath));

	DWORD dwResult = GetModuleFileName(NULL, tsPath, MAX_PATH);

    if (dwResult == 0)
    {
        return cConfigPath;
    }

    cConfigPath = tsPath;

    // remove file name.
    int iIndex = cConfigPath.ReverseFind(_T("\\"));
    
    if (iIndex != -1)
    {
        cConfigPath = cConfigPath.SubStr(0, iIndex);
    }

    return cConfigPath;
}

// OpcGetModuleVersion
bool OpcGetModuleVersion(OpcVersionInfo& cInfo)
{
	// initialize output parameters.
	cInfo.cFileDescription.Empty();
	cInfo.wMajorVersion = 0;
	cInfo.wMinorVersion = 0;
	cInfo.wBuildNumber = 0;
	cInfo.wRevisionNumber = 0;

	// get module path.
	TCHAR tsModuleName[MAX_PATH+1];
	memset(tsModuleName, 0, sizeof(tsModuleName));

	DWORD dwResult = GetModuleFileName(NULL, tsModuleName, sizeof(tsModuleName));

	if (dwResult == 0)
	{
		return false;
	}

	// get the size of the version info blob.
	DWORD dwSize = GetFileVersionInfoSize(tsModuleName, NULL);

	if (dwSize == 0)
	{
		return false;
	}

	// allocate space for the version info blob.
	BYTE* pBuffer = OpcArrayAlloc(BYTE, dwSize);
	memset(pBuffer, 0, dwSize);

	// get the version info blob.
	if (GetFileVersionInfo(tsModuleName, NULL, dwSize, pBuffer))
	{
		// get the base version info.
		UINT uInfoSize = 0;
		VS_FIXEDFILEINFO* pInfo = NULL;

		if (VerQueryValue(pBuffer, _T("\\"), (void**)&pInfo, &uInfoSize))
		{
			cInfo.wMajorVersion   = (WORD)((pInfo->dwFileVersionMS>>16)&0x00FF);
			cInfo.wMinorVersion   = (WORD)(pInfo->dwFileVersionMS&0x00FF);
			cInfo.wBuildNumber    = (WORD)((pInfo->dwFileVersionLS>>16)&0x00FF);
			cInfo.wRevisionNumber = (WORD)(pInfo->dwFileVersionLS&0x00FF);
		}

		UINT uTranslateSize = 0;
		struct LANGANDCODEPAGE { WORD wLanguage; WORD wCodePage; }* pTranslate = NULL;

		// read the list of languages and code pages.
		if (VerQueryValue(pBuffer, 
              _T("\\VarFileInfo\\Translation"),
              (void**)&pTranslate,
              &uTranslateSize))
		{
			TCHAR tsSubBlock[MAX_PATH+1];
			memset(tsSubBlock, 0, sizeof(tsSubBlock));

			_stprintf(
				tsSubBlock, 
				_T("\\StringFileInfo\\%04x%04x\\FileDescription"),
				pTranslate->wLanguage,
				pTranslate->wCodePage
			);

			TCHAR* pBlock = NULL;
			UINT   uBlockSize = 0;

			if (VerQueryValue(pBuffer, tsSubBlock, (void**)&pBlock, &uTranslateSize))
			{
				cInfo.cFileDescription = pBlock;
			}
		}
	}

	// free the buffer.
	OpcFree(pBuffer);
		
	return true;
}

// 132B3E2B-0E92-4816-972B-E42AA9532529
static const GUID CATID_ComDllServerHost = {0x132B3E2B, 0x0E92, 0x4816, { 0x97, 0x2B, 0xE4, 0x2A, 0xA9, 0x53, 0x25, 0x29 } };

// GetHostCreatorsByCategory
HRESULT GetHostCreatorsByCategory (const TOpcClassTableEntry* pClasses, CreatorsMap& creatorsByCategory)
{
	HRESULT hResult = S_OK;
	ICatInformation* ipInfo     = NULL; 
	IEnumCATID* penumCATID = NULL;

	TRY
    {
        // create component category manager. 
        hResult = CoCreateInstance(
             CLSID_StdComponentCategoriesMgr,  
             NULL, 
             CLSCTX_INPROC_SERVER, 
             IID_ICatInformation, 
             (void**)&ipInfo
        ); 

	    if (FAILED(hResult)) 
        {
            THROW();
        }

		creatorsByCategory.RemoveAll();

		for (UINT i = 0; pClasses[i].tsClassName != NULL; i++)
		{
			hResult = ipInfo->EnumImplCategoriesOfClass(*(pClasses[i].pClsid), &penumCATID);

			if (FAILED(hResult)) 
			{
				THROW();
			}

			do
			{
				ULONG ulFetched = 0;
				CATID catid    = GUID_NULL;

				hResult = penumCATID->Next(1, &catid, &ulFetched);

				if (ulFetched == 1 && (catid != CATID_ComDllServerHost))
				{
					creatorsByCategory[catid] = pClasses[i].pfnCreateInstance;
				}
			}
			while (hResult == S_OK);

			if (FAILED(hResult)) 
			{
				THROW();
			}
		}

		// error while enumerating clsids.
		if (FAILED(hResult)) 
		{
			THROW();
		}
	}
    CATCH 
    {
        creatorsByCategory.RemoveAll();
    }
    FINALLY
    {
        if (penumCATID != NULL) penumCATID->Release();
        if (ipInfo != NULL) ipInfo->Release();
    }   

    return hResult;

}

// FindHostCreatorByCategory
PfnOpcCreateInstance* FindHostCreatorByCategory(const CreatorsMap& creatorsByCategory, const GUID& cClsid)
{
	
	HRESULT hResult = S_OK;
	ICatInformation* ipInfo     = NULL; 
	IEnumCATID* penumCATID = NULL;
	PfnOpcCreateInstance* pCreator = NULL;

	TRY
    {
        // create component category manager. 
        hResult = CoCreateInstance(
             CLSID_StdComponentCategoriesMgr,  
             NULL, 
             CLSCTX_INPROC_SERVER, 
             IID_ICatInformation, 
             (void**)&ipInfo
        ); 

	    if (FAILED(hResult)) 
        {
            THROW();
        }

		hResult = ipInfo->EnumImplCategoriesOfClass(cClsid, &penumCATID);

		do
		{
			ULONG ulFetched = 0;
			CATID catid    = GUID_NULL;

			hResult = penumCATID->Next(1, &catid, &ulFetched);

			if (ulFetched == 1 && (catid != CATID_PseudoComServers))
			{
				if (creatorsByCategory.Lookup(catid, pCreator))
				{
					break;
				}
			}
		}
		while (hResult == S_OK);

		if (FAILED(hResult)) 
		{
			THROW();
		}

		// error while enumerating clsids.
		if (FAILED(hResult)) 
		{
			THROW();
		}
	}
    CATCH 
    {
		pCreator = NULL;
    }
    FINALLY
    {
        if (penumCATID != NULL) penumCATID->Release();
        if (ipInfo != NULL) ipInfo->Release();
    }   

    return pCreator;
}
