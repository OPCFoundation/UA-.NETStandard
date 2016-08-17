//	WebHelp 5.10.001
// const strings
var gIEA = new Array();
function aIE()
{
	var len = gIEA.length;
	gIEA[len] = new indexEntry(aIE.arguments);
}

function topic(sName, sURL)
{
	this.sName = sName;
	this.sURL = sURL;
}

function indexEntry(fn_arguments) 
{
	if (fn_arguments.length && fn_arguments.length >= 3) 
	{
		this.nType = fn_arguments[0];
		this.nPKOff = 0;
		this.nNKOff = fn_arguments[1];
		this.sName = fn_arguments[2];
		this.aTopics = null;
		var nLen = fn_arguments.length;
		if (Math.floor(nLen / 2) * 2 == nLen)
		{
			this.sTarget = fn_arguments[3];
			if (nLen > 5)
			{
				this.aTopics = new Array();
				for (var i = 0; i < (nLen - 5)/2; i++)
				{
					this.aTopics[i] = new topic(fn_arguments[i * 2 + 4], fn_arguments[i * 2 + 5]);
				}
			}

		}
		else 
		{
			if (nLen > 4) 
			{
				this.aTopics = new Array();
				for (var i = 0; i < (nLen - 4)/2; i++)
				{
					this.aTopics[i] = new topic(fn_arguments[i * 2 + 3], fn_arguments[i * 2 + 4]);
				}
			}
		}
	}
}

function getIndexTopicMappingList(nItemIndex)
{
	var sTopics = "";
	if (gIEA.length > nItemIndex)
	{
		if (gIEA[nItemIndex].aTopics) 
		{
			var i = 0;
			var nLen = gIEA[nItemIndex].aTopics.length;
			for (i = 0; i < nLen; i ++) 
			{
				sTopics += "," + gIEA[nItemIndex].aTopics[i];
			}
		}
	}
	return sTopics;		
}

function window_OnLoad()
{
	if (parent && parent != this) {
		if (parent.putData) 
		{
			for (var i = 0; i < gIEA.length; i ++ )
			{
				if (gIEA[i].nNKOff != 0 && i + gIEA[i].nNKOff + 1 < gIEA.length)
				{
				
					gIEA[i + gIEA[i].nNKOff + 1].nPKOff = gIEA[i].nNKOff;
				}
			}
			parent.putData(gIEA);
		}
	}
}

window.onload = window_OnLoad;