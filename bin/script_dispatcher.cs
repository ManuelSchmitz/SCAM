/*
 * R e a d m e
 * -----------
 * 
 * In this file you can include any instructions or other comments you want to have injected onto the 
 * top of your final script. You can safely delete this file if you do not want any such comments.
 */

const string Ver = "0.10.0"; // Must be the same on dispatcher and agents.

//static bool WholeAirspaceLocking = false;
static long DbgIgc = 0;
//static long DbgIgc = 76932813351402441; // pertam
//static long DbgIgc = 141426525525683227; // space
static bool IsLargeGrid;
static double Dt = 1 / 60f;
static float MAX_SP = 104.38f;
const float G = 9.81f;
const string DockHostTag = "docka-min3r";
bool ClearDocksOnReload = false;
const string logLevel = "Notice"; // Verbosity of log: "Debug", "Notice", "Warning" or "Critical".

static float StoppingPowerQuotient = 0.5f;
static bool MaxBrakeInProximity = true;
static bool MaxAccelInProximity = false;
static bool MoreRejectDampening = true;

static string LOCK_NAME_GeneralSection = "general";
static string LOCK_NAME_MiningSection = "mining-site";///< Airspace above the mining site.
static string LOCK_NAME_BaseSection = "base";         ///< Airspace above the base.


/** \brief The configuration state of the PB. */
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
	/** \brief Flips the toggle and returns the state after the flip. */
	public bool Invert(string key)
	{
sw[key] = !sw[key];
onToggleStateChangeHandler(key);
return sw[key];
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

bool pendingInitSequence;CommandRegistry commandRegistry;class CommandRegistry{Dictionary<string,Action<string[]>>
commands;public CommandRegistry(Dictionary<string,Action<string[]>>commands){this.commands=commands;}public void RunCommand(
string id,string[]cmdParts){this.commands[id].Invoke(cmdParts);}}static class E{public enum LogLevel{Critical=0,Warning=1,
Notice=2,Debug=3}static string debugTag="";static Action<string>echoClbk;static IMyTextSurface p;static IMyTextSurface lcd;
static bool bClearLCD=false;public static double simT;static List<string>linesToLog=new List<string>();static LogLevel
filterLevel=LogLevel.Notice;public static void Init(Action<string>echo,IMyGridTerminalSystem g,IMyProgrammableBlock me){echoClbk=
echo;p=me.GetSurface(0);p.ContentType=ContentType.TEXT_AND_IMAGE;p.WriteText("");if(!Enum.TryParse(logLevel,out filterLevel)
)Log("Invalid log-level: \""+logLevel+"\". Defaulting to \"Notice\".",LogLevel.Notice);}public static void Echo(string s)
{if((debugTag=="")||(s.Contains(debugTag)))echoClbk(s);}public static void Log(string msg,LogLevel lvl=LogLevel.Notice){
if(lvl>filterLevel)return;p.WriteText($"{simT:f1}: {msg}\n",true);switch(lvl){case LogLevel.Critical:linesToLog.Add(
$"{simT:f1}: CRITICAL: {msg}");break;case LogLevel.Warning:linesToLog.Add($"{simT:f1}: Warning: {msg}");break;default:linesToLog.Add(
$"{simT:f1}: {msg}");break;}}public static void ClearLog(){lcd?.WriteText("");linesToLog.Clear();}public static void SetLCD(IMyTextSurface
s){lcd=s;lcd.ContentType=ContentType.TEXT_AND_IMAGE;lcd.FontColor=new Color(r:0,g:255,b:116);lcd.Font="Monospace";lcd.
FontSize=0.65f;bClearLCD=true;}public static void EndOfTick(){if(linesToLog.Any()){if(lcd!=null){if(bClearLCD){lcd.WriteText("")
;bClearLCD=false;}linesToLog.Reverse();var t=string.Join("\n",linesToLog)+"\n"+lcd.GetText();var u=Variables.Get<int>(
"logger-char-limit");if(t.Length>u)t=t.Substring(0,u-1);lcd.WriteText(t);linesToLog.Clear();}else while(linesToLog.Count()>100)linesToLog.
RemoveAt(0);}}}static void Log(string msg,E.LogLevel lvl=E.LogLevel.Notice){E.Log(msg,lvl);}static int TickCount;void
StartOfTick(string arg){if(pendingInitSequence&&string.IsNullOrEmpty(arg)){pendingInitSequence=false;BroadcastToChannel("miners",
"dispatcher-change");arg=string.Join(",",Me.CustomData.Trim('\n').Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Where(s=>!s.
StartsWith("//")).Select(s=>"["+s+"]"));}if(string.IsNullOrEmpty(arg)||!arg.Contains(":"))return;var commands=arg.Split(new[]{
"],["},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim('[',']')).ToList();foreach(var c in commands){string[]cmdParts
=c.Split(new[]{':'},StringSplitOptions.RemoveEmptyEntries);if(cmdParts[0]=="command"){try{this.commandRegistry.RunCommand
(cmdParts[1],cmdParts);}catch(Exception ex){Log($"Run command '{cmdParts[1]}' failed.\n{ex}");}}if(cmdParts[0]=="toggle")
{Toggle.C.Invert(cmdParts[1]);Log($"Switching '{cmdParts[1]}' to state '{Toggle.C.Check(cmdParts[1])}'");}}}void
EndOfTick(){Scheduler.C.HandleTick();E.EndOfTick();}void Ctor(){if(!string.IsNullOrEmpty(Me.CustomData))pendingInitSequence=true;
E.Init(Echo,GridTerminalSystem,Me);Toggle.Init(new Dictionary<string,bool>{{"adaptive-mining",false},{
"adjust-entry-by-elevation",true},{"log-message",false},{"show-pstate",false},{"suppress-transition-control",false},{"suppress-gyro-control",false}
,{"damp-when-idle",true},{"ignore-user-thruster",false},{"cc",true}},key=>{switch(key){case"log-message":break;}});
NamedTeleData.Add("docking",new TargetTelemetry(1,"docking"));stateWrapper=new StateWrapper(s=>Storage=s);if(!stateWrapper.TryLoad(
Storage)){E.Echo("State load failed, clearing Storage now");stateWrapper.Save();Runtime.UpdateFrequency=UpdateFrequency.None;}
else{var p=(IMyTextPanel)GridTerminalSystem.GetBlockWithId(stateWrapper.PState.logLCD);if(p!=null)E.SetLCD(p);}
GridTerminalSystem.GetBlocksOfType(cameras,c=>c.IsSameConstructAs(Me));cameras.ForEach(c=>c.EnableRaycast=true);IsLargeGrid=Me.CubeGrid.
GridSizeEnum==MyCubeSize.Large;this.commandRegistry=new CommandRegistry(new Dictionary<string,Action<string[]>>{{"set-value",(parts)
=>Variables.Set(parts[2],parts[3])},{"add-panel",(parts)=>{List<IMyTextPanel>b=new List<IMyTextPanel>();GridTerminalSystem
.GetBlocksOfType(b,x=>x.IsSameConstructAs(Me)&&x.CustomName.Contains(parts[2]));var p=b.FirstOrDefault();if(p!=null){E.
Log($"Added {p.CustomName} as GUI panel");p.ContentType=ContentType.TEXT_AND_IMAGE;rawPanel=p;}}},{"add-gui-controller",(
parts)=>{List<IMyShipController>b=new List<IMyShipController>();GridTerminalSystem.GetBlocksOfType(b,x=>x.IsSameConstructAs(
Me)&&x.CustomName.Contains(parts[2]));guiSeat=b.FirstOrDefault();if(guiSeat!=null)E.Log(
$"Added {guiSeat.CustomName} as GUI controller");}},{"add-logger",(parts)=>{List<IMyTextPanel>b=new List<IMyTextPanel>();GridTerminalSystem.GetBlocksOfType(b,x=>x.
IsSameConstructAs(Me)&&x.CustomName.Contains(parts[2]));var p=b.FirstOrDefault();if(p==null)return;E.SetLCD(p);E.Log(
"Set logging LCD screen to: "+p.CustomName,E.LogLevel.Notice);stateWrapper.PState.logLCD=p.EntityId;}},{"set-role",(parts)=>Log(
"command:set-role is deprecated.",E.LogLevel.Warning)},{"low-update-rate",(parts)=>Runtime.UpdateFrequency=UpdateFrequency.Update10},{
"create-task-raycast",(parts)=>RaycastTaskHandler(parts)},{"create-task-gps",(parts)=>GPStaskHandler(parts)},{"recall",(parts)=>
dispatcherService?.Recall()},{"clear-storage-state",(parts)=>stateWrapper?.ClearPersistentState()},{"save",(parts)=>stateWrapper?.Save()}
,{"global",(parts)=>{var cmdParts=parts.Skip(2).ToArray();IGC.SendBroadcastMessage("miners.command",string.Join(":",
cmdParts),TransmissionDistance.TransmissionDistanceMax);Log("broadcasting global "+string.Join(":",cmdParts));commandRegistry.
RunCommand(cmdParts[1],cmdParts);}},{"get-toggles",(parts)=>{IGC.SendUnicastMessage(long.Parse(parts[2]),
$"menucommand.get-commands.reply:{string.Join(":",parts.Take(3))}",Toggle.C.GetToggleCommands());}},});dispatcherService=new Dispatcher(IGC,stateWrapper);var dockingPoints=new List<
IMyShipConnector>();GridTerminalSystem.GetBlocksOfType(dockingPoints,c=>c.IsSameConstructAs(Me)&&c.CustomName.Contains(DockHostTag));if(
ClearDocksOnReload)dockingPoints.ForEach(d=>d.CustomData="");dockHost=new DockHost(dispatcherService,dockingPoints,GridTerminalSystem);if(
stateWrapper.PState.ShaftStates.Count>0){var cap=stateWrapper.PState.ShaftStates;dispatcherService.CreateTask(stateWrapper.PState.
shaftRadius.Value,stateWrapper.PState.corePoint.Value,stateWrapper.PState.miningPlaneNormal.Value,stateWrapper.PState.layout_cur,
stateWrapper.PState.maxGen_cur,stateWrapper.PState.CurrentTaskGroup,stateWrapper.PState.bDense_cur);for(int n=0;n<Math.Min(
dispatcherService.CurrentTask.Shafts.Count,cap.Count());n++)dispatcherService.CurrentTask.Shafts[n].State=(ShaftState)cap[n];stateWrapper
.PState.ShaftStates=dispatcherService.CurrentTask.Shafts.Select(x=>(byte)x.State).ToList();E.Log(
$"Restored task from pstate, shaft count: {cap.Count}");}}static void AddUniqueItem<T>(T item,IList<T>c)where T:class{if((item!=null)&&!c.Contains(item))c.Add(item);}void
BroadcastToChannel<T>(string tag,T data){var channel=IGC.RegisterBroadcastListener(tag);IGC.SendBroadcastMessage(channel.Tag,data,
TransmissionDistance.TransmissionDistanceMax);}
/** \note Must have same values in the agent script!  */
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


/** \brief The layout of the mining task's shaft. */
public enum TaskLayout : byte {
	Circle  = 0, ///< Concentric circles, default.
	Hexagon = 1  ///< Tesselated hexagon: Clean, leaves fewer residue.
}


public enum ShaftState { Planned, InProgress, Complete, Cancelled }

StateWrapper stateWrapper;
public class StateWrapper
{
	public PersistentState PState { get; private set; }

	public void ClearPersistentState()
	{
var currentState = PState;
PState = new PersistentState();

/* Preserve some of the values. */
PState.StaticDockOverride    = currentState.StaticDockOverride;
PState.LifetimeAcceptedTasks = currentState.LifetimeAcceptedTasks;
PState.airspaceLockRequests  = currentState.airspaceLockRequests;
/* Task Parameters */
PState.layout                = currentState.layout;
PState.bDense                = currentState.bDense;
PState.maxGen                = currentState.maxGen;
/* Job Parameters */
PState.maxDepth              = currentState.maxDepth;
PState.skipDepth             = currentState.skipDepth;
PState.leastDepth            = currentState.leastDepth;
PState.safetyDist            = currentState.safetyDist;
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
	PState.Save(stateSaver);
}
catch (Exception ex)
{
	E.Log("State save failed.");
	E.Log(ex.ToString());
}
	}

	public bool TryLoad(string serialized)
	{
PState = new PersistentState();
try
{
	PState.Load(serialized);
	return true;
}
catch (Exception ex)
{
	E.Log("State load failed.");
	E.Log(ex.ToString());
}
return false;
	}
}


/** \brief Request for an airspace lock. */
public struct LockRequest {
	public long   id;      ///< ID of the requesting PB. Send answer there.
	public string lockName;///< Name of the requested lock.
	public LockRequest(long _id, string _ln) { id = _id; lockName = _ln; }
	public override string ToString() { return $"{id},{lockName}"; }
	public static LockRequest Parse(string s) {
var p = s.Split(',');
return new LockRequest(long.Parse(p[0]), p[1]);
	}
}


public class PersistentState
{
	public int LifetimeAcceptedTasks = 0;

	// cleared by specific command
	public Vector3D? StaticDockOverride { get; set; }

	// cleared by clear-storage-state (task-dependent)
	public long logLCD;                            ///< Entity ID of the logging screen.
	public List<LockRequest> airspaceLockRequests  ///< Airspace lock queue.
	                         = new List<LockRequest>();
	public Vector3D? miningPlaneNormal;
	public Vector3D? corePoint;
	public float? shaftRadius;

	/* Task parameters. */
	public TaskLayout layout;    ///< Layout of the shaft arrangement. (future tasks)
	public TaskLayout layout_cur;///< Layout of the shaft arrangement. (current task)
	public bool  bDense;         ///< Dense / overlapping shaft layout? (future tasks)
	public bool  bDense_cur;     ///< Dense / overlapping shaft layout? (current task)
	public int   maxGen;         ///< Size of the layout. (future tasks)
	public int   maxGen_cur;     ///< Size of the layout. (current task)

	/* Job parameters. */
	public float maxDepth;
	public float skipDepth;
	public float leastDepth;
	public float safetyDist;

	public List<byte> ShaftStates = new List<byte>();
	public string CurrentTaskGroup;

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
	else if (typeof(T) == typeof(long))
		return (T)(object)long.Parse(res);
	else if (typeof(T) == typeof(long?))
		return (T)(object)long.Parse(res);
	else if (typeof(T) == typeof(Vector3D?))
	{
		var d = res.Split(':');
		return (T)(object)new Vector3D(double.Parse(d[0]), double.Parse(d[1]), double.Parse(d[2]));
	}
	else if (typeof(T) == typeof(List<LockRequest>))
	{
		var d = res.Split(':');
		var q = new List<LockRequest>();
		foreach (var p in d)
			q.Add(LockRequest.Parse(p));
		return (T)(object)q;
	}
	else if (typeof(T) == typeof(List<byte>))
	{
		var d = res.Split(':');
		return (T)(object)d.Select(x => byte.Parse(x)).ToList();
	}
	else if (typeof(T) == typeof(TaskLayout))
	{
		return (T)Enum.Parse(typeof(TaskLayout), res);
	}
}
return default(T);
	}

	public PersistentState Load(string storage)
	{
if (string.IsNullOrEmpty(storage))
	return this;

var values = storage.Split('\n').ToDictionary(s => s.Split('=')[0], s => string.Join("=", s.Split('=').Skip(1)));
foreach (var v in values)
	E.Log("Load: " + v.Key + " <-- " + v.Value , E.LogLevel.Debug);

LifetimeAcceptedTasks = ParseValue<int>(values, "LifetimeAcceptedTasks");

StaticDockOverride   = ParseValue<Vector3D?>        (values, "StaticDockOverride");
logLCD               = ParseValue<long>             (values, "logLCD");
airspaceLockRequests = ParseValue<List<LockRequest>>(values, "airspaceLockRequests") ?? new List<LockRequest>();
miningPlaneNormal    = ParseValue<Vector3D?>        (values, "miningPlaneNormal");
corePoint = ParseValue<Vector3D?>(values, "corePoint");
shaftRadius = ParseValue<float?>(values, "shaftRadius");

/* Task Parameters. */
layout     = ParseValue<TaskLayout> (values, "layout");
layout_cur = ParseValue<TaskLayout> (values, "layout_cur");
bDense     = ParseValue<bool>       (values, "bDense");
bDense_cur = ParseValue<bool>       (values, "bDense_cur");
maxGen     = ParseValue<int>        (values, "maxGen");
maxGen_cur = ParseValue<int>        (values, "maxGen_cur");

/* Job parameters. */
maxDepth    = ParseValue<float>     (values, "maxDepth");
skipDepth   = ParseValue<float>     (values, "skipDepth");
leastDepth  = ParseValue<float>     (values, "leastDepth");
Toggle.C.Set("adaptive-mining",           ParseValue<bool>(values, "adaptiveMode"));
Toggle.C.Set("adjust-entry-by-elevation", ParseValue<bool>(values, "adjustAltitude"));
safetyDist = ParseValue<float>      (values, "safetyDist");

CurrentTaskGroup = ParseValue<string>(values, "CurrentTaskGroup");

ShaftStates = ParseValue<List<byte>>(values, "ShaftStates") ?? new List<byte>();
return this;
	}
public void Save(Action<string>store){store(Serialize());}string Serialize(){string[]pairs=new string[]{
"LifetimeAcceptedTasks="+LifetimeAcceptedTasks,"StaticDockOverride="+(StaticDockOverride.HasValue?VectorOpsHelper.V3DtoBroadcastString(
StaticDockOverride.Value):""),"logLCD="+logLCD,"airspaceLockRequests="+string.Join(":",airspaceLockRequests),"miningPlaneNormal="+(
miningPlaneNormal.HasValue?VectorOpsHelper.V3DtoBroadcastString(miningPlaneNormal.Value):""),"corePoint="+(corePoint.HasValue?
VectorOpsHelper.V3DtoBroadcastString(corePoint.Value):""),"shaftRadius="+shaftRadius,"layout="+layout,"layout_cur="+layout_cur,
"bDense="+bDense,"bDense_cur="+bDense_cur,"maxGen="+maxGen,"maxGen_cur="+maxGen_cur,"maxDepth="+maxDepth,"skipDepth="+skipDepth,
"leastDepth="+leastDepth,"adaptiveMode="+Toggle.C.Check("adaptive-mining"),"adjustAltitude="+Toggle.C.Check(
"adjust-entry-by-elevation"),"safetyDist="+safetyDist,"CurrentTaskGroup="+CurrentTaskGroup,"ShaftStates="+string.Join(":",ShaftStates),};string s=
string.Join("\n",pairs);Log("Serialised persistent state: "+s,E.LogLevel.Debug);return s;}public override string ToString(){
return Serialize();}}void Save(){stateWrapper.Save();}Program(){Runtime.UpdateFrequency=UpdateFrequency.Update1;Ctor();}List<
MyIGCMessage>uniMsgs=new List<MyIGCMessage>();int _cycle_counter=0;void Main(string param,UpdateType updateType){_cycle_counter=(++
_cycle_counter%5);if(_cycle_counter!=0)return;uniMsgs.Clear();while(IGC.UnicastListener.HasPendingMessage){uniMsgs.Add(IGC.
UnicastListener.AcceptMessage());}var commandChannel=IGC.RegisterBroadcastListener("miners.command");if(commandChannel.
HasPendingMessage){var msg=commandChannel.AcceptMessage();param=msg.Data.ToString();Log("Got miners.command: "+param);}TickCount++;Echo(
"Run count: "+TickCount);StartOfTick(param);foreach(var m in uniMsgs){if(m.Tag=="apck.ntv.update"){var igcDto=(MyTuple<MyTuple<string
,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>)m.Data;var name=igcDto.Item1.Item1;UpdateNTV(name,igcDto);}
else if(m.Tag=="apck.depart.complete"){dockHost?.DepartComplete(m.Source.ToString());}else if(m.Tag=="apck.depart.request"){
dockHost.RequestDocking(m.Source,(Vector3D)m.Data,true);}else if(m.Tag=="apck.docking.request"){dockHost.RequestDocking(m.Source
,(Vector3D)m.Data);}else if(m.Tag=="apck.docking.approach"||m.Tag=="apck.depart.approach"){throw new Exception(
"Dispatcher cannot process incoming approach paths.");}}E.Echo($"Version: {Ver}");E.Echo(dispatcherService.ToString());dispatcherService.HandleIGC(uniMsgs);if(TickCount>100
)dispatcherService.GrantAirspaceLocks();if(guiH!=null){foreach(var s in dispatcherService.subordinates)IGC.
SendUnicastMessage(s.Id,"report.request","");}guiH?.UpdateTaskSummary(dispatcherService);dockHost.Handle(IGC,TickCount);if(rawPanel!=null)
{if(guiSeat!=null){if(guiH==null){guiH=new GuiHandler(rawPanel,dispatcherService,stateWrapper);dispatcherService.
OnTaskUpdate=guiH.UpdateMiningScheme;guiH.OnShaftClick=id=>dispatcherService.CancelShaft(id);if(dispatcherService.CurrentTask!=null)
dispatcherService.OnTaskUpdate.Invoke(dispatcherService.CurrentTask);}else guiH.Handle(rawPanel,guiSeat);}}if(Toggle.C.Check(
"show-pstate"))E.Echo(stateWrapper.PState.ToString());EndOfTick();CheckExpireNTV();if(DbgIgc!=0)EmitFlush(DbgIgc);Dt=Math.Max(0.001,
Runtime.TimeSinceLastRun.TotalSeconds);E.simT+=Dt;iCount=Math.Max(iCount,Runtime.CurrentInstructionCount);E.Echo(
$"InstructionCount (Max): {Runtime.CurrentInstructionCount} ({iCount})");E.Echo($"Processed in {Runtime.LastRunTimeMs:f3} ms");}int iCount;void GPStaskHandler(string[]cmdString){if(
dispatcherService!=null){var vdtoArr=cmdString.Skip(2).ToArray();var pos=new Vector3D(double.Parse(vdtoArr[0]),double.Parse(vdtoArr[1]),
double.Parse(vdtoArr[2]));Vector3D n;if(vdtoArr.Length>3){n=Vector3D.Normalize(pos-new Vector3D(double.Parse(vdtoArr[3]),
double.Parse(vdtoArr[4]),double.Parse(vdtoArr[5])));}else{if(guiSeat==null){E.Log(
"WARNING: the normal was not supplied and there is no Control Station available to check if we are in gravity");n=-dockHost.GetFirstNormal();E.Log("Using 'first dock connector Backward' as a normal");}else{Vector3D pCent;if(
guiSeat.TryGetPlanetPosition(out pCent)){n=Vector3D.Normalize(pCent-pos);E.Log(
"Using mining-center-to-planet-center direction as a normal because we are in gravity");}else{n=-dockHost.GetFirstNormal();E.Log("Using 'first dock connector Backward' as a normal");}}}var c=Variables.Get<
string>("group-constraint");if(!string.IsNullOrEmpty(c)){dispatcherService.CreateTask(Variables.Get<float>(
"circular-pattern-shaft-radius"),pos,n,stateWrapper.PState.layout,stateWrapper.PState.maxGen,c,stateWrapper.PState.bDense);dispatcherService.
BroadcastStart(c);}else E.Log(
"To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");}else E.Log("GPStaskHandler is intended for Dispatcher role");}List<IMyCameraBlock>cameras=new List<IMyCameraBlock>();
Vector3D?castedSurfacePoint;Vector3D?castedNormal;void RaycastTaskHandler(string[]cmdString){var cam=cameras.Where(c=>c.IsActive
).FirstOrDefault();if(cam!=null){cam.CustomData="";var pos=cam.GetPosition()+cam.WorldMatrix.Forward*Variables.Get<float>
("ct-raycast-range");cam.CustomData+="GPS:dir0:"+VectorOpsHelper.V3DtoBroadcastString(pos)+":\n";Log(
$"RaycastTaskHandler tries to raycast point GPS:create-task base point:{VectorOpsHelper.V3DtoBroadcastString(pos)}:");if(cam.CanScan(pos)){var dei=cam.Raycast(pos);if(!dei.IsEmpty()){castedSurfacePoint=dei.HitPosition.Value;Log(
$"GPS:Raycasted base point:{VectorOpsHelper.V3DtoBroadcastString(dei.HitPosition.Value)}:");cam.CustomData+="GPS:castedSurfacePoint:"+VectorOpsHelper.V3DtoBroadcastString(castedSurfacePoint.Value)+":\n";
IMyShipController gravGetter=guiSeat;Vector3D pCent;if((gravGetter!=null)&&gravGetter.TryGetPlanetPosition(out pCent)){castedNormal=
Vector3D.Normalize(pCent-castedSurfacePoint.Value);E.Log(
"Using mining-center-to-planet-center direction as a normal because we are in gravity");}else{var toBasePoint=castedSurfacePoint.Value-cam.GetPosition();var perp=Vector3D.Normalize(Vector3D.
CalculatePerpendicularVector(toBasePoint));var p1=castedSurfacePoint.Value+perp*Math.Min(10,toBasePoint.Length());var p2=castedSurfacePoint.Value+
Vector3D.Normalize(Vector3D.Cross(perp,toBasePoint))*Math.Min(20,toBasePoint.Length());var pt1=p1+Vector3D.Normalize(p1-cam.
GetPosition())*500;var pt2=p2+Vector3D.Normalize(p2-cam.GetPosition())*500;cam.CustomData+="GPS:target1:"+VectorOpsHelper.
V3DtoBroadcastString(pt1)+":\n";if(cam.CanScan(pt1)){var cast1=cam.Raycast(pt1);if(!cast1.IsEmpty()){Log(
$"GPS:Raycasted aux point 1:{VectorOpsHelper.V3DtoBroadcastString(cast1.HitPosition.Value)}:");cam.CustomData+="GPS:cast1:"+VectorOpsHelper.V3DtoBroadcastString(cast1.HitPosition.Value)+":\n";cam.CustomData+=
"GPS:target2:"+VectorOpsHelper.V3DtoBroadcastString(pt2)+":\n";if(cam.CanScan(pt2)){var cast2=cam.Raycast(pt2);if(!cast2.IsEmpty()){
Log($"GPS:Raycasted aux point 2:{VectorOpsHelper.V3DtoBroadcastString(cast2.HitPosition.Value)}:");cam.CustomData+=
"GPS:cast2:"+VectorOpsHelper.V3DtoBroadcastString(cast2.HitPosition.Value)+":";castedNormal=-Vector3D.Normalize(Vector3D.Cross(cast1
.HitPosition.Value-castedSurfacePoint.Value,cast2.HitPosition.Value-castedSurfacePoint.Value));}}}}}if(castedNormal.
HasValue&&castedSurfacePoint.HasValue){E.Log("Successfully got mining center and mining normal");if(dispatcherService!=null){var
c=Variables.Get<string>("group-constraint");if(!string.IsNullOrEmpty(c)){dispatcherService.BroadcastStart(c);
dispatcherService.CreateTask(Variables.Get<float>("circular-pattern-shaft-radius"),castedSurfacePoint.Value-castedNormal.Value*10,
castedNormal.Value,stateWrapper.PState.layout,stateWrapper.PState.maxGen,c,stateWrapper.PState.bDense);}else Log(
"To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");}}else{E.Log($"RaycastTaskHandler failed to get castedNormal or castedSurfacePoint");}}}else{E.Log($"RaycastTaskHandler couldn't raycast initial position. Camera '{cam.CustomName}' had {cam.AvailableScanRange} AvailableScanRange"
);}}else{throw new Exception($"No active cam, {cameras.Count} known");}}Dispatcher dispatcherService;class Dispatcher{
public List<Subordinate>subordinates=new List<Subordinate>();public Action<MiningTask>OnTaskUpdate;public class Subordinate{
public long Id;public string ObtainedLock;public float Echelon;public string Group;public TransponderMsg Report;}public string
GetSubordinateName(long id){var sb=subordinates.FirstOrDefault(s=>s.Id==id);return sb==null?"n/a":sb.Report.name;}
IMyIntergridCommunicationSystem IGC;StateWrapper stateWrapper;public Dispatcher(IMyIntergridCommunicationSystem igc,StateWrapper stateWrapper){IGC=igc;
this.stateWrapper=stateWrapper;}private void EnqueueLockRequest(long src,string lockName){int idx=stateWrapper.PState.
airspaceLockRequests.FindIndex(s=>s.id==src);if(idx<0){stateWrapper.PState.airspaceLockRequests.Add(new LockRequest(src,lockName));Log(
"Airspace permission request added to requests queue: "+GetSubordinateName(src)+" / "+lockName,E.LogLevel.Debug);return;}if(lockName==""){stateWrapper.PState.
airspaceLockRequests.RemoveAt(idx);Log("Airspace permission request by "+GetSubordinateName(src)+" was cancelled.");}else{stateWrapper.
PState.airspaceLockRequests.RemoveAt(idx);stateWrapper.PState.airspaceLockRequests.Add(new LockRequest(src,lockName));Log(
"Airspace permission request by "+GetSubordinateName(src)+" was changed: "+lockName);}}public bool IsLockGranteable(string lockName,long agentId){if(
subordinates.Any(s=>s.ObtainedLock==lockName&&s.Id!=agentId))return false;if(lockName==LOCK_NAME_MiningSection&&subordinates.Any(s=>
s.ObtainedLock==LOCK_NAME_GeneralSection&&s.Id!=agentId))return false;if(lockName==LOCK_NAME_BaseSection&&subordinates.
Any(s=>s.ObtainedLock==LOCK_NAME_GeneralSection&&s.Id!=agentId))return false;if(lockName==LOCK_NAME_MiningSection||lockName
==LOCK_NAME_BaseSection){var applicant=subordinates.FirstOrDefault(s=>s.Id==agentId);foreach(var sb in subordinates){if(sb
.Id==agentId)continue;switch(sb.Report.state){case MinerState.Disabled:case MinerState.Idle:case MinerState.Drilling:case
MinerState.AscendingInShaft:case MinerState.Maintenance:case MinerState.Docked:continue;case MinerState.GoingToEntry:case
MinerState.GoingToUnload:case MinerState.ChangingShaft:if(lockName==LOCK_NAME_MiningSection)return false;else continue;case
MinerState.Docking:case MinerState.Takeoff:if(lockName==LOCK_NAME_BaseSection)return false;else continue;default:return false;case
MinerState.WaitingForDocking:case MinerState.ReturningToShaft:case MinerState.ReturningHome:Vector3D dist=sb.Report.WM.Translation
-applicant.Report.WM.Translation;Vector3D v_rel=sb.Report.v-applicant.Report.v;if(Vector3D.Dot(sb.Report.v,applicant.
Report.v)>0&&dist.Length()>50.0)continue;if(dist.Length()>600.0)continue;if(applicant.Echelon<sb.Echelon)continue;if(sb.Report
.v.Length()<=0.1&&stateWrapper.PState.airspaceLockRequests.Any(s=>s.id==sb.Id))continue;return false;}}}return true;}
public void GrantAirspaceLocks(){int pref=-1;Subordinate pref_sb=new Subordinate();for(int i=0;i<stateWrapper.PState.
airspaceLockRequests.Count();++i){var sb=subordinates.First(s=>s.Id==stateWrapper.PState.airspaceLockRequests[i].id);if(sb==null){Log($"Agent {stateWrapper.PState.airspaceLockRequests[i].id} is no longer a subordinate. Dropping its request for airspace lock."
,E.LogLevel.Warning);stateWrapper.PState.airspaceLockRequests.RemoveAt(i);--i;continue;}if(!IsLockGranteable(stateWrapper
.PState.airspaceLockRequests[i].lockName,stateWrapper.PState.airspaceLockRequests[i].id))continue;if(pref<0){pref=i;
pref_sb=sb;continue;}if(pref_sb.Report.state==MinerState.Docked&&sb.Report.state!=MinerState.Docked){pref=i;pref_sb=sb;continue
;}float urgency=sb.Report.Urgency();float pref_urgency=pref_sb.Report.Urgency();if(urgency>pref_urgency){pref=i;pref_sb=
sb;continue;}}if(pref<0)return;var cand=stateWrapper.PState.airspaceLockRequests[pref];stateWrapper.PState.
airspaceLockRequests.RemoveAt(pref);subordinates.First(s=>s.Id==cand.id).ObtainedLock=cand.lockName;IGC.SendUnicastMessage(cand.id,"miners",
"common-airspace-lock-granted:"+cand.lockName);Log(cand.lockName+" granted to "+GetSubordinateName(cand.id),E.LogLevel.Debug);}public void HandleIGC(
List<MyIGCMessage>uniMsgs){var minerChannel=IGC.RegisterBroadcastListener("miners");while(minerChannel.HasPendingMessage){
var msg=minerChannel.AcceptMessage();if(msg.Data==null)continue;if(msg.Data.ToString().Contains(
"common-airspace-ask-for-lock")){var sectionName=msg.Data.ToString().Split(':')[1];EnqueueLockRequest(msg.Source,sectionName);}if(msg.Data.ToString().
Contains("common-airspace-lock-released")){var sectionName=msg.Data.ToString().Split(':')[1];Log(
"(Dispatcher) received lock-released notification "+sectionName+" from "+GetSubordinateName(msg.Source),E.LogLevel.Debug);subordinates.Single(s=>s.Id==msg.Source).
ObtainedLock="";}}var minerHandshakeChannel=IGC.RegisterBroadcastListener("miners.handshake");while(minerHandshakeChannel.
HasPendingMessage){var msg=minerHandshakeChannel.AcceptMessage();if(!(msg.Data is MyTuple<string,MyTuple<MyTuple<long,string>,MyTuple<
MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,
string>>>,string>)){Log("Ignoring handshake broadcast with malformed transponder data.",E.LogLevel.Warning);continue;}var data
=(MyTuple<string,MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,
MyTuple<bool,bool,float,float>,ImmutableArray<MyTuple<string,string>>>,string>)msg.Data;if(data.Item3!=Ver){Log(
$"Ignoring handshake broadcast by {data.Item2.Item1.Item2}: Wrong s/w version {data.Item3} (vs {Ver}).",E.LogLevel.Warning);continue;}Log($"Initiated handshake by {data.Item2.Item1.Item2}, group tag: {data.Item1}",E.
LogLevel.Notice);Subordinate sb;if(!subordinates.Any(s=>s.Id==msg.Source)){sb=new Subordinate{Id=msg.Source,Echelon=(
subordinates.Count+1)*Variables.Get<float>("echelon-offset")+10f,Group=data.Item1};subordinates.Add(sb);sb.Report=new TransponderMsg
();}else{sb=subordinates.Single(s=>s.Id==msg.Source);sb.Group=data.Item1;}sb.Report.UpdateFromIgc(data.Item2);IGC.
SendUnicastMessage(msg.Source,"miners.handshake.reply",IGC.Me);IGC.SendUnicastMessage(msg.Source,"miners.echelon",sb.Echelon);if(
stateWrapper.PState.miningPlaneNormal.HasValue){IGC.SendUnicastMessage(msg.Source,"miners.normal",stateWrapper.PState.
miningPlaneNormal.Value);}var vals=new string[]{"getAbove-altitude"};Scheduler.C.After(500).RunCmd(()=>{foreach(var v in vals){Log(
$"Propagating set-value:'{v}' to "+sb.Report.name,E.LogLevel.Debug);IGC.SendUnicastMessage(msg.Source,"set-value",$"{v}:{Variables.Get<float>(v)}");}});}
var minerReportChannel=IGC.RegisterBroadcastListener("miners.report");while(minerReportChannel.HasPendingMessage){var msg=
minerReportChannel.AcceptMessage();var sub=subordinates.FirstOrDefault(s=>s.Id==msg.Source);if(sub==null)continue;var data=(MyTuple<
MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,float>,
ImmutableArray<MyTuple<string,string>>>)msg.Data;sub.Report.UpdateFromIgc(data);}foreach(var msg in uniMsgs){if(msg.Tag=="create-task"
){var data=(MyTuple<float,Vector3D,Vector3D>)msg.Data;var sub=subordinates.First(s=>s.Id==msg.Source);Log(
"Got new mining task from agent "+sub.Report.name);sub.ObtainedLock=LOCK_NAME_GeneralSection;CreateTask(data.Item1,data.Item2,data.Item3,stateWrapper.
PState.layout,stateWrapper.PState.maxGen,sub.Group,stateWrapper.PState.bDense);BroadcastStart(sub.Group);}if(msg.Tag.Contains(
"request-new")){if(msg.Tag=="shaft-complete-request-new"){CompleteShaft((int)msg.Data);Log(GetSubordinateName(msg.Source)+
$" reports shaft {msg.Data} complete.",E.LogLevel.Notice);}Vector3D?entry=Vector3D.Zero;Vector3D?getabove=Vector3D.Zero;int shId=0;if((CurrentTask!=null)&&
AssignNewShaft(ref entry,ref getabove,ref shId)){IGC.SendUnicastMessage(msg.Source,"miners.assign-shaft",new MyTuple<int,Vector3D,
Vector3D,MyTuple<float,float,float,bool,bool>>(shId,entry.Value,getabove.Value,new MyTuple<float,float,float,bool,bool>(
stateWrapper.PState.maxDepth,stateWrapper.PState.skipDepth,stateWrapper.PState.leastDepth,Toggle.C.Check("adaptive-mining"),Toggle.C
.Check("adjust-entry-by-elevation"))));E.Log($"Assigned new shaft {shId} to "+GetSubordinateName(msg.Source)+'.',E.
LogLevel.Notice);}else{IGC.SendUnicastMessage(msg.Source,"command","force-finish");}}if(msg.Tag=="ban-direction"){
BanDirectionByPoint((int)msg.Data);}}foreach(var s in subordinates){E.Echo(s.Id+": echelon = "+s.Echelon+" lock: "+s.ObtainedLock);}}public
void BroadcastResume(){var g=stateWrapper.PState.CurrentTaskGroup;if(string.IsNullOrEmpty(g))return;Log(
$"Broadcasting task resume for mining group '{g}'");foreach(var s in subordinates.Where(x=>x.Group==g)){IGC.SendUnicastMessage(s.Id,"miners.resume",stateWrapper.PState.
miningPlaneNormal.Value);}}public void BroadCastHalt(){Log($"Broadcasting global Halt & Clear state");IGC.SendBroadcastMessage(
"miners.command","command:halt");}public void BroadcastStart(string group){Log($"Preparing start for mining group '{group}'");IGC.
SendBroadcastMessage("miners.command","command:clear-storage-state");Scheduler.C.Clear();stateWrapper.PState.LifetimeAcceptedTasks++;
Scheduler.C.After(500).RunCmd(()=>{foreach(var s in subordinates.Where(x=>x.Group==group)){IGC.SendUnicastMessage(s.Id,
"miners.normal",stateWrapper.PState.miningPlaneNormal.Value);}});Scheduler.C.After(1000).RunCmd(()=>{Log(
$"Broadcasting start for mining group '{group}'");foreach(var s in subordinates.Where(x=>x.Group==group)){IGC.SendUnicastMessage(s.Id,"command","mine");}});}public void
Recall(){IGC.SendBroadcastMessage("miners.command","command:force-finish");Log($"Broadcasting Recall");}public void Recall(int
n){if(n>=0&&n<subordinates.Count())IGC.SendUnicastMessage(subordinates[n].Id,"command","force-finish");}public void
PurgeLocks(){IGC.SendBroadcastMessage("miners.command","command:dispatch");stateWrapper.PState.airspaceLockRequests.Clear();
subordinates.ForEach(x=>x.ObtainedLock="");Log($"WARNING! Purging Locks, green light for everybody...");}public MiningTask
CurrentTask;public class MiningTask{public float R{get;private set;}public Vector3D miningPlaneNormal{get;private set;}public
Vector3D corePoint{get;private set;}public Vector3D planeXunit{get;private set;}public Vector3D planeYunit{get;private set;}
public string GroupConstraint{get;private set;}public List<MiningShaft>Shafts;public MiningTask(TaskLayout layout,int
maxGenerations,float shaftRadius,Vector3D coreP,Vector3D normal,string groupConstraint,bool bDense){R=shaftRadius;GroupConstraint=
groupConstraint;miningPlaneNormal=normal;corePoint=coreP;planeXunit=Vector3D.Normalize(Vector3D.Cross(coreP,normal));planeYunit=
Vector3D.Cross(planeXunit,normal);if(layout==TaskLayout.Circle){var radInterval=R*2f*0.866f*(bDense?0.866f:1f);Shafts=new List<
MiningShaft>(maxGenerations*6+1);Shafts.Add(new MiningShaft());int id=1;for(int g=1;g<maxGenerations;g++){for(int i=0;i<g*6;i++){
double angle=60f/180f*Math.PI*i/g;var p=new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle))*radInterval*g;Shafts.Add(
new MiningShaft(){Point=p,Id=id++});}}}else if(layout==TaskLayout.Hexagon){Shafts=new List<MiningShaft>(maxGenerations*6+1)
;Shafts.Add(new MiningShaft());int id=1;float d=R*2f*(bDense?0.866f:1f);Vector2[]_dir=new Vector2[]{new Vector2(-0.50000f
,0.86603f),new Vector2(-1.00000f,0.00000f),new Vector2(-0.50000f,-0.86603f),new Vector2(0.50000f,-0.86603f),new Vector2(
1.00000f,0.00000f),new Vector2(0.50000f,0.86603f)};for(int g=1;g<maxGenerations;++g){var p=new Vector2(d*g,0f);for(int i=0;i<g*6
;++i){Shafts.Add(new MiningShaft(){Point=p,Id=id++});p+=_dir[i/g]*d;}}}}public void BanDirection(int id){var pt=Shafts.
First(x=>x.Id==id).Point;foreach(var sh in Shafts){if(Vector2.Dot(Vector2.Normalize(sh.Point),Vector2.Normalize(pt))>.8f)sh.
State=ShaftState.Cancelled;}}public void SetShaftState(int id,ShaftState state){var sh=Shafts.First(x=>x.Id==id);var current=
sh.State;if(current==ShaftState.Cancelled&&current==state)state=ShaftState.Planned;sh.State=state;}public bool
RequestShaft(ref Vector3D?entry,ref Vector3D?getAbove,ref int id,float d_safety=0){var sh=Shafts.FirstOrDefault(x=>x.State==
ShaftState.Planned&&Shafts.All(other=>other.State!=ShaftState.InProgress||(other.Point-x.Point).Length()>=d_safety));if(sh==null)
return false;entry=corePoint+planeXunit*sh.Point.X+planeYunit*sh.Point.Y;getAbove=entry.Value-miningPlaneNormal*Variables.Get<
float>("getAbove-altitude");id=sh.Id;sh.State=ShaftState.InProgress;return true;}public class MiningShaft{public ShaftState
State=ShaftState.Planned;public Vector2 Point;public int Id;}}public void CreateTask(float r,Vector3D corePoint,Vector3D
miningPlaneNormal,TaskLayout layout,int maxGenerations,string groupConstraint,bool bDense){CurrentTask=new MiningTask(layout,
maxGenerations,r,corePoint,miningPlaneNormal,groupConstraint,bDense);OnTaskUpdate?.Invoke(CurrentTask);stateWrapper.
ClearPersistentState();stateWrapper.PState.layout_cur=stateWrapper.PState.layout;stateWrapper.PState.bDense_cur=stateWrapper.PState.bDense;
stateWrapper.PState.maxGen_cur=stateWrapper.PState.maxGen;stateWrapper.PState.corePoint=corePoint;stateWrapper.PState.shaftRadius=r;
stateWrapper.PState.miningPlaneNormal=miningPlaneNormal;stateWrapper.PState.ShaftStates=CurrentTask.Shafts.Select(x=>(byte)x.State).
ToList();stateWrapper.PState.CurrentTaskGroup=groupConstraint;Log($"Creating task...");Log(VectorOpsHelper.GetGPSString(
"min3r.task.P",corePoint,Color.Red));Log(VectorOpsHelper.GetGPSString("min3r.task.Np",corePoint-miningPlaneNormal,Color.Red));Log(
$"shaftRadius: {r}");Log($"maxGenerations: {maxGenerations}");Log($"shafts: {CurrentTask.Shafts.Count}");Log(
$"groupConstraint: {CurrentTask.GroupConstraint}");Log($"Task created");}public void CancelShaft(int id){var s=ShaftState.Cancelled;CurrentTask?.SetShaftState(id,s);
stateWrapper.PState.ShaftStates[id]=(byte)s;OnTaskUpdate?.Invoke(CurrentTask);}public void CompleteShaft(int id){var s=ShaftState.
Complete;CurrentTask?.SetShaftState(id,ShaftState.Complete);stateWrapper.PState.ShaftStates[id]=(byte)s;OnTaskUpdate?.Invoke(
CurrentTask);}public void BanDirectionByPoint(int id){CurrentTask?.BanDirection(id);OnTaskUpdate?.Invoke(CurrentTask);}public bool
AssignNewShaft(ref Vector3D?entry,ref Vector3D?getAbove,ref int id){Log("CurrentTask.RequestShaft",E.LogLevel.Debug);bool res=
CurrentTask.RequestShaft(ref entry,ref getAbove,ref id,stateWrapper.PState.shaftRadius.Value*2.0f*stateWrapper.PState.safetyDist);
stateWrapper.PState.ShaftStates[id]=(byte)ShaftState.InProgress;OnTaskUpdate?.Invoke(CurrentTask);return res;}StringBuilder sb=new
StringBuilder();public override string ToString(){sb.Clear();sb.AppendLine(
$"CircularPattern radius: {stateWrapper.PState.shaftRadius:f2}");sb.AppendLine($" ");sb.AppendLine($"Total subordinates: {subordinates.Count}");sb.AppendLine(
$"Lock queue: {stateWrapper.PState.airspaceLockRequests.Count}");sb.AppendLine($"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");return sb.ToString();}}static
class VectorOpsHelper{public static string V3DtoBroadcastString(params Vector3D[]vectors){return string.Join(":",vectors.
Select(v=>string.Format("{0}:{1}:{2}",v.X,v.Y,v.Z)));}public static string GetGPSString(string name,Vector3D p,Color c){return
$"GPS:{name}:{p.X}:{p.Y}:{p.Z}:#{c.R:X02}{c.G:X02}{c.B:X02}:";}}IMyTextPanel rawPanel;IMyShipController guiSeat;class Scheduler{static Scheduler inst=new Scheduler();Scheduler(){}
public static Scheduler C{get{inst.delayForNextCmd=0;inst.repeatCondition=null;return inst;}}class DelayedCommand{public
DateTime TimeStamp;public Action Command;public Func<bool>repeatCondition;public long delay;}Queue<DelayedCommand>q=new Queue<
DelayedCommand>();long delayForNextCmd;Func<bool>repeatCondition;public Scheduler After(int ms){this.delayForNextCmd+=ms;return this;}
public Scheduler RunCmd(Action cmd){q.Enqueue(new DelayedCommand{TimeStamp=DateTime.Now.AddMilliseconds(delayForNextCmd),
Command=cmd,repeatCondition=repeatCondition,delay=delayForNextCmd});return this;}public void HandleTick(){if(q.Count>0){E.Echo(
"Scheduled actions count:"+q.Count);var c=q.Peek();if(c.TimeStamp<DateTime.Now){if(c.repeatCondition!=null){if(c.repeatCondition.Invoke()){c.
Command.Invoke();c.TimeStamp=DateTime.Now.AddMilliseconds(c.delay);}else{q.Dequeue();}}else{c.Command.Invoke();q.Dequeue();}}}}
public void Clear(){q.Clear();delayForNextCmd=0;repeatCondition=null;}}void UpdateNTV(string key,MyTuple<MyTuple<string,long,
long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>dto){NamedTeleData[key].ParseIgc(dto,TickCount);}void CheckExpireNTV(
){foreach(var x in NamedTeleData.Values.Where(v=>v.Position.HasValue)){E.Echo(x.Name+((x.Position.Value==Vector3D.Zero)?
" Zero!":" OK"));x.CheckExpiration(TickCount);}}Dictionary<string,TargetTelemetry>NamedTeleData=new Dictionary<string,
TargetTelemetry>();struct TeleDto{public Vector3D?pos;public Vector3D?vel;public MatrixD?rot;public BoundingBoxD?bb;public
MyDetectedEntityType?type;}class TargetTelemetry{public long TickStamp;int clock;public string Name;public long EntityId;public Vector3D?
Position{get;private set;}public Vector3D?Velocity;public Vector3D?Acceleration;public MatrixD?OrientationUnit;public
BoundingBoxD?BoundingBox;public int?ExpiresAfterTicks=60;public MyDetectedEntityType?Type{get;set;}public delegate void
InvalidatedHandler();public event InvalidatedHandler OnInvalidated;public TargetTelemetry(int clock,string name){this.clock=clock;Name=
name;}public void SetPosition(Vector3D pos,long tickStamp){Position=pos;TickStamp=tickStamp;}public void CheckExpiration(int
locTick){if((TickStamp!=0)&&ExpiresAfterTicks.HasValue&&(locTick-TickStamp>ExpiresAfterTicks.Value))Invalidate();}public void
PredictPostion(int tick,int clock){if((Velocity.HasValue)&&(Velocity.Value.Length()>double.Epsilon)&&(tick-TickStamp)>0){Position+=
Velocity*(tick-TickStamp)*clock/60;}}public enum TeleMetaFlags:byte{HasVelocity=1,HasOrientation=2,HasBB=4}bool HasFlag(
TeleMetaFlags packed,TeleMetaFlags flag){return(packed&flag)==flag;}public void ParseIgc(MyTuple<MyTuple<string,long,long,byte,byte>,
Vector3D,Vector3D,MatrixD,BoundingBoxD>igcDto,int localTick){var meta=igcDto.Item1;EntityId=meta.Item2;Type=(
MyDetectedEntityType)meta.Item4;TeleMetaFlags tm=(TeleMetaFlags)meta.Item5;SetPosition(igcDto.Item2,localTick);if(HasFlag(tm,TeleMetaFlags.
HasVelocity)){var newVel=igcDto.Item3;if(!Velocity.HasValue)Velocity=newVel;Acceleration=(newVel-Velocity.Value)*60/clock;Velocity=
newVel;}if(HasFlag(tm,TeleMetaFlags.HasOrientation))OrientationUnit=igcDto.Item4;if(HasFlag(tm,TeleMetaFlags.HasBB))
BoundingBox=igcDto.Item5;}public static TargetTelemetry FromIgc(MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,
MatrixD,BoundingBoxD>igcDto,Func<string[],TeleDto>parser,int localTick){var t=new TargetTelemetry(1,igcDto.Item1.Item1);t.
ParseIgc(igcDto,localTick);return t;}public MyTuple<MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>
GetIgcDto(){var mask=0|(Velocity.HasValue?1:0)|(OrientationUnit.HasValue?2:0)|(BoundingBox.HasValue?4:0);var x=new MyTuple<
MyTuple<string,long,long,byte,byte>,Vector3D,Vector3D,MatrixD,BoundingBoxD>(new MyTuple<string,long,long,byte,byte>(Name,
EntityId,DateTime.Now.Ticks,(byte)MyDetectedEntityType.LargeGrid,(byte)mask),Position.Value,Velocity??Vector3D.Zero,
OrientationUnit??MatrixD.Identity,BoundingBox??new BoundingBoxD());return x;}public void Invalidate(){Position=null;Velocity=null;
OrientationUnit=null;BoundingBox=null;var tmp=OnInvalidated;if(tmp!=null)OnInvalidated();}}static class UserCtrlTest{public static List
<IMyShipController>ctrls;public static void Init(List<IMyShipController>c){if(ctrls==null)ctrls=c;}public static Vector3
GetUserCtrlVector(MatrixD fwRef){Vector3 res=new Vector3();if(Toggle.C.Check("ignore-user-thruster"))return res;var c=ctrls.Where(x=>x.
IsUnderControl).FirstOrDefault();if(c!=null&&(c.MoveIndicator!=Vector3.Zero))return Vector3D.TransformNormal(c.MoveIndicator,fwRef*
MatrixD.Transpose(c.WorldMatrix));return res;}}DockHost dockHost;class DockHost{Dispatcher dispatcher;List<IMyShipConnector>
ports;Dictionary<IMyShipConnector,Vector3D>pPositions=new Dictionary<IMyShipConnector,Vector3D>();public DockHost(Dispatcher
disp,List<IMyShipConnector>docks,IMyGridTerminalSystem gts){dispatcher=disp;ports=docks;ports.ForEach(x=>pPositions.Add(x,x.
GetPosition()));}public void Handle(IMyIntergridCommunicationSystem i,int t){var z=new List<MyTuple<Vector3D,Vector3D,Vector4>>();i
.SendUnicastMessage(DbgIgc,"draw-lines",z.ToImmutableArray());foreach(var s in dockRequests)E.Echo(s+" awaits docking");
foreach(var s in depRequests)E.Echo(s+" awaits dep");if(dockRequests.Any()){var fd=ports.FirstOrDefault(d=>(string.
IsNullOrEmpty(d.CustomData)||(d.CustomData==dockRequests.Peek().ToString()))&&(d.Status==MyShipConnectorStatus.Unconnected));if(fd!=
null){var id=dockRequests.Dequeue();fd.CustomData=id.ToString();E.Log("Assigned docking port "+fd.CustomName+" to "+
dispatcher.GetSubordinateName(id),E.LogLevel.Notice);i.SendUnicastMessage(id,"apck.docking.approach","");}}if(depRequests.Any()){
foreach(var s in depRequests){E.Echo(s+" awaits departure");}var r=depRequests.Peek();var bd=ports.FirstOrDefault(d=>d.
CustomData==r.ToString());if(bd!=null){depRequests.Dequeue();E.Log($"Sent 0-node departure path");i.SendUnicastMessage(r,
"apck.depart.approach","");}}foreach(var d in ports.Where(d=>!string.IsNullOrEmpty(d.CustomData))){long id;if(!long.TryParse(d.CustomData,out
id))continue;E.Echo($"Channeling DV to {id}");var x=new TargetTelemetry(1,"docking");var m=d.WorldMatrix;x.SetPosition(m.
Translation+m.Forward*(d.CubeGrid.GridSizeEnum==MyCubeSize.Large?1.25:0.5),t);if(pPositions[d]!=Vector3D.Zero)x.Velocity=(d.
GetPosition()-pPositions[d])/Dt;pPositions[d]=d.GetPosition();x.OrientationUnit=m;var k=x.GetIgcDto();i.SendUnicastMessage(id,
"apck.ntv.update",k);}}public void DepartComplete(string id){ports.First(x=>x.CustomData==id).CustomData="";}public void RequestDocking(
long id,Vector3D d,bool depart=false){if(depart){if(!depRequests.Contains(id))depRequests.Enqueue(id);}else{if(!dockRequests
.Contains(id))dockRequests.Enqueue(id);}dests[id]=d;}Dictionary<long,Vector3D>dests=new Dictionary<long,Vector3D>();Queue
<long>dockRequests=new Queue<long>();Queue<long>depRequests=new Queue<long>();public Vector3D GetFirstNormal(){return
ports.First().WorldMatrix.Forward;}public List<IMyShipConnector>GetPorts(){return ports;}}GuiHandler guiH;class GuiHandler{
Vector2 mOffset;List<ActiveElement>controls=new List<ActiveElement>();List<ActiveElement>recallBtns=new List<ActiveElement>();
Dispatcher _dispatcher;StateWrapper _stateWrapper;Vector2 viewPortSize;int current_page=0;public GuiHandler(IMyTextSurface p,
Dispatcher dispatcher,StateWrapper stateWrapper){_dispatcher=dispatcher;_stateWrapper=stateWrapper;viewPortSize=p.TextureSize;
Vector2 btnSize=new Vector2(84,40);float y_btn=0.85f*viewPortSize.Y;int x_btn=20;x_btn+=20;var bCyclePage=CreateButton(-1,p,new
Vector2(40,40),new Vector2(x_btn,y_btn),">",1.2f);bCyclePage.OnClick=xy=>{current_page=(current_page+1)%2;};AddTipToAe(
bCyclePage,"Next page ...");controls.Add(bCyclePage);x_btn+=20+10+42;var bRecall=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn)
,"Recall");bRecall.OnClick=xy=>{dispatcher.Recall();};AddTipToAe(bRecall,"Finish work (broadcast command:force-finish)");
controls.Add(bRecall);x_btn+=42+10+42;var bResume=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn),"Resume");bResume.OnClick=xy
=>{dispatcher.BroadcastResume();};AddTipToAe(bResume,"Resume work (broadcast 'miners.resume' message)");controls.Add(
bResume);x_btn+=42+10+42;var bClearState=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn),"Clear state");bClearState.OnClick=
xy=>{stateWrapper?.ClearPersistentState();};AddTipToAe(bClearState,"Clear Dispatcher state");controls.Add(bClearState);
x_btn+=42+10+42;var bClearLog=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn),"Clear log");bClearLog.OnClick=xy=>{E.
ClearLog();};controls.Add(bClearLog);x_btn+=42+10+42;var bPurgeLocks=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn),
"Purge locks");bPurgeLocks.OnClick=xy=>{dispatcher.PurgeLocks();};AddTipToAe(bPurgeLocks,
"Clear lock ownership. Last resort in case of deadlock");controls.Add(bPurgeLocks);x_btn+=42+10+42;var bHalt=CreateButton(0,p,btnSize,new Vector2(x_btn,y_btn),"EMRG HALT");
bHalt.OnClick=xy=>{dispatcher.BroadCastHalt();};AddTipToAe(bHalt,
"Halt all activity, restore overrides, release control, clear states");controls.Add(bHalt);for(int i=0;i<8;++i){var bRec=CreateButton(-1,p,new Vector2(30,30),new Vector2(25,85+i*40),"-",
1.2f);int idx=i;bRec.OnClick=xy=>{dispatcher.Recall(idx);};AddTipToAe(bRec,"Recalls that individual agent only.");recallBtns
.Add(bRec);}var bLayout=CreateButton(1,p,new Vector2(110,30),new Vector2(300,55),_stateWrapper.PState.layout.ToString(),
0.6f);bLayout.OnClick=xy=>{_stateWrapper.PState.layout=(TaskLayout)(((byte)_stateWrapper.PState.layout+1)%2);bLayout.
fgSprite.Data=_stateWrapper.PState.layout.ToString();};AddTipToAe(bLayout,
"Change the arrangement of the shafts for the next task.");controls.Add(bLayout);{var chkDense=CreateCheckbox(1,new Vector2(30,30),new Vector2(300,90));chkDense.bChecked=
_stateWrapper.PState.bDense;chkDense.OnClick=xy=>{chkDense.bChecked=(_stateWrapper.PState.bDense=!_stateWrapper.PState.bDense);};
AddTipToAe(chkDense,"Toggle dense (overlapping) shafts for next task.");controls.Add(chkDense);}var bIncMaxGen=CreateButton(1,p,
new Vector2(30,30),new Vector2(300+55-15,125),"+",1.2f);bIncMaxGen.OnClick=xy=>{++_stateWrapper.PState.maxGen;};AddTipToAe(
bIncMaxGen,"Increase size of next task. (0 is single shaft only)");controls.Add(bIncMaxGen);var bDecMaxGen=CreateButton(1,p,new
Vector2(30,30),new Vector2(300-55+15,125),"-",1.2f);bDecMaxGen.OnClick=xy=>{_stateWrapper.PState.maxGen=Math.Max(0,--
_stateWrapper.PState.maxGen);};AddTipToAe(bDecMaxGen,"Decrease size of next task. (0 is single shaft only)");controls.Add(bDecMaxGen)
;var bIncDepthLimit=CreateButton(1,p,new Vector2(30,30),new Vector2(300+55-15,195),"+",1.2f);bIncDepthLimit.OnClick=xy=>{
_stateWrapper.PState.maxDepth+=5f;};AddTipToAe(bIncDepthLimit,"Increase depth limit by 5 m");controls.Add(bIncDepthLimit);var
bDecDepthLimit=CreateButton(1,p,new Vector2(30,30),new Vector2(300-55+15,195),"-",1.2f);bDecDepthLimit.OnClick=xy=>{_stateWrapper.
PState.maxDepth=Math.Max(_stateWrapper.PState.leastDepth,_stateWrapper.PState.maxDepth-5f);};AddTipToAe(bDecDepthLimit,
"Decrease depth limit by 5 m");controls.Add(bDecDepthLimit);var bIncSkipDepth=CreateButton(1,p,new Vector2(30,30),new Vector2(300+55-15,230),"+",1.2f
);bIncSkipDepth.OnClick=xy=>{_stateWrapper.PState.skipDepth=Math.Min(_stateWrapper.PState.maxDepth,_stateWrapper.PState.
skipDepth+5f);};AddTipToAe(bIncSkipDepth,"Increase skip-depth by 5 m");controls.Add(bIncSkipDepth);var bDecSkipDepth=CreateButton
(1,p,new Vector2(30,30),new Vector2(300-55+15,230),"-",1.2f);bDecSkipDepth.OnClick=xy=>{_stateWrapper.PState.skipDepth=
Math.Max(0f,_stateWrapper.PState.skipDepth-5f);};AddTipToAe(bDecSkipDepth,"Decrease skip-depth by 5 m");controls.Add(
bDecSkipDepth);var bIncLeastDepth=CreateButton(1,p,new Vector2(30,30),new Vector2(300+55-15,265),"+",1.2f);bIncLeastDepth.OnClick=xy
=>{_stateWrapper.PState.leastDepth=Math.Min(_stateWrapper.PState.maxDepth,_stateWrapper.PState.leastDepth+5f);};AddTipToAe
(bIncLeastDepth,"Increase least-depth by 5 m");controls.Add(bIncLeastDepth);var bDecLeastDepth=CreateButton(1,p,new
Vector2(30,30),new Vector2(300-55+15,265),"-",1.2f);bDecLeastDepth.OnClick=xy=>{_stateWrapper.PState.leastDepth=Math.Max(0f,
_stateWrapper.PState.leastDepth-5f);};AddTipToAe(bDecLeastDepth,"Decrease least-depth by 5 m");controls.Add(bDecLeastDepth);{var
chkAdaptive=CreateCheckbox(1,new Vector2(30,30),new Vector2(300,300));chkAdaptive.bChecked=Toggle.C.Check("adaptive-mining");
chkAdaptive.OnClick=xy=>{chkAdaptive.bChecked=Toggle.C.Invert("adaptive-mining");};AddTipToAe(chkAdaptive,"Toggle adaptive mining")
;controls.Add(chkAdaptive);}{var chkAdjEntry=CreateCheckbox(1,new Vector2(30,30),new Vector2(300,335));chkAdjEntry.
bChecked=Toggle.C.Check("adjust-entry-by-elevation");chkAdjEntry.OnClick=xy=>{chkAdjEntry.bChecked=Toggle.C.Invert(
"adjust-entry-by-elevation");};AddTipToAe(chkAdjEntry,"Toggle automatic entry point adjustment");controls.Add(chkAdjEntry);}var bIncSafetyDist=
CreateButton(1,p,new Vector2(30,30),new Vector2(300+55-15,370),"+",1.2f);bIncSafetyDist.OnClick=xy=>{_stateWrapper.PState.safetyDist
+=0.2f;};AddTipToAe(bIncSafetyDist,"Increase safety distance by 0.2 (multiple of the shaft diameter).");controls.Add(
bIncSafetyDist);var bDecSafetyDist=CreateButton(1,p,new Vector2(30,30),new Vector2(300-55+15,370),"-",1.2f);bDecSafetyDist.OnClick=xy
=>{_stateWrapper.PState.safetyDist=Math.Max(1f,_stateWrapper.PState.safetyDist-0.2f);};AddTipToAe(bDecSafetyDist,
"Decrease safety distance by 0.2 (multiple of shaft diameter).");controls.Add(bDecSafetyDist);shaftTip=new MySprite(SpriteType.TEXT,"",new Vector2(viewPortSize.X/1.2f,viewPortSize.Y*
0.9f),null,Color.White,"Debug",TextAlignment.CENTER,0.5f);buttonTip=new MySprite(SpriteType.TEXT,"",bRecall.Min-Vector2.
UnitY*17,null,Color.White,"Debug",TextAlignment.LEFT,0.5f);taskSummary=new MySprite(SpriteType.TEXT,"No active task",new
Vector2(viewPortSize.X/1.2f,viewPortSize.Y/20f),null,Color.White,"Debug",TextAlignment.CENTER,0.5f);}ActiveElement
CreateCheckbox(int page,Vector2 size,Vector2 pos){var checkBox=new ActiveElement(page,size,pos);checkBox.bkSprite0=new MySprite(
SpriteType.TEXTURE,"SquareSimple",new Vector2(0,0),size,Color.CornflowerBlue);checkBox.bkSprite1=new MySprite(SpriteType.TEXTURE,
"SquareSimple",new Vector2(0,0),size,Color.Black);checkBox.fgSprite=new MySprite(SpriteType.TEXTURE,"Cross",new Vector2(0,0),size*0.8f
,Color.DarkKhaki);return checkBox;}ActiveElement CreateButton(int page,IMyTextSurface p,Vector2 btnSize,Vector2 posN,
string label,float fsize=0.5f){var lbl_ypos=Vector2.Zero;lbl_ypos.Y=-p.MeasureStringInPixels(new StringBuilder(label),"Debug",
fsize+0.2f).Y/btnSize.Y;var btn=new ActiveElement(page,btnSize,posN);btn.bkSprite0=new MySprite(SpriteType.TEXTURE,
"SquareSimple",new Vector2(0,0),btnSize,Color.CornflowerBlue);btn.bkSprite1=new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2
(0,0),btnSize,Color.Black);btn.fgSprite=new MySprite(SpriteType.TEXT,label,lbl_ypos,Vector2.One,Color.White,"Debug",
TextAlignment.CENTER,fsize);return btn;}void AddTipToAe(ActiveElement ae,string tip){ae.OnMouseIn+=()=>buttonTip.Data=tip;ae.
OnMouseOut+=()=>buttonTip.Data="";}bool eDown;public void Handle(IMyTextPanel panel,IMyShipController seat){Vector2 r=Vector2.Zero
;bool clickE=false;if(seat.IsUnderControl&&Toggle.C.Check("cc")){r=seat.RotationIndicator;var roll=seat.RollIndicator;var
eDownUpdate=(roll>0);if(!eDownUpdate&&eDown)clickE=true;eDown=eDownUpdate;}if(r.LengthSquared()>0||clickE){mOffset.X+=r.Y;mOffset.Y
+=r.X;mOffset=Vector2.Clamp(mOffset,-panel.TextureSize/2,panel.TextureSize/2);}var cursP=mOffset+panel.TextureSize/2;for(
int i=0;i<8;++i)recallBtns[i].Visible=(current_page==0&&i<_dispatcher.subordinates.Count()&&_dispatcher.subordinates[i].
Report.state!=MinerState.Disabled&&_dispatcher.subordinates[i].Report.state!=MinerState.Idle&&!_dispatcher.subordinates[i].
Report.bRecalled);using(var frame=panel.DrawFrame()){if(current_page==0)DrawReportRepeater(frame);foreach(var ae in controls.
Union(shaftControls).Union(recallBtns).Where(x=>x.Visible&&(x.page==current_page||x.page<0))){if(ae.CheckHover(cursP)){if(
clickE)ae.OnClick?.Invoke(cursP);}}foreach(var ae in controls.Union(shaftControls).Union(recallBtns).Where(x=>x.Visible&&(x.
page==current_page||x.page<0)))frame.AddRange(ae.GetSprites());if(current_page==0)frame.Add(shaftTip);frame.Add(buttonTip);
if(current_page==0){frame.Add(taskSummary);DrawAgents(frame);}else if(current_page==1)DrawDispatcherParameters(frame);var
cur=new MySprite(SpriteType.TEXTURE,"Triangle",cursP,new Vector2(7f,10f),Color.White);cur.RotationOrScale=6f;frame.Add(cur)
;}if(r.LengthSquared()>0){panel.ContentType=ContentType.TEXT_AND_IMAGE;panel.ContentType=ContentType.SCRIPT;}}void
DrawAgents(MySpriteDrawFrame frame){var z=new Vector2(viewPortSize.X/1.2f,viewPortSize.Y/2f);foreach(var ag in _dispatcher.
subordinates){var pos=ag.Report.WM.Translation;var task=_dispatcher.CurrentTask;if(task!=null){var posLoc=Vector3D.Transform(pos,
worldToScheme);float scale=3.5f;var posViewport=z+new Vector2((float)posLoc.Y,(float)posLoc.X)*scale;var btnSize=Vector2.One*scale*
task.R*2;var btnSpr=new MySprite(SpriteType.TEXTURE,"AH_BoreSight",posViewport+new Vector2(0,5),btnSize*0.8f,Color.Orange);
btnSpr.RotationOrScale=(float)Math.PI/2f;var btnSprBack=new MySprite(SpriteType.TEXTURE,
"Textures\\FactionLogo\\Miners\\MinerIcon_3.dds",posViewport,btnSize*1.2f,Color.Black);frame.Add(btnSprBack);frame.Add(btnSpr);}}}void DrawDispatcherParameters(
MySpriteDrawFrame frame){int offY=0,startY=20;int offX=0,startX=65;offX+=145;frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new
Vector2(startX+offX,startY+offY),new Vector2(290-4,30),Color.Black));frame.Add(new MySprite(SpriteType.TEXT,"Task Parameters",
new Vector2(startX+offX,startY+offY-9),null,Color.White,"Debug",TextAlignment.CENTER,0.6f));offX+=145;offY+=35;offX=0;offX
+=90;frame.Add(new MySprite(SpriteType.TEXT,"Task layout",new Vector2(startX+offX+70,startY+offY-9),null,Color.DarkKhaki,
"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offY+=35;offX=0;offX+=90;frame.Add(new MySprite(SpriteType.TEXT,
"Dense shaft layout",new Vector2(startX+offX+70,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offY+=35;
offX=0;offX+=90;frame.Add(new MySprite(SpriteType.TEXT,"Size (# shaft rings)",new Vector2(startX+offX+70,startY+offY-9),null
,Color.DarkKhaki,"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offX+=55;frame.Add(new MySprite(SpriteType.TEXT,
_stateWrapper.PState.maxGen.ToString(),new Vector2(startX+offX,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.6f)
);offX+=55;offY+=35;offX=0;offX+=145;frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2(startX+offX,
startY+offY),new Vector2(290-4,30),Color.Black));frame.Add(new MySprite(SpriteType.TEXT,"Job Parameters",new Vector2(startX+
offX,startY+offY-9),null,Color.White,"Debug",TextAlignment.CENTER,0.6f));offX+=145;offY+=35;offX=0;offX+=90;frame.Add(new
MySprite(SpriteType.TEXT,"Depth Limit",new Vector2(startX+offX+70,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.
RIGHT,0.6f));offX+=90;offX+=55;frame.Add(new MySprite(SpriteType.TEXT,_stateWrapper.PState.maxDepth.ToString("f0")+" m",new
Vector2(startX+offX,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.6f));offX+=55;offY+=35;offX=0;offX+=90;
frame.Add(new MySprite(SpriteType.TEXT,"Skip depth",new Vector2(startX+offX+70,startY+offY-9),null,Color.DarkKhaki,"Debug",
TextAlignment.RIGHT,0.6f));offX+=90;offX+=55;frame.Add(new MySprite(SpriteType.TEXT,_stateWrapper.PState.skipDepth.ToString("f0")+
" m",new Vector2(startX+offX,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.6f));offX+=55;offY+=35;offX=
0;offX+=90;frame.Add(new MySprite(SpriteType.TEXT,"Least depth",new Vector2(startX+offX+70,startY+offY-9),null,Color.
DarkKhaki,"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offX+=55;frame.Add(new MySprite(SpriteType.TEXT,_stateWrapper.PState.
leastDepth.ToString("f0")+" m",new Vector2(startX+offX,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.6f));
offX+=55;offY+=35;offX=0;offX+=90;frame.Add(new MySprite(SpriteType.TEXT,"Adaptive mining",new Vector2(startX+offX+70,startY
+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offY+=35;offX=0;offX+=90;frame.Add(new MySprite
(SpriteType.TEXT,"Adjust entry altitude",new Vector2(startX+offX+70,startY+offY-9),null,Color.DarkKhaki,"Debug",
TextAlignment.RIGHT,0.6f));offX+=90;offY+=35;offX=0;offX+=90;frame.Add(new MySprite(SpriteType.TEXT,"Safety distance",new Vector2(
startX+offX+70,startY+offY-9),null,Color.DarkKhaki,"Debug",TextAlignment.RIGHT,0.6f));offX+=90;offX+=55;frame.Add(new MySprite
(SpriteType.TEXT,_stateWrapper.PState.safetyDist.ToString("f1"),new Vector2(startX+offX,startY+offY-9),null,Color.
DarkKhaki,"Debug",TextAlignment.CENTER,0.6f));offX+=55;}void DrawReportRepeater(MySpriteDrawFrame frame){bool madeHeader=false;
int offY=0,startY=30;const float fontHeight=15;foreach(var su in _dispatcher.subordinates){int offX=0,startX=45;int
interval=75;if(!madeHeader){offX+=55;frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2(startX+offX,startY),
new Vector2(110-4,40),Color.Black));frame.Add(new MySprite(SpriteType.TEXT,"Name\nState",new Vector2(startX+offX,startY-16)
,null,Color.White,"Debug",TextAlignment.CENTER,0.5f));offX+=55;offX+=22;frame.Add(new MySprite(SpriteType.TEXTURE,
"SquareSimple",new Vector2(startX+offX,startY),new Vector2(44-4,40),Color.Black));frame.Add(new MySprite(SpriteType.TEXTURE,
"IconEnergy",new Vector2(startX+offX-7,startY-7),new Vector2(18,18),Color.White));frame.Add(new MySprite(SpriteType.TEXTURE,
"IconHydrogen",new Vector2(startX+offX+7,startY+7),new Vector2(16,16),Color.White));offX+=22;offX+=22;frame.Add(new MySprite(
SpriteType.TEXTURE,"SquareSimple",new Vector2(startX+offX,startY),new Vector2(44-4,40),Color.Black));frame.Add(new MySprite(
SpriteType.TEXTURE,"MyObjectBuilder_Ore/Stone",new Vector2(startX+offX,startY),new Vector2(40,40),Color.White));offX+=22;offX+=22;
frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2(startX+offX,startY),new Vector2(44-4,40),Color.Black));
frame.Add(new MySprite(SpriteType.TEXTURE,"Arrow",new Vector2(startX+offX,startY),new Vector2(40,40),Color.White,"",
TextAlignment.CENTER,(float)Math.PI));offX+=22;offX+=75;frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2(startX+
offX,startY),new Vector2(150-4,40),Color.Black));frame.Add(new MySprite(SpriteType.TEXT,"Flags\nDamage",new Vector2(startX+
offX,startY-16),null,Color.White,"Debug",TextAlignment.CENTER,0.5f));offX+=75;offX+=interval/2;if(!su.Report.KeyValuePairs.
IsDefault)foreach(var kvp in su.Report.KeyValuePairs){frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",new Vector2(startX
+offX,startY),new Vector2(interval-5,40),Color.Black));frame.Add(new MySprite(SpriteType.TEXT,kvp.Item1,new Vector2(
startX+offX,startY-16),null,Color.White,"Debug",TextAlignment.CENTER,0.5f));offX+=interval;}madeHeader=true;offY+=40;}offX=0;
offX+=55;frame.Add(new MySprite(SpriteType.TEXT,su.Report.name+"\n"+su.Report.state.ToString(),new Vector2(startX+offX,
startY+offY),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.5f));offX+=55;offX+=22;frame.Add(new MySprite(SpriteType.TEXT
,(su.Report.f_bat*100f).ToString("f0")+"%",new Vector2(startX+offX,startY+offY),null,(su.Report.f_bat>su.Report.f_bat_min
?Color.DarkKhaki:Color.DarkRed),"Debug",TextAlignment.CENTER,0.5f));frame.Add(new MySprite(SpriteType.TEXT,(su.Report.
f_fuel*100f).ToString("f0")+"%",new Vector2(startX+offX,startY+offY+fontHeight),null,(su.Report.f_fuel>su.Report.f_fuel_min?
Color.DarkKhaki:Color.DarkRed),"Debug",TextAlignment.CENTER,0.5f));offX+=22;offX+=22;frame.Add(new MySprite(SpriteType.TEXT,(
su.Report.f_cargo*100f).ToString("f0")+"%",new Vector2(startX+offX,startY+offY),null,(su.Report.f_cargo<=su.Report.
f_cargo_max?Color.DarkKhaki:Color.DarkOrange),"Debug",TextAlignment.CENTER,0.5f));if(su.Report.bUnload)frame.Add(new MySprite(
SpriteType.TEXTURE,"Danger",new Vector2(startX+offX,startY+offY+24),new Vector2(22,22),Color.Red));offX+=22;offX+=22;frame.Add(new
MySprite(SpriteType.TEXT,su.Report.t_shaft.ToString("f2")+" m",new Vector2(startX+offX,startY+offY),null,(su.Report.t_shaft<=
_stateWrapper.PState.maxDepth?Color.DarkKhaki:Color.DarkRed),"Debug",TextAlignment.CENTER,0.5f));frame.Add(new MySprite(SpriteType.
TEXT,"("+su.Report.t_ore.ToString("f2")+" m)",new Vector2(startX+offX,startY+offY+fontHeight),null,Color.DarkKhaki,"Debug",
TextAlignment.CENTER,0.5f));offX+=22;offX+=75;List<string>s_flags=new List<string>();if(su.Report.bAdaptive)s_flags.Add("A");if(su.
Report.bRecalled)s_flags.Add("R");frame.Add(new MySprite(SpriteType.TEXT,string.Join(" ",s_flags),new Vector2(startX+offX,
startY+offY),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.5f));frame.Add(new MySprite(SpriteType.TEXT,su.Report.damage,
new Vector2(startX+offX,startY+offY+fontHeight),null,Color.DarkRed,"Debug",TextAlignment.CENTER,0.5f));offX+=75;offX+=
interval/2;if(!su.Report.KeyValuePairs.IsDefault)foreach(var kvp in su.Report.KeyValuePairs){frame.Add(new MySprite(SpriteType.
TEXT,kvp.Item2,new Vector2(startX+offX,startY+offY),null,Color.DarkKhaki,"Debug",TextAlignment.CENTER,0.5f));offX+=interval;
}offY+=40;}}List<ActiveElement>shaftControls=new List<ActiveElement>();public Action<int>OnShaftClick;MySprite shaftTip;
MySprite buttonTip;MySprite taskSummary;MatrixD worldToScheme;internal void UpdateMiningScheme(Dispatcher.MiningTask obj){
worldToScheme=MatrixD.Invert(MatrixD.CreateWorld(obj.corePoint,obj.miningPlaneNormal,obj.planeXunit));shaftControls=new List<
ActiveElement>();Vector2 bPos=new Vector2(viewPortSize.X/1.2f,viewPortSize.Y/2f);float scale=3.5f;Vector2 btnSize=Vector2.One*scale*
obj.R*1.6f;foreach(var t in obj.Shafts){var pos=bPos+new Vector2(t.Point.X,-t.Point.Y)*scale;Color mainCol=Color.White;if(t
.State==ShaftState.Planned)mainCol=Color.Darken(Color.CornflowerBlue,0.1f);else if(t.State==ShaftState.Complete)mainCol=
Color.Darken(Color.CornflowerBlue,0.4f);else if(t.State==ShaftState.InProgress)mainCol=Color.Lighten(Color.CornflowerBlue,
0.2f);else if(t.State==ShaftState.Cancelled)mainCol=Color.Darken(Color.DarkSlateGray,0.2f);var hoverColor=Color.Red;var btn=
new ActiveElement(0,btnSize,pos);btn.bkSprite0=new MySprite(SpriteType.TEXTURE,"Circle",new Vector2(0,0),btnSize,mainCol);
btn.bkSprite1=new MySprite(SpriteType.TEXTURE,"Circle",new Vector2(0,0),btnSize,hoverColor);btn.OnHover=p=>shaftTip.Data=
$"id: {t.Id}, {t.State}";btn.OnMouseOut=()=>{shaftTip.Data="Hover over shaft for more info,\n tap E to cancel it";};btn.OnClick=x=>OnShaftClick?
.Invoke(t.Id);shaftControls.Add(btn);}}public void UpdateTaskSummary(Dispatcher d){if(d?.CurrentTask!=null)taskSummary.
Data=$"Layout: {_stateWrapper.PState.layout}"+(_stateWrapper.PState.bDense_cur?", dense":"")+
$"\nShafts: {d.CurrentTask.Shafts.Count}\nRadius: {d.CurrentTask.R:f2}\n"+$"Group: {d.CurrentTask.GroupConstraint}";}class ActiveElement{public int page=0;public bool Visible=true;public bool
bChecked;public Vector2 Min;private Vector2 Max;private Vector2 Center;public MySprite bkSprite0;public MySprite bkSprite1;
public MySprite fgSprite;public Vector2 SizePx;public Action OnMouseIn{get;set;}public Action OnMouseOut{get;set;}public
Action<Vector2>OnHover{get;set;}public Action<Vector2>OnClick{get;set;}private bool bHover{get;set;}public ActiveElement(int
_p,Vector2 sizeN,Vector2 posN){page=_p;SizePx=sizeN;Center=posN;Min=Center-SizePx/2f;Max=Center+SizePx/2f;bChecked=true;}
public bool CheckHover(Vector2 cursorPosition){bool res=(cursorPosition.X>Min.X)&&(cursorPosition.X<Max.X)&&(cursorPosition.Y>
Min.Y)&&(cursorPosition.Y<Max.Y);if(res){if(!bHover)OnMouseIn?.Invoke();bHover=true;OnHover?.Invoke(cursorPosition);}else{
if(bHover)OnMouseOut?.Invoke();bHover=false;}return res;}public IEnumerable<MySprite>GetSprites(){if(bHover){var retval=
bkSprite1;retval.Position=Center+SizePx/2f*bkSprite1.Position;yield return retval;}else{var retval=bkSprite0;retval.Position=
Center+SizePx/2f*bkSprite0.Position;yield return retval;}if(bChecked){var retval=fgSprite;retval.Position=Center+SizePx/2f*
fgSprite.Position;yield return retval;}}}}class TransponderMsg{public long Id;public string name;public MatrixD WM;public
Vector3D v;public float f_bat;public float f_bat_min;public float f_fuel;public float f_fuel_min;public string damage;public
MinerState state;public float f_cargo;public float f_cargo_max;public bool bAdaptive;public bool bRecalled;public float t_shaft;
public float t_ore;public bool bUnload;public ImmutableArray<MyTuple<string,string>>KeyValuePairs;public void UpdateFromIgc(
MyTuple<MyTuple<long,string>,MyTuple<MatrixD,Vector3D>,MyTuple<byte,string,bool>,ImmutableArray<float>,MyTuple<bool,bool,float,
float>,ImmutableArray<MyTuple<string,string>>>dto){Id=dto.Item1.Item1;name=dto.Item1.Item2;WM=dto.Item2.Item1;v=dto.Item2.
Item2;state=(MinerState)dto.Item3.Item1;damage=dto.Item3.Item2;bUnload=dto.Item3.Item3;f_bat=dto.Item4[0];f_bat_min=dto.Item4
[1];f_fuel=dto.Item4[2];f_fuel_min=dto.Item4[3];f_cargo=dto.Item4[4];f_cargo_max=dto.Item4[5];bAdaptive=dto.Item5.Item1;
bRecalled=dto.Item5.Item2;t_shaft=dto.Item5.Item3;t_ore=dto.Item5.Item4;KeyValuePairs=dto.Item6;}public float Urgency(){return-
Math.Min(f_bat-f_bat_min,f_fuel-f_fuel_min);}}List<MyTuple<string,Vector3D,ImmutableArray<string>>>prjs=new List<MyTuple<
string,Vector3D,ImmutableArray<string>>>();void EmitProjection(string tag,Vector3D p,params string[]s){prjs.Add(new MyTuple<
string,Vector3D,ImmutableArray<string>>(tag,p,s.ToImmutableArray()));}void EmitFlush(long addr){IGC.SendUnicastMessage(addr,
"hud.apck.proj",prjs.ToImmutableArray());prjs.Clear();}