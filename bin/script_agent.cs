/*
 * R e a d m e
 * -----------
 * 
 * In this file you can include any instructions or other comments you want to have injected onto the 
 * top of your final script. You can safely delete this file if you do not want any such comments.
 */

const string Ver = "0.10.0"; // Must be the same on dispatcher and agents.

static bool WholeAirspaceLocking = false;
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
{ "echelon-offset", new Variable<float> { value = 12f, parser = s => float.Parse(s) } },
{ "getAbove-altitude", new Variable<float> { value = 20, parser = s => float.Parse(s) } },
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

bool ʗ;ʖ ʘ;class ʖ{Dictionary<string,Action<string[]>>ʔ;public ʖ(Dictionary<string,Action<string[]>>ʔ){this.ʔ=ʔ;}public
void ʓ(string ʒ,string[]ʈ){this.ʔ[ʒ].Invoke(ʈ);}}static int ʑ;void ʕ(string ʐ){ʑ++;Echo("Run count: "+ʑ);Echo("Name: "+me.
CubeGrid.CustomName);if(ʗ&&string.IsNullOrEmpty(ʐ)){ʗ=false;ʇ();ʐ=string.Join(",",Me.CustomData.Trim('\n').Split(new[]{'\n'},
StringSplitOptions.RemoveEmptyEntries).Where(Ê=>!Ê.StartsWith("//")).Select(Ê=>"["+Ê+"]"));}if(!string.IsNullOrEmpty(ʐ)&&ʐ.Contains(":")){
var ʔ=ʐ.Split(new[]{"],["},StringSplitOptions.RemoveEmptyEntries).Select(Ê=>Ê.Trim('[',']')).ToList();foreach(var P in ʔ){
string[]ʈ=P.Split(new[]{':'},StringSplitOptions.RemoveEmptyEntries);if(ʈ[0]=="command"){try{this.ʘ.ʓ(ʈ[1],ʈ);}catch(Exception
ex){ʉ($"Run command '{ʈ[1]}' failed.\n{ex}");}}if(ʈ[0]=="toggle"){Toggle.C.Invert(ʈ[1]);ʉ(
$"Switching '{ʈ[1]}' to state '{Toggle.C.Check(ʈ[1])}'");}}}}void ī(){Ĩ.Ħ.Ĺ();ř.ī();}IMyProgrammableBlock ʛ;void ʚ(){if(!string.IsNullOrEmpty(Me.CustomData))ʗ=true;ř.Ŕ(Echo,
GridTerminalSystem);Toggle.Init(new Dictionary<string,bool>{{"adaptive-mining",false},{"adjust-entry-by-elevation",true},{"log-message",
false},{"show-pstate",false},{"suppress-transition-control",false},{"suppress-gyro-control",false},{"damp-when-idle",true},{
"ignore-user-thruster",false},{"cc",true}},ǳ=>{switch(ǳ){case"log-message":var ʙ=ɾ?.Σ;if(ʙ!=null)ʙ.CustomData="";break;}});Ʉ.Add("docking",new
Ƚ(1,"docking"));stateWrapper=new StateWrapper(Ê=>Storage=Ê);if(!stateWrapper.TryLoad(Storage)){ř.Œ(
"State load failed, clearing Storage now");stateWrapper.Save();Runtime.UpdateFrequency=UpdateFrequency.None;}GridTerminalSystem.GetBlocksOfType(ɭ,P=>P.
IsSameConstructAs(Me));ɭ.ForEach(P=>P.EnableRaycast=true);IsLargeGrid=Me.CubeGrid.GridSizeEnum==MyCubeSize.Large;this.ʘ=new ʖ(new
Dictionary<string,Action<string[]>>{{"set-value",(ǘ)=>Variables.Set(ǘ[2],ǘ[3])},{"add-panel",(ǘ)=>{List<IMyTextPanel>U=new List<
IMyTextPanel>();GridTerminalSystem.GetBlocksOfType(U,O=>O.IsSameConstructAs(Me)&&O.CustomName.Contains(ǘ[2]));var Ï=U.FirstOrDefault
();if(Ï!=null){ř.ĭ($"Added {Ï.CustomName} as GUI panel");outputPanelInitializer(Ï);ş=Ï;}}},{"add-logger",(ǘ)=>{List<
IMyTextPanel>U=new List<IMyTextPanel>();GridTerminalSystem.GetBlocksOfType(U,O=>O.IsSameConstructAs(Me)&&O.CustomName.Contains(ǘ[2])
);var Ï=U.FirstOrDefault();if(Ï!=null){logPanelInitializer(Ï);ř.Ĭ(Ï);ř.ĭ("Added logger: "+Ï.CustomName);}}},{
"create-task",(ǘ)=>ɾ?.λ()},{"mine",(ǘ)=>ɾ?.β()},{"skip",(ǘ)=>ɾ?.α()},{"set-role",(ǘ)=>ʉ("command:set-role is deprecated.")},{
"low-update-rate",(ǘ)=>Runtime.UpdateFrequency=UpdateFrequency.Update10},{"create-task-raycast",(ǘ)=>ɩ(ǘ)},{"force-finish",(ǘ)=>ɾ?.ϧ()},{
"static-dock",(ǘ)=>ɾ?.ΰ(ǘ)},{"set-state",(ǘ)=>ɾ?.A(ǘ[2])},{"halt",(ǘ)=>ɾ?.ɠ()},{"clear-storage-state",(ǘ)=>stateWrapper?.
ClearPersistentState()},{"save",(ǘ)=>stateWrapper?.Save()},{"static-dock-gps",(ǘ)=>{if((ɾ!=null)&&(ɾ.Σ!=null)){ɾ.Σ.CustomData=
"GPS:static-dock:"+(stateWrapper.PState.StaticDockOverride.HasValue?ͽ.ͼ(stateWrapper.PState.StaticDockOverride.Value):"-")+":";}}},{
"dispatch",(ǘ)=>ɾ?.π()},{"global",(ǘ)=>{var ʈ=ǘ.Skip(2).ToArray();IGC.SendBroadcastMessage("miners.command",string.Join(":",ʈ),
TransmissionDistance.TransmissionDistanceMax);ʉ("broadcasting global "+string.Join(":",ʈ));ʘ.ʓ(ʈ[1],ʈ);}},{"get-toggles",(ǘ)=>{IGC.
SendUnicastMessage(long.Parse(ǘ[2]),$"menucommand.get-commands.reply:{string.Join(":",ǘ.Take(3))}",Toggle.C.GetToggleCommands());}},});ɾ=
new ɽ(GridTerminalSystem,IGC,stateWrapper,Í);}void ʇ(){var U=new List<IMyProgrammableBlock>();GridTerminalSystem.
GetBlocksOfType(U,ʏ=>ʏ.CustomName.Contains("core")&&ʏ.IsSameConstructAs(Me)&&ʏ.Enabled);ʛ=U.FirstOrDefault();if(ʛ!=null)ɾ.ɣ(ʛ);else{Ķ=
new ĵ(stateWrapper.PState,GridTerminalSystem,IGC,Í);ɾ.ɣ(Ķ);ɾ.Ϥ=new ʖ(new Dictionary<string,Action<string[]>>{{"create-wp",(
ǘ)=>Ű(ǘ)},{"pillock-mode",(ǘ)=>Ķ?.A(ǘ[2])},{"request-docking",(ǘ)=>{ř.ĭ("Embedded lone mode is not supported");}},{
"request-depart",(ǘ)=>{ř.ĭ("Embedded lone mode is not supported");}}});}if(!string.IsNullOrEmpty(stateWrapper.PState.lastAPckCommand)){Ĩ
.Ħ.ľ(5000).ļ(()=>ɾ.ϣ(stateWrapper.PState.lastAPckCommand));}Ĩ.Ħ.ĺ(()=>!ɾ.ɻ.HasValue).ľ(1000).ļ(()=>ɾ.κ());if(stateWrapper
.PState.miningEntryPoint.HasValue){ɾ.γ();}}static void ʎ<ŕ>(ŕ ʍ,IList<ŕ>P)where ŕ:class{if((ʍ!=null)&&!P.Contains(ʍ))P.
Add(ʍ);}void ʌ<ŕ>(string ũ,ŕ ʋ){var ʊ=IGC.RegisterBroadcastListener(ũ);IGC.SendBroadcastMessage(ʊ.Tag,ʋ,
TransmissionDistance.TransmissionDistanceMax);}void ʉ(string ɯ){ř.ĭ(ɯ);}
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
	ChangingShaft        = 10,
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
	public ə PState { get; private set; }

	public void ClearPersistentState()
	{
var currentState = PState;
PState = new ə();
PState.StaticDockOverride = currentState.StaticDockOverride;
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
	PState.ɘ(stateSaver);
}
catch (Exception ex)
{
	ř.ĭ("State save failed.");
	ř.ĭ(ex.ToString());
}
	}

	public bool TryLoad(string serialized)
	{
PState = new ə();
try
{
	PState.Load(serialized);
	return true;
}
catch (Exception ex)
{
	ř.ĭ("State load failed.");
	ř.ĭ(ex.ToString());
}
return false;
	}
}

public class ə
{
	public int LifetimeOperationTime = 0;
	public int LifetimeAcceptedTasks = 0;
	public int LifetimeWentToMaintenance = 0;
	public float LifetimeOreAmount = 0;
	public float LifetimeYield = 0;
	public bool bRecalled; ///< Has the agent been oredered to return to base?

	// cleared by specific command
	public Vector3D? StaticDockOverride { get; set; }

	// cleared by clear-storage-state (task-dependent)
	public MinerState MinerState = MinerState.Idle;
	public Vector3D? miningPlaneNormal;
	public Vector3D? getAbovePt;       ///< Point above the current shaft. (Add echelon value to get intersection of shaft and assigned flight level.)
	public Vector3D? miningEntryPoint;
	public Vector3D? corePoint;
	public float? shaftRadius;

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

	public ə Load(string storage)
	{
if (!string.IsNullOrEmpty(storage))
{
	ř.Œ(storage);

	var values = storage.Split('\n').ToDictionary(s => s.Split('=')[0], s => string.Join("=", s.Split('=').Skip(1)));

	LifetimeAcceptedTasks = ParseValue<int>(values, "LifetimeAcceptedTasks");
	LifetimeOperationTime = ParseValue<int>(values, "LifetimeOperationTime");
	LifetimeWentToMaintenance = ParseValue<int>(values, "LifetimeWentToMaintenance");
	LifetimeOreAmount = ParseValue<float>(values, "LifetimeOreAmount");
	LifetimeYield = ParseValue<float>(values, "LifetimeYield");
	bRecalled     = ParseValue<bool> (values, "bRecalled");

	StaticDockOverride = ParseValue<Vector3D?>(values, "StaticDockOverride");
	MinerState = ParseValue<MinerState>(values, "MinerState");
	miningPlaneNormal = ParseValue<Vector3D?>(values, "miningPlaneNormal");
	getAbovePt = ParseValue<Vector3D?>(values, "getAbovePt");
	miningEntryPoint = ParseValue<Vector3D?>(values, "miningEntryPoint");
	corePoint = ParseValue<Vector3D?>(values, "corePoint");
	shaftRadius = ParseValue<float?>(values, "shaftRadius");

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
public void ɘ(Action<string>ɗ){ɗ(ɖ());}string ɖ(){string[]ɕ=new string[]{"LifetimeAcceptedTasks="+LifetimeAcceptedTasks,
"LifetimeOperationTime="+LifetimeOperationTime,"LifetimeWentToMaintenance="+LifetimeWentToMaintenance,"LifetimeOreAmount="+LifetimeOreAmount,
"LifetimeYield="+LifetimeYield,"bRecalled="+bRecalled,"StaticDockOverride="+(StaticDockOverride.HasValue?ͽ.ͼ(StaticDockOverride.Value):
""),"MinerState="+MinerState,"miningPlaneNormal="+(miningPlaneNormal.HasValue?ͽ.ͼ(miningPlaneNormal.Value):""),
"getAbovePt="+(getAbovePt.HasValue?ͽ.ͼ(getAbovePt.Value):""),"miningEntryPoint="+(miningEntryPoint.HasValue?ͽ.ͼ(miningEntryPoint.
Value):""),"corePoint="+(corePoint.HasValue?ͽ.ͼ(corePoint.Value):""),"shaftRadius="+shaftRadius,"maxDepth="+maxDepth,
"skipDepth="+skipDepth,"leastDepth="+leastDepth,"adaptiveMode="+Toggle.C.Check("adaptive-mining"),"adjustAltitude="+Toggle.C.Check(
"adjust-entry-by-elevation"),"currentWp="+(currentWp.HasValue?ͽ.ͼ(currentWp.Value):""),"lastFoundOreDepth="+lastFoundOreDepth,
"CurrentJobMaxShaftYield="+CurrentJobMaxShaftYield,"minFoundOreDepth="+minFoundOreDepth,"maxFoundOreDepth="+maxFoundOreDepth,"CurrentShaftId="+
CurrentShaftId??"","lastAPckCommand="+lastAPckCommand};return string.Join("\n",ɕ);}public override string ToString(){return ɖ();}}void
Save(){stateWrapper.Save();}Program(){me=Me;Runtime.UpdateFrequency=UpdateFrequency.Update1;ʚ();}List<MyIGCMessage>ɚ=new
List<MyIGCMessage>();void Main(string ɳ,UpdateType ɱ){ɚ.Clear();while(IGC.UnicastListener.HasPendingMessage){ɚ.Add(IGC.
UnicastListener.AcceptMessage());}var ɰ=IGC.RegisterBroadcastListener("miners.command");if(ɰ.HasPendingMessage){var ɯ=ɰ.AcceptMessage()
;ɳ=ɯ.Data.ToString();ʉ("Got miners.command: "+ɳ);}ʕ(ɳ);foreach(var ɮ in ɚ){if(ɮ.Tag=="apck.ntv.update"){var ȥ=(MyTuple<
MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>)ɮ.Data;var Ć=ȥ.Item1.Item1;Ǵ(Ć,ȥ);if(ɾ?.ɢ!=null){
IGC.SendUnicastMessage(ɾ.ɢ.EntityId,"apck.ntv.update",ȥ);}}else if(ɮ.Tag=="apck.depart.complete"){if(ɾ?.ɻ!=null)IGC.
SendUnicastMessage(ɾ.ɻ.Value,"apck.depart.complete","");}else if(ɮ.Tag=="apck.docking.approach"||ɮ.Tag=="apck.depart.approach"){if(ɾ?.ɢ!=
null){IGC.SendUnicastMessage(ɾ.ɢ.EntityId,ɮ.Tag,(ImmutableArray<Vector3D>)ɮ.Data);}else{if(ɮ.Tag.Contains("depart")){var ŵ=
new Ƥ("fin",Ķ.š);ŵ.Ɵ=1;ŵ.ƣ=()=>IGC.SendUnicastMessage(ɮ.Source,"apck.depart.complete","");Ķ.Ű(ŵ);}Ķ.ĳ.Disconnect();}}}ř.Œ(
$"Version: {Ver}");ɾ.ů(ɚ);ř.Œ("Min3r state: "+ɾ.ɞ());ř.Œ("Static dock override: "+(stateWrapper.PState.StaticDockOverride.HasValue?"ON":
"OFF"));ř.Œ("Dispatcher: "+ɾ.ɻ);ř.Œ("Echelon: "+ɾ.ɺ);ř.Œ("HoldingLock: "+ɾ.ɹ);ř.Œ("WaitedSection: "+ɾ.ɸ);ř.Œ(
$"Estimated shaft radius: {Variables.Get<float>("circular-pattern-shaft-radius"):f2}");ř.Œ("LifetimeAcceptedTasks: "+stateWrapper.PState.LifetimeAcceptedTasks);ř.Œ("LifetimeOreAmount: "+ŝ(stateWrapper.
PState.LifetimeOreAmount));ř.Œ("LifetimeOperationTime: "+TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime).
ToString());ř.Œ("LifetimeWentToMaintenance: "+stateWrapper.PState.LifetimeWentToMaintenance);if(Ķ!=null){if(Ķ.Ž.þ!=Vector3D.Zero
)Ǆ("agent-dest",Ķ.Ž.þ,"");if(Ķ.Ž.Ą!=Vector3D.Zero)Ǆ("agent-vel",Ķ.Ž.Ą,Ķ.Ž.ì);}if(ş!=null){ő(
$"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");ő($"LifetimeOreAmount: {ŝ(stateWrapper.PState.LifetimeOreAmount)}");ő(
$"LifetimeOperationTime: {TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime)}");ő($"LifetimeWentToMaintenance: {stateWrapper.PState.LifetimeWentToMaintenance}");ő("\n");ő(
$"CurrentJobMaxShaftYield: {ŝ(stateWrapper.PState.CurrentJobMaxShaftYield)}");ő($"CurrentShaftYield: "+ɾ?.ɼ?.ʪ());ő(ɾ?.ɼ?.ToString());Ş();}if(Toggle.C.Check("show-pstate"))ř.Œ(stateWrapper.PState.
ToString());ī();ș();if(DbgIgc!=0)Ő(DbgIgc);Dt=Math.Max(0.001,Runtime.TimeSinceLastRun.TotalSeconds);ř.ŕ+=Dt;ɲ=Math.Max(ɲ,Runtime
.CurrentInstructionCount);ř.Œ($"InstructionCount (Max): {Runtime.CurrentInstructionCount} ({ɲ})");ř.Œ(
$"Processed in {Runtime.LastRunTimeMs:f3} ms");}int ɲ;List<IMyCameraBlock>ɭ=new List<IMyCameraBlock>();Vector3D?ɫ;Vector3D?ɪ;void ɩ(string[]ɨ){var ɧ=ɭ.Where(P=>P.
IsActive).FirstOrDefault();if(ɧ!=null){ɧ.CustomData="";var Ǜ=ɧ.GetPosition()+ɧ.WorldMatrix.Forward*Variables.Get<float>(
"ct-raycast-range");ɧ.CustomData+="GPS:dir0:"+ͽ.ͼ(Ǜ)+":\n";ʉ(
$"RaycastTaskHandler tries to raycast point GPS:create-task base point:{ͽ.ͼ(Ǜ)}:");if(ɧ.CanScan(Ǜ)){var ɬ=ɧ.Raycast(Ǜ);if(!ɬ.IsEmpty()){ɫ=ɬ.HitPosition.Value;ʉ(
$"GPS:Raycasted base point:{ͽ.ͼ(ɬ.HitPosition.Value)}:");ɧ.CustomData+="GPS:castedSurfacePoint:"+ͽ.ͼ(ɫ.Value)+":\n";IMyShipController ɦ=ɾ?.ĕ;Vector3D ɴ;if((ɦ!=null)&&ɦ.
TryGetPlanetPosition(out ɴ)){ɪ=Vector3D.Normalize(ɴ-ɫ.Value);ř.ĭ(
"Using mining-center-to-planet-center direction as a normal because we are in gravity");}else{var ʆ=ɫ.Value-ɧ.GetPosition();var ʅ=Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(ʆ));var ʄ=ɫ.Value+ʅ
*Math.Min(10,ʆ.Length());var ʃ=ɫ.Value+Vector3D.Normalize(Vector3D.Cross(ʅ,ʆ))*Math.Min(20,ʆ.Length());var ʂ=ʄ+Vector3D.
Normalize(ʄ-ɧ.GetPosition())*500;var ʁ=ʃ+Vector3D.Normalize(ʃ-ɧ.GetPosition())*500;ɧ.CustomData+="GPS:target1:"+ͽ.ͼ(ʂ)+":\n";if(ɧ
.CanScan(ʂ)){var ʀ=ɧ.Raycast(ʂ);if(!ʀ.IsEmpty()){ʉ($"GPS:Raycasted aux point 1:{ͽ.ͼ(ʀ.HitPosition.Value)}:");ɧ.CustomData
+="GPS:cast1:"+ͽ.ͼ(ʀ.HitPosition.Value)+":\n";ɧ.CustomData+="GPS:target2:"+ͽ.ͼ(ʁ)+":\n";if(ɧ.CanScan(ʁ)){var ɿ=ɧ.Raycast(ʁ
);if(!ɿ.IsEmpty()){ʉ($"GPS:Raycasted aux point 2:{ͽ.ͼ(ɿ.HitPosition.Value)}:");ɧ.CustomData+="GPS:cast2:"+ͽ.ͼ(ɿ.
HitPosition.Value)+":";ɪ=-Vector3D.Normalize(Vector3D.Cross(ʀ.HitPosition.Value-ɫ.Value,ɿ.HitPosition.Value-ɫ.Value));}}}}}if(ɪ.
HasValue&&ɫ.HasValue){ř.ĭ("Successfully got mining center and mining normal");if(ɾ!=null){if(ɾ.ɻ.HasValue)IGC.SendUnicastMessage
(ɾ.ɻ.Value,"create-task",new MyTuple<float,Vector3D,Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),ɫ.
Value-ɪ.Value*10,ɪ.Value));}}else{ř.ĭ($"RaycastTaskHandler failed to get castedNormal or castedSurfacePoint");}}}else{ř.ĭ($"RaycastTaskHandler couldn't raycast initial position. Camera '{ɧ.CustomName}' had {ɧ.AvailableScanRange} AvailableScanRange"
);}}else{throw new Exception($"No active cam, {ɭ.Count} known");}}ɽ ɾ;class ɽ{Ċ ż;public Ϙ ɼ{get;private set;}public long
?ɻ;public float?ɺ;public string ɹ="";public string ɸ="";public bool ɷ;public bool ɶ;public Vector3D ɵ(){if(!Ĵ.
miningPlaneNormal.HasValue){var ɥ=ĕ.GetNaturalGravity();if(ɥ==Vector3D.Zero)throw new Exception(
"Need either natural gravity or miningPlaneNormal");else return Vector3D.Normalize(ɥ);}return Ĵ.miningPlaneNormal.Value;}public MinerState ɞ(){return Ĵ.MinerState;}public
ə Ĵ{get{return ɜ.PState;}}Func<string,Ƚ>ɝ;StateWrapper ɜ;public ɽ(IMyGridTerminalSystem Ɗ,IMyIntergridCommunicationSystem
ē,StateWrapper ɜ,Func<string,Ƚ>Í){ɝ=Í;this.Ɗ=Ɗ;è=ē;this.ɜ=ɜ;Σ=ξ<IMyGyro>(U=>U.CustomName.Contains(ForwardGyroTag)&&U.
IsSameConstructAs(me));ĕ=ξ<IMyRemoteControl>(U=>U.IsSameConstructAs(me));ĳ=ξ<IMyShipConnector>(U=>U.IsSameConstructAs(me));Ɗ.
GetBlocksOfType(Φ,Ý=>Ý.IsSameConstructAs(me));Ɗ.GetBlocksOfType(Τ,Ý=>Ý.IsSameConstructAs(me)&&Ý.HasInventory&&((Ý is IMyCargoContainer)
||(Ý is IMyShipDrill)||(Ý is IMyShipConnector)));Ɗ.GetBlocksOfType(η,U=>U.IsSameConstructAs(me));Ɗ.GetBlocksOfType(ζ,U=>U.
IsSameConstructAs(me));List<IMyTimerBlock>Ĉ=new List<IMyTimerBlock>();Ɗ.GetBlocksOfType(Ĉ,U=>U.IsSameConstructAs(me));ż=new Ċ(Ĉ);float ɛ=
0;float ǥ=me.CubeGrid.GridSizeEnum==MyCubeSize.Large?2f:1.5f;foreach(var Ý in Φ){var Ŷ=Vector3D.Reject(Ý.GetPosition()-Σ.
GetPosition(),Σ.WorldMatrix.Forward).Length();ɛ=(float)Math.Max(Ŷ+ǥ,ɛ);}Variables.Set("circular-pattern-shaft-radius",ɛ);var É=new
List<IMyRadioAntenna>();Ɗ.GetBlocksOfType(É,U=>U.IsSameConstructAs(me));ñ=É.FirstOrDefault();var ɤ=new List<IMyLightingBlock
>();Ɗ.GetBlocksOfType(ɤ,U=>U.IsSameConstructAs(me));ε=ɤ.FirstOrDefault();Ɗ.GetBlocksOfType(δ,U=>U.IsSameConstructAs(me));
}public void ɣ(IMyProgrammableBlock ɢ){this.ɢ=ɢ;}public void ɣ(ĵ Î){Υ=Î;}public MinerState ɡ{get;private set;}public void
ě(MinerState ę){ż.ć(ɞ()+".OnExit");ʉ("SetState: "+ɞ()+"=>"+ę);ż.ć(ę+".OnEnter");ɡ=Ĵ.MinerState;Ĵ.MinerState=ę;if((ę==
MinerState.Disabled)||(ę==MinerState.Idle)){Φ.ForEach(Ý=>Ý.Enabled=false);ϣ("command:pillock-mode:Inert",Ú=>Ú.ě(ApckState.Inert));
}}public void ɠ(){ϴ(1,1);ϣ("command:pillock-mode:Disabled",Ú=>Ú.Ž.ě(đ.Ģ.Ġ));Φ.ForEach(Ý=>Ý.Enabled=false);ɜ.
ClearPersistentState();}public void A(string Ě){MinerState ę;if(Enum.TryParse(Ě,out ę))ě(ę);}public ŕ ξ<ŕ>(Func<IMyTerminalBlock,bool>ν)
where ŕ:class{var μ=new List<IMyTerminalBlock>();Ɗ.GetBlocksOfType(μ,U=>((U is ŕ)&&ν(U)));return μ.First()as ŕ;}public void λ
(){var ɥ=ĕ.GetNaturalGravity();if(ɥ!=Vector3D.Zero)Ĵ.miningPlaneNormal=Vector3D.Normalize(ɥ);else Ĵ.miningPlaneNormal=Σ.
WorldMatrix.Forward;double ô;if(ĕ.TryGetPlanetElevation(MyPlanetElevation.Surface,out ô))Ĵ.miningEntryPoint=Σ.WorldMatrix.
Translation+Ĵ.miningPlaneNormal.Value*(ô-5);else Ĵ.miningEntryPoint=Σ.WorldMatrix.Translation;if(ɻ.HasValue){è.SendUnicastMessage(ɻ
.Value,"create-task",new MyTuple<float,Vector3D,Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),Ĵ.
miningEntryPoint.Value,Ĵ.miningPlaneNormal.Value));}}public void κ(){var θ=δ.FirstOrDefault(U=>!U.IsFunctional);var ʲ=new ƾ();ʲ.ƽ=è.Me;ʲ
.Ƽ=Σ.WorldMatrix;ʲ.Ʒ=ĕ.GetShipVelocities().LinearVelocity;ʲ.ƻ=Ϟ;ʲ.ƺ=Variables.Get<float>("battery-low-factor");ʲ.ƿ=ϝ;ʲ.ǂ=
Variables.Get<float>("gas-low-factor");ʲ.ǈ=(θ!=null?θ.CustomName:"");ʲ.Ĝ=Ĵ.MinerState;ʲ.ǐ=Ϝ;ʲ.Ǐ=Variables.Get<float>(
"cargo-full-factor");ʲ.ǎ=Toggle.C.Check("adaptive-mining");ʲ.Ǎ=Ĵ.bRecalled;ʲ.ǌ=ɼ!=null?ɼ.ʰ:0f;ʲ.ǋ=ɼ!=null?ɼ.ʮ.GetValueOrDefault(0f):0f;ʲ.Ǌ=
Ϛ;ʲ.Ć=me.CubeGrid.CustomName;ɼ?.ʳ(ʲ,Ĵ.MinerState);var ʋ=new MyTuple<string,MyTuple<MyTuple<long,string>,MyTuple<MatrixD,
Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,string>>>
,string>();ʋ.Item1=Variables.Get<string>("group-constraint");ʋ.Item2=ʲ.ǉ();ʋ.Item3=Ver;ʌ("miners.handshake",ʋ);}public
void ů(List<MyIGCMessage>ɚ){Ϗ();ό();switch(Ĵ.MinerState){default:ψ();break;case MinerState.Disabled:case MinerState.Idle:
case MinerState.GoingToEntry:case MinerState.WaitingForDocking:case MinerState.ReturningToShaft:case MinerState.Takeoff:case
MinerState.ReturningHome:case MinerState.Docking:break;}ř.Œ(Υ!=null?"Embedded APck":ɢ.CustomName);Υ?.ů(ʑ,ř.Œ);if((ɼ!=null)&&(!ɷ)){
if(ɻ.HasValue)ɼ.ʷ(Ĵ.MinerState);}var ƍ=ɼ;var ι=è.RegisterBroadcastListener("miners");foreach(var ɯ in ɚ){if(!ɯ.Tag.
Contains("set-vectors"))Ω(ɯ,false);if((ɯ.Tag=="miners.assign-shaft")&&(ɯ.Data is MyTuple<int,Vector3D,Vector3D,MyTuple<float,
float,float,bool,bool>>)){var ʋ=(MyTuple<int,Vector3D,Vector3D,MyTuple<float,float,float,bool,bool>>)ɯ.Data;if(ƍ!=null){ƍ.ʺ(ʋ
.Item1,ʋ.Item2,ʋ.Item3);Ĵ.maxDepth=ʋ.Item4.Item1;Ĵ.skipDepth=ʋ.Item4.Item2;Ĵ.leastDepth=ʋ.Item4.Item3;Toggle.C.Set(
"adaptive-mining",ʋ.Item4.Item4);Toggle.C.Set("adjust-entry-by-elevation",ʋ.Item4.Item5);ʉ("Got new ShaftVectors");π();}}if(ɯ.Tag==
"miners.handshake.reply"){ʉ("Received reply from dispatcher "+ɯ.Source);ɻ=ɯ.Source;}if(ɯ.Tag=="miners.echelon"){ʉ("Was assigned an echelon of "+
ɯ.Data);ɺ=(float)ɯ.Data;}if(ɯ.Tag=="miners.normal"){var ή=(Vector3D)ɯ.Data;ʉ("Was assigned a normal of "+ή);Ĵ.
miningPlaneNormal=ή;}if(ɯ.Tag=="miners.resume"){var ή=(Vector3D)ɯ.Data;ʉ(
"Received resume command. Clearing state, running MineCommandHandler, assigned a normal of "+ή);ɜ.ClearPersistentState();Ĵ.miningPlaneNormal=ή;β();}if(ɯ.Tag=="command"){if(ɯ.Data.ToString()=="force-finish")ϧ();if
(ɯ.Data.ToString()=="mine")β();}if(ɯ.Tag=="set-value"){var ǘ=((string)ɯ.Data).Split(':');ʉ(
$"Set value '{ǘ[0]}' to '{ǘ[1]}'");Variables.Set(ǘ[0],ǘ[1]);}if(ɯ.Data.ToString().Contains("common-airspace-lock-granted")){var υ=ɯ.Data.ToString().Split
(':')[1];if(!string.IsNullOrEmpty(ɹ)&&(ɹ!=υ)){ʉ($"{υ} common-airspace-lock hides current ObtainedLock {ɹ}!");}ɹ=υ;ʉ(υ+
" common-airspace-lock-granted");if(ɸ==υ||(ɸ==LOCK_NAME_MiningSection&&υ==LOCK_NAME_GeneralSection))π();}if(ɯ.Tag=="report.request"){var θ=δ.
FirstOrDefault(U=>!U.IsFunctional);var ʲ=new ƾ();ʲ.ƽ=è.Me;ʲ.Ƽ=Σ.WorldMatrix;ʲ.Ʒ=ĕ.GetShipVelocities().LinearVelocity;ʲ.ƻ=Ϟ;ʲ.ƺ=
Variables.Get<float>("battery-low-factor");ʲ.ƿ=ϝ;ʲ.ǂ=Variables.Get<float>("gas-low-factor");ʲ.ǈ=(θ!=null?θ.CustomName:"");ʲ.Ĝ=Ĵ.
MinerState;ʲ.ǐ=Ϝ;ʲ.Ǐ=Variables.Get<float>("cargo-full-factor");ʲ.ǎ=Toggle.C.Check("adaptive-mining");ʲ.Ǎ=Ĵ.bRecalled;ʲ.ǌ=ɼ!=null?ɼ
.ʰ:0f;ʲ.ǋ=ɼ!=null?ɼ.ʮ.GetValueOrDefault(0f):0f;ʲ.Ǌ=Ϛ;ʲ.Ć=me.CubeGrid.CustomName;ɼ?.ʳ(ʲ,Ĵ.MinerState);è.
SendBroadcastMessage("miners.report",ʲ.ǉ());}}while(ι.HasPendingMessage){var ɯ=ι.AcceptMessage();Ω(ɯ,false);if(ɯ.Data!=null){if(ɯ.Data.
ToString().Contains("common-airspace-lock-released")){var ς=ɯ.Data.ToString().Split(':')[1];ʉ(
"(Agent) received lock-released notification "+ς+" from "+ɯ.Source);}if(ɯ.Data.ToString()=="dispatcher-change"){ɻ=null;Ĩ.Ħ.ĺ(()=>!ɻ.HasValue).ľ(1000).ļ(()=>κ());}}}}
Queue<Action<ɽ>>τ=new Queue<Action<ɽ>>();public void σ(string ς,Action<ɽ>ρ){ɷ=true;if(!string.IsNullOrEmpty(ς))ɸ=ς;τ.Enqueue(
ρ);ʉ("WaitForDispatch section \""+ς+"\", callback chain: "+τ.Count);}public void π(){ɷ=false;ɸ="";var ο=τ.Count;if(ο>0){ʉ
("Dispatching, callback chain: "+ο);var Ƶ=τ.Dequeue();Ƶ.Invoke(this);}else ʉ("WARNING: empty Dispatch()");}public void ʌ<
ŕ>(string ũ,ŕ ʋ){è.SendBroadcastMessage(ũ,ʋ,TransmissionDistance.TransmissionDistanceMax);Ω(ʋ,true);}public void Ϊ<ŕ>(
string ũ,ŕ ʋ){if(ɻ.HasValue)è.SendUnicastMessage(ɻ.Value,ũ,ʋ);}public void ʉ(object ɯ){ř.ĭ($"MinerController -> {ɯ}");}public
void Ω(object ɯ,bool Ψ){string ʋ=ɯ.GetType().Name;if(ɯ is string)ʋ=(string)ɯ;else if((ɯ is ImmutableArray<Vector3D>)||(ɯ is
Vector3D))ʋ="some vector(s)";if(Toggle.C.Check("log-message")){if(!Ψ)ř.ĭ($"MinerController MSG-IN -> {ʋ}");else ř.ĭ(
$"MinerController MSG-OUT -> {ʋ}");}}public Action Χ;public IMyProgrammableBlock ɢ;ĵ Υ;public IMyGridTerminalSystem Ɗ;public
IMyIntergridCommunicationSystem è;public IMyRemoteControl ĕ;public List<IMyTerminalBlock>Τ=new List<IMyTerminalBlock>();public IMyTerminalBlock Σ;
public List<IMyShipDrill>Φ=new List<IMyShipDrill>();public IMyShipConnector ĳ;public IMyRadioAntenna ñ;public List<
IMyBatteryBlock>η=new List<IMyBatteryBlock>();public List<IMyGasTank>ζ=new List<IMyGasTank>();public IMyLightingBlock ε;public List<
IMyTerminalBlock>δ=new List<IMyTerminalBlock>();public void γ(){ɼ=new Ϙ(this);ɼ.ʴ=DateTime.Now;}public void β(){ɼ=new Ϙ(this);ɼ.ʴ=
DateTime.Now;Ĵ.LifetimeAcceptedTasks++;if(!ϫ()){ɼ.ϕ();}}public void α(){if(ɼ!=null){ɼ.ʻ();}}public void ΰ(string[]ɨ){if((ɨ.
Length>2)&&(ɨ[2]=="clear"))Ĵ.StaticDockOverride=null;else Ĵ.StaticDockOverride=Σ.WorldMatrix.Translation;}public Vector3D ί(
Vector3D ȓ){if(ɺ.HasValue){return ȓ-ɵ()*ɺ.Value;}return ȓ;}public Vector3D ί(Vector3D ȓ,Vector3D ή){if(ɺ.HasValue){return ȓ-ή*ɺ.
Value;}return ȓ;}public bool έ(bool ά){if(Ĵ.StaticDockOverride.HasValue){string Ϋ=
"command:create-wp:Name=StaticDock,Ng=Forward:"+ͽ.ͼ(Ĵ.StaticDockOverride.Value);if(!WholeAirspaceLocking)ϥ(LOCK_NAME_GeneralSection);if(ɺ.HasValue){Ϋ=
"command:create-wp:Name=StaticDock.echelon,Ng=Forward:"+ͽ.ͼ(ί(Ĵ.StaticDockOverride.Value))+":"+Ϋ;}if(ά){if(Ĵ.getAbovePt.HasValue)ϣ(
"command:create-wp:Name=StaticDock.getAbovePt,Ng=Forward,SpeedLimit="+Variables.Get<float>("speed-clear")+":"+ͽ.ͼ(ί(Ĵ.getAbovePt.Value))+":"+Ϋ);else{var φ=Ĵ.StaticDockOverride.Value;
Vector3D ţ;ĕ.TryGetPlanetPosition(out ţ);var χ=φ-ţ;var Ϭ=(Σ.GetPosition()-ţ);var ʿ=Vector3D.Normalize(Ϭ);var Ϫ=χ.Length()>Ϭ.
Length()?χ:Ϭ;var ϩ=ţ+ʿ*(Ϫ.Length()+100f);ϣ("command:create-wp:Name=StaticDock.approachP,Ng=Forward:"+ͽ.ͼ(ϩ)+":"+Ϋ);}}else{ϣ(Ϋ)
;}return true;}return false;}public void Ϩ(){if(Ĵ.StaticDockOverride.HasValue){if(έ(false)){if(!WholeAirspaceLocking)ϥ(ɹ)
;ě(MinerState.Docking);}}else{if(ɻ.HasValue){if(!WholeAirspaceLocking)ϥ(ɹ);Χ?.Invoke();è.SendUnicastMessage(ɻ.Value,
"apck.docking.request",ĳ.GetPosition());ě(MinerState.WaitingForDocking);}}}public void ϧ(){if(ĳ.Status==MyShipConnectorStatus.Connected){η.
ForEach(U=>U.ChargeMode=ChargeMode.Recharge);ζ.ForEach(U=>U.Stockpile=true);Ĵ.lastAPckCommand="";ě(MinerState.Disabled);}else Ĵ
.bRecalled=true;}public bool ϫ(){if(ĳ.Status!=MyShipConnectorStatus.Connected)return false;if(Ĵ.getAbovePt.HasValue){ě(
MinerState.Docked);return true;}Ϊ("request-new","");σ("",ʵ=>{ʵ.ě(MinerState.Docked);});return true;}public void Ϧ(string ς,Action<
ɽ>Ů){if(ɹ==ς){Ů.Invoke(this);return;}ʌ("miners","common-airspace-ask-for-lock:"+ς);σ(ς,Ů);}public void ϥ(string ς){if(ɹ==
ς){ɹ=null;ʌ("miners","common-airspace-lock-released:"+ς);ʉ($"Released lock: {ς}");}else{ʉ(
"Tried to release non-owned lock section "+ς);}}public ʖ Ϥ;public void ϣ(string Ļ,Action<ĵ>Ϣ=null){Ĵ.lastAPckCommand=Ļ;ř.ĭ("CommandAutoPillock: "+Ļ);if(Υ!=null){
if(Ϣ!=null){Ϣ(Υ);}else{var ϭ=Ļ.Split(new[]{"],["},StringSplitOptions.RemoveEmptyEntries).Select(Ê=>Ê.Trim('[',']')).ToList
();foreach(var Ǯ in ϭ){string[]ʈ=Ǯ.Split(new[]{':'},StringSplitOptions.RemoveEmptyEntries);if(ʈ[0]=="command"){Ϥ.ʓ(ʈ[1],ʈ
);}}}}else{if(è.IsEndpointReachable(ɢ.EntityId)){è.SendUnicastMessage(ɢ.EntityId,"apck.command",Ļ);}else{throw new
Exception($"APck {ɢ.EntityId} is not reachable");}}}DateTime ϸ;bool Ϸ(float ϳ,float ϲ){var ϵ=DateTime.Now;if((ϵ-ϸ).TotalSeconds>
60){ϸ=ϵ;return ϴ(ϳ,ϲ);}return true;}bool ϴ(float ϳ,float ϲ){δ.ForEach(O=>Ϯ(O,ϱ(O),true));if(δ.Any(U=>!U.IsFunctional)){if(
ñ!=null)ñ.CustomName=ñ.CubeGrid.CustomName+"> Damaged. Fix me asap!";δ.Where(U=>!U.IsFunctional).ToList().ForEach(U=>ř.ĭ(
$"{U.CustomName} is damaged or destroyed"));return false;}if(ζ.Any()&&(ϝ<ϲ)){if(ñ!=null)ñ.CustomName=
$"{ñ.CubeGrid.CustomName}> Maintenance. Gas level: {ϝ:f2}/{ϲ:f2}";return false;}if(Ϟ<ϳ){if(ñ!=null)ñ.CustomName=$"{ñ.CubeGrid.CustomName}> Maintenance. Charge level: {Ϟ:f2}/{ϳ:f2}";
return false;}return true;}float ϱ(IMyTerminalBlock ϰ){IMySlimBlock ϯ=ϰ.CubeGrid.GetCubeBlock(ϰ.Position);if(ϯ!=null)return(ϯ.
BuildIntegrity-ϯ.CurrentDamage)/ϯ.MaxIntegrity;else return 1f;}void Ϯ(IMyTerminalBlock ϐ,float ϡ,bool ϟ){string Ć=ϐ.CustomName;if((ϡ<
1f)&&(!ϟ||!ϐ.IsFunctional)){if(!(ϐ is IMyRadioAntenna)&&!(ϐ is IMyBeacon)){ϐ.SetValue("ShowOnHUD",true);}string ϓ;if(Ć.
Contains("||")){string ϒ=@"(?<=DAMAGED: )(?<label>\d+)(?=%)";System.Text.RegularExpressions.Regex Ŷ=new System.Text.
RegularExpressions.Regex(ϒ);ϓ=Ŷ.Replace(Ć,delegate(System.Text.RegularExpressions.Match ɮ){return(ϡ*100).ToString("F0");});}else{ϓ=string.
Format("{0} || DAMAGED: {1}%",Ć,ϡ.ToString("F0"));ʉ($"{Ć} was damaged. Showing on HUD.");}ϐ.CustomName=ϓ;}else{ϑ(ϐ);}}void ϑ(
IMyTerminalBlock ϐ){if(ϐ.CustomName.Contains("||")){string Ć=ϐ.CustomName;ϐ.CustomName=Ć.Split('|')[0].Trim();if(!(ϐ is IMyRadioAntenna)
&&!(ϐ is IMyBeacon)){ϐ.SetValue("ShowOnHUD",false);}ʉ($"{ϐ.CustomName} was fixed.");}}void Ϗ(){float ώ=0;float ύ=0;foreach
(var U in η){ύ+=U.MaxStoredPower;ώ+=U.CurrentStoredPower;}Ϟ=(ύ>0?ώ/ύ:1f);}void ό(){float ϋ=0;float ϊ=0;double ω=0;foreach
(var U in ζ){ϋ+=U.Capacity*(float)U.FilledRatio;ϊ+=U.Capacity;ω+=U.FilledRatio;}ϝ=(ϊ>0?ϋ/ϊ:1f);}void ψ(){float ϔ=0;float
Ϡ=0;ϛ=0;for(int Ǯ=0;Ǯ<Τ.Count;Ǯ++){var Ʃ=Τ[Ǯ].GetInventory(0);if(Ʃ==null)continue;ϔ+=(float)Ʃ.MaxVolume;Ϡ+=(float)Ʃ.
CurrentVolume;ϛ+=(float)Ʃ.CurrentMass;}Ϝ=(ϔ>0?Ϡ/ϔ:1f);}float Ϟ;float ϝ;float Ϝ;float ϛ;bool Ϛ;bool ϙ(){return Ϝ>=Variables.Get<float>
("cargo-full-factor");}public class Ϙ{protected ɽ P;bool ϗ(double ϖ){return(!P.Ĵ.currentWp.HasValue||(P.Ĵ.currentWp.Value
-P.Σ.WorldMatrix.Translation).Length()<=ϖ);}public Ϙ(ɽ ɾ){P=ɾ;}public void ϕ(){P.Ϊ("request-new","");P.σ("",ʵ=>{P.Ϧ(
LOCK_NAME_MiningSection,O=>{O.ě(MinerState.ChangingShaft);O.Φ.ForEach(Ý=>Ý.Enabled=false);var ʭ=-15;var ȓ=O.ί(P.Ĵ.miningEntryPoint.Value+P.ɵ()*
ʭ);var ʥ=$"command:create-wp:Name=ChangingShaft,Ng=Forward,UpNormal=1;0;0,"+$"AimNormal={ͽ.ͼ(P.ɵ()).Replace(':',';')}"+
$":{ͽ.ͼ(ȓ)}";P.ϣ(ʥ);P.Ĵ.currentWp=ȓ;});});}public void ʻ(){if(P.Ĵ.CurrentJobMaxShaftYield<ˁ+ʧ-ʦ)P.Ĵ.CurrentJobMaxShaftYield=ˁ+ʧ-ʦ;if
(Toggle.C.Check("adaptive-mining")){if(!ʮ.HasValue||((ˁ+ʧ-ʦ)/P.Ĵ.CurrentJobMaxShaftYield<0.5f)){P.Ϊ("ban-direction",P.Ĵ.
CurrentShaftId.Value);}}ʨ();ʮ=null;var ʭ=-15;var ȓ=P.Ĵ.miningEntryPoint.Value+P.ɵ()*ʭ;P.Ϊ("shaft-complete-request-new",P.Ĵ.
CurrentShaftId.Value);P.σ("",ʵ=>{P.Ϧ(LOCK_NAME_MiningSection,O=>{O.ě(MinerState.ChangingShaft);O.Φ.ForEach(Ý=>Ý.Enabled=false);O.ϣ(
"command:create-wp:Name=ChangingShaft,Ng=Forward:"+ͽ.ͼ(ȓ));P.Ĵ.currentWp=ȓ;});});}public void ʺ(int ʒ,Vector3D ʹ,Vector3D ʸ){P.Ĵ.miningEntryPoint=ʹ;P.Ĵ.getAbovePt=ʸ;P.Ĵ.
CurrentShaftId=ʒ;}public void ʷ(MinerState Ĝ){if(Ĝ==MinerState.GoingToEntry){if(P.Ĵ.bRecalled){P.ě(MinerState.GoingToUnload);P.Φ.
ForEach(Ý=>Ý.Enabled=false);var ȓ=P.ί(P.Ĵ.getAbovePt.Value);P.ϣ("command:create-wp:Name=GoingToUnload,Ng=Forward:"+ͽ.ͼ(ȓ));P.Ĵ.
currentWp=ȓ;return;}if(!ϗ(0.5f))return;P.ϥ(P.ɹ);P.Φ.ForEach(Ý=>Ý.Enabled=true);P.ě(MinerState.Drilling);P.ϣ(
"command:create-wp:Name=drill,Ng=Forward,PosDirectionOverride=Forward"+",AimNormal="+ͽ.ͼ(P.ɵ()).Replace(':',';')+",UpNormal=1;0;0,SpeedLimit="+Variables.Get<float>("speed-drill")+":0:0:0");}
else if(Ĝ==MinerState.Drilling){ʰ=(float)(P.Σ.WorldMatrix.Translation-P.Ĵ.miningEntryPoint.Value).Length();ř.Œ(
$"Depth: current: {ʰ:f1} skip: {P.Ĵ.skipDepth:f1}");ř.Œ($"Depth: least: {P.Ĵ.leastDepth:f1} max: {P.Ĵ.maxDepth:f1}");ř.Œ($"Cargo: {P.Ϝ:f2} / "+Variables.Get<float>(
"cargo-full-factor").ToString("f2"));if(P.Ĵ.bRecalled){ʤ();return;}if(ʰ>P.Ĵ.maxDepth){ʤ();return;}if(!P.Ϸ(Variables.Get<float>(
"battery-low-factor"),Variables.Get<float>("gas-low-factor"))){ʤ();return;}if(P.ϙ()){ʤ();return;}if(ʰ<=P.Ĵ.skipDepth){P.Φ.ForEach(Ý=>Ý.
UseConveyorSystem=false);return;}P.Φ.ForEach(Ý=>Ý.UseConveyorSystem=true);bool ʶ=ˆ();if(ʶ)ʮ=Math.Max(ʰ,ʮ??0);if(ʰ<=P.Ĵ.leastDepth)return;
if(ʶ){if((!ʬ.HasValue)||(ʬ>ʰ))ʬ=ʰ;if((!ʫ.HasValue)||(ʫ<ʰ))ʫ=ʰ;if(Toggle.C.Check("adaptive-mining")){P.Ĵ.skipDepth=ʬ.Value-
2f;P.Ĵ.maxDepth=ʫ.Value+2f;}}else{if(ʮ.HasValue&&(ʰ-ʮ>2))ʤ();}}if(Ĝ==MinerState.AscendingInShaft){if(!ϗ(0.5f))return;if(P.
Ĵ.bRecalled||P.ϙ()||!P.ϴ(Variables.Get<float>("battery-low-factor"),Variables.Get<float>("gas-low-factor"))){P.Ϧ(
LOCK_NAME_MiningSection,ʵ=>{ʵ.ě(MinerState.GoingToUnload);ʵ.Φ.ForEach(Ý=>Ý.Enabled=false);var ȓ=P.ί(P.Ĵ.getAbovePt.Value);ʵ.ϣ(
"command:create-wp:Name=GoingToUnload,Ng=Forward:"+ͽ.ͼ(ȓ));P.Ĵ.currentWp=ȓ;});}else{ʻ();}}else if(Ĝ==MinerState.ChangingShaft){if(ϗ(0.5f)){var ʭ=-15;var ȓ=P.Ĵ.
miningEntryPoint.Value+P.ɵ()*ʭ;P.ί(ȓ);P.ϣ("command:create-wp:Name=GoingToEntry (ChangingShaft),Ng=Forward:"+ͽ.ͼ(ȓ));P.Ĵ.currentWp=ȓ;P.ě(
MinerState.ReturningToShaft);}}if(Ĝ==MinerState.Takeoff){if(!ϗ(1.0f))return;if(P.ɹ!=null)P.ϥ(P.ɹ);if(P.Ĵ.bRecalled){P.Ϩ();return;}
P.ϣ("command:create-wp:Name=xy,Ng=Forward,AimNormal="+ͽ.ͼ(P.ɵ()).Replace(':',';')+":"+ͽ.ͼ(P.ί(P.Ĵ.getAbovePt.Value)));P.Ĵ
.currentWp=P.ί(P.Ĵ.getAbovePt.Value);P.ě(MinerState.ReturningToShaft);return;}else if(Ĝ==MinerState.ReturningToShaft){if(
P.Ĵ.bRecalled){if(P.ɷ){P.ʌ("miners","common-airspace-ask-for-lock:");P.ɷ=false;P.τ.Clear();}P.Ϩ();return;}if(!ϗ(1.0f))
return;P.Ϧ(LOCK_NAME_MiningSection,ʵ=>{ʵ.ě(MinerState.GoingToEntry);P.Φ.ForEach(Ý=>Ý.Enabled=true);var ˀ=
$"command:create-wp:Name=drill entry,Ng=Forward,UpNormal=1;0;0,AimNormal="+$"{ͽ.ͼ(P.ɵ()).Replace(':',';')}:";double ô;if(Toggle.C.Check("adjust-entry-by-elevation")&&P.ĕ.TryGetPlanetElevation(
MyPlanetElevation.Surface,out ô)){Vector3D ţ;P.ĕ.TryGetPlanetPosition(out ţ);var ʿ=Vector3D.Normalize(P.Ĵ.miningEntryPoint.Value-ţ);var Ī
=(P.Σ.WorldMatrix.Translation-ţ).Length()-ô+5f;var ʾ=ţ+ʿ*Ī;ʵ.ϣ(ˀ+ͽ.ͼ(ʾ));P.Ĵ.currentWp=ʾ;}else{ʵ.ϣ(ˀ+ͽ.ͼ(P.Ĵ.
miningEntryPoint.Value));P.Ĵ.currentWp=P.Ĵ.miningEntryPoint;}});}if(Ĝ==MinerState.GoingToUnload){if(ϗ(0.5f)){P.Ϩ();}}else if(Ĝ==
MinerState.ReturningHome){if(!ϗ(1.0f))return;P.Ϧ(LOCK_NAME_BaseSection,ʵ=>{Ƚ ʽ=P.ɝ("docking");Vector3D ʼ=P.ί(ʽ.ȹ.Value,ʽ.ȷ.Value.
Backward)-ʽ.ȷ.Value.Backward*Variables.Get<float>("getAbove-altitude");P.ϣ(
"command:create-wp:Name=DynamicDock.echelon,Ng=Forward,AimNormal="+ͽ.ͼ(P.ɵ()).Replace(':',';')+",TransformChannel=docking:"+ͽ.ͼ(Vector3D.Transform(ʼ,MatrixD.Invert(ʽ.ȷ.Value)))+
":command:pillock-mode:DockingFinal");P.ě(MinerState.Docking);});}else if(Ĝ==MinerState.WaitingForDocking){Ƚ ʽ=P.ɝ("docking");if(!ʽ.ȹ.HasValue)return;
Vector3D ʼ=P.ί(ʽ.ȹ.Value,ʽ.ȷ.Value.Backward)-ʽ.ȷ.Value.Backward*Variables.Get<float>("getAbove-altitude");P.ϣ(
"command:create-wp:Name=xy,Ng=Forward,AimNormal="+ͽ.ͼ(P.ɵ()).Replace(':',';')+":"+ͽ.ͼ(ʼ));P.Ĵ.currentWp=ʼ;P.ě(MinerState.ReturningHome);}if(Ĝ==MinerState.Docking){if(P.ĳ
.Status!=MyShipConnectorStatus.Connected){if(P.ɶ)P.ɶ=false;if(P.Ĵ.StaticDockOverride.HasValue)P.ĳ.Connect();return;}if(!P
.ɶ){P.ɶ=true;ř.ĭ("Regular docking handled");P.ϣ("command:pillock-mode:Disabled");P.ĕ.DampenersOverride=false;P.η.ForEach(
U=>U.ChargeMode=ChargeMode.Recharge);P.ĳ.OtherConnector.CustomData="";P.Χ?.Invoke();P.ζ.ForEach(U=>U.Stockpile=true);if(P
.ɹ!=null)P.ϥ(P.ɹ);P.ě(MinerState.Docked);}}if(Ĝ==MinerState.Docked){ř.Œ("Docking: Connected");if(P.Ϛ=!ʢ()){ř.Œ(
"Docking: still have items");return;}if(!P.ϴ(Variables.Get<float>("battery-low-factor"),Variables.Get<float>("gas-low-factor"))){P.ě(MinerState.
Maintenance);P.Ĵ.LifetimeWentToMaintenance++;Ĩ.Ħ.ľ(10000).ĺ(()=>P.ɞ()==MinerState.Maintenance).ļ(()=>{if(P.ϴ(Variables.Get<float>(
"battery-full-factor"),0.99f)){P.ě(MinerState.Docked);}});return;}if(P.Ĵ.bRecalled){P.ě(MinerState.Disabled);P.Ĵ.bRecalled=false;P.Υ?.ų();ʩ()
;P.Ĵ.LifetimeOperationTime+=(int)(DateTime.Now-ʴ).TotalSeconds;P.ɜ.Save();P.ɼ=null;return;}P.Ϧ(LOCK_NAME_BaseSection,ʵ=>{
P.η.ForEach(U=>U.ChargeMode=ChargeMode.Auto);P.ζ.ForEach(U=>U.Stockpile=false);ʩ();Ή(P.ĳ.OtherConnector);P.ĳ.Disconnect()
;P.ě(MinerState.Takeoff);});}else if(Ĝ==MinerState.Maintenance){if((P.ɡ!=MinerState.Docking)&&(P.ĳ.Status==
MyShipConnectorStatus.Connected)){P.ϣ("command:pillock-mode:Disabled");Ĩ.Ħ.ľ(10000).ĺ(()=>P.ɞ()==MinerState.Maintenance).ļ(()=>{if(P.ϴ(
Variables.Get<float>("battery-full-factor"),0.99f)){P.ě(MinerState.Docking);}});}}}bool ʢ(){var ʡ=new List<IMyCargoContainer>();P
.Ɗ.GetBlocksOfType(ʡ,U=>U.IsSameConstructAs(P.ĳ.OtherConnector)&&U.HasInventory&&U.IsFunctional&&(U is IMyCargoContainer)
);var ʠ=P.Τ.Select(P=>P.GetInventory()).Where(Ǯ=>Ǯ.ItemCount>0);if(!ʠ.Any())return true;ř.Œ("Docking: still have items");
foreach(var ʟ in ʠ){var ʣ=new List<MyInventoryItem>();ʟ.GetItems(ʣ);for(int ƌ=0;ƌ<ʣ.Count;ƌ++){var ʞ=ʣ[ƌ];IMyInventory ʝ;var ʜ=
Variables.Get<string>("preferred-container");if(!string.IsNullOrEmpty(ʜ))ʝ=ʡ.Where(O=>O.CustomName.Contains(ʜ)).Select(P=>P.
GetInventory()).FirstOrDefault();else ʝ=ʡ.Select(P=>P.GetInventory()).Where(Ǯ=>Ǯ.CanItemsBeAdded((MyFixedPoint)(1f),ʞ.Type)).OrderBy
(Ǯ=>(float)Ǯ.CurrentVolume).FirstOrDefault();if(ʝ!=null){if(!ʟ.TransferItemTo(ʝ,ʣ[ƌ])){ř.Œ(
"Docking: failing to transfer from "+(ʟ.Owner as IMyTerminalBlock).CustomName+" to "+(ʝ.Owner as IMyTerminalBlock).CustomName);}}}}return false;}void ʤ(){ʰ=
0;P.ě(MinerState.AscendingInShaft);var ʭ=Math.Min(8,(P.Σ.WorldMatrix.Translation-P.Ĵ.miningEntryPoint.Value).Length());
var ȓ=P.Ĵ.miningEntryPoint.Value+P.ɵ()*ʭ;P.ϣ("command:create-wp:Name=AscendingInShaft,Ng=Forward"+",AimNormal="+ͽ.ͼ(P.ɵ()).
Replace(':',';')+",UpNormal=1;0;0,SpeedLimit="+Variables.Get<float>("speed-clear")+":"+ͽ.ͼ(ȓ));P.Ĵ.currentWp=ȓ;}public void ʳ(ƾ
ʲ,MinerState Ĝ){var U=ImmutableArray.CreateBuilder<MyTuple<string,string>>(10);U.Add(new MyTuple<string,string>(
"Lock\nrequested",P.ɸ));U.Add(new MyTuple<string,string>("Lock\nowned",P.ɹ));ʲ.Ǒ=U.ToImmutableArray();}StringBuilder ʱ=new StringBuilder(
);public override string ToString(){ʱ.Clear();ʱ.AppendFormat("session uptime: {0}\n",(ʴ==default(DateTime)?"-":(DateTime.
Now-ʴ).ToString()));ʱ.AppendFormat("session ore mass: {0}\n",ʯ);ʱ.AppendFormat("cargoFullness: {0:f2}\n",P.Ϝ);ʱ.
AppendFormat("cargoMass: {0:f2}\n",P.ϛ);ʱ.AppendFormat("cargoYield: {0:f2}\n",ˁ);ʱ.AppendFormat("lastFoundOreDepth: {0}\n",ʮ.
HasValue?ʮ.Value.ToString("f2"):"-");ʱ.AppendFormat("minFoundOreDepth: {0}\n",ʬ.HasValue?ʬ.Value.ToString("f2"):"-");ʱ.
AppendFormat("maxFoundOreDepth: {0}\n",ʫ.HasValue?ʫ.Value.ToString("f2"):"-");ʱ.AppendFormat("shaft id: {0}\n",P.Ĵ.CurrentShaftId??-
1);return ʱ.ToString();}public float ʰ;public float ʯ;public DateTime ʴ;public float?ʮ;float?ʬ{get{return P.Ĵ.
minFoundOreDepth;}set{P.Ĵ.minFoundOreDepth=value;}}float?ʫ{get{return P.Ĵ.maxFoundOreDepth;}set{P.Ĵ.maxFoundOreDepth=value;}}public
float ʪ(){return ˁ+ʧ-ʦ;}void ʩ(){ʯ+=P.ϛ;P.Ĵ.LifetimeOreAmount+=P.ϛ;P.Ĵ.LifetimeYield+=ˁ;ʧ+=ˁ-ʦ;ʦ=0;ˁ=0;}void ʨ(){ʦ=ˁ;ʧ=0;}
float ʧ=0;float ʦ=0;float ˁ=0;bool ˆ(){float Π=0;for(int Ǯ=0;Ǯ<P.Τ.Count;Ǯ++){var Ʃ=P.Τ[Ǯ].GetInventory(0);if(Ʃ==null)
continue;List<MyInventoryItem>ʣ=new List<MyInventoryItem>();Ʃ.GetItems(ʣ);ʣ.Where(Ό=>Ό.Type.ToString().Contains("Ore")&&!Ό.Type.
ToString().Contains("Stone")).ToList().ForEach(O=>Π+=(float)O.Amount);}bool Ί=false;if((ˁ>0)&&(Π>ˁ)){Ί=true;}ˁ=Π;return Ί;}void
Ή(IMyShipConnector Έ){var Ύ=P.ί(Έ.WorldMatrix.Translation,Έ.WorldMatrix.Backward)-Έ.WorldMatrix.Backward*Variables.Get<
float>("getAbove-altitude");var Ά="[command:pillock-mode:Disabled],[command:create-wp:Name=Dock.Echelon,Ng=Forward:"+ͽ.ͼ(Ύ)+
"]";P.ϣ(Ά);P.Ĵ.currentWp=Ύ;}}}static class ͽ{public static string ͼ(params Vector3D[]ͻ){return string.Join(":",ͻ.Select(Ʒ=>
string.Format("{0}:{1}:{2}",Ʒ.X,Ʒ.Y,Ʒ.Z)));}public static string ͺ(MatrixD ͷ){StringBuilder ʱ=new StringBuilder();for(int Ǯ=0;
Ǯ<4;Ǯ++){for(int ƍ=0;ƍ<4;ƍ++){ʱ.Append(ͷ[Ǯ,ƍ]+":");}}return ʱ.ToString().TrimEnd(':');}public static Vector3D Ώ(MatrixD Ρ
,Vector3D Ο,BoundingSphereD Ξ){RayD Ŷ=new RayD(Ρ.Translation,Vector3D.Normalize(Ο-Ρ.Translation));double?Ν=Ŷ.Intersects(Ξ
);if(Ν.HasValue){var Μ=Vector3D.Normalize(Ξ.Center-Ρ.Translation);if(Ξ.Contains(Ρ.Translation)==ContainmentType.Contains)
return Ξ.Center-Μ*Ξ.Radius;var Λ=Vector3D.Cross(Ŷ.Direction,Μ);Vector3D Κ;if(Λ.Length()<double.Epsilon)Μ.
CalculatePerpendicularVector(out Κ);else Κ=Vector3D.Cross(Λ,-Μ);return Ξ.Center+Vector3D.Normalize(Κ)*Ξ.Radius;}return Ο;}public static Vector3D Ι(
Vector3D Θ,MatrixD Η,MatrixD Ζ,Vector3D Ε,ref Vector3D Δ){var Ǚ=Η.Up;var Γ=Vector3D.ProjectOnPlane(ref Θ,ref Ǚ);var Β=-(float)
Math.Atan2(Vector3D.Dot(Vector3D.Cross(Η.Forward,Γ),Ǚ),Vector3D.Dot(Η.Forward,Γ));Ǚ=Η.Right;Γ=Vector3D.ProjectOnPlane(ref Θ,
ref Ǚ);var Α=-(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(Η.Forward,Γ),Ǚ),Vector3D.Dot(Η.Forward,Γ));float ΐ=0;if((Ε!=
Vector3D.Zero)&&(Math.Abs(Β)<.05f)&&(Math.Abs(Α)<.05f)){Ǚ=Η.Forward;Γ=Vector3D.ProjectOnPlane(ref Ε,ref Ǚ);ΐ=-(float)Math.Atan2(
Vector3D.Dot(Vector3D.Cross(Η.Down,Γ),Ǚ),Vector3D.Dot(Η.Up,Γ));}var Ͷ=new Vector3D(Α,Β,ΐ*Variables.Get<float>(
"roll-power-factor"));Δ=new Vector3D(Math.Abs(Ͷ.X),Math.Abs(Ͷ.Y),Math.Abs(Ͷ.Z));var ˢ=Vector3D.TransformNormal(Ͷ,Η);var Ƶ=Vector3D.
TransformNormal(ˢ,MatrixD.Transpose(Ζ));Ƶ.X*=-1;return Ƶ;}public static void ˠ(IMyGyro ˑ,Vector3 ː,Vector3D ˏ){float ˎ=ː.Y;float ˍ=ː.X;
float ˌ=ː.Z;var ˡ=IsLargeGrid?30:60;var ˋ=new Vector3D(1.92f,1.92f,1.92f);Func<double,double,double,double>ˉ=(O,Ʒ,Ƶ)=>{var ˈ=
Math.Abs(O);double Ŷ;if(ˈ>(Ʒ*Ʒ*1.7)/(2*Ƶ))Ŷ=ˡ*Math.Sign(O)*Math.Max(Math.Min(ˈ,1),0.002);else{Ŷ=-ˡ*Math.Sign(O)*Math.Max(
Math.Min(ˈ,1),0.002);}return Ŷ*0.6;};var ˇ=(float)ˉ(ˎ,ˏ.Y,ˋ.Y);var ˊ=(float)ˉ(ˍ,ˏ.X,ˋ.X);var ˣ=(float)ˉ(ˌ,ˏ.Z,ˋ.Z);ˑ.
SetValue("Pitch",ˊ);ˑ.SetValue("Yaw",ˇ);ˑ.SetValue("Roll",ˣ);}public static void Ƹ(IMyGyro ˑ,Vector3 ː,Vector3D ʹ,Vector3D ˏ){if
(Variables.Get<bool>("amp")){var ͳ=5f;var Ͳ=2f;Func<double,double,double>ͱ=(O,Ý)=>O*(Math.Exp(-Ý*Ͳ)+0.8)*ͳ/2;ʹ/=Math.PI/
180f;if((ʹ.X<2)&&(ː.X>0.017))ː.X=(float)ͱ(ː.X,ʹ.X);if((ʹ.Y<2)&&(ː.Y>0.017))ː.Y=(float)ͱ(ː.Y,ʹ.Y);if((ʹ.Z<2)&&(ː.Z>0.017))ː.Z
=(float)ͱ(ː.Z,ʹ.Z);}ˠ(ˑ,ː,ˏ);}public static Vector3D ˤ(Vector3D Ͱ,Vector3D ˮ,Vector3D ó,Vector3D Ŏ,Vector3D ˬ,bool Ō){
double ō=Vector3D.Dot(Vector3D.Normalize(ó-Ͱ),ˬ);if(ō<30)ō=30;return ˤ(Ͱ,ˮ,ó,Ŏ,ō,Ō);}public static Vector3D ˤ(Vector3D ɟ,
Vector3D ɔ,Vector3D ó,Vector3D Ŏ,double ō,bool Ō){double ŋ=Vector3D.Distance(ɟ,ó);Vector3D Ŋ=ó-ɟ;Vector3D ŉ=Vector3D.Normalize(Ŋ
);Vector3D ň=ó;Vector3D ŏ;if(Ō){var Ň=Vector3D.Reject(ɔ,ŉ);Ŏ-=Ň;}if(Ŏ.Length()>float.Epsilon){ŏ=Vector3D.Normalize(Ŏ);var
ņ=Math.PI-Math.Acos(Vector3D.Dot(ŉ,ŏ));var Ņ=(Ŏ.Length()*Math.Sin(ņ))/ō;if(Math.Abs(Ņ)<=1){var ń=Math.Asin(Ņ);var Ê=ŋ*
Math.Sin(ń)/Math.Sin(ņ+ń);ň=ó+ŏ*Ê;}}return ň;}public static string Ń(string Ć,Vector3D Ï,Color P){return
$"GPS:{Ć}:{Ï.X}:{Ï.Y}:{Ï.Z}:#{P.R:X02}{P.G:X02}{P.B:X02}:";}}StringBuilder ł=new StringBuilder();void ő(string Š){if(ş!=null){ł.AppendLine(Š);}}IMyTextPanel ş;void Ş(){if(ł.
Length>0){var Ê=ł.ToString();ł.Clear();ş?.WriteText(Ê);}}string ŝ(float Ŝ,string ś=""){string Ś;if(Math.Abs(Ŝ)>=1000000){if(!
string.IsNullOrEmpty(ś))Ś=string.Format("{0:0.##} M{1}",Ŝ/1000000,ś);else Ś=string.Format("{0:0.##}M",Ŝ/1000000);}else if(Math
.Abs(Ŝ)>=1000){if(!string.IsNullOrEmpty(ś))Ś=string.Format("{0:0.##} k{1}",Ŝ/1000,ś);else Ś=string.Format("{0:0.##}k",Ŝ/
1000);}else{if(!string.IsNullOrEmpty(ś))Ś=string.Format("{0:0.##} {1}",Ŝ,ś);else Ś=string.Format("{0:0.##}",Ŝ);}return Ś;}
static class ř{static string Ř="";static Action<string>ŗ;static IMyTextSurface Ï;static IMyTextSurface Ŗ;public static double
ŕ;public static void Ŕ(Action<string>ø,IMyGridTerminalSystem œ){ŗ=ø;Ï=me.GetSurface(0);Ï.ContentType=ContentType.
TEXT_AND_IMAGE;Ï.WriteText("");}public static void Œ(string Ê){if((Ř=="")||(Ê.Contains(Ř)))ŗ(Ê);}static string Ł="";public static void
į(string Ê){Ł+=Ê+"\n";}static List<string>Į=new List<string>();public static void ĭ(string Ê){Ï.WriteText(
$"{ŕ:f2}: {Ê}\n",true);if(Ŗ!=null){Į.Add(Ê);}}public static void Ĭ(IMyTextSurface Ê){Ŗ=Ê;}public static void ī(){if(!string.
IsNullOrEmpty(Ł)){var Ī=ƒ.Ū.Where(O=>O.IsUnderControl).FirstOrDefault()as IMyTextSurfaceProvider;if((Ī!=null)&&(Ī.SurfaceCount>0))Ī.
GetSurface(0).WriteText(Ł);Ł="";}if(Į.Any()){if(Ŗ!=null){Į.Reverse();var Q=string.Join("\n",Į)+"\n"+Ŗ.GetText();var Ú=Variables.
Get<int>("logger-char-limit");if(Q.Length>Ú)Q=Q.Substring(0,Ú-1);Ŗ.WriteText($"{ŕ:f2}: {Q}");}Į.Clear();}}}class Ĩ{static Ĩ
ħ=new Ĩ();Ĩ(){}public static Ĩ Ħ{get{ħ.Ŀ=0;ħ.İ=null;return ħ;}}class ĥ{public DateTime Ĥ;public Action ĩ;public Func<bool
>İ;public long ĸ;}Queue<ĥ>ŀ=new Queue<ĥ>();long Ŀ;Func<bool>İ;public Ĩ ľ(int Ľ){this.Ŀ+=Ľ;return this;}public Ĩ ļ(Action
Ļ){ŀ.Enqueue(new ĥ{Ĥ=DateTime.Now.AddMilliseconds(Ŀ),ĩ=Ļ,İ=İ,ĸ=Ŀ});return this;}public Ĩ ĺ(Func<bool>İ){this.İ=İ;return
this;}public void Ĺ(){if(ŀ.Count>0){ř.Œ("Scheduled actions count:"+ŀ.Count);var P=ŀ.Peek();if(P.Ĥ<DateTime.Now){if(P.İ!=null
){if(P.İ.Invoke()){P.ĩ.Invoke();P.Ĥ=DateTime.Now.AddMilliseconds(P.ĸ);}else{ŀ.Dequeue();}}else{P.ĩ.Invoke();ŀ.Dequeue();}
}}}public void ķ(){ŀ.Clear();Ŀ=0;İ=null;}}ĵ Ķ;class ĵ{public ə Ĵ;public IMyShipConnector ĳ;public List<IMyWarhead>Ĳ;
IMyRadioAntenna ñ;Ñ ı;public Á š;public Func<string,Ƚ>ƃ;public IMyGyro Ɓ;public IMyGridTerminalSystem ƀ;public
IMyIntergridCommunicationSystem ſ;public IMyRemoteControl ž;public đ Ž;public Ċ ż;public IMyProgrammableBlock Ż;public IMyProgrammableBlock Ƃ;HashSet<
IMyTerminalBlock>ź=new HashSet<IMyTerminalBlock>();ŕ Ź<ŕ>(string Ć,List<IMyTerminalBlock>Ÿ,bool ŷ=false)where ŕ:class,IMyTerminalBlock{ŕ
Ŷ;ř.Œ("Looking for "+Ć);var ŵ=Ÿ.Where(U=>U is ŕ&&U.CustomName.Contains(Ć)).Cast<ŕ>().ToList();Ŷ=ŷ?ŵ.Single():ŵ.
FirstOrDefault();if(Ŷ!=null)ź.Add(Ŷ);return Ŷ;}List<ŕ>Ŵ<ŕ>(List<IMyTerminalBlock>Ÿ,string ƌ=null)where ŕ:class,IMyTerminalBlock{var ŵ=
Ÿ.Where(U=>U is ŕ&&((ƌ==null)||(U.CustomName==ƌ))).Cast<ŕ>().ToList();foreach(var U in ŵ)ź.Add(U);return ŵ;}public ĵ(ə Ƌ,
IMyGridTerminalSystem Ɗ,IMyIntergridCommunicationSystem ē,Func<string,Ƚ>Ɖ){Ĵ=Ƌ;ƀ=Ɗ;ƃ=Ɖ;ſ=ē;Func<IMyTerminalBlock,bool>ŵ=U=>U.
IsSameConstructAs(me);var ƈ=new List<IMyTerminalBlock>();Ɗ.GetBlocks(ƈ);ƈ=ƈ.Where(U=>ŵ(U)).ToList();Ƈ(ƈ);}public void Ƈ(List<
IMyTerminalBlock>Ɔ){var ŵ=Ɔ;ř.ĭ("subset: "+Ɔ.Count);Ż=Ź<IMyProgrammableBlock>("a-thrust-provider",ŵ);var ƅ=Ŵ<IMyMotorStator>(ŵ);var Ƅ=
new List<IMyProgrammableBlock>();ƀ.GetBlocksOfType(Ƅ,ƍ=>ƅ.Any(O=>(O.Top!=null)&&O.Top.CubeGrid==ƍ.CubeGrid));Ƃ=Ź<
IMyProgrammableBlock>("a-tgp",ŵ)??Ƅ.FirstOrDefault(O=>O.CustomName.Contains("a-tgp"));Ɓ=Ź<IMyGyro>(ForwardGyroTag,ŵ,true);var Ū=Ŵ<
IMyShipController>(ŵ);ƒ.Ŕ(Ū);ñ=Ŵ<IMyRadioAntenna>(ŵ).FirstOrDefault();ĳ=Ŵ<IMyShipConnector>(ŵ).First();Ĳ=Ŵ<IMyWarhead>(ŵ);ž=Ŵ<
IMyRemoteControl>(ŵ).First();ž.CustomData="";var à=Ŵ<IMyTimerBlock>(ŵ);ż=new Ċ(à);ū=new List<IMyTerminalBlock>();ū.AddRange(Ŵ<IMyThrust>
(ŵ));ū.AddRange(Ŵ<IMyArtificialMassBlock>(ŵ));string ũ=Variables.Get<string>("ggen-tag");if(!string.IsNullOrEmpty(ũ)){var
œ=new List<IMyGravityGenerator>();var Ũ=ƀ.GetBlockGroupWithName(ũ);if(Ũ!=null)Ũ.GetBlocksOfType(œ,U=>ŵ.Contains(U));
foreach(var U in œ)ź.Add(U);ū.AddRange(œ);}else ū.AddRange(Ŵ<IMyGravityGenerator>(ŵ));Ž=new đ(ž,ż,ſ,Ż,Ɓ,ñ,ť,this,ū.Count>5);ı=
new Ñ(this,ƃ);ě(ApckState.Standby);Ž.ě(đ.Ģ.Ğ);}List<IMyTerminalBlock>ū;ȝ ĉ;int Ŧ;public ȝ ť(){if(ĉ==null)ĉ=new ȝ(Ɓ,ū);else
if((ģ!=Ŧ)&&(ģ%60==0)){Ŧ=ģ;if(ū.Any(O=>!O.IsFunctional)){ū.RemoveAll(O=>!O.IsFunctional);ĉ=new ȝ(Ɓ,ū);}}if(ū.Any(O=>O is
IMyThrust&&(O as IMyThrust)?.MaxEffectiveThrust!=(O as IMyThrust)?.MaxThrust))ĉ.Ȭ();return ĉ;}public Vector3D?Ť;public Vector3D?ţ
;public bool ŧ(){if(Ž.å!=null){if(ţ==null){Vector3D Ţ;if(ž.TryGetPlanetPosition(out Ţ)){ţ=Ţ;return true;}}return ţ.
HasValue;}return false;}public void A(string Ě){ApckState Ê;if(Enum.TryParse(Ě,out Ê)){ě(Ê);}}public void ě(ApckState Ê){if(ı.A(
Ê)){š=ı.S();ų();}}public void ų(){if(ñ!=null)ñ.CustomName=$"{Ɓ.CubeGrid.CustomName}"+(Ĵ.bRecalled?" [recalled]":"")+
$"> {ı.R().H} / {Ŭ?.Value?.À}";}public void Ų(ApckState K,Á U){ı.Ù(K,U);}public Á ű(ApckState K){return ı.M(K).B;}public void Ű(Ƥ Q){ř.ĭ("CreateWP "+Q
.À);Ô(Q);}public void ů(int ģ,Action<string>ŗ){this.ģ=ģ;Ø();var Ů=Ŭ?.Value;Á È;if((Ů!=null)&&(ı.R().H==Ů.ƞ))È=Ů.B??ű(Ů.ƞ)
;else È=š;Ž.ú(ģ,ŗ,È);}LinkedList<Ƥ>ŭ=new LinkedList<Ƥ>();LinkedListNode<Ƥ>Ŭ;int ģ;void Ø(){if(Ŭ!=null){var Q=Ŭ.Value;if(Q
.Ǔ(ģ,Ž)){ř.ĭ($"TFin {Q.À}");Ó();}}}public Ƥ Õ(){return Ŭ?.Value;}public void Ô(Ƥ Q){ŭ.AddFirst(Q);ř.ĭ(
$"Added {Q.À}, total: {ŭ.Count}");Ŭ=ŭ.First;if(Ŭ.Next==null)Ŭ.Value.Ɯ=ı.R().H;Q.Ŕ(Ž,ģ);ě(Q.ƞ);}public void Ó(){var Ò=Ŭ;var P=Ò.Value;P.ƣ?.Invoke();if(Ò.
Next!=null){P=Ò.Next.Value;Ŭ=Ò.Next;P.Ŕ(Ž,ģ);ŭ.Remove(Ò);}else{Ŭ=null;ŭ.Clear();ě(Ò.Value.Ɯ);}}}class Ñ{ĵ Ö;Dictionary<
ApckState,I>Ð=new Dictionary<ApckState,I>();public Ñ(ĵ Î,Func<string,Ƚ>Í){Ö=Î;var Ì=Ö.Ž;var Ë=new Á{À="Default"};foreach(var Ê in
Enum.GetValues(typeof(ApckState))){Ð.Add((ApckState)Ê,new I((ApckState)Ê,Ë));}Ð[ApckState.Standby].B=new Á{À="Standby"};L=Ð[
ApckState.Standby];Ð[ApckState.Formation].B=new Á{À="follow formation",º=false,č=()=>Í("wingman"),X=(W)=>Ì.Ā.GetPosition()+Í(
"wingman").ȷ.Value.Forward*5000,Æ=Ï=>{var É=new BoundingSphereD(Í("wingman").ȷ.Value.Translation,30);return ͽ.Ώ(Ì.Ā.WorldMatrix,Ï
,É);},Þ=()=>Ì.Ā.GetPosition()};Ð[ApckState.Brake].B=new Á{À="reverse",Ď=true,Æ=W=>Ì.Ƿ(-150),X=(W)=>Ì.Ƿ(1),ď=true};Ð[
ApckState.DockingAwait].B=new Á{À="awaiting docking",º=false,Ď=true,č=()=>Í("wingman"),X=W=>Ì.Ā.GetPosition()+Ì.Ā.WorldMatrix.
Forward,Æ=W=>Í("wingman").ȹ.HasValue?Í("wingman").ȹ.Value:Ì.Ā.GetPosition(),o=(Ý,Ü,P,Û,Ú)=>{if(Í("docking").ȹ.HasValue&&(Ö.ĳ!=
null)){Ú.ě(ApckState.DockingFinal);}}};Ð[ApckState.DockingFinal].B=new Á{X=W=>Ö.ĳ.GetPosition()-Í("docking").ȷ.Value.Forward
*10000,Æ=Ï=>Ï+Í("docking").ȷ.Value.Forward*(IsLargeGrid?1.25f:0.5f),V=()=>Ö.ĳ.WorldMatrix,Þ=()=>Ö.ĳ.GetPosition(),č=()=>Í
("docking"),º=false,Z=()=>Ì.ç,o=(Ý,Ü,P,Û,Ú)=>{if((Ý<20)&&(P.ê.Length()<0.8)&&(Ö.ĳ!=null)){Ö.ĳ.Connect();if(Ö.ĳ.Status==
MyShipConnectorStatus.Connected){Ú.ě(ApckState.Inert);Ö.ĳ.OtherConnector.CustomData="";P.â.DampenersOverride=false;}}}};Ð[ApckState.Inert].D=
Ê=>Î.Ž.ě(đ.Ģ.ğ);Ð[ApckState.Inert].F=Ê=>Î.Ž.ě(đ.Ģ.Ğ);}public void Ù(ApckState K,Á È){Ð[K].B=È;}public Á S(){return L.B;}
public bool A(ApckState K){if(K==L.H)return true;var Q=M(K);if(Q!=null){var P=J.FirstOrDefault(O=>O.Ä.H==L.H||O.Ã.H==L.H||O.Ã.
H==K||O.Ä.H==K);if(P!=null){if(!(P.Ä==L&&P.Ã==Q)){return false;}}var N=L;ř.ĭ($"{N.H} -> {Q.H}");L=Q;N.F?.Invoke(L.H);P?.Â
?.Invoke();Q.D?.Invoke(Q.H);return true;}return false;}public I M(ApckState K){return Ð[K];}public I R(){return L;}I L;
List<Å>J=new List<Å>();public class I{public ApckState H;public Action<ApckState>F;public Action<ApckState>D;public Á B;
public I(ApckState K,Á U,Action<ApckState>ª=null,Action<ApckState>Ç=null){B=U;H=K;F=ª;D=Ç;}}public class Å{public I Ä;public I
Ã;public Action Â;}}class Á{public string À="Default";public bool º=true;public Func<Vector3D,Vector3D>Æ{get;set;}public
Func<Vector3D,Vector3D>µ{get;set;}public Func<Vector3D>w{get;set;}public Action<double,double,đ,Á,ĵ>o{get;set;}public Func<
Vector3D>Z;public bool Y=false;public Func<Vector3D,Vector3D>X=(W)=>W;public Func<MatrixD>V;public Func<Vector3D>Þ;public bool ß
;public float?ġ;public bool Đ=false;public bool ď=false;public bool Ď;public Func<Ƚ>č;public static Á Č(Vector3D Ï,string
Ć,Func<Vector3D>ċ=null){return new Á(){À=Ć,Ď=true,Æ=O=>Ï,X=O=>ċ?.Invoke()??Ï};}}class Ċ{Dictionary<string,IMyTimerBlock>Ĉ
=new Dictionary<string,IMyTimerBlock>();List<IMyTimerBlock>ĉ;public Ċ(List<IMyTimerBlock>Ĉ){ĉ=Ĉ;}public bool ć(string Ć){
IMyTimerBlock U;if(!Ĉ.TryGetValue(Ć,out U)){U=ĉ.FirstOrDefault(P=>P.CustomName.Contains(Ć));if(U!=null)Ĉ.Add(Ć,U);else return false;}
U.GetActionWithName("TriggerNow").Apply(U);return true;}}class đ{public enum Ģ{Ġ=0,ğ,Ğ}public bool ĝ;Ģ Ĝ=Ģ.Ġ;public void
ě(Ģ ę){if(ę==Ģ.Ğ)Ę();else if(ę==Ģ.ğ)ė(false);else if(ę==Ģ.Ġ)ė();Ĝ=ę;}public void A(string Ě){Ģ ę;if(Enum.TryParse(Ě,out ę
))ě(ę);}void Ę(){â.DampenersOverride=false;}void ė(bool Ė=true){Ă.GyroOverride=false;û().Ȱ();â.DampenersOverride=Ė;}
public đ(IMyRemoteControl ĕ,Ċ Ĕ,IMyIntergridCommunicationSystem ē,IMyProgrammableBlock Ē,IMyGyro ą,IMyTerminalBlock ñ,Func<ȝ>à
,ĵ ï,bool î){â=ĕ;è=ē;Ă=ą;ā=ñ;á=Ĕ;ò=Ē;û=à;Ú=ï;ĝ=î;}Vector3D í;public string ì;public Vector3D ë{get;private set;}public
Vector3D ê{get;private set;}Vector3D é=Vector3D.Zero;public double ð{get;private set;}public Á B{get;private set;}public
Vector3D ç{get{return â.GetShipVelocities().LinearVelocity;}}Vector3D?æ;public Vector3D?å{get{return(æ!=Vector3D.Zero)?æ:null;}}
Vector3D ä{get;set;}public Vector3D ã{get;set;}public IMyRemoteControl â;public Ċ á{get;private set;}
IMyIntergridCommunicationSystem è;IMyProgrammableBlock ò;Func<ȝ>û;ĵ Ú;int ă;IMyGyro Ă;IMyTerminalBlock ā;public IMyTerminalBlock Ā{get{return Ă;}}
public Vector3D ÿ;public Vector3D þ;public Vector3D ý;public Vector3D Ą;public Vector3D ü;public void ú(int ù,Action<string>ø,
Á È){var ö=ù-ă;ă=ù;if(ö>0)ã=(ç-ä)*60f/ö;ä=ç;æ=â.GetNaturalGravity();B=È;MyPlanetElevation õ=new MyPlanetElevation();
double ô;â.TryGetPlanetElevation(õ,out ô);Vector3D Ţ;â.TryGetPlanetPosition(out Ţ);Func<Vector3D>Ǝ=null;bool ß=false;float?ȋ=
null;Ƚ ȉ=null;switch(Ĝ){case Ģ.Ġ:return;case Ģ.Ğ:try{var Û=È;if(Û==null){ě(Ģ.Ġ);return;}if(!ĝ&&!Û.Y)return;var Ȉ=Vector3D.
TransformNormal(â.GetShipVelocities().AngularVelocity,MatrixD.Transpose(Ā.WorldMatrix));Ȉ=new Vector3D(Math.Abs(Ȉ.X),Math.Abs(Ȉ.Y),Math
.Abs(Ȉ.Z));var ȇ=(Ȉ-é)/Dt;é=Ȉ;Ǝ=Û.Z;var Ȇ=Û.X;ß=Û.ß;ȋ=Û.ġ;if(Û.č!=null)ȉ=Û.č();if(Û.Ď||((ȉ!=null)&&ȉ.ȹ.HasValue)){
Vector3D Ǭ;Vector3D?Ŏ=null;if((ȉ!=null)&&(ȉ.ȹ.HasValue)){Ǭ=ȉ.ȹ.Value;if(Ŏ.IsValid())Ŏ=ȉ.ç;else ř.ĭ("Ivalid targetVelocity");}
else Ǭ=Vector3D.Zero;if(Û.µ!=null)Ǭ=Û.µ(Ǭ);var Ȋ=(Û.Þ!=null)?Û.Þ():Ā.GetPosition();if((Ǝ!=null)&&(Ŏ.HasValue)&&(Ŏ.Value.
Length()>0)){Vector3D ó=Ǭ;Vector3D Ȅ=ͽ.ˤ(Ȋ,ç,ó,Ŏ.Value,Ǝ(),Û.Đ);if((Ǭ-Ȅ).Length()<2500){Ǭ=Ȅ;}þ=Ȅ;}ÿ=Ǭ;if(Û.Æ!=null)Ą=Û.Æ(Ǭ);
else Ą=Ǭ;double ȃ=(Ǭ-Ȋ).Length();double Ȃ=(Ą-Ȋ).Length();if(DbgIgc!=0)ì=$"origD: {ȃ:f1}\nshiftD: {Ȃ:f1}";Û.o?.Invoke(ȃ,Ȃ,
this,Û,Ú);Ă.GyroOverride=true;ü=Ǭ;if(Ȇ!=null)ü=Ȇ(ü);if((Variables.Get<bool>("hold-thrust-on-rotation")&&(í.Length()>1))||
Toggle.C.Check("suppress-transition-control")){ř.Œ($"prev cv: {í.Length():f2} HOLD");Ș(Ā.WorldMatrix.Translation,Ā.WorldMatrix
.Translation,false,null,null,false);}else{ř.Œ($"prev cv: {í.Length():f2} OK");Ș(Ȋ,Ą,ß,Ŏ,ȋ,Û.ď);}var ȁ=(Û.V!=null)?Û.V():Ā
.WorldMatrix;if(!ĝ&&(ý!=Ā.WorldMatrix.Translation))ü=ý;Vector3D Ȁ=Vector3D.Zero;var ǿ=ü-Ā.WorldMatrix.Translation;if(ǿ!=
Vector3D.Zero){var Ǿ=Vector3D.Normalize(ǿ);var ȅ=MatrixD.CreateFromDir(Ǿ);Vector3D ǽ=Vector3D.Zero;var Ǚ=Û.w?.Invoke()??Vector3D
.Zero;Ȁ=ͽ.Ι(Ǿ,ȁ,Ā.WorldMatrix,Ǚ,ref ǽ);ǽ.Z=0;ê=ǽ;ë=ê-í;í=ê;ð=Vector3D.Dot(Ǿ,ȁ.Forward);}if(!Toggle.C.Check(
"suppress-gyro-control"))ͽ.Ƹ(Ă,Ȁ,ë,é);}else{Ă.GyroOverride=false;if(Toggle.C.Check("damp-when-idle"))Ș(Ā.WorldMatrix.Translation,Ā.WorldMatrix.
Translation,false,null,0,false);else Ș(Ā.WorldMatrix.Translation,Ā.WorldMatrix.Translation,false,null,null,false);}}catch(Exception
ex){ā.CustomName+="HC Exception! See remcon cdata or PB screen";var Ŷ=â;var ŗ=
$"HC EPIC FAIL\nNTV:{ȉ?.À}\nBehavior:{B.À}\n{ex}";Ŷ.CustomData+=ŗ;ř.ĭ(ŗ);ě(Ģ.Ġ);throw ex;}finally{ř.ī();}break;}}void Ș(Vector3D ȗ,Vector3D Ȗ,bool ß,Vector3D?ȕ,float?ȋ,
bool Ȕ){ý=Ȗ;if(Ĝ!=Ģ.Ğ)return;var ȓ=Ȗ;var Ȓ=Ă.WorldMatrix;Ȓ.Translation=ȗ;var ǿ=Ȗ-Ȓ.Translation;var ȑ=MatrixD.Transpose(Ȓ);
var Ȑ=Vector3D.TransformNormal(ç,ȑ);var ȏ=Ȑ;if(!ĝ&&(ǿ!=Vector3D.Zero)){û().ƨ().Ɩ(1f*Math.Max(0.2f,ð));if(ç!=Vector3D.Zero)ý
=Vector3D.Normalize(Vector3D.Reflect(ç,ǿ))+ȗ+Vector3D.Normalize(ǿ)*ç.Length()*0.5f;return;}float ƶ=â.CalculateShipMass().
PhysicalMass;BoundingBoxD Ȏ=û().ȯ(ƶ);if(Ȏ.Volume==0)return;Vector3D ȍ=Vector3D.Zero;if(å!=null){ȍ=Vector3D.TransformNormal(å.Value,ȑ
);Ȏ+=-ȍ;}Vector3D Ȍ=Vector3D.Zero;Vector3D Ǽ=Vector3D.Zero;Vector3D ǲ=new Vector3D();if(ǿ.Length()>double.Epsilon){
Vector3D Ǣ=Vector3D.TransformNormal(ǿ,ȑ);RayD ǰ=new RayD(-Ǣ*(MaxAccelInProximity?1000:1),Vector3D.Normalize(Ǣ));RayD ǯ=new RayD(
Ǣ*(MaxBrakeInProximity?1000:1),Vector3D.Normalize(-Ǣ));var ƍ=ǯ.Intersects(Ȏ);var Ǯ=ǰ.Intersects(Ȏ);if(!ƍ.HasValue||!Ǯ.
HasValue)throw new InvalidOperationException("Not enough thrust to compensate for gravity");var ǭ=ǯ.Position+(Vector3D.Normalize
(ǯ.Direction)*ƍ.Value);var Ǭ=ǰ.Position+(Vector3D.Normalize(ǰ.Direction)*Ǯ.Value);var ǫ=ǭ.Length();Vector3D Ǳ=Vector3D.
Reject(Ȑ,Vector3D.Normalize(Ǣ));if(ȕ.HasValue){var Ǫ=Vector3D.TransformNormal(ȕ.Value,ȑ);ȏ=Ȑ-Ǫ;Ǳ=Vector3D.Reject(ȏ,Vector3D.
Normalize(Ǣ));}else{ȏ-=Ǳ;}var ǩ=Vector3D.Dot(ȏ,Vector3D.Normalize(Ǣ));bool Ǩ=ǩ>0;bool ǧ=true;var Ǧ=Math.Pow(Math.Max(0,ǩ),2)/(2*ǫ
*StoppingPowerQuotient);var ǥ=ǿ.Length()-Ǧ;if(DbgIgc!=0){ì+=$"\nSTP: {Ǧ:f2}\nRelSP: {ǩ:f2}";}if(Ǩ){if(Ǧ>ǿ.Length())ǧ=
false;else if(MoreRejectDampening)Ǳ/=Dt;}if(Ȕ||ǧ){if(ȋ.HasValue&&(Vector3D.Dot(Vector3D.Normalize(ǿ),ç)>=ȋ)){ǲ=ǭ;ǲ*=(ǩ-ȋ.
Value)/ǫ;}else ǲ=Ǭ;}else ǲ=ǭ;if(ǧ){var Ǥ=Vector3D.Dot(Vector3D.Normalize(ǿ),ç);if(Ǥ>MAX_SP-0.001){ǲ=Vector3D.Zero;}}Ǽ=ǲ;Ȍ=Ǳ;
if(Ǳ.IsValid())ǲ+=Ǳ;}else if(ȋ.HasValue&&(ȋ==0)){ǲ+=Ȑ/(MoreRejectDampening?Dt:1);}if(å!=null){ǲ+=ȍ;}ǲ-=ƒ.Ƒ(Ā.WorldMatrix)*
1000;if(ǲ!=Vector3D.Zero){ý=Vector3D.TransformNormal(ǲ,Ȓ)+ȗ;if(DbgIgc!=0){var ǣ=new List<MyTuple<Vector3D,Vector3D,Vector4>>
();var P=Color.SeaGreen;P.A=40;ǣ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(ȗ,ý,P));var Q=new MyTuple<string,Vector2,
Vector3D,Vector3D,float,string>("Circle",Vector2.One*4,ý,Vector3D.Zero,1f,ǲ.Length().ToString("f2"));è.SendUnicastMessage(DbgIgc
,"draw-projection",Q);P=Color.Blue;P.A=40;ǣ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(ȗ,Vector3D.TransformNormal(Ȍ,Ȓ)+ȗ,
P));P=Color.Red;P.A=40;ǣ.Add(new MyTuple<Vector3D,Vector3D,Vector4>(ȗ,Vector3D.TransformNormal(Ǽ,Ȓ)+ȗ,P));var Ƕ=new RayD(
ǲ*1000,Vector3D.Normalize(-ǲ));var Ǯ=Ƕ.Intersects(Ȏ);if(Ǯ.HasValue){var Ǹ=Ƕ.Position+(Vector3D.Normalize(Ƕ.Direction)*Ǯ.
Value);var Ǜ=Vector3D.TransformNormal(Ǹ,Ȓ)+ȗ;var ǻ=new MyTuple<string,Vector2,Vector3D,Vector3D,float,string>("Circle",
Vector2.One*4,Ǜ,Vector3D.Zero,1f,Ǹ.Length().ToString("f2"));è.SendUnicastMessage(DbgIgc,"draw-projection",ǻ);}var Ǻ=new RayD(-ǲ
*1000,Vector3D.Normalize(ǲ));var ǹ=Ǻ.Intersects(Ȏ);if(ǹ.HasValue){var Ǹ=Ǻ.Position+(Vector3D.Normalize(Ǻ.Direction)*ǹ.
Value);var Ǜ=Vector3D.TransformNormal(Ǹ,Ȓ)+ȗ;var ǻ=new MyTuple<string,Vector2,Vector3D,Vector3D,float,string>("Circle",
Vector2.One*4,Ǜ,Vector3D.Zero,1f,Ǹ.Length().ToString("f2"));è.SendUnicastMessage(DbgIgc,"draw-projection",ǻ);}è.
SendUnicastMessage(DbgIgc,"draw-lines",ǣ.ToImmutableArray());}}ǲ.Y*=-1;û().Ƹ(ǲ,ƶ);}public Vector3D Ƿ(float ǵ){return Ā.GetPosition()+Ā.
WorldMatrix.Forward*ǵ;}}Ƚ Í(string ǳ){Ƚ Ŷ;if(Ʉ.TryGetValue(ǳ,out Ŷ))return Ŷ;throw new InvalidOperationException("No TV named "+ǳ);
}void Ǵ(string ǳ,MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>Ǉ){Ʉ[ǳ].ɒ(Ǉ,ʑ);}void
ș(){foreach(var O in Ʉ.Values.Where(Ʒ=>Ʒ.ȹ.HasValue)){ř.Œ(O.À+((O.ȹ.Value==Vector3D.Zero)?" Zero!":" OK"));O.ɏ(ʑ);}}
Dictionary<string,Ƚ>Ʉ=new Dictionary<string,Ƚ>();struct ɂ{public Vector3D?Ǜ;public Vector3D?Ɂ;public MatrixD?ɀ;public BoundingBoxD
?ȿ;public MyDetectedEntityType?Ⱦ;}class Ƚ{public long Ƀ;int ȼ;public string À;public long Ⱥ;public Vector3D?ȹ{get;private
set;}public Vector3D?ç;public Vector3D?ȸ;public MatrixD?ȷ;public BoundingBoxD?ȶ;public int?Ȼ=60;public MyDetectedEntityType
?ȵ{get;set;}public delegate void Ʌ();public event Ʌ ɓ;public Ƚ(int ȼ,string Ć){this.ȼ=ȼ;À=Ć;}public void ɑ(Vector3D Ǜ,
long ɐ){ȹ=Ǜ;Ƀ=ɐ;}public void ɏ(int Ɏ){if((Ƀ!=0)&&Ȼ.HasValue&&(Ɏ-Ƀ>Ȼ.Value))ȟ();}public void ɍ(int ģ,int ȼ){if((ç.HasValue)&&
(ç.Value.Length()>double.Epsilon)&&(ģ-Ƀ)>0){ȹ+=ç*(ģ-Ƀ)*ȼ/60;}}public enum Ɍ:byte{ɋ=1,Ɋ=2,ɉ=4}bool Ɉ(Ɍ ɇ,Ɍ Ɇ){return(ɇ&Ɇ)
==Ɇ;}public void ɒ(MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>ȥ,int ȣ){var ȩ=ȥ.
Item1;Ⱥ=ȩ.Item2;ȵ=(MyDetectedEntityType)ȩ.Item4;Ɍ Ȩ=(Ɍ)ȩ.Item5;ɑ(ȥ.Item2,ȣ);if(Ɉ(Ȩ,Ɍ.ɋ)){var ȧ=ȥ.Item3;if(!ç.HasValue)ç=ȧ;ȸ=(
ȧ-ç.Value)*60/ȼ;ç=ȧ;}if(Ɉ(Ȩ,Ɍ.Ɋ))ȷ=ȥ.Item4;if(Ɉ(Ȩ,Ɍ.ɉ))ȶ=ȥ.Item5;}public static Ƚ Ȧ(MyTuple<MyTuple<string,long,long,byte
,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>ȥ,Func<string[],ɂ>Ȥ,int ȣ){var Q=new Ƚ(1,ȥ.Item1.Item1);Q.ɒ(ȥ,ȣ);return Q;}
public MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>Ȣ(){var Ƞ=0|(ç.HasValue?1:0)|(ȷ.
HasValue?2:0)|(ȶ.HasValue?4:0);var O=new MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>(new
MyTuple<string,long,long,byte,byte>(À,Ⱥ,DateTime.Now.Ticks,(byte)MyDetectedEntityType.LargeGrid,(byte)Ƞ),ȹ.Value,ç??Vector3D.
Zero,ȷ??MatrixD.Identity,ȶ??new BoundingBoxD());return O;}public void ȟ(){ȹ=null;ç=null;ȷ=null;ȶ=null;var Ȟ=ɓ;if(Ȟ!=null)ɓ()
;}}class ȝ{List<Ɨ>Ȝ;List<Ɨ>ț;List<Ɨ>ȡ;List<Ɨ>Ț;List<Ɨ>ǝ;List<Ɨ>ȴ;List<Ɨ>ȳ;public double[]Ȳ=new double[6];bool ȱ;public
void Ȱ(){if(!ȱ){ț.ForEach(Ƶ=>Ƶ.Ɠ());ȡ.ForEach(Ƶ=>Ƶ.Ɠ());Ț.ForEach(Ƶ=>Ƶ.Ɠ());ǝ.ForEach(Ƶ=>Ƶ.Ɠ());ȴ.ForEach(Ƶ=>Ƶ.Ɠ());ȳ.
ForEach(Ƶ=>Ƶ.Ɠ());ȱ=true;}}public BoundingBoxD ȯ(float ƶ){Vector3D Ȯ=new Vector3D(-Ȳ[5],-Ȳ[3],-Ȳ[1])/ƶ;Vector3D ȭ=new Vector3D(
Ȳ[4],Ȳ[2],Ȳ[0])/ƶ;return new BoundingBoxD(Ȯ,ȭ);}public void Ȭ(){Ȳ[0]=ƨ().Ƴ();Ȳ[1]=Ƨ().Ƴ();Ȳ[2]=ƪ().Ƴ();Ȳ[3]=Ʀ().Ƴ();Ȳ[4]=
ƹ().Ƴ();Ȳ[5]=Ư().Ƴ();}public ȝ(IMyTerminalBlock ȫ,List<IMyTerminalBlock>Ȫ){MatrixD ǡ=ȫ.WorldMatrix;Func<Vector3D,List<Ɨ>>
Ʈ=ƚ=>{var Ŷ=Ȫ.Where(U=>U is IMyThrust&&ƚ==U.WorldMatrix.Forward).Select(O=>O as IMyThrust).ToList();return Ŷ.Select(Q=>
new Ƙ(Q)).Cast<Ɨ>().ToList();};ț=Ʈ(ǡ.Backward);ȡ=Ʈ(ǡ.Forward);Ț=Ʈ(ǡ.Down);ǝ=Ʈ(ǡ.Up);ȴ=Ʈ(ǡ.Left);ȳ=Ʈ(ǡ.Right);var ƭ=Ȫ.Where(
U=>U is IMyArtificialMassBlock).Cast<IMyArtificialMassBlock>().ToList();var Ƭ=Ȫ.Where(U=>U is IMyGravityGenerator).Cast<
IMyGravityGenerator>().ToList();Func<Vector3D,bool,List<Ɨ>>ƫ=(ƚ,Ʃ)=>{var œ=Ƭ.Where(U=>ƚ==U.WorldMatrix.Up);return œ.Select(Ï=>new ư(Ï,ƭ,Ʃ))
.Cast<Ɨ>().ToList();};ț.AddRange(ƫ(ǡ.Forward,true));ȡ.AddRange(ƫ(ǡ.Forward,false));ț.AddRange(ƫ(ǡ.Backward,false));ȡ.
AddRange(ƫ(ǡ.Backward,true));Ț.AddRange(ƫ(ǡ.Up,true));ǝ.AddRange(ƫ(ǡ.Up,false));Ț.AddRange(ƫ(ǡ.Down,false));ǝ.AddRange(ƫ(ǡ.Down,
true));ȴ.AddRange(ƫ(ǡ.Right,true));ȳ.AddRange(ƫ(ǡ.Right,false));ȴ.AddRange(ƫ(ǡ.Left,false));ȳ.AddRange(ƫ(ǡ.Left,true));Ȭ();}
public ȝ ƨ(){Ȝ=ț;return this;}public ȝ Ƨ(){Ȝ=ȡ;return this;}public ȝ ƪ(){Ȝ=Ț;return this;}public ȝ Ʀ(){Ȝ=ǝ;return this;}public
ȝ Ư(){Ȝ=ȴ;return this;}public ȝ ƹ(){Ȝ=ȳ;return this;}public void Ƹ(Vector3D Ʒ,float ƶ){ȱ=false;Func<Ɨ,bool>ƴ=Ƶ=>!(Ƶ is ư)
;Ƨ().Ɩ(-Ʒ.Z/Ȳ[1]*ƶ);ƨ().Ɩ(Ʒ.Z/Ȳ[0]*ƶ);Ʀ().Ɩ(-Ʒ.Y/Ȳ[3]*ƶ);ƪ().Ɩ(Ʒ.Y/Ȳ[2]*ƶ);Ư().Ɩ(-Ʒ.X/Ȳ[5]*ƶ);ƹ().Ɩ(Ʒ.X/Ȳ[4]*ƶ);}public
bool Ɩ(double ŀ,Func<Ɨ,bool>ƴ=null){if(Ȝ!=null){ŀ=Math.Min(1,Math.Abs(ŀ))*Math.Sign(ŀ);foreach(var Ʊ in ƴ==null?Ȝ:Ȝ.Where(ƴ)
){Ʊ.Ɩ(ŀ);}}Ȝ=null;return true;}public float Ƴ(){float Ʋ=0;if(Ȝ!=null){foreach(var Ʊ in Ȝ){Ʋ+=Ʊ.ƕ();}}Ȝ=null;return Ʋ;}}
class ư:Ɨ{IMyGravityGenerator œ;List<IMyArtificialMassBlock>ƥ;bool ƙ;public ư(IMyGravityGenerator œ,List<
IMyArtificialMassBlock>ƥ,bool ƙ){this.œ=œ;this.ƥ=ƥ;this.ƙ=ƙ;}public void Ɩ(double Ə){if(Ə>=0)œ.GravityAcceleration=(float)(ƙ?-Ə:Ə)*G;}public
void Ɠ(){œ.GravityAcceleration=0;}public float ƕ(){return ƥ.Count*50000*G;}}class Ƙ:Ɨ{IMyThrust Q;public Ƙ(IMyThrust Q){this
.Q=Q;}public void Ɩ(double Ə){if(Ə<=0)Q.ThrustOverride=0.00000001f;else Q.ThrustOverride=(float)Ə*Q.MaxThrust;}public
void Ɠ(){Q.ThrustOverride=0;Q.Enabled=true;}public float ƕ(){return Q.MaxEffectiveThrust;}}interface Ɨ{void Ɩ(double Ə);
float ƕ();void Ɠ();}static class ƒ{public static List<IMyShipController>Ū;public static void Ŕ(List<IMyShipController>P){if(Ū
==null)Ū=P;}public static Vector3 Ƒ(MatrixD Ɛ){Vector3 Ɣ=new Vector3();if(Toggle.C.Check("ignore-user-thruster"))return Ɣ;
var P=Ū.Where(O=>O.IsUnderControl).FirstOrDefault();if(P!=null&&(P.MoveIndicator!=Vector3.Zero))return Vector3D.
TransformNormal(P.MoveIndicator,Ɛ*MatrixD.Transpose(P.WorldMatrix));return Ɣ;}}class Ƥ{public string À;public Vector3D?Ƣ;public Func<
Vector3D>ơ;double?Ơ;public int?Ɵ;public ApckState ƞ=ApckState.CwpTask;public Á B;public Action ƣ;int Ɲ;public ApckState Ɯ;public
Ƥ(string Ć,Á È,int?ƛ=null){B=È;À=Ć;Ɵ=ƛ;}public Ƥ(string Ć,ApckState K,int?ƛ=null){ƞ=K;À=Ć;Ɵ=ƛ;}public void Ŕ(đ Ž,int ģ){
if(Ɲ==0)Ɲ=ģ;Ž.á.ć(À+".OnStart");}public static Ƥ Ǘ(string Ć,Vector3D Ï,Á È){var Q=new Ƥ(Ć,È);Q.Ơ=0.5;Q.Ƣ=Ï;return Q;}
public static Ƥ ǖ(string Ć,Func<Vector3D>Ǖ,Á È){var Q=new Ƥ(Ć,È);Q.Ơ=0.5;Q.ơ=Ǖ;return Q;}public bool Ǔ(int ģ,đ Ž){if(Ɵ.
HasValue&&(ģ-Ɲ>Ɵ)){return true;}if(Ơ.HasValue){Vector3D Ï;var Ò=B.Þ?.Invoke()??Ž.Ā.GetPosition();if(ơ!=null)Ï=ơ();else Ï=Ƣ.Value
;if((Ò-Ï).Length()<Ơ)return true;}return false;}}Ƥ ǔ;void Ű(string[]ǘ){ǀ();var Ǡ=Ķ;var Ì=Ǡ.Ž;var ǟ=Ì.Ā.GetPosition();var
Ǟ=ǘ[2].Split(',').ToDictionary(Ê=>Ê.Split('=')[0],Ê=>Ê.Split('=')[1]);var È=new Á(){À="Deserialized Behavior",Ď=true,º=
false};ǔ=new Ƥ("twp",È);float ǝ=1;var ǜ=ǘ.Take(6).Skip(1).ToArray();var Ǜ=new Vector3D(double.Parse(ǜ[2]),double.Parse(ǜ[3]),
double.Parse(ǜ[4]));Func<Vector3D,Vector3D>ǚ=Ï=>Ǜ;ǔ.Ƣ=Ǜ;Vector3D?ƌ=null;if(Ǟ.ContainsKey("AimNormal")){var Ʒ=Ǟ["AimNormal"].
Split(';');ƌ=new Vector3D(double.Parse(Ʒ[0]),double.Parse(Ʒ[1]),double.Parse(Ʒ[2]));}if(Ǟ.ContainsKey("UpNormal")){var Ʒ=Ǟ[
"UpNormal"].Split(';');var Ǚ=Vector3D.Normalize(new Vector3D(double.Parse(Ʒ[0]),double.Parse(Ʒ[1]),double.Parse(Ʒ[2])));È.w=()=>Ǚ;
}if(Ǟ.ContainsKey("Name"))ǔ.À=Ǟ["Name"];if(Ǟ.ContainsKey("FlyThrough"))È.ď=true;if(Ǟ.ContainsKey("SpeedLimit"))È.ġ=float.
Parse(Ǟ["SpeedLimit"]);if(Ǟ.ContainsKey("TriggerDistance"))ǝ=float.Parse(Ǟ["TriggerDistance"]);if(Ǟ.ContainsKey(
"PosDirectionOverride")&&(Ǟ["PosDirectionOverride"]=="Forward")){if(ƌ.HasValue){ǚ=Ï=>ǟ+ƌ.Value*((Ì.Ā.GetPosition()-ǟ).Length()+5);}else ǚ=Ï=>Ì
.Ƿ(50000);}if(ǘ.Length>6){È.o=(Ý,ǒ,Ž,Û,Ú)=>{if(ǒ<ǝ){ǀ();ɾ.Ϥ.ʓ(ǘ[7],ǘ.Skip(6).ToArray());}};}if(Ǟ.ContainsKey("Ng")){Func<
MatrixD>ƚ=()=>Ì.Ā.WorldMatrix;if(Ǟ["Ng"]=="Down")È.V=()=>MatrixD.CreateFromDir(Ì.Ā.WorldMatrix.Down,Ì.Ā.WorldMatrix.Forward);if
(Ǡ.ŧ()&&!Ǟ.ContainsKey("IgG")){È.X=Ï=>Ǡ.ţ.Value;È.Æ=Ï=>Vector3D.Normalize(ǚ(Ï)-Ǡ.ţ.Value)*(ǟ-Ǡ.ţ.Value).Length()+Ǡ.ţ.
Value;}else{if(ƌ.HasValue){È.X=Ï=>Ì.Ā.GetPosition()+ƌ.Value*1000;}else È.X=Ï=>Ì.Ā.GetPosition()+(È.V??ƚ)().Forward*1000;}}if(
Ǟ.ContainsKey("TransformChannel")){Func<Vector3D,Vector3D>ǁ=Ï=>Vector3D.Transform(Ǜ,Í(Ǟ["TransformChannel"]).ȷ.Value);È.µ
=ǁ;ǔ.ơ=()=>Vector3D.Transform(Ǜ,Í(Ǟ["TransformChannel"]).ȷ.Value);È.Đ=true;È.č=()=>Í(Ǟ["TransformChannel"]);È.Z=()=>Ì.ç;È
.Æ=null;}else È.Æ=ǚ;ǔ.ƞ=ApckState.CwpTask;Ǡ.Ų(ǔ.ƞ,È);Ǡ.Ô(ǔ);}void ǀ(){if(ǔ!=null){if(Ķ.Õ()==ǔ)Ķ.Ó();ǔ=null;}}class ƾ{
public long ƽ;public string Ć;public MatrixD Ƽ;public Vector3D Ʒ;public float ƻ;public float ƺ;public float ƿ;public float ǂ;
public string ǈ;public MinerState Ĝ;public float ǐ;public float Ǐ;public bool ǎ;public bool Ǎ;public float ǌ;public float ǋ;
public bool Ǌ;public ImmutableArray<MyTuple<string,string>>Ǒ;public MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,
MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,string>>>ǉ(){var Ǉ
=new MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,
bool,float,float>,ImmutableArray<MyTuple<string,string>>>();Ǉ.Item1.Item1=ƽ;Ǉ.Item1.Item2=Ć;Ǉ.Item2.Item1=Ƽ;Ǉ.Item2.Item2=Ʒ;
Ǉ.Item3.Item1=(byte)Ĝ;Ǉ.Item3.Item2=ǈ;Ǉ.Item3.Item3=Ǌ;var ǆ=ImmutableArray.CreateBuilder<float>(6);ǆ.Add(ƻ);ǆ.Add(ƺ);ǆ.
Add(ƿ);ǆ.Add(ǂ);ǆ.Add(ǐ);ǆ.Add(Ǐ);Ǉ.Item4=ǆ.ToImmutableArray();Ǉ.Item5.Item1=ǎ;Ǉ.Item5.Item2=Ǎ;Ǉ.Item5.Item3=ǌ;Ǉ.Item5.
Item4=ǋ;Ǉ.Item6=Ǒ;return Ǉ;}}List<MyTuple<string,Vector3D,ImmutableArray<string>>>ǅ=new List<MyTuple<string,Vector3D,
ImmutableArray<string>>>();void Ǆ(string ũ,Vector3D Ï,params string[]Ê){ǅ.Add(new MyTuple<string,Vector3D,ImmutableArray<string>>(ũ,Ï,
Ê.ToImmutableArray()));}void Ő(long ǃ){IGC.SendUnicastMessage(ǃ,"hud.apck.proj",ǅ.ToImmutableArray());ǅ.Clear();}