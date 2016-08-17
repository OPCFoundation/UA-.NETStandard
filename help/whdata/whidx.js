//	WebHelp 5.10.001
var gaFileMapping = new Array();
function fileMapping(sBK, sEK, sFileName, nNum)
{
	this.sBK = sBK;
	this.sEK = sEK;
	this.sFileName = sFileName;
	this.aKs = null;
	this.nNum = nNum;
	this.oUsedItems = null;
}


function iFM(sBK, sEK, sFileName, nNum)
{
	var i = gaFileMapping.length;
	gaFileMapping[i] = new fileMapping(sBK, sEK, sFileName, nNum);	
	if (i == 0) {
		gaFileMapping[i].nTotal = nNum;
	}
	else {
		gaFileMapping[i].nTotal = nNum + gaFileMapping[i - 1].nTotal;
	}
}

function window_OnLoad()
{
	if (parent && parent != this && parent.projReady)
	{
		parent.projReady(gaFileMapping);
	}		
}

window.onload = window_OnLoad;
