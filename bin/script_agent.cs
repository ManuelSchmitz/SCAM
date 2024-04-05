/*
 * R e a d m e
 * -----------
 * 
 * Project Page on Steam: https://steamcommunity.com/sharedfiles/filedetails/?id=2572154209
 * 
 * IMPORTANT:
 * For the most recent version, install the Steam Workshop version forst. Then, update the
 * scipts as described on the Github page.
 * 
 * Instructions on Github: https://github.com/ManuelSchmitz/SCAM/tree/main
 */

const string Ver = "0.11.0"; // Must be the same on dispatcher and agents.

static long DbgIgc = 0;
//static long DbgIgc = 76932813351402441; // pertam
//static long DbgIgc = 141426525525683227; // space
static bool IsLargeGrid;
static double Dt = 1 / 60f;
static float MAX_SP = 104.38f;
const float G = 9.81f;
const string DockHostTag = "docka-min3r";
const string ForwardGyroTag = "forward-gyro";

static float StoppingPowerQuotient = 0.5f;
static bool MaxBrakeInProximity = true;
static bool MaxAccelInProximity = false;
static bool MoreRejectDampening = true;

static string LOCK_NAME_GeneralSection     = "general";
static string LOCK_NAME_MiningSection      = "mining-site";///< Airspace above the mining site.
static string LOCK_NAME_BaseSection        = "base";       ///< Airspace above the base.

static IMyProgrammableBlock me; ///< Reference to the programmable block on which this script is running. (same as "Me", but available in all scopes)

Action<IMyTextPanel> outputPanelInitializer = x =>
{
	x.ContentType = ContentType.TEXT_AND_IMAGE;
};

Action<IMyTextPanel> logPanelInitializer = x =>
{
	x.ContentType = ContentType.TEXT_AND_IMAGE;
	x.FontColor = new Color(r: 0, g: 255, b: 116);
	x.FontSize = 0.65f;
};

static class Variables
{
	static Dictionary<string, object> v = new Dictionary<string, object> {
{ "circular-pattern-shaft-radius", new Variable<float> { value = 3.6f, parser = s => float.Parse(s) } },
{ "ct-raycast-range", new Variable<float> { value = 1000, parser = s => float.Parse(s) } },
{ "preferred-container", new Variable<string> { value = "", parser = s => s } },
{ "group-constraint", new Variable<string> { value = "general", parser = s => s } },
{ "logger-char-limit", new Variable<int> { value = 5000, parser = s => int.Parse(s) } },
{ "cargo-full-factor", new Variable<float> { value = 0.8f, parser = s => float.Parse(s) } },
{ "battery-low-factor", new Variable<float> { value = 0.2f, parser = s => float.Parse(s) } },
{ "battery-full-factor", new Variable<float> { value = 0.8f, parser = s => float.Parse(s) } },
{ "gas-low-factor", new Variable<float> { value = 0.2f, parser = s => float.Parse(s) } },
{ "speed-clear", new Variable<float> { value = 2f, parser = s => float.Parse(s) } },
{ "speed-drill", new Variable<float> { value = 0.6f, parser = s => float.Parse(s) } },
{ "roll-power-factor", new Variable<float> { value = 1f, parser = s => float.Parse(s) } },
// apck
{ "ggen-tag", new Variable<string> { value = "", parser = s => s } },
{ "hold-thrust-on-rotation", new Variable<bool> { value = true, parser = s => s == "true" } },
{ "amp", new Variable<bool> { value = false, parser = s => s == "true" } }
	};
	public static void Set(string key, string value) { (v[key] as ISettable).Set(value); }
	public static void Set<T>(string key, T value) { (v[key] as ISettable).Set(value); }
	public static T Get<T>(string key) { return (v[key] as ISettable).Get<T>(); }
	public interface ISettable
	{
void Set(string v);
T1 Get<T1>();
void Set<T1>(T1 v);
	}
	public class Variable<T> : ISettable
	{
public T value;
public Func<string, T> parser;
public void Set(string v) { value = parser(v); }
public void Set<T1>(T1 v) { value = (T)(object)v; }
public T1 Get<T1>() { return (T1)(object)value; }
	}
}

class Toggle
{
	static Toggle inst;
	Toggle() { }
	Action<string> onToggleStateChangeHandler;
	Dictionary<string, bool> sw;
	Toggle(Dictionary<string, bool> switches, Action<string> handler)
	{
onToggleStateChangeHandler = handler;
sw = switches;
	}

	public static Toggle C => inst;

	public static void Init(Dictionary<string, bool> switches, Action<string> handler)
	{
if (inst == null)
	inst = new Toggle(switches, handler);
	}

	public void Set(string key, bool value)
	{
if (sw[key] != value)
	Invert(key);
	}
	public void Invert(string key)
	{
sw[key] = !sw[key];
onToggleStateChangeHandler(key);
	}
	public bool Check(string key)
	{
return sw[key];
	}
	public ImmutableArray<MyTuple<string, string>> GetToggleCommands()
	{
return sw.Select(n => new MyTuple<string, string>("Toggle " + n.Key + (n.Value ? " (off)" : " (on)"), "toggle:" + n.Key)).ToImmutableArray();
	}
}

bool ʑ;ʏ ʐ;class ʏ{Dictionary<string,Action<string[]>>ʎ;public ʏ(Dictionary<string,Action<string[]>>ʎ){this.ʎ=ʎ;}public
void ʍ(string ʌ,string[]ʁ){this.ʎ[ʌ].Invoke(ʁ);}}static int ʋ;void ʊ(string ʉ){ʋ++;Echo("Run count: "+ʋ);Echo("Name: "+me.
CubeGrid.CustomName);if(ʑ&&string.IsNullOrEmpty(ʉ)){ʑ=false;ʀ();ʉ=string.Join(",",Me.CustomData.Trim('\n').Split(new[]{'\n'},
StringSplitOptions.RemoveEmptyEntries).Where(Í=>!Í.StartsWith("//")).Select(Í=>"["+Í+"]"));}if(!string.IsNullOrEmpty(ʉ)&&ʉ.Contains(":")){
var ʎ=ʉ.Split(new[]{"],["},StringSplitOptions.RemoveEmptyEntries).Select(Í=>Í.Trim('[',']')).ToList();foreach(var A in ʎ){
string[]ʁ=A.Split(new[]{':'},StringSplitOptions.RemoveEmptyEntries);if(ʁ[0]=="command"){try{this.ʐ.ʍ(ʁ[1],ʁ);}catch(Exception
ex){ʃ($"Run command '{ʁ[1]}' failed.\n{ex}");}}if(ʁ[0]=="toggle"){Toggle.C.Invert(ʁ[1]);ʃ(
$"Switching '{ʁ[1]}' to state '{Toggle.C.Check(ʁ[1])}'");}}}}void Į(){Ĭ.Ī.ļ();ś.Į();}IMyProgrammableBlock ʔ;void ʓ(){if(!string.IsNullOrEmpty(Me.CustomData))ʑ=true;ś.ŝ(Echo,
GridTerminalSystem);Toggle.Init(new Dictionary<string,bool>{{"adaptive-mining",false},{"adjust-entry-by-elevation",true},{"log-message",
false},{"show-pstate",false},{"suppress-transition-control",false},{"suppress-gyro-control",false},{"damp-when-idle",true},{
"ignore-user-thruster",false},{"cc",true}},ǭ=>{switch(ǭ){case"log-message":var ʒ=ɵ?.Ο;if(ʒ!=null)ʒ.CustomData="";break;}});ǫ.Add("docking",new
Ƚ(1,"docking"));stateWrapper=new StateWrapper(Í=>Storage=Í);if(!stateWrapper.TryLoad(Storage)){ś.Ŕ(
"State load failed, clearing Storage now");stateWrapper.Save();Runtime.UpdateFrequency=UpdateFrequency.None;}GridTerminalSystem.GetBlocksOfType(ɥ,A=>A.
IsSameConstructAs(Me));ɥ.ForEach(A=>A.EnableRaycast=true);IsLargeGrid=Me.CubeGrid.GridSizeEnum==MyCubeSize.Large;this.ʐ=new ʏ(new
Dictionary<string,Action<string[]>>{{"set-value",(Ǔ)=>Variables.Set(Ǔ[2],Ǔ[3])},{"add-panel",(Ǔ)=>{List<IMyTextPanel>D=new List<
IMyTextPanel>();GridTerminalSystem.GetBlocksOfType(D,R=>R.IsSameConstructAs(Me)&&R.CustomName.Contains(Ǔ[2]));var Ì=D.FirstOrDefault
();if(Ì!=null){ś.Ő($"Added {Ì.CustomName} as GUI panel");outputPanelInitializer(Ì);Ń=Ì;}}},{"add-logger",(Ǔ)=>{List<
IMyTextPanel>D=new List<IMyTextPanel>();GridTerminalSystem.GetBlocksOfType(D,R=>R.IsSameConstructAs(Me)&&R.CustomName.Contains(Ǔ[2])
);var Ì=D.FirstOrDefault();if(Ì!=null){logPanelInitializer(Ì);ś.Ł(Ì);ś.Ő("Added logger: "+Ì.CustomName);}}},{
"create-task",(Ǔ)=>ɵ?.ι()},{"mine",(Ǔ)=>ɵ?.ί()},{"skip",(Ǔ)=>ɵ?.ή()},{"set-role",(Ǔ)=>ʃ("command:set-role is deprecated.")},{
"low-update-rate",(Ǔ)=>Runtime.UpdateFrequency=UpdateFrequency.Update10},{"create-task-raycast",(Ǔ)=>ɢ(Ǔ)},{"force-finish",(Ǔ)=>ɵ?.δ()},{
"static-dock",(Ǔ)=>ʃ("command:static-dock is deprecated.")},{"set-state",(Ǔ)=>ɵ?.Ø(Ǔ[2])},{"halt",(Ǔ)=>ɵ?.ɚ()},{"clear-storage-state"
,(Ǔ)=>stateWrapper?.ClearPersistentState()},{"save",(Ǔ)=>stateWrapper?.Save()},{"static-dock-gps",(Ǔ)=>ʃ(
"command:static-dock-gps is deprecated.")},{"dispatch",(Ǔ)=>ɵ?.ξ()},{"global",(Ǔ)=>{var ʁ=Ǔ.Skip(2).ToArray();IGC.SendBroadcastMessage("miners.command",string.
Join(":",ʁ),TransmissionDistance.TransmissionDistanceMax);ʃ("broadcasting global "+string.Join(":",ʁ));ʐ.ʍ(ʁ[1],ʁ);}},{
"get-toggles",(Ǔ)=>{IGC.SendUnicastMessage(long.Parse(Ǔ[2]),$"menucommand.get-commands.reply:{string.Join(":",Ǔ.Take(3))}",Toggle.C.
GetToggleCommands());}},});ɵ=new ɴ(GridTerminalSystem,IGC,stateWrapper,Õ);}void ʀ(){var D=new List<IMyProgrammableBlock>();
GridTerminalSystem.GetBlocksOfType(D,ʂ=>ʂ.CustomName.Contains("core")&&ʂ.IsSameConstructAs(Me)&&ʂ.Enabled);ʔ=D.FirstOrDefault();if(ʔ!=null
)ɵ.ɝ(ʔ);else{ĺ=new Ŀ(stateWrapper.PState,GridTerminalSystem,IGC,Õ);ɵ.ɝ(ĺ);ɵ.σ=new ʏ(new Dictionary<string,Action<string[]
>>{{"create-wp",(Ǔ)=>ů(Ǔ)},{"pillock-mode",(Ǔ)=>ĺ?.Ø(Ǔ[2])},{"request-docking",(Ǔ)=>{ś.Ő(
"Embedded lone mode is not supported");}},{"request-depart",(Ǔ)=>{ś.Ő("Embedded lone mode is not supported");}}});}if(!string.IsNullOrEmpty(stateWrapper.
PState.lastAPckCommand)){Ĭ.Ī.İ(5000).ŀ(()=>ɵ.ϰ(stateWrapper.PState.lastAPckCommand));}Ĭ.Ī.Ľ(()=>!ɵ.ɲ.HasValue).İ(1000).ŀ(()=>ɵ
.θ());if(stateWrapper.PState.miningEntryPoint.HasValue){ɵ.ΰ();}}static void ʈ<ŗ>(ŗ ʇ,IList<ŗ>A)where ŗ:class{if((ʇ!=null)
&&!A.Contains(ʇ))A.Add(ʇ);}void ʆ<ŗ>(string Ų,ŗ ʅ){var ʄ=IGC.RegisterBroadcastListener(Ų);IGC.SendBroadcastMessage(ʄ.Tag,ʅ
,TransmissionDistance.TransmissionDistanceMax);}void ʃ(string ɨ){ś.Ő(ɨ);}
/** \note Must have same values in the dispatcher script!  */
public enum MinerState : byte
{
	Disabled              = 0,
	Idle                  = 1,
	GoingToEntry          = 2, ///< Descending to shaft, through shared airspace.
	Drilling              = 3, ///< Descending into the shaft, until there is a reasong to leave.
	//GettingOutTheShaft    = 4, (deprecated, was used for Lone mode)
	GoingToUnload         = 5, ///< Ascending from the shaft, through shared airspace, into assigned flight level.
	WaitingForDocking     = 6, ///< Loitering above the shaft, waiting to be assign a docking port for returning home.
	Docked                = 7, ///< Docked to base. Fuel tanks are no stockpile, and batteries on recharge.
	ReturningToShaft      = 8, ///< Traveling from base to point above shaft on a reserved flight level.
	AscendingInShaft      = 9, ///< Slowly ascending in the shaft after drilling. Waiting for permission to enter airspace above shaft.
	ChangingShaft        = 10, ///< Ascending from the shaft, through shafed airspace, into assigned flight level.
	Maintenance          = 11,
	//ForceFinish          = 12, (deprecated, now replaced by bRecalled)
	Takeoff              = 13, ///< Ascending from docking port, through shared airspace, into assigned flight level.
	ReturningHome        = 14, ///< Traveling from the point above the shaft to the base on a reserved flight level.
	Docking              = 15  ///< Descending to the docking port through shared airspace. (docking final approach)
}

public enum ShaftState { Planned, InProgress, Complete, Cancelled }

public enum ApckState
{
	Inert, Standby, Formation, DockingAwait, DockingFinal, Brake, CwpTask
}

StateWrapper stateWrapper;
public class StateWrapper
{
	public ɕ PState { get; private set; }

	public void ClearPersistentState()
	{
var currentState = PState;
PState = new ɕ();
PState.LifetimeAcceptedTasks = currentState.LifetimeAcceptedTasks;
PState.LifetimeOperationTime = currentState.LifetimeOperationTime;
PState.LifetimeWentToMaintenance = currentState.LifetimeWentToMaintenance;
PState.LifetimeOreAmount = currentState.LifetimeOreAmount;
PState.LifetimeYield = currentState.LifetimeYield;
	}

	Action<string> stateSaver;
	public StateWrapper(Action<string> stateSaver)
	{
this.stateSaver = stateSaver;
	}

	public void Save()
	{
try
{
	PState.ɔ(stateSaver);
}
catch (Exception ex)
{
	ś.Ő("State save failed.");
	ś.Ő(ex.ToString());
}
	}

	public bool TryLoad(string serialized)
	{
PState = new ɕ();
try
{
	PState.Load(serialized);
	return true;
}
catch (Exception ex)
{
	ś.Ő("State load failed.");
	ś.Ő(ex.ToString());
}
return false;
	}
}

public class ɕ
{
	public int LifetimeOperationTime = 0;
	public int LifetimeAcceptedTasks = 0;
	public int LifetimeWentToMaintenance = 0;
	public float LifetimeOreAmount = 0;
	public float LifetimeYield = 0;
	public bool bRecalled; ///< Has the agent been oredered to return to base?

	// cleared by clear-storage-state (task-dependent)
	public MinerState MinerState = MinerState.Idle;
	public Vector3D? miningPlaneNormal;
	public Vector3D? getAbovePt;       ///< Point above the current shaft. (Add echelon value to get intersection of shaft and assigned flight level.)
	public Vector3D? miningEntryPoint;
	public Vector3D? corePoint;
	public float? shaftRadius;

	/* Airspace geometry. */
	public Vector3D n_FL; ///< Assigned Flight level normal vector.
	public Vector3D p_FL; ///< Point on the assigned flight level.

	/* Job parameters. */
	public float maxDepth;
	public float skipDepth;
	public float leastDepth;

	public Vector3D? currentWp; ///< Current target waypoint for autopilot.
	public float? lastFoundOreDepth;
	public float CurrentJobMaxShaftYield;

	public float? minFoundOreDepth;
	public float? maxFoundOreDepth;
	public float? prevTickValCount = 0;

	public int? CurrentShaftId;
	public string CurrentTaskGroup;

	public string lastAPckCommand;
	// banned directions?

	T ParseValue<T>(Dictionary<string, string> values, string key)
	{
string res;
if (values.TryGetValue(key, out res) && !string.IsNullOrEmpty(res))
{
	if (typeof(T) == typeof(String))
		return (T)(object)res;
	else if (typeof(T) == typeof(bool))
		return (T)(object)bool.Parse(res);
	else if (typeof(T) == typeof(int))
		return (T)(object)int.Parse(res);
	else if (typeof(T) == typeof(int?))
		return (T)(object)int.Parse(res);
	else if (typeof(T) == typeof(float))
		return (T)(object)float.Parse(res);
	else if (typeof(T) == typeof(float?))
		return (T)(object)float.Parse(res);
	else if (typeof(T) == typeof(long?))
		return (T)(object)long.Parse(res);
	else if (typeof(T) == typeof(Vector3D))
	{
		var d = res.Split(':');
		return (T)(object)new Vector3D(double.Parse(d[0]), double.Parse(d[1]), double.Parse(d[2]));
	}
	else if (typeof(T) == typeof(Vector3D?))
	{
		var d = res.Split(':');
		return (T)(object)new Vector3D(double.Parse(d[0]), double.Parse(d[1]), double.Parse(d[2]));
	}
	else if (typeof(T) == typeof(List<byte>))
	{
		var d = res.Split(':');
		return (T)(object)d.Select(x => byte.Parse(x)).ToList();
	}
	else if (typeof(T) == typeof(MinerState))
	{
		return (T)Enum.Parse(typeof(MinerState), res);
	}
}
return default(T);
	}

	public ɕ Load(string storage)
	{
if (!string.IsNullOrEmpty(storage))
{
	ś.Ŕ(storage);

	var values = storage.Split('\n').ToDictionary(s => s.Split('=')[0], s => string.Join("=", s.Split('=').Skip(1)));

	LifetimeAcceptedTasks = ParseValue<int>(values, "LifetimeAcceptedTasks");
	LifetimeOperationTime = ParseValue<int>(values, "LifetimeOperationTime");
	LifetimeWentToMaintenance = ParseValue<int>(values, "LifetimeWentToMaintenance");
	LifetimeOreAmount = ParseValue<float>(values, "LifetimeOreAmount");
	LifetimeYield = ParseValue<float>(values, "LifetimeYield");
	bRecalled     = ParseValue<bool> (values, "bRecalled");

	MinerState = ParseValue<MinerState>(values, "MinerState");
	miningPlaneNormal = ParseValue<Vector3D?>(values, "miningPlaneNormal");
	getAbovePt = ParseValue<Vector3D?>(values, "getAbovePt");
	miningEntryPoint = ParseValue<Vector3D?>(values, "miningEntryPoint");
	corePoint = ParseValue<Vector3D?>(values, "corePoint");
	shaftRadius = ParseValue<float?>(values, "shaftRadius");

	/* Airspace geometry. */
	n_FL       = ParseValue<Vector3D>(values, "n_FL");
	p_FL       = ParseValue<Vector3D>(values, "p_FL");

	/* Job parameters. */
	maxDepth   = ParseValue<float>(values, "maxDepth");
	skipDepth  = ParseValue<float>(values, "skipDepth");
	leastDepth = ParseValue<float>(values, "leastDepth");
	Toggle.C.Set("adaptive-mining",           ParseValue<bool>(values, "adaptiveMode"));
	Toggle.C.Set("adjust-entry-by-elevation", ParseValue<bool>(values, "adjustAltitude"));

	currentWp = ParseValue<Vector3D?>(values, "currentWp");
	lastFoundOreDepth = ParseValue<float?>(values, "lastFoundOreDepth");
	CurrentJobMaxShaftYield = ParseValue<float>(values, "CurrentJobMaxShaftYield");

	minFoundOreDepth = ParseValue<float?>(values, "minFoundOreDepth");
	maxFoundOreDepth = ParseValue<float?>(values, "maxFoundOreDepth");

	CurrentShaftId = ParseValue<int?>(values, "CurrentShaftId");

	lastAPckCommand = ParseValue<string>(values, "lastAPckCommand");
}
return this;
	}
public void ɔ(Action<string>ɓ){ɓ(ɒ());}string ɒ(){string[]ɑ=new string[]{"LifetimeAcceptedTasks="+LifetimeAcceptedTasks,
"LifetimeOperationTime="+LifetimeOperationTime,"LifetimeWentToMaintenance="+LifetimeWentToMaintenance,"LifetimeOreAmount="+LifetimeOreAmount,
"LifetimeYield="+LifetimeYield,"bRecalled="+bRecalled,"MinerState="+MinerState,"miningPlaneNormal="+(miningPlaneNormal.HasValue?ͼ.ͻ(
miningPlaneNormal.Value):""),"getAbovePt="+(getAbovePt.HasValue?ͼ.ͻ(getAbovePt.Value):""),"miningEntryPoint="+(miningEntryPoint.HasValue?
ͼ.ͻ(miningEntryPoint.Value):""),"corePoint="+(corePoint.HasValue?ͼ.ͻ(corePoint.Value):""),"shaftRadius="+shaftRadius,
"n_FL="+ͼ.ͻ(n_FL),"p_FL="+ͼ.ͻ(p_FL),"maxDepth="+maxDepth,"skipDepth="+skipDepth,"leastDepth="+leastDepth,"adaptiveMode="+Toggle
.C.Check("adaptive-mining"),"adjustAltitude="+Toggle.C.Check("adjust-entry-by-elevation"),"currentWp="+(currentWp.
HasValue?ͼ.ͻ(currentWp.Value):""),"lastFoundOreDepth="+lastFoundOreDepth,"CurrentJobMaxShaftYield="+CurrentJobMaxShaftYield,
"minFoundOreDepth="+minFoundOreDepth,"maxFoundOreDepth="+maxFoundOreDepth,"CurrentShaftId="+CurrentShaftId??"","lastAPckCommand="+
lastAPckCommand};return string.Join("\n",ɑ);}public override string ToString(){return ɒ();}}void Save(){stateWrapper.Save();}Program(){
me=Me;Runtime.UpdateFrequency=UpdateFrequency.Update1;ʓ();}List<MyIGCMessage>ɬ=new List<MyIGCMessage>();void Main(string ɫ
,UpdateType ɪ){ɬ.Clear();while(IGC.UnicastListener.HasPendingMessage){ɬ.Add(IGC.UnicastListener.AcceptMessage());}var ɩ=
IGC.RegisterBroadcastListener("miners.command");if(ɩ.HasPendingMessage){var ɨ=ɩ.AcceptMessage();ɫ=ɨ.Data.ToString();ʃ(
"Got miners.command: "+ɫ);}ʊ(ɫ);foreach(var ɧ in ɬ){if(ɧ.Tag=="apck.ntv.update"){var ȡ=(MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,
Vector3D,MatrixD,BoundingBoxD>)ɧ.Data;var Ĉ=ȡ.Item1.Item1;Ǯ(Ĉ,ȡ);if(ɵ?.ɜ!=null){IGC.SendUnicastMessage(ɵ.ɜ.EntityId,
"apck.ntv.update",ȡ);}}else if(ɧ.Tag=="apck.depart.complete"){if(ɵ?.ɲ!=null)IGC.SendUnicastMessage(ɵ.ɲ.Value,"apck.depart.complete","");}
else if(ɧ.Tag=="apck.docking.approach"||ɧ.Tag=="apck.depart.approach"){if(ɵ?.ɜ!=null){IGC.SendUnicastMessage(ɵ.ɜ.EntityId,ɧ.
Tag,(ImmutableArray<Vector3D>)ɧ.Data);}else{if(ɧ.Tag.Contains("depart")){var Ŷ=new ƞ("fin",ĺ.Ĵ);Ŷ.ƙ=1;Ŷ.Ɲ=()=>IGC.
SendUnicastMessage(ɧ.Source,"apck.depart.complete","");ĺ.ů(Ŷ);}ĺ.ķ.Disconnect();}}}ś.Ŕ($"Version: {Ver}");ɵ.Ů(ɬ);ś.Ŕ("Min3r state: "+ɵ.ə()
);ś.Ŕ("Dispatcher: "+ɵ.ɲ);ś.Ŕ("HoldingLock: "+ɵ.ɱ);ś.Ŕ("WaitedSection: "+ɵ.ɰ);ś.Ŕ(
$"Estimated shaft radius: {Variables.Get<float>("circular-pattern-shaft-radius"):f2}");ś.Ŕ("LifetimeAcceptedTasks: "+stateWrapper.PState.LifetimeAcceptedTasks);ś.Ŕ("LifetimeOreAmount: "+ŏ(stateWrapper.
PState.LifetimeOreAmount));ś.Ŕ("LifetimeOperationTime: "+TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime).
ToString());ś.Ŕ("LifetimeWentToMaintenance: "+stateWrapper.PState.LifetimeWentToMaintenance);if(ĺ!=null){if(ĺ.ž.Ā!=Vector3D.Zero
)ƿ("agent-dest",ĺ.ž.Ā,"");if(ĺ.ž.þ!=Vector3D.Zero)ƿ("agent-vel",ĺ.ž.þ,ĺ.ž.ï);}if(Ń!=null){Ņ(
$"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");Ņ($"LifetimeOreAmount: {ŏ(stateWrapper.PState.LifetimeOreAmount)}");Ņ(
$"LifetimeOperationTime: {TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime)}");Ņ($"LifetimeWentToMaintenance: {stateWrapper.PState.LifetimeWentToMaintenance}");Ņ("\n");Ņ(
$"CurrentJobMaxShaftYield: {ŏ(stateWrapper.PState.CurrentJobMaxShaftYield)}");Ņ($"CurrentShaftYield: "+ɵ?.ɳ?.ʦ());Ņ(ɵ?.ɳ?.ToString());ł();}if(Toggle.C.Check("show-pstate"))ś.Ŕ(stateWrapper.PState.
ToString());Į();Ǭ();if(DbgIgc!=0)Ψ(DbgIgc);Dt=Math.Max(0.001,Runtime.TimeSinceLastRun.TotalSeconds);ś.ŗ+=Dt;ɦ=Math.Max(ɦ,Runtime
.CurrentInstructionCount);ś.Ŕ($"InstructionCount (Max): {Runtime.CurrentInstructionCount} ({ɦ})");ś.Ŕ(
$"Processed in {Runtime.LastRunTimeMs:f3} ms");}int ɦ;List<IMyCameraBlock>ɥ=new List<IMyCameraBlock>();Vector3D?ɤ;Vector3D?ɣ;void ɢ(string[]ɡ){var ɠ=ɥ.Where(A=>A.
IsActive).FirstOrDefault();if(ɠ!=null){ɠ.CustomData="";var ǖ=ɠ.GetPosition()+ɠ.WorldMatrix.Forward*Variables.Get<float>(
"ct-raycast-range");ɠ.CustomData+="GPS:dir0:"+ͼ.ͻ(ǖ)+":\n";ʃ(
$"RaycastTaskHandler tries to raycast point GPS:create-task base point:{ͼ.ͻ(ǖ)}:");if(ɠ.CanScan(ǖ)){var ɭ=ɠ.Raycast(ǖ);if(!ɭ.IsEmpty()){ɤ=ɭ.HitPosition.Value;ʃ(
$"GPS:Raycasted base point:{ͼ.ͻ(ɭ.HitPosition.Value)}:");ɠ.CustomData+="GPS:castedSurfacePoint:"+ͼ.ͻ(ɤ.Value)+":\n";IMyShipController ɿ=ɵ?.Ę;Vector3D ɾ;if((ɿ!=null)&&ɿ.
TryGetPlanetPosition(out ɾ)){ɣ=Vector3D.Normalize(ɾ-ɤ.Value);ś.Ő(
"Using mining-center-to-planet-center direction as a normal because we are in gravity");}else{var ɽ=ɤ.Value-ɠ.GetPosition();var ɼ=Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(ɽ));var ɻ=ɤ.Value+ɼ
*Math.Min(10,ɽ.Length());var ɺ=ɤ.Value+Vector3D.Normalize(Vector3D.Cross(ɼ,ɽ))*Math.Min(20,ɽ.Length());var ɹ=ɻ+Vector3D.
Normalize(ɻ-ɠ.GetPosition())*500;var ɸ=ɺ+Vector3D.Normalize(ɺ-ɠ.GetPosition())*500;ɠ.CustomData+="GPS:target1:"+ͼ.ͻ(ɹ)+":\n";if(ɠ
.CanScan(ɹ)){var ɷ=ɠ.Raycast(ɹ);if(!ɷ.IsEmpty()){ʃ($"GPS:Raycasted aux point 1:{ͼ.ͻ(ɷ.HitPosition.Value)}:");ɠ.CustomData
+="GPS:cast1:"+ͼ.ͻ(ɷ.HitPosition.Value)+":\n";ɠ.CustomData+="GPS:target2:"+ͼ.ͻ(ɸ)+":\n";if(ɠ.CanScan(ɸ)){var ɶ=ɠ.Raycast(ɸ
);if(!ɶ.IsEmpty()){ʃ($"GPS:Raycasted aux point 2:{ͼ.ͻ(ɶ.HitPosition.Value)}:");ɠ.CustomData+="GPS:cast2:"+ͼ.ͻ(ɶ.
HitPosition.Value)+":";ɣ=-Vector3D.Normalize(Vector3D.Cross(ɷ.HitPosition.Value-ɤ.Value,ɶ.HitPosition.Value-ɤ.Value));}}}}}if(ɣ.
HasValue&&ɤ.HasValue){ś.Ő("Successfully got mining center and mining normal");if(ɵ!=null){if(ɵ.ɲ.HasValue)IGC.SendUnicastMessage
(ɵ.ɲ.Value,"create-task",new MyTuple<float,Vector3D,Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),ɤ.
Value-ɣ.Value*10,ɣ.Value));}}else{ś.Ő($"RaycastTaskHandler failed to get castedNormal or castedSurfacePoint");}}}else{ś.Ő($"RaycastTaskHandler couldn't raycast initial position. Camera '{ɠ.CustomName}' had {ɠ.AvailableScanRange} AvailableScanRange"
);}}else{throw new Exception($"No active cam, {ɥ.Count} known");}}ɴ ɵ;class ɴ{č Ž;public χ ɳ{get;private set;}public long
?ɲ;public string ɱ="";public string ɰ="";public bool ɯ;public Vector3D ɮ(){if(!Ĺ.miningPlaneNormal.HasValue){var ɟ=Ę.
GetNaturalGravity();if(ɟ==Vector3D.Zero)throw new Exception("Need either natural gravity or miningPlaneNormal");else return Vector3D.
Normalize(ɟ);}return Ĺ.miningPlaneNormal.Value;}public MinerState ə(){return Ĺ.MinerState;}public ɕ Ĺ{get{return ɗ.PState;}}Func<
string,Ƚ>ɘ;StateWrapper ɗ;public ɴ(IMyGridTerminalSystem Ƈ,IMyIntergridCommunicationSystem Ė,StateWrapper ɗ,Func<string,Ƚ>Õ){ɘ
=Õ;this.Ƈ=Ƈ;ä=Ė;this.ɗ=ɗ;Ο=μ<IMyGyro>(D=>D.CustomName.Contains(ForwardGyroTag)&&D.IsSameConstructAs(me));Ę=μ<
IMyRemoteControl>(D=>D.IsSameConstructAs(me));ķ=μ<IMyShipConnector>(D=>D.IsSameConstructAs(me));Ƈ.GetBlocksOfType(Σ,Ý=>Ý.
IsSameConstructAs(me));Ƈ.GetBlocksOfType(Π,Ý=>Ý.IsSameConstructAs(me)&&Ý.HasInventory&&((Ý is IMyCargoContainer)||(Ý is IMyShipDrill)||(Ý
is IMyShipConnector)));Ƈ.GetBlocksOfType(ε,D=>D.IsSameConstructAs(me));Ƈ.GetBlocksOfType(γ,D=>D.IsSameConstructAs(me));
List<IMyTimerBlock>Ċ=new List<IMyTimerBlock>();Ƈ.GetBlocksOfType(Ċ,D=>D.IsSameConstructAs(me));Ž=new č(Ċ);float ɖ=0;float ǟ=
me.CubeGrid.GridSizeEnum==MyCubeSize.Large?2f:1.5f;foreach(var Ý in Σ){var ŷ=Vector3D.Reject(Ý.GetPosition()-Ο.GetPosition
(),Ο.WorldMatrix.Forward).Length();ɖ=(float)Math.Max(ŷ+ǟ,ɖ);}Variables.Set("circular-pattern-shaft-radius",ɖ);var Ë=new
List<IMyRadioAntenna>();Ƈ.GetBlocksOfType(Ë,D=>D.IsSameConstructAs(me));ē=Ë.FirstOrDefault();var ɞ=new List<IMyLightingBlock
>();Ƈ.GetBlocksOfType(ɞ,D=>D.IsSameConstructAs(me));β=ɞ.FirstOrDefault();Ƈ.GetBlocksOfType(α,D=>D.IsSameConstructAs(me));
}public void ɝ(IMyProgrammableBlock ɜ){this.ɜ=ɜ;}public void ɝ(Ŀ Ð){Ρ=Ð;}public MinerState ɛ{get;private set;}public void
Ğ(MinerState Ĝ){Ž.ĉ(ə()+".OnExit");ʃ("SetState: "+ə()+"=>"+Ĝ);Ž.ĉ(Ĝ+".OnEnter");ɛ=Ĺ.MinerState;Ĺ.MinerState=Ĝ;if((Ĝ==
MinerState.Disabled)||(Ĝ==MinerState.Idle)){Σ.ForEach(Ý=>Ý.Enabled=false);ϰ("command:pillock-mode:Inert",Ü=>Ü.Ğ(ApckState.Inert));
}}public void ɚ(){ϝ(1,1);ϰ("command:pillock-mode:Disabled",Ü=>Ü.ž.Ğ(ć.ċ.Ć));Σ.ForEach(Ý=>Ý.Enabled=false);ɗ.
ClearPersistentState();}public void Ø(string ĝ){MinerState Ĝ;if(Enum.TryParse(ĝ,out Ĝ))Ğ(Ĝ);}public ŗ μ<ŗ>(Func<IMyTerminalBlock,bool>λ)
where ŗ:class{var κ=new List<IMyTerminalBlock>();Ƈ.GetBlocksOfType(κ,D=>((D is ŗ)&&λ(D)));return κ.First()as ŗ;}public void ι
(){var ɟ=Ę.GetNaturalGravity();if(ɟ!=Vector3D.Zero)Ĺ.miningPlaneNormal=Vector3D.Normalize(ɟ);else Ĺ.miningPlaneNormal=Ο.
WorldMatrix.Forward;double ö;if(Ę.TryGetPlanetElevation(MyPlanetElevation.Surface,out ö))Ĺ.miningEntryPoint=Ο.WorldMatrix.
Translation+Ĺ.miningPlaneNormal.Value*(ö-5);else Ĺ.miningEntryPoint=Ο.WorldMatrix.Translation;if(ɲ.HasValue){ä.SendUnicastMessage(ɲ
.Value,"create-task",new MyTuple<float,Vector3D,Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),Ĺ.
miningEntryPoint.Value,Ĺ.miningPlaneNormal.Value));}}public void θ(){var ζ=α.FirstOrDefault(D=>!D.IsFunctional);var ʕ=new ƹ();ʕ.Ƹ=ä.Me;ʕ
.Ʒ=Ο.WorldMatrix;ʕ.Ƴ=Ę.GetShipVelocities().LinearVelocity;ʕ.ƶ=ό;ʕ.Ƶ=Variables.Get<float>("battery-low-factor");ʕ.ƺ=ϋ;ʕ.ƽ=
Variables.Get<float>("gas-low-factor");ʕ.ǃ=(ζ!=null?ζ.CustomName:"");ʕ.ğ=Ĺ.MinerState;ʕ.ǋ=ϐ;ʕ.Ǌ=Variables.Get<float>(
"cargo-full-factor");ʕ.ǉ=Toggle.C.Check("adaptive-mining");ʕ.ǈ=Ĺ.bRecalled;ʕ.Ǉ=ɳ!=null?ɳ.ʧ():0f;ʕ.ǆ=ɳ!=null?ɳ.ʪ.GetValueOrDefault(0f):0f;ʕ.
ǅ=ω;ʕ.Ĉ=me.CubeGrid.CustomName;ɳ?.ʖ(ʕ,Ĺ.MinerState);var ʅ=new MyTuple<string,MyTuple<MyTuple<long,string>,MyTuple<MatrixD
,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,
string>>>,string>();ʅ.Item1=Variables.Get<string>("group-constraint");ʅ.Item2=ʕ.Ǆ();ʅ.Item3=Ver;ʆ("miners.handshake",ʅ);}
public void Ů(List<MyIGCMessage>ɬ){Ϩ();ϥ();switch(Ĺ.MinerState){default:Ϗ();break;case MinerState.Disabled:case MinerState.
Idle:case MinerState.GoingToEntry:case MinerState.WaitingForDocking:case MinerState.ReturningToShaft:case MinerState.Takeoff
:case MinerState.ReturningHome:case MinerState.Docking:break;}ś.Ŕ(Ρ!=null?"Embedded APck":ɜ.CustomName);Ρ?.Ů(ʋ,ś.Ŕ);if((ɳ
!=null)&&(!ɯ)){if(ɲ.HasValue)ɳ.ʲ(Ĺ.MinerState);}var ƀ=ɳ;var η=ä.RegisterBroadcastListener("miners");foreach(var ɨ in ɬ){if
(!ɨ.Tag.Contains("set-vectors"))Φ(ɨ,false);if((ɨ.Tag=="miners.assign-shaft")&&(ɨ.Data is MyTuple<int,Vector3D,Vector3D,
MyTuple<float,float,float,bool,bool>>)){var ʅ=(MyTuple<int,Vector3D,Vector3D,MyTuple<float,float,float,bool,bool>>)ɨ.Data;if(ƀ
!=null){ƀ.ϕ(ʅ.Item1,ʅ.Item2,ʅ.Item3);Ĺ.maxDepth=ʅ.Item4.Item1;Ĺ.skipDepth=ʅ.Item4.Item2;Ĺ.leastDepth=ʅ.Item4.Item3;Toggle.
C.Set("adaptive-mining",ʅ.Item4.Item4);Toggle.C.Set("adjust-entry-by-elevation",ʅ.Item4.Item5);ʃ("Got new ShaftVectors");
ξ();}}if(ɨ.Tag=="miners.handshake.reply"){ʃ("Received reply from dispatcher "+ɨ.Source);ɲ=ɨ.Source;}if(ɨ.Tag==
"miners.normal"){var ς=(Vector3D)ɨ.Data;ʃ("Was assigned a normal of "+ς);Ĺ.miningPlaneNormal=ς;}if(ɨ.Tag=="miners.resume"){var ς=(
Vector3D)ɨ.Data;ʃ("Received resume command. Clearing state, running MineCommandHandler, assigned a normal of "+ς);ɗ.
ClearPersistentState();Ĺ.miningPlaneNormal=ς;ί();}if(ɨ.Tag=="command"){if(ɨ.Data.ToString()=="force-finish")δ();if(ɨ.Data.ToString()=="mine"
)ί();}if(ɨ.Tag=="set-value"){var Ǔ=((string)ɨ.Data).Split(':');ʃ($"Set value '{Ǔ[0]}' to '{Ǔ[1]}'");Variables.Set(Ǔ[0],Ǔ[
1]);}if(ɨ.Tag=="miners"){if(!(ɨ.Data is MyTuple<string,Vector3D,Vector3D>)){ʃ(
"Ignoring granted lock with malformed data.");continue;}var ʅ=(MyTuple<string,Vector3D,Vector3D>)ɨ.Data;Ĺ.p_FL=ʅ.Item2;Ĺ.n_FL=ʅ.Item3;if(!string.IsNullOrEmpty(ɱ)&&(
ɱ!=ʅ.Item1)){ʃ($"{ʅ.Item1} common-airspace-lock hides current ObtainedLock {ɱ}!");}ɱ=ʅ.Item1;ʃ(ʅ.Item1+
" common-airspace-lock-granted");if(ɰ==ʅ.Item1||(ɰ==LOCK_NAME_MiningSection&&ʅ.Item1==LOCK_NAME_GeneralSection))ξ();}if(ɨ.Tag=="report.request"){var ζ=
α.FirstOrDefault(D=>!D.IsFunctional);var ʕ=new ƹ();ʕ.Ƹ=ä.Me;ʕ.Ʒ=Ο.WorldMatrix;ʕ.Ƴ=Ę.GetShipVelocities().LinearVelocity;ʕ.
ƶ=ό;ʕ.Ƶ=Variables.Get<float>("battery-low-factor");ʕ.ƺ=ϋ;ʕ.ƽ=Variables.Get<float>("gas-low-factor");ʕ.ǃ=(ζ!=null?ζ.
CustomName:"");ʕ.ğ=Ĺ.MinerState;ʕ.ǋ=ϐ;ʕ.Ǌ=Variables.Get<float>("cargo-full-factor");ʕ.ǉ=Toggle.C.Check("adaptive-mining");ʕ.ǈ=Ĺ.
bRecalled;ʕ.Ǉ=ɳ!=null?ɳ.ʧ():0f;ʕ.ǆ=ɳ!=null?ɳ.ʪ.GetValueOrDefault(0f):0f;ʕ.ǅ=ω;ʕ.Ĉ=me.CubeGrid.CustomName;ɳ?.ʖ(ʕ,Ĺ.MinerState);ä.
SendBroadcastMessage("miners.report",ʕ.Ǆ());}}while(η.HasPendingMessage){var ɨ=η.AcceptMessage();Φ(ɨ,false);if(ɨ.Data!=null){if(ɨ.Data.
ToString().Contains("common-airspace-lock-released")){var Ϊ=ɨ.Data.ToString().Split(':')[1];ʃ(
"(Agent) received lock-released notification "+Ϊ+" from "+ɨ.Source);}if(ɨ.Data.ToString()=="dispatcher-change"){ɲ=null;Ĭ.Ī.Ľ(()=>!ɲ.HasValue).İ(1000).ŀ(()=>θ());}}}}
Queue<Action<ɴ>>ρ=new Queue<Action<ɴ>>();public void π(string Ϊ,Action<ɴ>ο){ɯ=true;if(!string.IsNullOrEmpty(Ϊ))ɰ=Ϊ;ρ.Enqueue(
ο);ʃ("WaitForDispatch section \""+Ϊ+"\", callback chain: "+ρ.Count);}public void ξ(){ɯ=false;ɰ="";var ν=ρ.Count;if(ν>0){ʃ
("Dispatching, callback chain: "+ν);var Ʊ=ρ.Dequeue();Ʊ.Invoke(this);}else ʃ("WARNING: empty Dispatch()");}public void ʆ<
ŗ>(string Ų,ŗ ʅ){ä.SendBroadcastMessage(Ų,ʅ,TransmissionDistance.TransmissionDistanceMax);Φ(ʅ,true);}public void Χ<ŗ>(
string Ų,ŗ ʅ){if(ɲ.HasValue)ä.SendUnicastMessage(ɲ.Value,Ų,ʅ);}public void ʃ(object ɨ){ś.Ő($"MinerController -> {ɨ}");}public
void Φ(object ɨ,bool Υ){string ʅ=ɨ.GetType().Name;if(ɨ is string)ʅ=(string)ɨ;else if((ɨ is ImmutableArray<Vector3D>)||(ɨ is
Vector3D))ʅ="some vector(s)";if(Toggle.C.Check("log-message")){if(!Υ)ś.Ő($"MinerController MSG-IN -> {ʅ}");else ś.Ő(
$"MinerController MSG-OUT -> {ʅ}");}}public Action Τ;public IMyProgrammableBlock ɜ;Ŀ Ρ;public IMyGridTerminalSystem Ƈ;public
IMyIntergridCommunicationSystem ä;public IMyRemoteControl Ę;public List<IMyTerminalBlock>Π=new List<IMyTerminalBlock>();public IMyTerminalBlock Ο;
public List<IMyShipDrill>Σ=new List<IMyShipDrill>();public IMyShipConnector ķ;public IMyRadioAntenna ē;public List<
IMyBatteryBlock>ε=new List<IMyBatteryBlock>();public List<IMyGasTank>γ=new List<IMyGasTank>();public IMyLightingBlock β;public List<
IMyTerminalBlock>α=new List<IMyTerminalBlock>();public void ΰ(){ɳ=new χ(this);ɳ.ʫ=DateTime.Now;}public void ί(){ɳ=new χ(this);ɳ.ʫ=
DateTime.Now;Ĺ.LifetimeAcceptedTasks++;if(!ά()){ɳ.ϖ();}}public void ή(){if(ɳ!=null){ɳ.ϗ();}}public void έ(){if(!ɲ.HasValue)
return;Ω(ɱ);Τ?.Invoke();ä.SendUnicastMessage(ɲ.Value,"apck.docking.request",ķ.GetPosition());Ğ(MinerState.WaitingForDocking);}
public void δ(){if(ķ.Status==MyShipConnectorStatus.Connected){ε.ForEach(D=>D.ChargeMode=ChargeMode.Recharge);γ.ForEach(D=>D.
Stockpile=true);Ĺ.lastAPckCommand="";Ğ(MinerState.Disabled);}else{Ĺ.bRecalled=true;if(ɯ&&ɰ=="")ξ();}}public bool ά(){if(ķ.Status
!=MyShipConnectorStatus.Connected)return false;if(Ĺ.getAbovePt.HasValue){Ğ(MinerState.Docked);return true;}Χ("request-new"
,"");π("",ʰ=>{ʰ.Ğ(MinerState.Docked);});return true;}public void Ϋ(string Ϊ,Action<ɴ>Ŭ){if(ɱ==Ϊ){Ŭ.Invoke(this);return;}ʆ
("miners","common-airspace-ask-for-lock:"+Ϊ);π(Ϊ,Ŭ);}public void Ω(string Ϊ){if(ɱ==Ϊ){ɱ=null;ʆ("miners",
"common-airspace-lock-released:"+Ϊ);ʃ($"Released lock: {Ϊ}");}else{ʃ("Tried to release non-owned lock section "+Ϊ);}}public ʏ σ;public void ϰ(string ľ,
Action<Ŀ>Ϣ=null){Ĺ.lastAPckCommand=ľ;if(Ρ!=null){if(Ϣ!=null){Ϣ(Ρ);}else{var ϡ=ľ.Split(new[]{"],["},StringSplitOptions.
RemoveEmptyEntries).Select(Í=>Í.Trim('[',']')).ToList();foreach(var ǩ in ϡ){string[]ʁ=ǩ.Split(new[]{':'},StringSplitOptions.
RemoveEmptyEntries);if(ʁ[0]=="command"){σ.ʍ(ʁ[1],ʁ);}}}}else{if(ä.IsEndpointReachable(ɜ.EntityId)){ä.SendUnicastMessage(ɜ.EntityId,
"apck.command",ľ);}else{throw new Exception($"APck {ɜ.EntityId} is not reachable");}}}DateTime Ϡ;bool ϟ(float Ϝ,float ϛ){var Ϟ=
DateTime.Now;if((Ϟ-Ϡ).TotalSeconds>60){Ϡ=Ϟ;return ϝ(Ϝ,ϛ);}return true;}bool ϝ(float Ϝ,float ϛ){α.ForEach(R=>ϯ(R,Ϛ(R),true));if(α
.Any(D=>!D.IsFunctional)){if(ē!=null)ē.CustomName=ē.CubeGrid.CustomName+"> Damaged. Fix me asap!";α.Where(D=>!D.
IsFunctional).ToList().ForEach(D=>ś.Ő($"{D.CustomName} is damaged or destroyed"));return false;}if(γ.Any()&&(ϋ<ϛ)){if(ē!=null)ē.
CustomName=$"{ē.CubeGrid.CustomName}> Maintenance. Gas level: {ϋ:f2}/{ϛ:f2}";return false;}if(ό<Ϝ){if(ē!=null)ē.CustomName=
$"{ē.CubeGrid.CustomName}> Maintenance. Charge level: {ό:f2}/{Ϝ:f2}";return false;}return true;}float Ϛ(IMyTerminalBlock ϣ){IMySlimBlock ϱ=ϣ.CubeGrid.GetCubeBlock(ϣ.Position);if(ϱ!=null)
return(ϱ.BuildIntegrity-ϱ.CurrentDamage)/ϱ.MaxIntegrity;else return 1f;}void ϯ(IMyTerminalBlock ϩ,float Ϯ,bool ϭ){string Ĉ=ϩ.
CustomName;if((Ϯ<1f)&&(!ϭ||!ϩ.IsFunctional)){if(!(ϩ is IMyRadioAntenna)&&!(ϩ is IMyBeacon)){ϩ.SetValue("ShowOnHUD",true);}string Ϭ
;if(Ĉ.Contains("||")){string ϫ=@"(?<=DAMAGED: )(?<label>\d+)(?=%)";System.Text.RegularExpressions.Regex ŷ=new System.Text
.RegularExpressions.Regex(ϫ);Ϭ=ŷ.Replace(Ĉ,delegate(System.Text.RegularExpressions.Match ɧ){return(Ϯ*100).ToString("F0");
});}else{Ϭ=string.Format("{0} || DAMAGED: {1}%",Ĉ,Ϯ.ToString("F0"));ʃ($"{Ĉ} was damaged. Showing on HUD.");}ϩ.CustomName=
Ϭ;}else{Ϫ(ϩ);}}void Ϫ(IMyTerminalBlock ϩ){if(ϩ.CustomName.Contains("||")){string Ĉ=ϩ.CustomName;ϩ.CustomName=Ĉ.Split('|')
[0].Trim();if(!(ϩ is IMyRadioAntenna)&&!(ϩ is IMyBeacon)){ϩ.SetValue("ShowOnHUD",false);}ʃ($"{ϩ.CustomName} was fixed.");
}}void Ϩ(){float ϧ=0;float Ϧ=0;foreach(var D in ε){Ϧ+=D.MaxStoredPower;ϧ+=D.CurrentStoredPower;}ό=(Ϧ>0?ϧ/Ϧ:1f);}void ϥ(){
float Ϥ=0;float ϙ=0;double ϑ=0;foreach(var D in γ){Ϥ+=D.Capacity*(float)D.FilledRatio;ϙ+=D.Capacity;ϑ+=D.FilledRatio;}ϋ=(ϙ>0?
Ϥ/ϙ:1f);}void Ϗ(){float ώ=0;float ύ=0;ϊ=0;for(int ǩ=0;ǩ<Π.Count;ǩ++){var Ƥ=Π[ǩ].GetInventory(0);if(Ƥ==null)continue;ώ+=(
float)Ƥ.MaxVolume;ύ+=(float)Ƥ.CurrentVolume;ϊ+=(float)Ƥ.CurrentMass;}ϐ=(ώ>0?ύ/ώ:1f);}float ό;float ϋ;float ϐ;float ϊ;bool ω;
bool ψ(){return ϐ>=Variables.Get<float>("cargo-full-factor");}public class χ{protected ɴ A;bool φ(double υ){if(A.Ĺ.currentWp
.HasValue){double τ=(A.Ĺ.currentWp.Value-A.Ο.WorldMatrix.Translation).Length();ś.Ŕ($"ds_WP: {τ:f2}");}return(!A.Ĺ.
currentWp.HasValue||(A.Ĺ.currentWp.Value-A.Ο.WorldMatrix.Translation).Length()<=υ);}public χ(ɴ ɵ){A=ɵ;}public void ϖ(){A.Χ(
"request-new","");A.π("",ʰ=>{A.Ϋ(LOCK_NAME_MiningSection,R=>{R.Ğ(MinerState.ChangingShaft);R.Σ.ForEach(Ý=>Ý.Enabled=false);var ʗ=-15;
var Ȑ=A.Ĺ.miningEntryPoint.Value+A.ɮ()*ʗ;var Ϙ=$"command:create-wp:Name=ChangingShaft,Ng=Forward,UpNormal=1;0;0,"+
$"AimNormal={ͼ.ͻ(A.ɮ()).Replace(':',';')}"+$":{ͼ.ͻ(Ȑ)}";A.ϰ(Ϙ);A.Ĺ.currentWp=Ȑ;});});}public void ϗ(){if(A.Ĺ.CurrentJobMaxShaftYield<ʡ+ʤ-ʢ)A.Ĺ.
CurrentJobMaxShaftYield=ʡ+ʤ-ʢ;if(Toggle.C.Check("adaptive-mining")){if(!ʪ.HasValue||((ʡ+ʤ-ʢ)/A.Ĺ.CurrentJobMaxShaftYield<0.5f)){A.Χ(
"ban-direction",A.Ĺ.CurrentShaftId.Value);}}ʠ();ʬ();ʪ=null;var Ȑ=ϒ();A.Χ("shaft-complete-request-new",A.Ĺ.CurrentShaftId.Value);A.π("",
ʰ=>{A.Ϋ(LOCK_NAME_MiningSection,R=>{R.Ğ(MinerState.ChangingShaft);R.Σ.ForEach(Ý=>Ý.Enabled=false);R.ϰ(
"command:create-wp:Name=ChangingShaft (Ascent),Ng=Forward:"+ͼ.ͻ(Ȑ));A.Ĺ.currentWp=Ȑ;});});}public void ϕ(int ʌ,Vector3D ϔ,Vector3D ϓ){A.Ĺ.miningEntryPoint=ϔ;A.Ĺ.getAbovePt=ϓ;A.Ĺ.
CurrentShaftId=ʌ;}public Vector3D ϒ(){double ř=Vector3D.Dot(A.ɮ(),A.Ĺ.n_FL);double Ý=Vector3D.Dot(A.Ĺ.p_FL-A.Ĺ.miningEntryPoint.Value,
A.Ĺ.n_FL)/ř;return A.Ĺ.miningEntryPoint.Value+A.ɮ()*Ý;}public Vector3D Ξ(Vector3D ʴ,Vector3D Μ){double ř=Vector3D.Dot(Μ,A
.Ĺ.n_FL);if(Math.Abs(ř)<1e-5){ś.Ŕ("Error: Docking axis and flight level are parallel. Cannot compute flight plan.");}
double Ý=Vector3D.Dot(A.Ĺ.p_FL-ʴ,A.Ĺ.n_FL)/ř;return ʴ+Μ*Ý;}public void ʲ(MinerState ğ){if(ğ==MinerState.GoingToEntry){if(A.Ĺ.
bRecalled){A.Ğ(MinerState.GoingToUnload);A.Σ.ForEach(Ý=>Ý.Enabled=false);var Ȑ=ϒ();A.ϰ(
"command:create-wp:Name=GoingToUnload,Ng=Forward:"+ͼ.ͻ(Ȑ));A.Ĺ.currentWp=Ȑ;return;}if(!φ(0.5f))return;A.Ω(A.ɱ);A.Σ.ForEach(Ý=>Ý.Enabled=true);A.Ğ(MinerState.Drilling);A.ϰ
("command:create-wp:Name=drill,Ng=Forward,PosDirectionOverride=Forward"+",AimNormal="+ͼ.ͻ(A.ɮ()).Replace(':',';')+
",UpNormal=1;0;0,SpeedLimit="+Variables.Get<float>("speed-drill")+":0:0:0");}else if(ğ==MinerState.Drilling){float ʳ=ʧ();ś.Ŕ(
$"Depth: current: {ʳ:f1} skip: {A.Ĺ.skipDepth:f1}");ś.Ŕ($"Depth: least: {A.Ĺ.leastDepth:f1} max: {A.Ĺ.maxDepth:f1}");ś.Ŕ($"Cargo: {A.ϐ:f2} / "+Variables.Get<float>(
"cargo-full-factor").ToString("f2"));if(A.Ĺ.bRecalled){ʘ();return;}if(ʳ>A.Ĺ.maxDepth){ʘ();return;}if(!A.ϟ(Variables.Get<float>(
"battery-low-factor"),Variables.Get<float>("gas-low-factor"))){ʘ();return;}if(A.ψ()){ʘ();return;}if(ʳ<=A.Ĺ.skipDepth){A.Σ.ForEach(Ý=>Ý.
UseConveyorSystem=false);return;}A.Σ.ForEach(Ý=>Ý.UseConveyorSystem=true);bool ʱ=ʠ();if(ʱ)ʪ=Math.Max(ʳ,ʪ??0);if(ʳ<=A.Ĺ.leastDepth)return;
if(ʱ){if((!ʩ.HasValue)||(ʩ>ʳ))ʩ=ʳ;if((!ʨ.HasValue)||(ʨ<ʳ))ʨ=ʳ;if(Toggle.C.Check("adaptive-mining")){A.Ĺ.skipDepth=ʩ.Value-
2f;A.Ĺ.maxDepth=ʨ.Value+2f;}}else{if(ʪ.HasValue&&(ʳ-ʪ>2)){ʘ();}}}if(ğ==MinerState.AscendingInShaft){if(!φ(0.5f))return;if(
A.Ĺ.bRecalled||A.ψ()||!A.ϝ(Variables.Get<float>("battery-low-factor"),Variables.Get<float>("gas-low-factor"))){A.Ϋ(
LOCK_NAME_MiningSection,ʰ=>{ʰ.Ğ(MinerState.GoingToUnload);ʰ.Σ.ForEach(Ý=>Ý.Enabled=false);var Ȑ=ϒ();ʰ.ϰ(
"command:create-wp:Name=GoingToUnload,Ng=Forward:"+ͼ.ͻ(Ȑ));A.Ĺ.currentWp=Ȑ;});}else ϗ();}else if(ğ==MinerState.ChangingShaft){if(!φ(0.5f))return;var Ȑ=ϒ();A.ϰ(
"command:create-wp:Name=ChangingShaft (Traverse),Ng=Forward:"+ͼ.ͻ(Ȑ));A.Ĺ.currentWp=Ȑ;A.Ğ(MinerState.ReturningToShaft);A.Ω(A.ɱ);}if(ğ==MinerState.Takeoff){if(!φ(1.0f))return;if(A.ɱ
!=null)A.Ω(A.ɱ);if(A.Ĺ.bRecalled){A.έ();return;}var ʯ=ϒ();A.ϰ("command:create-wp:Name=xy,Ng=Forward,AimNormal="+ͼ.ͻ(A.ɮ())
.Replace(':',';')+":"+ͼ.ͻ(ʯ));A.Ĺ.currentWp=ʯ;A.Ğ(MinerState.ReturningToShaft);return;}else if(ğ==MinerState.
ReturningToShaft){if(A.Ĺ.bRecalled){if(A.ɯ){A.ʆ("miners","common-airspace-ask-for-lock:");A.ɯ=false;A.ρ.Clear();}A.έ();return;}if(!φ(
1.0f))return;A.Ϋ(LOCK_NAME_MiningSection,ʰ=>{ʰ.Ğ(MinerState.GoingToEntry);A.Σ.ForEach(Ý=>Ý.Enabled=true);var ʷ=
$"command:create-wp:Name=drill entry,Ng=Forward,UpNormal=1;0;0,AimNormal="+$"{ͼ.ͻ(A.ɮ()).Replace(':',';')}:";double ö;if(Toggle.C.Check("adjust-entry-by-elevation")&&A.Ę.TryGetPlanetElevation(
MyPlanetElevation.Surface,out ö)){Vector3D Ţ;A.Ę.TryGetPlanetPosition(out Ţ);var ʻ=Vector3D.Normalize(A.Ĺ.miningEntryPoint.Value-Ţ);var ĭ
=(A.Ο.WorldMatrix.Translation-Ţ).Length()-ö+5f;var ʺ=Ţ+ʻ*ĭ;ʰ.ϰ(ʷ+ͼ.ͻ(ʺ));A.Ĺ.currentWp=ʺ;}else{ʰ.ϰ(ʷ+ͼ.ͻ(A.Ĺ.
miningEntryPoint.Value));A.Ĺ.currentWp=A.Ĺ.miningEntryPoint;}});}if(ğ==MinerState.GoingToUnload){if(φ(0.5f)){A.έ();}}else if(ğ==
MinerState.ReturningHome){if(!φ(1.0f))return;A.Ϋ(LOCK_NAME_BaseSection,ʰ=>{Ƚ ʹ=A.ɘ("docking");var ʸ=Ξ(ʹ.ȸ.Value,ʹ.ȶ.Value.Forward)
;A.ϰ("command:create-wp:Name=DynamicDock.echelon,Ng=Forward,AimNormal="+ͼ.ͻ(A.ɮ()).Replace(':',';')+
",TransformChannel=docking:"+ͼ.ͻ(Vector3D.Transform(ʸ,MatrixD.Invert(ʹ.ȶ.Value)))+":command:pillock-mode:DockingFinal");A.Ğ(MinerState.Docking);});}
else if(ğ==MinerState.WaitingForDocking){Ƚ ʹ=A.ɘ("docking");if(!ʹ.ȸ.HasValue)return;var ʸ=Ξ(ʹ.ȸ.Value,ʹ.ȶ.Value.Forward);A.ϰ
("command:create-wp:Name=xy,Ng=Forward,AimNormal="+ͼ.ͻ(A.ɮ()).Replace(':',';')+":"+ͼ.ͻ(ʸ));A.Ĺ.currentWp=ʸ;A.Ğ(MinerState
.ReturningHome);}if(ğ==MinerState.Docking){if(A.ķ.Status!=MyShipConnectorStatus.Connected)return;A.ϰ(
"command:pillock-mode:Disabled");A.Ę.DampenersOverride=false;A.ε.ForEach(D=>D.ChargeMode=ChargeMode.Recharge);A.ķ.OtherConnector.CustomData="";A.Τ?.
Invoke();A.γ.ForEach(D=>D.Stockpile=true);if(A.ɱ!=null)A.Ω(A.ɱ);A.Ğ(MinerState.Docked);}if(ğ==MinerState.Docked){ś.Ŕ(
"Docking: Connected");if(A.ω=!ʶ()){ś.Ŕ("Docking: still have items");return;}if(!A.ϝ(1f,1f)){A.Ğ(MinerState.Maintenance);A.Ĺ.
LifetimeWentToMaintenance++;Ĭ.Ī.İ(10000).Ľ(()=>A.ə()==MinerState.Maintenance).ŀ(()=>{if(A.ϝ(Variables.Get<float>("battery-full-factor"),0.99f)){A
.Ğ(MinerState.Docked);}});return;}if(A.Ĺ.bRecalled){A.Ğ(MinerState.Disabled);A.Ĺ.bRecalled=false;A.Ρ?.ŭ();ʥ();A.Ĺ.
LifetimeOperationTime+=(int)(DateTime.Now-ʫ).TotalSeconds;A.ɗ.Save();A.ɳ=null;return;}A.Ϋ(LOCK_NAME_BaseSection,ʰ=>{A.ε.ForEach(D=>D.
ChargeMode=ChargeMode.Auto);A.γ.ForEach(D=>D.Stockpile=false);ʥ();Ί(A.ķ.OtherConnector);A.ķ.Disconnect();A.Ğ(MinerState.Takeoff);}
);}else if(ğ==MinerState.Maintenance){if((A.ɛ!=MinerState.Docking)&&(A.ķ.Status==MyShipConnectorStatus.Connected)){A.ϰ(
"command:pillock-mode:Disabled");Ĭ.Ī.İ(10000).Ľ(()=>A.ə()==MinerState.Maintenance).ŀ(()=>{if(A.ϝ(Variables.Get<float>("battery-full-factor"),0.99f)){A.
Ğ(MinerState.Docked);}});}}}bool ʶ(){var ʵ=new List<IMyCargoContainer>();A.Ƈ.GetBlocksOfType(ʵ,D=>D.IsSameConstructAs(A.ķ
.OtherConnector)&&D.HasInventory&&D.IsFunctional&&(D is IMyCargoContainer));var ʮ=A.Π.Select(A=>A.GetInventory()).Where(ǩ
=>ǩ.ItemCount>0);if(!ʮ.Any())return true;ś.Ŕ("Docking: still have items");foreach(var ʝ in ʮ){var ʜ=new List<
MyInventoryItem>();ʝ.GetItems(ʜ);for(int ų=0;ų<ʜ.Count;ų++){var ʛ=ʜ[ų];IMyInventory ʚ;var ʙ=Variables.Get<string>("preferred-container"
);if(!string.IsNullOrEmpty(ʙ))ʚ=ʵ.Where(R=>R.CustomName.Contains(ʙ)).Select(A=>A.GetInventory()).FirstOrDefault();else ʚ=
ʵ.Select(A=>A.GetInventory()).Where(ǩ=>ǩ.CanItemsBeAdded((MyFixedPoint)(1f),ʛ.Type)).OrderBy(ǩ=>(float)ǩ.CurrentVolume).
FirstOrDefault();if(ʚ!=null){if(!ʝ.TransferItemTo(ʚ,ʜ[ų])){ś.Ŕ("Docking: failing to transfer from "+(ʝ.Owner as IMyTerminalBlock).
CustomName+" to "+(ʚ.Owner as IMyTerminalBlock).CustomName);}}}}return false;}void ʘ(){A.Ğ(MinerState.AscendingInShaft);var ʗ=Math
.Min(8,(A.Ο.WorldMatrix.Translation-A.Ĺ.miningEntryPoint.Value).Length());var Ȑ=A.Ĺ.miningEntryPoint.Value+A.ɮ()*ʗ;A.ϰ(
"command:create-wp:Name=AscendingInShaft,Ng=Forward"+",AimNormal="+ͼ.ͻ(A.ɮ()).Replace(':',';')+",UpNormal=1;0;0,SpeedLimit="+Variables.Get<float>("speed-clear")+":"+ͼ.ͻ(Ȑ))
;A.Ĺ.currentWp=Ȑ;}public void ʖ(ƹ ʕ,MinerState ğ){var D=ImmutableArray.CreateBuilder<MyTuple<string,string>>(10);D.Add(
new MyTuple<string,string>("Lock\nrequested",A.ɰ));D.Add(new MyTuple<string,string>("Lock\nowned",A.ɱ));ʕ.ǌ=D.
ToImmutableArray();}StringBuilder ʣ=new StringBuilder();public override string ToString(){ʣ.Clear();ʣ.AppendFormat(
"session uptime: {0}\n",(ʫ==default(DateTime)?"-":(DateTime.Now-ʫ).ToString()));ʣ.AppendFormat("session ore mass: {0}\n",ʭ);ʣ.AppendFormat(
"cargoFullness: {0:f2}\n",A.ϐ);ʣ.AppendFormat("cargoMass: {0:f2}\n",A.ϊ);ʣ.AppendFormat("cargoYield: {0:f2}\n",ʡ);ʣ.AppendFormat(
"lastFoundOreDepth: {0}\n",ʪ.HasValue?ʪ.Value.ToString("f2"):"-");ʣ.AppendFormat("minFoundOreDepth: {0}\n",ʩ.HasValue?ʩ.Value.ToString("f2"):"-");
ʣ.AppendFormat("maxFoundOreDepth: {0}\n",ʨ.HasValue?ʨ.Value.ToString("f2"):"-");ʣ.AppendFormat("shaft id: {0}\n",A.Ĺ.
CurrentShaftId??-1);return ʣ.ToString();}public float ʭ;public DateTime ʫ;public float?ʪ;float?ʩ{get{return A.Ĺ.minFoundOreDepth;}set{
A.Ĺ.minFoundOreDepth=value;}}float?ʨ{get{return A.Ĺ.maxFoundOreDepth;}set{A.Ĺ.maxFoundOreDepth=value;}}public float ʧ(){
if(A.Ĺ.MinerState==MinerState.Drilling||A.Ĺ.MinerState==MinerState.GoingToEntry||A.Ĺ.MinerState==MinerState.GoingToUnload
||A.Ĺ.MinerState==MinerState.AscendingInShaft||A.Ĺ.MinerState==MinerState.ChangingShaft)return(float)Vector3D.Dot(A.Ο.
WorldMatrix.Translation-A.Ĺ.miningEntryPoint.Value,A.Ĺ.miningPlaneNormal.Value);return 0f;}public float ʦ(){return ʡ+ʤ-ʢ;}void ʥ(){
ʭ+=A.ϊ;A.Ĺ.LifetimeOreAmount+=A.ϊ;A.Ĺ.LifetimeYield+=ʡ;ʤ+=ʡ-ʢ;ʢ=0;ʡ=0;}void ʬ(){ʢ=ʡ;ʤ=0;}float ʤ=0;float ʢ=0;float ʡ=0;
bool ʠ(){float ʟ=0;for(int ǩ=0;ǩ<A.Π.Count;++ǩ){var Ƥ=A.Π[ǩ].GetInventory(0);if(Ƥ==null)continue;List<MyInventoryItem>ʜ=new
List<MyInventoryItem>();Ƥ.GetItems(ʜ);ʜ.Where(ʞ=>ʞ.Type.ToString().Contains("Ore")&&!ʞ.Type.ToString().Contains("Stone")).
ToList().ForEach(R=>ʟ+=(float)R.Amount);}bool ʼ=((ʡ>0)&&(ʟ>ʡ));ʡ=ʟ;return ʼ;}void Ί(IMyShipConnector Έ){var Ά=Ξ(Έ.WorldMatrix.
Translation,Έ.WorldMatrix.Forward);var ͽ="[command:pillock-mode:Disabled],[command:create-wp:Name=Dock.Echelon,Ng=Forward:"+ͼ.ͻ(Ά)+
"]";A.ϰ(ͽ);A.Ĺ.currentWp=Ά;}}}static class ͼ{public static string ͻ(params Vector3D[]ͺ){return string.Join(":",ͺ.Select(Ƴ=>
string.Format("{0}:{1}:{2}",Ƴ.X,Ƴ.Y,Ƴ.Z)));}public static string Ή(MatrixD ͷ){StringBuilder ʣ=new StringBuilder();for(int ǩ=0;
ǩ<4;ǩ++){for(int ƀ=0;ƀ<4;ƀ++){ʣ.Append(ͷ[ǩ,ƀ]+":");}}return ʣ.ToString().TrimEnd(':');}public static Vector3D Ͷ(MatrixD ʹ
,Vector3D ͳ,BoundingSphereD Ͳ){RayD ŷ=new RayD(ʹ.Translation,Vector3D.Normalize(ͳ-ʹ.Translation));double?ͱ=ŷ.Intersects(Ͳ
);if(ͱ.HasValue){var Ό=Vector3D.Normalize(Ͳ.Center-ʹ.Translation);if(Ͳ.Contains(ʹ.Translation)==ContainmentType.Contains)
return Ͳ.Center-Ό*Ͳ.Radius;var Ν=Vector3D.Cross(ŷ.Direction,Ό);Vector3D Λ;if(Ν.Length()<double.Epsilon)Ό.
CalculatePerpendicularVector(out Λ);else Λ=Vector3D.Cross(Ν,-Ό);return Ͳ.Center+Vector3D.Normalize(Λ)*Ͳ.Radius;}return ͳ;}public static Vector3D Κ(
Vector3D Ι,MatrixD Θ,MatrixD Η,Vector3D Ζ,ref Vector3D Ε){var ǔ=Θ.Up;var Δ=Vector3D.ProjectOnPlane(ref Ι,ref ǔ);var Γ=-(float)
Math.Atan2(Vector3D.Dot(Vector3D.Cross(Θ.Forward,Δ),ǔ),Vector3D.Dot(Θ.Forward,Δ));ǔ=Θ.Right;Δ=Vector3D.ProjectOnPlane(ref Ι,
ref ǔ);var Β=-(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(Θ.Forward,Δ),ǔ),Vector3D.Dot(Θ.Forward,Δ));float Α=0;if((Ζ!=
Vector3D.Zero)&&(Math.Abs(Γ)<.05f)&&(Math.Abs(Β)<.05f)){ǔ=Θ.Forward;Δ=Vector3D.ProjectOnPlane(ref Ζ,ref ǔ);Α=-(float)Math.Atan2(
Vector3D.Dot(Vector3D.Cross(Θ.Down,Δ),ǔ),Vector3D.Dot(Θ.Up,Δ));}var ΐ=new Vector3D(Β,Γ,Α*Variables.Get<float>(
"roll-power-factor"));Ε=new Vector3D(Math.Abs(ΐ.X),Math.Abs(ΐ.Y),Math.Abs(ΐ.Z));var Ώ=Vector3D.TransformNormal(ΐ,Θ);var Ʊ=Vector3D.
TransformNormal(Ώ,MatrixD.Transpose(Η));Ʊ.X*=-1;return Ʊ;}public static void Ύ(IMyGyro ˀ,Vector3 ʿ,Vector3D ˈ){float ˎ=ʿ.Y;float ʽ=ʿ.X;
float ˌ=ʿ.Z;var ˋ=IsLargeGrid?30:60;var ˊ=new Vector3D(1.92f,1.92f,1.92f);Func<double,double,double,double>ˉ=(R,Ƴ,Ʊ)=>{var ˍ=
Math.Abs(R);double ŷ;if(ˍ>(Ƴ*Ƴ*1.7)/(2*Ʊ))ŷ=ˋ*Math.Sign(R)*Math.Max(Math.Min(ˍ,1),0.002);else{ŷ=-ˋ*Math.Sign(R)*Math.Max(
Math.Min(ˍ,1),0.002);}return ŷ*0.6;};var ˇ=(float)ˉ(ˎ,ˈ.Y,ˊ.Y);var ˆ=(float)ˉ(ʽ,ˈ.X,ˊ.X);var ˁ=(float)ˉ(ˌ,ˈ.Z,ˊ.Z);ˀ.
SetValue("Pitch",ˆ);ˀ.SetValue("Yaw",ˇ);ˀ.SetValue("Roll",ˁ);}public static void ƴ(IMyGyro ˀ,Vector3 ʿ,Vector3D ʾ,Vector3D ˈ){if
(Variables.Get<bool>("amp")){var ˏ=5f;var ˣ=2f;Func<double,double,double>Ͱ=(R,Ý)=>R*(Math.Exp(-Ý*ˣ)+0.8)*ˏ/2;ʾ/=Math.PI/
180f;if((ʾ.X<2)&&(ʿ.X>0.017))ʿ.X=(float)Ͱ(ʿ.X,ʾ.X);if((ʾ.Y<2)&&(ʿ.Y>0.017))ʿ.Y=(float)Ͱ(ʿ.Y,ʾ.Y);if((ʾ.Z<2)&&(ʿ.Z>0.017))ʿ.Z
=(float)Ͱ(ʿ.Z,ʾ.Z);}Ύ(ˀ,ʿ,ˈ);}public static Vector3D ˢ(Vector3D ˬ,Vector3D ˤ,Vector3D ǿ,Vector3D ȁ,Vector3D ˮ,bool ː){
double ˑ=Vector3D.Dot(Vector3D.Normalize(ǿ-ˬ),ˮ);if(ˑ<30)ˑ=30;return ˢ(ˬ,ˤ,ǿ,ȁ,ˑ,ː);}public static Vector3D ˢ(Vector3D ˡ,
Vector3D ˠ,Vector3D ǿ,Vector3D ȁ,double ˑ,bool ː){double ɐ=Vector3D.Distance(ˡ,ǿ);Vector3D Ŏ=ǿ-ˡ;Vector3D ó=Vector3D.Normalize(Ŏ
);Vector3D Ō=ǿ;Vector3D ŋ;if(ː){var Ŋ=Vector3D.Reject(ˠ,ó);ȁ-=Ŋ;}if(ȁ.Length()>float.Epsilon){ŋ=Vector3D.Normalize(ȁ);var
ŉ=Math.PI-Math.Acos(Vector3D.Dot(ó,ŋ));var ň=(ȁ.Length()*Math.Sin(ŉ))/ˑ;if(Math.Abs(ň)<=1){var Ň=Math.Asin(ň);var Í=ɐ*
Math.Sin(Ň)/Math.Sin(ŉ+Ň);Ō=ǿ+ŋ*Í;}}return Ō;}public static string ō(string Ĉ,Vector3D Ì,Color A){return
$"GPS:{Ĉ}:{Ì.X}:{Ì.Y}:{Ì.Z}:#{A.R:X02}{A.G:X02}{A.B:X02}:";}}StringBuilder ņ=new StringBuilder();void Ņ(string ń){if(Ń!=null){ņ.AppendLine(ń);}}IMyTextPanel Ń;void ł(){if(ņ.
Length>0){var Í=ņ.ToString();ņ.Clear();Ń?.WriteText(Í);}}string ŏ(float Ŗ,string Ş=""){string Ŝ;if(Math.Abs(Ŗ)>=1000000){if(!
string.IsNullOrEmpty(Ş))Ŝ=string.Format("{0:0.##} M{1}",Ŗ/1000000,Ş);else Ŝ=string.Format("{0:0.##}M",Ŗ/1000000);}else if(Math
.Abs(Ŗ)>=1000){if(!string.IsNullOrEmpty(Ş))Ŝ=string.Format("{0:0.##} k{1}",Ŗ/1000,Ş);else Ŝ=string.Format("{0:0.##}k",Ŗ/
1000);}else{if(!string.IsNullOrEmpty(Ş))Ŝ=string.Format("{0:0.##} {1}",Ŗ,Ş);else Ŝ=string.Format("{0:0.##}",Ŗ);}return Ŝ;}
static class ś{static string Ś="";static Action<string>ř;static IMyTextSurface Ì;static IMyTextSurface Ř;public static double
ŗ;public static void ŝ(Action<string>û,IMyGridTerminalSystem ŕ){ř=û;Ì=me.GetSurface(0);Ì.ContentType=ContentType.
TEXT_AND_IMAGE;Ì.WriteText("");}public static void Ŕ(string Í){if((Ś=="")||(Í.Contains(Ś)))ř(Í);}static string œ="";public static void
Œ(string Í){œ+=Í+"\n";}static List<string>ő=new List<string>();public static void Ő(string Í){Ì.WriteText(
$"{ŗ:f2}: {Í}\n",true);if(Ř!=null){ő.Add(Í);}}public static void Ł(IMyTextSurface Í){Ř=Í;}public static void Į(){if(!string.
IsNullOrEmpty(œ)){var ĭ=Ǝ.ſ.Where(R=>R.IsUnderControl).FirstOrDefault()as IMyTextSurfaceProvider;if((ĭ!=null)&&(ĭ.SurfaceCount>0))ĭ.
GetSurface(0).WriteText(œ);œ="";}if(ő.Any()){if(Ř!=null){ő.Reverse();var Ê=string.Join("\n",ő)+"\n"+Ř.GetText();var Ü=Variables.
Get<int>("logger-char-limit");if(Ê.Length>Ü)Ê=Ê.Substring(0,Ü-1);Ř.WriteText($"{ŗ:f2}: {Ê}");}ő.Clear();}}}class Ĭ{static Ĭ
ī=new Ĭ();Ĭ(){}public static Ĭ Ī{get{ī.ģ=0;ī.Ħ=null;return ī;}}class ĩ{public DateTime Ĩ;public Action ħ;public Func<bool
>Ħ;public long ĥ;}Queue<ĩ>Ĥ=new Queue<ĩ>();long ģ;Func<bool>Ħ;public Ĭ İ(int ĸ){this.ģ+=ĸ;return this;}public Ĭ ŀ(Action
ľ){Ĥ.Enqueue(new ĩ{Ĩ=DateTime.Now.AddMilliseconds(ģ),ħ=ľ,Ħ=Ħ,ĥ=ģ});return this;}public Ĭ Ľ(Func<bool>Ħ){this.Ħ=Ħ;return
this;}public void ļ(){if(Ĥ.Count>0){ś.Ŕ("Scheduled actions count:"+Ĥ.Count);var A=Ĥ.Peek();if(A.Ĩ<DateTime.Now){if(A.Ħ!=null
){if(A.Ħ.Invoke()){A.ħ.Invoke();A.Ĩ=DateTime.Now.AddMilliseconds(A.ĥ);}else{Ĥ.Dequeue();}}else{A.ħ.Invoke();Ĥ.Dequeue();}
}}}public void Ļ(){Ĥ.Clear();ģ=0;Ħ=null;}}Ŀ ĺ;class Ŀ{public ɕ Ĺ;public IMyShipConnector ķ;public List<IMyWarhead>Ķ;
IMyRadioAntenna ē;Ó ĵ;public Æ Ĵ;public Func<string,Ƚ>ĳ;public IMyGyro Ĳ;public IMyGridTerminalSystem į;public
IMyIntergridCommunicationSystem ı;public IMyRemoteControl ş;public ć ž;public č Ž;public IMyProgrammableBlock ż;public IMyProgrammableBlock Ż;HashSet<
IMyTerminalBlock>ź=new HashSet<IMyTerminalBlock>();ŗ Ź<ŗ>(string Ĉ,List<IMyTerminalBlock>Ŵ,bool Ÿ=false)where ŗ:class,IMyTerminalBlock{ŗ
ŷ;ś.Ŕ("Looking for "+Ĉ);var Ŷ=Ŵ.Where(D=>D is ŗ&&D.CustomName.Contains(Ĉ)).Cast<ŗ>().ToList();ŷ=Ÿ?Ŷ.Single():Ŷ.
FirstOrDefault();if(ŷ!=null)ź.Add(ŷ);return ŷ;}List<ŗ>ŵ<ŗ>(List<IMyTerminalBlock>Ŵ,string ų=null)where ŗ:class,IMyTerminalBlock{var Ŷ=
Ŵ.Where(D=>D is ŗ&&((ų==null)||(D.CustomName==ų))).Cast<ŗ>().ToList();foreach(var D in Ŷ)ź.Add(D);return Ŷ;}public Ŀ(ɕ ƈ,
IMyGridTerminalSystem Ƈ,IMyIntergridCommunicationSystem Ė,Func<string,Ƚ>Ɔ){Ĺ=ƈ;į=Ƈ;ĳ=Ɔ;ı=Ė;Func<IMyTerminalBlock,bool>Ŷ=D=>D.
IsSameConstructAs(me);var ƅ=new List<IMyTerminalBlock>();Ƈ.GetBlocks(ƅ);ƅ=ƅ.Where(D=>Ŷ(D)).ToList();Ƅ(ƅ);}public void Ƅ(List<
IMyTerminalBlock>ƃ){var Ŷ=ƃ;ś.Ő("subset: "+ƃ.Count);ż=Ź<IMyProgrammableBlock>("a-thrust-provider",Ŷ);var Ƃ=ŵ<IMyMotorStator>(Ŷ);var Ɓ=
new List<IMyProgrammableBlock>();į.GetBlocksOfType(Ɓ,ƀ=>Ƃ.Any(R=>(R.Top!=null)&&R.Top.CubeGrid==ƀ.CubeGrid));Ż=Ź<
IMyProgrammableBlock>("a-tgp",Ŷ)??Ɓ.FirstOrDefault(R=>R.CustomName.Contains("a-tgp"));Ĳ=Ź<IMyGyro>(ForwardGyroTag,Ŷ,true);var ſ=ŵ<
IMyShipController>(Ŷ);Ǝ.ŝ(ſ);ē=ŵ<IMyRadioAntenna>(Ŷ).FirstOrDefault();ķ=ŵ<IMyShipConnector>(Ŷ).First();Ķ=ŵ<IMyWarhead>(Ŷ);ş=ŵ<
IMyRemoteControl>(Ŷ).First();ş.CustomData="";var Ē=ŵ<IMyTimerBlock>(Ŷ);Ž=new č(Ē);Ŧ=new List<IMyTerminalBlock>();Ŧ.AddRange(ŵ<IMyThrust>
(Ŷ));Ŧ.AddRange(ŵ<IMyArtificialMassBlock>(Ŷ));string Ų=Variables.Get<string>("ggen-tag");if(!string.IsNullOrEmpty(Ų)){var
ŕ=new List<IMyGravityGenerator>();var Š=į.GetBlockGroupWithName(Ų);if(Š!=null)Š.GetBlocksOfType(ŕ,D=>Ŷ.Contains(D));
foreach(var D in ŕ)ź.Add(D);Ŧ.AddRange(ŕ);}else Ŧ.AddRange(ŵ<IMyGravityGenerator>(Ŷ));ž=new ć(ş,Ž,ı,ż,Ĳ,ē,Ť,this,Ŧ.Count>5);ĵ=
new Ó(this,ĳ);Ğ(ApckState.Standby);ž.Ğ(ć.ċ.Ģ);}List<IMyTerminalBlock>Ŧ;ț Č;int ť;public ț Ť(){if(Č==null)Č=new ț(Ĳ,Ŧ);else
if((ũ!=ť)&&(ũ%60==0)){ť=ũ;if(Ŧ.Any(R=>!R.IsFunctional)){Ŧ.RemoveAll(R=>!R.IsFunctional);Č=new ț(Ĳ,Ŧ);}}if(Ŧ.Any(R=>R is
IMyThrust&&(R as IMyThrust)?.MaxEffectiveThrust!=(R as IMyThrust)?.MaxThrust))Č.Ȫ();return Č;}public Vector3D?ţ;public Vector3D?Ţ
;public bool š(){if(ž.é!=null){if(Ţ==null){Vector3D õ;if(ş.TryGetPlanetPosition(out õ)){Ţ=õ;return true;}}return Ţ.
HasValue;}return false;}public void Ø(string ĝ){ApckState Í;if(Enum.TryParse(ĝ,out Í)){Ğ(Í);}}public void Ğ(ApckState Í){if(ĵ.Ø(
Í)){Ĵ=ĵ.Ù();ŭ();}}public void ŭ(){if(ē!=null)ē.CustomName=$"{Ĳ.CubeGrid.CustomName}"+(Ĺ.bRecalled?" [recalled]":"")+
$"> {ĵ.O().L} / {Ū?.Value?.Å}";}public void ű(ApckState F,Æ D){ĵ.Û(F,D);}public Æ Ű(ApckState F){return ĵ.P(F).H;}public void ů(ƞ Ê){ś.Ő("CreateWP "+Ê
.Å);Ɖ(Ê);}public void Ů(int ũ,Action<string>ř){this.ũ=ũ;Ũ();var Ŭ=Ū?.Value;Æ Ú;if((Ŭ!=null)&&(ĵ.O().L==Ŭ.Ƙ))Ú=Ŭ.H??Ű(Ŭ.Ƙ)
;else Ú=Ĵ;ž.ü(ũ,ř,Ú);}LinkedList<ƞ>ū=new LinkedList<ƞ>();LinkedListNode<ƞ>Ū;int ũ;void Ũ(){if(Ū!=null){var Ê=Ū.Value;if(Ê
.ǎ(ũ,ž)){ś.Ő($"TFin {Ê.Å}");ġ();}}}public ƞ ŧ(){return Ū?.Value;}public void Ɖ(ƞ Ê){ū.AddFirst(Ê);ś.Ő(
$"Added {Ê.Å}, total: {ū.Count}");Ū=ū.First;if(Ū.Next==null)Ū.Value.Ɩ=ĵ.O().L;Ê.ŝ(ž,ũ);Ğ(Ê.Ƙ);}public void ġ(){var Ô=Ū;var A=Ô.Value;A.Ɲ?.Invoke();if(Ô.
Next!=null){A=Ô.Next.Value;Ū=Ô.Next;A.ŝ(ž,ũ);ū.Remove(Ô);}else{Ū=null;ū.Clear();Ğ(Ô.Value.Ɩ);}}}class Ó{Ŀ Ò;Dictionary<
ApckState,S>Ñ=new Dictionary<ApckState,S>();public Ó(Ŀ Ð,Func<string,Ƚ>Õ){Ò=Ð;var Ï=Ò.ž;var Î=new Æ{Å="Default"};foreach(var Í in
Enum.GetValues(typeof(ApckState))){Ñ.Add((ApckState)Í,new S((ApckState)Í,Î));}Ñ[ApckState.Standby].H=new Æ{Å="Standby"};N=Ñ[
ApckState.Standby];Ñ[ApckState.Formation].H=new Æ{Å="follow formation",Ä=false,Đ=()=>Õ("wingman"),ª=(w)=>Ï.Ă.GetPosition()+Õ(
"wingman").ȶ.Value.Forward*5000,Ã=Ì=>{var Ë=new BoundingSphereD(Õ("wingman").ȶ.Value.Translation,30);return ͼ.Ͷ(Ï.Ă.WorldMatrix,Ì
,Ë);},Z=()=>Ï.Ă.GetPosition()};Ñ[ApckState.Brake].H=new Æ{Å="reverse",à=true,Ã=w=>Ï.ǰ(-150),ª=(w)=>Ï.ǰ(1),W=true};Ñ[
ApckState.DockingAwait].H=new Æ{Å="awaiting docking",Ä=false,à=true,Đ=()=>Õ("wingman"),ª=w=>Ï.Ă.GetPosition()+Ï.Ă.WorldMatrix.
Forward,Ã=w=>Õ("wingman").ȸ.HasValue?Õ("wingman").ȸ.Value:Ï.Ă.GetPosition(),È=(Ý,ß,A,Þ,Ü)=>{if(Õ("docking").ȸ.HasValue&&(Ò.ķ!=
null)){Ü.Ğ(ApckState.DockingFinal);}}};Ñ[ApckState.DockingFinal].H=new Æ{ª=w=>Ò.ķ.GetPosition()-Õ("docking").ȶ.Value.Forward
*10000,Ã=Ì=>Ì+Õ("docking").ȶ.Value.Forward*(IsLargeGrid?1.25f:0.5f),o=()=>Ò.ķ.WorldMatrix,Z=()=>Ò.ķ.GetPosition(),Đ=()=>Õ
("docking"),Ä=false,À=()=>Ï.ê,È=(Ý,ß,A,Þ,Ü)=>{if((Ý<20)&&(A.í.Length()<0.8)&&(Ò.ķ!=null)){Ò.ķ.Connect();if(Ò.ķ.Status==
MyShipConnectorStatus.Connected){Ü.Ğ(ApckState.Inert);Ò.ķ.OtherConnector.CustomData="";A.æ.DampenersOverride=false;}}}};Ñ[ApckState.Inert].I=
Í=>Ð.ž.Ğ(ć.ċ.đ);Ñ[ApckState.Inert].J=Í=>Ð.ž.Ğ(ć.ċ.Ģ);}public void Û(ApckState F,Æ Ú){Ñ[F].H=Ú;}public Æ Ù(){return N.H;}
public bool Ø(ApckState F){if(F==N.L)return true;var Ê=P(F);if(Ê!=null){var A=M.FirstOrDefault(R=>R.º.L==N.L||R.É.L==N.L||R.É.
L==F||R.º.L==F);if(A!=null){if(!(A.º==N&&A.É==Ê)){return false;}}var Q=N;ś.Ő($"{Q.L} -> {Ê.L}");N=Ê;Q.J?.Invoke(N.L);A?.Ç
?.Invoke();Ê.I?.Invoke(Ê.L);return true;}return false;}public S P(ApckState F){return Ñ[F];}public S O(){return N;}S N;
List<V>M=new List<V>();public class S{public ApckState L;public Action<ApckState>J;public Action<ApckState>I;public Æ H;
public S(ApckState F,Æ D,Action<ApckState>B=null,Action<ApckState>K=null){H=D;L=F;J=B;I=K;}}public class V{public S º;public S
É;public Action Ç;}}class Æ{public string Å="Default";public bool Ä=true;public Func<Vector3D,Vector3D>Ã{get;set;}public
Func<Vector3D,Vector3D>Â{get;set;}public Func<Vector3D>Á{get;set;}public Action<double,double,ć,Æ,Ŀ>È{get;set;}public Func<
Vector3D>À;public bool µ=false;public Func<Vector3D,Vector3D>ª=(w)=>w;public Func<MatrixD>o;public Func<Vector3D>Z;public bool Y
;public float?X;public bool U=false;public bool W=false;public bool à;public Func<Ƚ>Đ;public static Æ ď(Vector3D Ì,string
Ĉ,Func<Vector3D>Ď=null){return new Æ(){Å=Ĉ,à=true,Ã=R=>Ì,ª=R=>Ď?.Invoke()??Ì};}}class č{Dictionary<string,IMyTimerBlock>Ċ
=new Dictionary<string,IMyTimerBlock>();List<IMyTimerBlock>Č;public č(List<IMyTimerBlock>Ċ){Č=Ċ;}public bool ĉ(string Ĉ){
IMyTimerBlock D;if(!Ċ.TryGetValue(Ĉ,out D)){D=Č.FirstOrDefault(A=>A.CustomName.Contains(Ĉ));if(D!=null)Ċ.Add(Ĉ,D);else return false;}
D.GetActionWithName("TriggerNow").Apply(D);return true;}}class ć{public enum ċ{Ć=0,đ,Ģ}public bool Ġ;ċ ğ=ċ.Ć;public void
Ğ(ċ Ĝ){if(Ĝ==ċ.Ģ)ě();else if(Ĝ==ċ.đ)Ě(false);else if(Ĝ==ċ.Ć)Ě();ğ=Ĝ;}public void Ø(string ĝ){ċ Ĝ;if(Enum.TryParse(ĝ,out Ĝ
))Ğ(Ĝ);}void ě(){æ.DampenersOverride=false;}void Ě(bool ę=true){ú.GyroOverride=false;â().ȯ();æ.DampenersOverride=ę;}
public ć(IMyRemoteControl Ę,č ė,IMyIntergridCommunicationSystem Ė,IMyProgrammableBlock ĕ,IMyGyro Ĕ,IMyTerminalBlock ē,Func<ț>Ē
,Ŀ ą,bool ñ){æ=Ę;ä=Ė;ú=Ĕ;Ą=ē;å=ė;ã=ĕ;â=Ē;Ü=ą;Ġ=ñ;}Vector3D á;public string ï;public Vector3D î{get;private set;}public
Vector3D í{get;private set;}Vector3D ì=Vector3D.Zero;public double ë{get;private set;}public Æ H{get;private set;}public
Vector3D ê{get{return æ.GetShipVelocities().LinearVelocity;}}Vector3D?ð;public Vector3D?é{get{return(ð!=Vector3D.Zero)?ð:null;}}
Vector3D è{get;set;}public Vector3D ç{get;set;}public IMyRemoteControl æ;public č å{get;private set;}
IMyIntergridCommunicationSystem ä;IMyProgrammableBlock ã;Func<ț>â;Ŀ Ü;int ò;IMyGyro ú;IMyTerminalBlock Ą;public IMyTerminalBlock Ă{get{return ú;}}
public Vector3D ā;public Vector3D Ā;public Vector3D ÿ;public Vector3D þ;public Vector3D ý;public void ü(int ă,Action<string>û,
Æ Ú){var ù=ă-ò;ò=ă;if(ù>0)ç=(ê-è)*60f/ù;è=ê;ð=æ.GetNaturalGravity();H=Ú;MyPlanetElevation ø=new MyPlanetElevation();
double ö;æ.TryGetPlanetElevation(ø,out ö);Vector3D õ;æ.TryGetPlanetPosition(out õ);Func<Vector3D>ô=null;bool Y=false;float?Ö=
null;Ƚ Ɗ=null;switch(ğ){case ċ.Ć:return;case ċ.Ģ:try{var Þ=Ú;if(Þ==null){Ğ(ċ.Ć);return;}if(!Ġ&&!Þ.µ)return;var ȅ=Vector3D.
TransformNormal(æ.GetShipVelocities().AngularVelocity,MatrixD.Transpose(Ă.WorldMatrix));ȅ=new Vector3D(Math.Abs(ȅ.X),Math.Abs(ȅ.Y),Math
.Abs(ȅ.Z));var ȃ=(ȅ-ì)/Dt;ì=ȅ;ô=Þ.À;var Ȃ=Þ.ª;Y=Þ.Y;Ö=Þ.X;if(Þ.Đ!=null)Ɗ=Þ.Đ();if(Þ.à||((Ɗ!=null)&&Ɗ.ȸ.HasValue)){
Vector3D ǧ;Vector3D?ȁ=null;if((Ɗ!=null)&&(Ɗ.ȸ.HasValue)){ǧ=Ɗ.ȸ.Value;if(ȁ.IsValid())ȁ=Ɗ.ê;else ś.Ő("Ivalid targetVelocity");}
else ǧ=Vector3D.Zero;if(Þ.Â!=null)ǧ=Þ.Â(ǧ);var Ȁ=(Þ.Z!=null)?Þ.Z():Ă.GetPosition();if((ô!=null)&&(ȁ.HasValue)&&(ȁ.Value.
Length()>0)){Vector3D ǿ=ǧ;Vector3D Ǿ=ͼ.ˢ(Ȁ,ê,ǿ,ȁ.Value,ô(),Þ.U);if((ǧ-Ǿ).Length()<2500){ǧ=Ǿ;}Ā=Ǿ;}ā=ǧ;if(Þ.Ã!=null)þ=Þ.Ã(ǧ);
else þ=ǧ;double Ȅ=(ǧ-Ȁ).Length();double ǽ=(þ-Ȁ).Length();if(DbgIgc!=0)ï=$"origD: {Ȅ:f1}\nshiftD: {ǽ:f1}";Þ.È?.Invoke(Ȅ,ǽ,
this,Þ,Ü);ú.GyroOverride=true;ý=ǧ;if(Ȃ!=null)ý=Ȃ(ý);if((Variables.Get<bool>("hold-thrust-on-rotation")&&(á.Length()>1))||
Toggle.C.Check("suppress-transition-control")){ś.Ŕ($"prev cv: {á.Length():f2} HOLD");ȍ(Ă.WorldMatrix.Translation,Ă.WorldMatrix
.Translation,false,null,null,false);}else{ś.Ŕ($"prev cv: {á.Length():f2} OK");ȍ(Ȁ,þ,Y,ȁ,Ö,Þ.W);}var Ǽ=(Þ.o!=null)?Þ.o():Ă
.WorldMatrix;if(!Ġ&&(ÿ!=Ă.WorldMatrix.Translation))ý=ÿ;Vector3D ǻ=Vector3D.Zero;var Ǻ=ý-Ă.WorldMatrix.Translation;if(Ǻ!=
Vector3D.Zero){var ǹ=Vector3D.Normalize(Ǻ);var Ǹ=MatrixD.CreateFromDir(ǹ);Vector3D Ƿ=Vector3D.Zero;var ǔ=Þ.Á?.Invoke()??Vector3D
.Zero;ǻ=ͼ.Κ(ǹ,Ǽ,Ă.WorldMatrix,ǔ,ref Ƿ);Ƿ.Z=0;í=Ƿ;î=í-á;á=í;ë=Vector3D.Dot(ǹ,Ǽ.Forward);}if(!Toggle.C.Check(
"suppress-gyro-control"))ͼ.ƴ(ú,ǻ,î,ì);}else{ú.GyroOverride=false;if(Toggle.C.Check("damp-when-idle"))ȍ(Ă.WorldMatrix.Translation,Ă.WorldMatrix.
Translation,false,null,0,false);else ȍ(Ă.WorldMatrix.Translation,Ă.WorldMatrix.Translation,false,null,null,false);}}catch(Exception
ex){Ą.CustomName+="HC Exception! See remcon cdata or PB screen";var ŷ=æ;var ř=
$"HC EPIC FAIL\nNTV:{Ɗ?.Å}\nBehavior:{H.Å}\n{ex}";ŷ.CustomData+=ř;ś.Ő(ř);Ğ(ċ.Ć);throw ex;}finally{ś.Į();}break;}}void ȍ(Vector3D Ȕ,Vector3D ȓ,bool Y,Vector3D?Ȓ,float?Ö,
bool ȑ){ÿ=ȓ;if(ğ!=ċ.Ģ)return;var Ȑ=ȓ;var ȏ=ú.WorldMatrix;ȏ.Translation=Ȕ;var Ǻ=ȓ-ȏ.Translation;var Ȏ=MatrixD.Transpose(ȏ);
var Ȍ=Vector3D.TransformNormal(ê,Ȏ);var ȋ=Ȍ;if(!Ġ&&(Ǻ!=Vector3D.Zero)){â().ƣ().ƒ(1f*Math.Max(0.2f,ë));if(ê!=Vector3D.Zero)ÿ
=Vector3D.Normalize(Vector3D.Reflect(ê,Ǻ))+Ȕ+Vector3D.Normalize(Ǻ)*ê.Length()*0.5f;return;}float Ʋ=æ.CalculateShipMass().
PhysicalMass;BoundingBoxD Ȋ=â().Ȯ(Ʋ);if(Ȋ.Volume==0)return;Vector3D ȉ=Vector3D.Zero;if(é!=null){ȉ=Vector3D.TransformNormal(é.Value,Ȏ
);Ȋ+=-ȉ;}Vector3D Ȉ=Vector3D.Zero;Vector3D ȇ=Vector3D.Zero;Vector3D Ȇ=new Vector3D();if(Ǻ.Length()>double.Epsilon){
Vector3D Ƕ=Vector3D.TransformNormal(Ǻ,Ȏ);RayD ǵ=new RayD(-Ƕ*(MaxAccelInProximity?1000:1),Vector3D.Normalize(Ƕ));RayD ǜ=new RayD(
Ƕ*(MaxBrakeInProximity?1000:1),Vector3D.Normalize(-Ƕ));var ƀ=ǜ.Intersects(Ȋ);var ǩ=ǵ.Intersects(Ȋ);if(!ƀ.HasValue||!ǩ.
HasValue)throw new InvalidOperationException("Not enough thrust to compensate for gravity");var Ǩ=ǜ.Position+(Vector3D.Normalize
(ǜ.Direction)*ƀ.Value);var ǧ=ǵ.Position+(Vector3D.Normalize(ǵ.Direction)*ǩ.Value);var Ǧ=Ǩ.Length();Vector3D ǥ=Vector3D.
Reject(Ȍ,Vector3D.Normalize(Ƕ));if(Ȓ.HasValue){var Ǥ=Vector3D.TransformNormal(Ȓ.Value,Ȏ);ȋ=Ȍ-Ǥ;ǥ=Vector3D.Reject(ȋ,Vector3D.
Normalize(Ƕ));}else{ȋ-=ǥ;}var Ǫ=Vector3D.Dot(ȋ,Vector3D.Normalize(Ƕ));bool ǣ=Ǫ>0;bool ǡ=true;var Ǡ=Math.Pow(Math.Max(0,Ǫ),2)/(2*Ǧ
*StoppingPowerQuotient);var ǟ=Ǻ.Length()-Ǡ;if(DbgIgc!=0){ï+=$"\nSTP: {Ǡ:f2}\nRelSP: {Ǫ:f2}";}if(ǣ){if(Ǡ>Ǻ.Length())ǡ=
false;else if(MoreRejectDampening)ǥ/=Dt;}if(ȑ||ǡ){if(Ö.HasValue&&(Vector3D.Dot(Vector3D.Normalize(Ǻ),ê)>=Ö)){Ȇ=Ǩ;Ȇ*=(Ǫ-Ö.
Value)/Ǧ;}else Ȇ=ǧ;}else Ȇ=Ǩ;if(ǡ){var Ǟ=Vector3D.Dot(Vector3D.Normalize(Ǻ),ê);if(Ǟ>MAX_SP-0.001){Ȇ=Vector3D.Zero;}}ȇ=Ȇ;Ȉ=ǥ;
if(ǥ.IsValid())Ȇ+=ǥ;}else if(Ö.HasValue&&(Ö==0)){Ȇ+=Ȍ/(MoreRejectDampening?Dt:1);}if(é!=null){Ȇ+=ȉ;}Ȇ-=Ǝ.ƍ(Ă.WorldMatrix)*
1000;if(Ȇ!=Vector3D.Zero){ÿ=Vector3D.TransformNormal(Ȇ,ȏ)+Ȕ;if(DbgIgc!=0){var ǝ=new List<MyTuple<Vector3D,Vector3D,Vector4>>
();var A=Color.SeaGreen;A.A=40;ǝ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(Ȕ,ÿ,A));var Ê=new MyTuple<string,Vector2,
Vector3D,Vector3D,float,string>("Circle",Vector2.One*4,ÿ,Vector3D.Zero,1f,Ȇ.Length().ToString("f2"));ä.SendUnicastMessage(DbgIgc
,"draw-projection",Ê);A=Color.Blue;A.A=40;ǝ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(Ȕ,Vector3D.TransformNormal(Ȉ,ȏ)+Ȕ,
A));A=Color.Red;A.A=40;ǝ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(Ȕ,Vector3D.TransformNormal(ȇ,ȏ)+Ȕ,A));var Ǣ=new RayD(
Ȇ*1000,Vector3D.Normalize(-Ȇ));var ǩ=Ǣ.Intersects(Ȋ);if(ǩ.HasValue){var ǯ=Ǣ.Position+(Vector3D.Normalize(Ǣ.Direction)*ǩ.
Value);var ǖ=Vector3D.TransformNormal(ǯ,ȏ)+Ȕ;var Ǳ=new MyTuple<string,Vector2,Vector3D,Vector3D,float,string>("Circle",
Vector2.One*4,ǖ,Vector3D.Zero,1f,ǯ.Length().ToString("f2"));ä.SendUnicastMessage(DbgIgc,"draw-projection",Ǳ);}var ǳ=new RayD(-Ȇ
*1000,Vector3D.Normalize(Ȇ));var ǲ=ǳ.Intersects(Ȋ);if(ǲ.HasValue){var ǯ=ǳ.Position+(Vector3D.Normalize(ǳ.Direction)*ǲ.
Value);var ǖ=Vector3D.TransformNormal(ǯ,ȏ)+Ȕ;var Ǳ=new MyTuple<string,Vector2,Vector3D,Vector3D,float,string>("Circle",
Vector2.One*4,ǖ,Vector3D.Zero,1f,ǯ.Length().ToString("f2"));ä.SendUnicastMessage(DbgIgc,"draw-projection",Ǳ);}ä.
SendUnicastMessage(DbgIgc,"draw-lines",ǝ.ToImmutableArray());}}Ȇ.Y*=-1;â().ƴ(Ȇ,Ʋ);}public Vector3D ǰ(float Ǵ){return Ă.GetPosition()+Ă.
WorldMatrix.Forward*Ǵ;}}Ƚ Õ(string ǭ){Ƚ ŷ;if(ǫ.TryGetValue(ǭ,out ŷ))return ŷ;throw new InvalidOperationException("No TV named "+ǭ);
}void Ǯ(string ǭ,MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>ǂ){ǫ[ǭ].ɂ(ǂ,ʋ);}void
Ǭ(){foreach(var R in ǫ.Values.Where(Ƴ=>Ƴ.ȸ.HasValue)){ś.Ŕ(R.Å+((R.ȸ.Value==Vector3D.Zero)?" Zero!":" OK"));R.Ɍ(ʋ);}}
Dictionary<string,Ƚ>ǫ=new Dictionary<string,Ƚ>();struct ȕ{public Vector3D?ǖ;public Vector3D?Ɂ;public MatrixD?ɀ;public BoundingBoxD
?ȿ;public MyDetectedEntityType?Ⱦ;}class Ƚ{public long ȼ;int Ȼ;public string Å;public long Ⱥ;public Vector3D?ȸ{get;private
set;}public Vector3D?ê;public Vector3D?ȷ;public MatrixD?ȶ;public BoundingBoxD?ȵ;public int?ȴ=60;public MyDetectedEntityType
?ȳ{get;set;}public delegate void ȹ();public event ȹ Ȳ;public Ƚ(int Ȼ,string Ĉ){this.Ȼ=Ȼ;Å=Ĉ;}public void Ɏ(Vector3D ǖ,
long ɍ){ȸ=ǖ;ȼ=ɍ;}public void Ɍ(int ɋ){if((ȼ!=0)&&ȴ.HasValue&&(ɋ-ȼ>ȴ.Value))ȝ();}public void Ɋ(int ũ,int Ȼ){if((ê.HasValue)&&
(ê.Value.Length()>double.Epsilon)&&(ũ-ȼ)>0){ȸ+=ê*(ũ-ȼ)*Ȼ/60;}}public enum ɉ:byte{Ɉ=1,ɇ=2,Ɇ=4}bool Ʌ(ɉ Ʉ,ɉ Ƀ){return(Ʉ&Ƀ)
==Ƀ;}public void ɂ(MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>ȡ,int ȟ){var ȥ=ȡ.
Item1;Ⱥ=ȥ.Item2;ȳ=(MyDetectedEntityType)ȥ.Item4;ɉ Ȗ=(ɉ)ȥ.Item5;Ɏ(ȡ.Item2,ȟ);if(Ʌ(Ȗ,ɉ.Ɉ)){var ȣ=ȡ.Item3;if(!ê.HasValue)ê=ȣ;ȷ=(
ȣ-ê.Value)*60/Ȼ;ê=ȣ;}if(Ʌ(Ȗ,ɉ.ɇ))ȶ=ȡ.Item4;if(Ʌ(Ȗ,ɉ.Ɇ))ȵ=ȡ.Item5;}public static Ƚ Ȣ(MyTuple<MyTuple<string,long,long,byte
,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>ȡ,Func<string[],ȕ>Ƞ,int ȟ){var Ê=new Ƚ(1,ȡ.Item1.Item1);Ê.ɂ(ȡ,ȟ);return Ê;}
public MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>Ȟ(){var Ȥ=0|(ê.HasValue?1:0)|(ȶ.
HasValue?2:0)|(ȵ.HasValue?4:0);var R=new MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>(new
MyTuple<string,long,long,byte,byte>(Å,Ⱥ,DateTime.Now.Ticks,(byte)MyDetectedEntityType.LargeGrid,(byte)Ȥ),ȸ.Value,ê??Vector3D.
Zero,ȶ??MatrixD.Identity,ȵ??new BoundingBoxD());return R;}public void ȝ(){ȸ=null;ê=null;ȶ=null;ȵ=null;var Ȝ=Ȳ;if(Ȝ!=null)Ȳ()
;}}class ț{List<Ɠ>Ț;List<Ɠ>ș;List<Ɠ>Ș;List<Ɠ>ȗ;List<Ɠ>ǘ;List<Ɠ>Ȧ;List<Ɠ>ȭ;public double[]ȱ=new double[6];bool Ȱ;public
void ȯ(){if(!Ȱ){ș.ForEach(Ʊ=>Ʊ.Ə());Ș.ForEach(Ʊ=>Ʊ.Ə());ȗ.ForEach(Ʊ=>Ʊ.Ə());ǘ.ForEach(Ʊ=>Ʊ.Ə());Ȧ.ForEach(Ʊ=>Ʊ.Ə());ȭ.
ForEach(Ʊ=>Ʊ.Ə());Ȱ=true;}}public BoundingBoxD Ȯ(float Ʋ){Vector3D Ȭ=new Vector3D(-ȱ[5],-ȱ[3],-ȱ[1])/Ʋ;Vector3D ȫ=new Vector3D(
ȱ[4],ȱ[2],ȱ[0])/Ʋ;return new BoundingBoxD(Ȭ,ȫ);}public void Ȫ(){ȱ[0]=ƣ().Ư();ȱ[1]=Ƣ().Ư();ȱ[2]=ơ().Ư();ȱ[3]=ƥ().Ư();ȱ[4]=
Ʈ().Ư();ȱ[5]=ƪ().Ư();}public ț(IMyTerminalBlock ȩ,List<IMyTerminalBlock>Ȩ){MatrixD ȧ=ȩ.WorldMatrix;Func<Vector3D,List<Ɠ>>
ɏ=Ʀ=>{var ŷ=Ȩ.Where(D=>D is IMyThrust&&Ʀ==D.WorldMatrix.Forward).Select(R=>R as IMyThrust).ToList();return ŷ.Select(Ê=>
new Ɣ(Ê)).Cast<Ɠ>().ToList();};ș=ɏ(ȧ.Backward);Ș=ɏ(ȧ.Forward);ȗ=ɏ(ȧ.Down);ǘ=ɏ(ȧ.Up);Ȧ=ɏ(ȧ.Left);ȭ=ɏ(ȧ.Right);var ƨ=Ȩ.Where(
D=>D is IMyArtificialMassBlock).Cast<IMyArtificialMassBlock>().ToList();var Ƨ=Ȩ.Where(D=>D is IMyGravityGenerator).Cast<
IMyGravityGenerator>().ToList();Func<Vector3D,bool,List<Ɠ>>Ʃ=(Ʀ,Ƥ)=>{var ŕ=Ƨ.Where(D=>Ʀ==D.WorldMatrix.Up);return ŕ.Select(Ì=>new ƫ(Ì,ƨ,Ƥ))
.Cast<Ɠ>().ToList();};ș.AddRange(Ʃ(ȧ.Forward,true));Ș.AddRange(Ʃ(ȧ.Forward,false));ș.AddRange(Ʃ(ȧ.Backward,false));Ș.
AddRange(Ʃ(ȧ.Backward,true));ȗ.AddRange(Ʃ(ȧ.Up,true));ǘ.AddRange(Ʃ(ȧ.Up,false));ȗ.AddRange(Ʃ(ȧ.Down,false));ǘ.AddRange(Ʃ(ȧ.Down,
true));Ȧ.AddRange(Ʃ(ȧ.Right,true));ȭ.AddRange(Ʃ(ȧ.Right,false));Ȧ.AddRange(Ʃ(ȧ.Left,false));ȭ.AddRange(Ʃ(ȧ.Left,true));Ȫ();}
public ț ƣ(){Ț=ș;return this;}public ț Ƣ(){Ț=Ș;return this;}public ț ơ(){Ț=ȗ;return this;}public ț ƥ(){Ț=ǘ;return this;}public
ț ƪ(){Ț=Ȧ;return this;}public ț Ʈ(){Ț=ȭ;return this;}public void ƴ(Vector3D Ƴ,float Ʋ){Ȱ=false;Func<Ɠ,bool>ư=Ʊ=>!(Ʊ is ƫ)
;Ƣ().ƒ(-Ƴ.Z/ȱ[1]*Ʋ);ƣ().ƒ(Ƴ.Z/ȱ[0]*Ʋ);ƥ().ƒ(-Ƴ.Y/ȱ[3]*Ʋ);ơ().ƒ(Ƴ.Y/ȱ[2]*Ʋ);ƪ().ƒ(-Ƴ.X/ȱ[5]*Ʋ);Ʈ().ƒ(Ƴ.X/ȱ[4]*Ʋ);}public
bool ƒ(double Ĥ,Func<Ɠ,bool>ư=null){if(Ț!=null){Ĥ=Math.Min(1,Math.Abs(Ĥ))*Math.Sign(Ĥ);foreach(var Ƭ in ư==null?Ț:Ț.Where(ư)
){Ƭ.ƒ(Ĥ);}}Ț=null;return true;}public float Ư(){float ƭ=0;if(Ț!=null){foreach(var Ƭ in Ț){ƭ+=Ƭ.Ƒ();}}Ț=null;return ƭ;}}
class ƫ:Ɠ{IMyGravityGenerator ŕ;List<IMyArtificialMassBlock>Ơ;bool Ɵ;public ƫ(IMyGravityGenerator ŕ,List<
IMyArtificialMassBlock>Ơ,bool Ɵ){this.ŕ=ŕ;this.Ơ=Ơ;this.Ɵ=Ɵ;}public void ƒ(double Ƌ){if(Ƌ>=0)ŕ.GravityAcceleration=(float)(Ɵ?-Ƌ:Ƌ)*G;}public
void Ə(){ŕ.GravityAcceleration=0;}public float Ƒ(){return Ơ.Count*50000*G;}}class Ɣ:Ɠ{IMyThrust Ê;public Ɣ(IMyThrust Ê){this
.Ê=Ê;}public void ƒ(double Ƌ){if(Ƌ<=0)Ê.ThrustOverride=0.00000001f;else Ê.ThrustOverride=(float)Ƌ*Ê.MaxThrust;}public
void Ə(){Ê.ThrustOverride=0;Ê.Enabled=true;}public float Ƒ(){return Ê.MaxEffectiveThrust;}}interface Ɠ{void ƒ(double Ƌ);
float Ƒ();void Ə();}static class Ǝ{public static List<IMyShipController>ſ;public static void ŝ(List<IMyShipController>A){if(ſ
==null)ſ=A;}public static Vector3 ƍ(MatrixD ƌ){Vector3 Ɛ=new Vector3();if(Toggle.C.Check("ignore-user-thruster"))return Ɛ;
var A=ſ.Where(R=>R.IsUnderControl).FirstOrDefault();if(A!=null&&(A.MoveIndicator!=Vector3.Zero))return Vector3D.
TransformNormal(A.MoveIndicator,ƌ*MatrixD.Transpose(A.WorldMatrix));return Ɛ;}}class ƞ{public string Å;public Vector3D?Ɯ;public Func<
Vector3D>ƛ;double?ƚ;public int?ƙ;public ApckState Ƙ=ApckState.CwpTask;public Æ H;public Action Ɲ;int Ɨ;public ApckState Ɩ;public
ƞ(string Ĉ,Æ Ú,int?ƕ=null){H=Ú;Å=Ĉ;ƙ=ƕ;}public ƞ(string Ĉ,ApckState F,int?ƕ=null){Ƙ=F;Å=Ĉ;ƙ=ƕ;}public void ŝ(ć ž,int ũ){
if(Ɨ==0)Ɨ=ũ;ž.å.ĉ(Å+".OnStart");}public static ƞ ǒ(string Ĉ,Vector3D Ì,Æ Ú){var Ê=new ƞ(Ĉ,Ú);Ê.ƚ=0.5;Ê.Ɯ=Ì;return Ê;}
public static ƞ Ǒ(string Ĉ,Func<Vector3D>ǐ,Æ Ú){var Ê=new ƞ(Ĉ,Ú);Ê.ƚ=0.5;Ê.ƛ=ǐ;return Ê;}public bool ǎ(int ũ,ć ž){if(ƙ.
HasValue&&(ũ-Ɨ>ƙ)){return true;}if(ƚ.HasValue){Vector3D Ì;var Ô=H.Z?.Invoke()??ž.Ă.GetPosition();if(ƛ!=null)Ì=ƛ();else Ì=Ɯ.Value
;if((Ô-Ì).Length()<ƚ)return true;}return false;}}ƞ Ǐ;void ů(string[]Ǔ){ƻ();var Ǜ=ĺ;var Ï=Ǜ.ž;var ǚ=Ï.Ă.GetPosition();var
Ǚ=Ǔ[2].Split(',').ToDictionary(Í=>Í.Split('=')[0],Í=>Í.Split('=')[1]);var Ú=new Æ(){Å="Deserialized Behavior",à=true,Ä=
false};Ǐ=new ƞ("twp",Ú);float ǘ=1;var Ǘ=Ǔ.Take(6).Skip(1).ToArray();var ǖ=new Vector3D(double.Parse(Ǘ[2]),double.Parse(Ǘ[3]),
double.Parse(Ǘ[4]));Func<Vector3D,Vector3D>Ǖ=Ì=>ǖ;Ǐ.Ɯ=ǖ;Vector3D?ų=null;if(Ǚ.ContainsKey("AimNormal")){var Ƴ=Ǚ["AimNormal"].
Split(';');ų=new Vector3D(double.Parse(Ƴ[0]),double.Parse(Ƴ[1]),double.Parse(Ƴ[2]));}if(Ǚ.ContainsKey("UpNormal")){var Ƴ=Ǚ[
"UpNormal"].Split(';');var ǔ=Vector3D.Normalize(new Vector3D(double.Parse(Ƴ[0]),double.Parse(Ƴ[1]),double.Parse(Ƴ[2])));Ú.Á=()=>ǔ;
}if(Ǚ.ContainsKey("Name"))Ǐ.Å=Ǚ["Name"];if(Ǚ.ContainsKey("FlyThrough"))Ú.W=true;if(Ǚ.ContainsKey("SpeedLimit"))Ú.X=float.
Parse(Ǚ["SpeedLimit"]);if(Ǚ.ContainsKey("TriggerDistance"))ǘ=float.Parse(Ǚ["TriggerDistance"]);if(Ǚ.ContainsKey(
"PosDirectionOverride")&&(Ǚ["PosDirectionOverride"]=="Forward")){if(ų.HasValue){Ǖ=Ì=>ǚ+ų.Value*((Ï.Ă.GetPosition()-ǚ).Length()+5);}else Ǖ=Ì=>Ï
.ǰ(50000);}if(Ǔ.Length>6){Ú.È=(Ý,Ǎ,ž,Þ,Ü)=>{if(Ǎ<ǘ){ƻ();ɵ.σ.ʍ(Ǔ[7],Ǔ.Skip(6).ToArray());}};}if(Ǚ.ContainsKey("Ng")){Func<
MatrixD>Ʀ=()=>Ï.Ă.WorldMatrix;if(Ǚ["Ng"]=="Down")Ú.o=()=>MatrixD.CreateFromDir(Ï.Ă.WorldMatrix.Down,Ï.Ă.WorldMatrix.Forward);if
(Ǜ.š()&&!Ǚ.ContainsKey("IgG")){Ú.ª=Ì=>Ǜ.Ţ.Value;Ú.Ã=Ì=>Vector3D.Normalize(Ǖ(Ì)-Ǜ.Ţ.Value)*(ǚ-Ǜ.Ţ.Value).Length()+Ǜ.Ţ.
Value;}else{if(ų.HasValue){Ú.ª=Ì=>Ï.Ă.GetPosition()+ų.Value*1000;}else Ú.ª=Ì=>Ï.Ă.GetPosition()+(Ú.o??Ʀ)().Forward*1000;}}if(
Ǚ.ContainsKey("TransformChannel")){Func<Vector3D,Vector3D>Ƽ=Ì=>Vector3D.Transform(ǖ,Õ(Ǚ["TransformChannel"]).ȶ.Value);Ú.Â
=Ƽ;Ǐ.ƛ=()=>Vector3D.Transform(ǖ,Õ(Ǚ["TransformChannel"]).ȶ.Value);Ú.U=true;Ú.Đ=()=>Õ(Ǚ["TransformChannel"]);Ú.À=()=>Ï.ê;Ú
.Ã=null;}else Ú.Ã=Ǖ;Ǐ.Ƙ=ApckState.CwpTask;Ǜ.ű(Ǐ.Ƙ,Ú);Ǜ.Ɖ(Ǐ);}void ƻ(){if(Ǐ!=null){if(ĺ.ŧ()==Ǐ)ĺ.ġ();Ǐ=null;}}class ƹ{
public long Ƹ;public string Ĉ;public MatrixD Ʒ;public Vector3D Ƴ;public float ƶ;public float Ƶ;public float ƺ;public float ƽ;
public string ǃ;public MinerState ğ;public float ǋ;public float Ǌ;public bool ǉ;public bool ǈ;public float Ǉ;public float ǆ;
public bool ǅ;public ImmutableArray<MyTuple<string,string>>ǌ;public MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,
MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,string>>>Ǆ(){var ǂ
=new MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,
bool,float,float>,ImmutableArray<MyTuple<string,string>>>();ǂ.Item1.Item1=Ƹ;ǂ.Item1.Item2=Ĉ;ǂ.Item2.Item1=Ʒ;ǂ.Item2.Item2=Ƴ;
ǂ.Item3.Item1=(byte)ğ;ǂ.Item3.Item2=ǃ;ǂ.Item3.Item3=ǅ;var ǁ=ImmutableArray.CreateBuilder<float>(6);ǁ.Add(ƶ);ǁ.Add(Ƶ);ǁ.
Add(ƺ);ǁ.Add(ƽ);ǁ.Add(ǋ);ǁ.Add(Ǌ);ǂ.Item4=ǁ.ToImmutableArray();ǂ.Item5.Item1=ǉ;ǂ.Item5.Item2=ǈ;ǂ.Item5.Item3=Ǉ;ǂ.Item5.
Item4=ǆ;ǂ.Item6=ǌ;return ǂ;}}List<MyTuple<string,Vector3D,ImmutableArray<string>>>ǀ=new List<MyTuple<string,Vector3D,
ImmutableArray<string>>>();void ƿ(string Ų,Vector3D Ì,params string[]Í){ǀ.Add(new MyTuple<string,Vector3D,ImmutableArray<string>>(Ų,Ì,
Í.ToImmutableArray()));}void Ψ(long ƾ){IGC.SendUnicastMessage(ƾ,"hud.apck.proj",ǀ.ToImmutableArray());ǀ.Clear();}