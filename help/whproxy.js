//	WebHelp 5.10.001
var gbInited=false;
var gWndStubPage=null;
function getStubPage()
{
	if(!gbInited)
	{
		gWndStubPage=getStubPage_inter(window);
		gbInited=true;
	}
	return gWndStubPage;
}

function getStubPage_inter(wCurrent)
{
	if(null==wCurrent.parent||wCurrent.parent==wCurrent)
		return null;

	if(typeof(wCurrent.parent.whname)=="string"&&"wh_stub"==wCurrent.parent.whname)
		return wCurrent.parent;
	else
		if(wCurrent.parent.frames.length!=0&&wCurrent.parent!=wCurrent)
			return getStubPage_inter(wCurrent.parent);
		else
			return null;
}

function RegisterListener(framename,nMessageId)
{
	var wSP=getStubPage();
	if(wSP&&wSP!=this)
		return wSP.RegisterListener(framename,nMessageId);
	else
		return false;
}

function RegisterListener2(oframe,nMessageId)
{
	var wSP=getStubPage();
	if(wSP&&wSP!=this)
		return wSP.RegisterListener2(oframe,nMessageId);
	else
		return false;
}

function UnRegisterListener2(oframe,nMessageId)
{
	var wSP=getStubPage();
	if(wSP&&wSP!=this&&wSP.UnRegisterListener2)
		return wSP.UnRegisterListener2(oframe,nMessageId);
	else
		return false;
}

function SendMessage(oMessage)
{
	var wSP=getStubPage();
	if(wSP&&wSP!=this&&wSP.SendMessage)
		return wSP.SendMessage(oMessage);
	else
		return false;
}

var gbWhProxy=true;

var gbPreview=false;
gbPreview=false; 
if (gbPreview)
	document.oncontextmenu=contextMenu;

function contextMenu()
{
	return false;
}
