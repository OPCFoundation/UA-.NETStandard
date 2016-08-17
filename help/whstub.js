//	WebHelp 5.10.002
window.whname="wh_stub";
function getframehandle(frames,framename)
{
	var frame=null;
	if(null==frames) return null;
	for(var i=0;i<frames.length;i++)
	{
		if(typeof(frames[i].name)!="unknown")
		{
			if(frames[i].name==framename)
				return frames[i];
		}
		if(frames[i].frames.length>0)
		{
			frame=getframehandle(frames[i].frames,framename);
			if(null!=frame)
				return frame;
		}
	}
	return frame;
}

function AddToArray(arr,obj)
{
	var bFound=false;
	for(var i=0;i<arr.length;i++){
		if(arr[i]==obj){
			bFound=true;
			break;
		}
		else if(arr[i]==null){
			break;
		}
	}
	if(!bFound) arr[i]=obj;
}

var gArrayRegistedMessage=new Array();
var gArrayCompoentsArray=new Array();

function GetComponentsArray(nMessageId)
{
	var len=gArrayRegistedMessage.length;
	for(var i=0;i<len;i++)
	{
		if(gArrayRegistedMessage[i]==nMessageId){
			if(gArrayCompoentsArray.length>i)
				return gArrayCompoentsArray[i];
			else
				return null;
		}
	}
	return null;
}

function CreateComponentsArray(nMessageId)
{
	var len=gArrayRegistedMessage.length;
	gArrayRegistedMessage[len]=nMessageId;
	gArrayCompoentsArray[len]=new Array();
	return gArrayCompoentsArray[len];
}

function listener(sName,oWindow)
{
	this.sName=sName;
	this.oWindow=oWindow;
}

function RegisterListener(windowName,nMessageId)
{
	var arrayComponents=GetComponentsArray(nMessageId);
	if(arrayComponents==null)
		arrayComponents=CreateComponentsArray(nMessageId);
	
	if(arrayComponents!=null)
	{
		for (var i=0;i<arrayComponents.length;i++)
		{
			if (arrayComponents[i].sName == windowName)
				return false;
		}
		var oListener=new listener(windowName,null);
		AddToArray(arrayComponents,oListener);
		return true;
	}
	else
		return false;
}

function RegisterListener2(oWindow,nMessageId)
{
	var arrayComponents=GetComponentsArray(nMessageId);
	if(arrayComponents==null)
		arrayComponents=CreateComponentsArray(nMessageId);
	
	if(arrayComponents!=null)
	{
		var oListener=new listener("",oWindow);
		AddToArray(arrayComponents,oListener);
		return true;
	}
	else
		return false;
}

function UnRegisterListener2(oWindow,nMessageId)
{
	var arrayComponents=GetComponentsArray(nMessageId);
	if(arrayComponents!=null)
	{
		for(var i=0;i<arrayComponents.length;i++)
		{
			if(arrayComponents[i].oWindow==oWindow)
			{
				removeItemFromArray(arrayComponents,i);
				return true;
			}
		}
	}
	else
		return false;
}

function SendMessage(oMessage)
{
	var bDelivered=false;
	var arrayComponents=GetComponentsArray(oMessage.nMessageId);
	if(arrayComponents!=null&&arrayComponents.length>0){
		for(var i=0;i<arrayComponents.length;i++)
		{
			if(null!=arrayComponents[i])
			{
				var pFrame;
				if(arrayComponents[i].oWindow==null)
					pFrame=getframehandle(frames,arrayComponents[i].sName);
				else
					pFrame=arrayComponents[i].oWindow;
				if(null!=pFrame)
				{
					if(pFrame.onSendMessageX)
					{
						bDelivered=true;
						if(!pFrame.onSendMessageX(oMessage))
							break;
					}
					if(pFrame.onSendMessage)
					{
						bDelivered=true;
						if(!pFrame.onSendMessage(oMessage))
							break;
					}
				}
			}
		}
	}
	return bDelivered;
}