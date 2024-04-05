using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;

using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;

using VRageMath;

namespace IngameScript {
class Program : MyGridProgram {

#region mdk preserve
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

#endregion

bool pendingInitSequence;
CommandRegistry commandRegistry;
public class CommandRegistry
{
	Dictionary<string, Action<string[]>> commands;
	public CommandRegistry(Dictionary<string, Action<string[]>> commands)
	{
		this.commands = commands;
	}
	public void RunCommand(string id, string[] cmdParts)
	{
		this.commands[id].Invoke(cmdParts);
	}
}

static int TickCount;
void StartOfTick(string arg)
{
	TickCount++;
	Echo("Run count: " + TickCount);
	Echo("Name: " + me.CubeGrid.CustomName);
	
	/* If this is the first cycle, and the game engine has loaded
	 * all the blocks, then we need to do some more initialisation. */
	if (pendingInitSequence && string.IsNullOrEmpty(arg)) {
		pendingInitSequence = false;

		CreateRole(); //TODO: Resolve or find a better name

		/* Process commands from the startup script. */
		arg = string.Join(",", Me.CustomData.Trim('\n').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith("//"))
				.Select(s => "[" + s + "]"));
	}

	if (!string.IsNullOrEmpty(arg) && arg.Contains(":"))
	{
		var commands = arg.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('[', ']')).ToList();
		foreach (var c in commands)
		{
			string[] cmdParts = c.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
			if (cmdParts[0] == "command")
			{
				try
				{
					this.commandRegistry.RunCommand(cmdParts[1], cmdParts);
				}
				catch (Exception ex)
				{
					Log($"Run command '{cmdParts[1]}' failed.\n{ex}");
				}

			}
			if (cmdParts[0] == "toggle")
			{
				Toggle.C.Invert(cmdParts[1]);
				Log($"Switching '{cmdParts[1]}' to state '{Toggle.C.Check(cmdParts[1])}'");
			}
		}
	}
}

void EndOfTick()
{
	Scheduler.C.HandleTick();
	E.EndOfTick();
}

IMyProgrammableBlock pillockCore;
//int Clock = 1;
void Ctor()
{
	if (!string.IsNullOrEmpty(Me.CustomData))
		pendingInitSequence = true;

	E.Init(Echo, GridTerminalSystem);
	Toggle.Init(new Dictionary<string, bool>
	{
		{ "adaptive-mining", false },
		{ "adjust-entry-by-elevation", true },
		{ "log-message", false },
		{ "show-pstate", false },
		{ "suppress-transition-control", false },
		{ "suppress-gyro-control", false },
		{ "damp-when-idle", true },
		{ "ignore-user-thruster", false },
		{ "cc", true }
	},
		key =>
		{
			switch (key)
			{
				case "log-message":
					var cd = minerController?.fwReferenceBlock;
					if (cd != null)
						cd.CustomData = "";
					break;
			}
		}
	);

	NamedTeleData.Add("docking", new TargetTelemetry(1, "docking"));

	stateWrapper = new StateWrapper(s => Storage = s);
	if (!stateWrapper.TryLoad(Storage))
	{
		E.Echo("State load failed, clearing Storage now");
		stateWrapper.Save();
		Runtime.UpdateFrequency = UpdateFrequency.None;
	}

	GridTerminalSystem.GetBlocksOfType(cameras, c => c.IsSameConstructAs(Me));
	cameras.ForEach(c => c.EnableRaycast = true);

	IsLargeGrid = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;

	this.commandRegistry = new CommandRegistry(
		new Dictionary<string, Action<string[]>>
			{
				{
					"set-value", (parts) => Variables.Set(parts[2], parts[3])
				},
				{
					"add-panel", (parts) => {
						List<IMyTextPanel> b = new List<IMyTextPanel>();
						GridTerminalSystem.GetBlocksOfType(b, x => x.IsSameConstructAs(Me) && x.CustomName.Contains(parts[2]));
						var p = b.FirstOrDefault();
						if (p != null)
						{
							E.DebugLog($"Added {p.CustomName} as GUI panel");
							outputPanelInitializer(p);
							rawPanel = p;
						}
					}
				},
				{
					"add-logger", (parts) => {
						List<IMyTextPanel> b = new List<IMyTextPanel>();
						GridTerminalSystem.GetBlocksOfType(b, x => x.IsSameConstructAs(Me) && x.CustomName.Contains(parts[2]));
						var p = b.FirstOrDefault();
						if (p != null)
						{
							logPanelInitializer(p);
							E.AddLogger(p);
							E.DebugLog("Added logger: " + p.CustomName);
						}
					}
				},
				{
					"create-task", (parts) => minerController?.CreateTask()
				},
				{
					"mine", (parts) => minerController?.MineCommandHandler()
				},
				{
					"skip", (parts) => minerController?.SkipCommandHandler()
				},
				{
					"set-role", (parts) => Log("command:set-role is deprecated.")
				},
				{
					"low-update-rate", (parts) => Runtime.UpdateFrequency = UpdateFrequency.Update10
				},
				{
					"create-task-raycast", (parts) => RaycastTaskHandler(parts)
				},
				{
					//TODO: Rename "recall".
					"force-finish", (parts) => minerController?.FinishAndDockHandler()
				},
				{
					"static-dock", (parts) => Log("command:static-dock is deprecated.")
				},
				{
					"set-state", (parts) => minerController?.TrySetState(parts[2])
				},
				{
					"halt", (parts) => minerController?.Halt()
				},
				{
					"clear-storage-state", (parts) => stateWrapper?.ClearPersistentState()
				},
				{
					"save", (parts) => stateWrapper?.Save()
				},
				{
					"static-dock-gps", (parts) => Log("command:static-dock-gps is deprecated.")
				},
				{
					"dispatch", (parts) => minerController?.Dispatch()
				},
				{
					"global", (parts) => {
						var cmdParts = parts.Skip(2).ToArray();
						IGC.SendBroadcastMessage("miners.command", string.Join(":", cmdParts), TransmissionDistance.TransmissionDistanceMax);
						Log("broadcasting global " + string.Join(":", cmdParts));
						commandRegistry.RunCommand(cmdParts[1], cmdParts);
					}
				},
				{
					"get-toggles", (parts) => {
						IGC.SendUnicastMessage(long.Parse(parts[2]),
						$"menucommand.get-commands.reply:{ string.Join(":", parts.Take(3)) }",
						Toggle.C.GetToggleCommands());
					}
				},
			}
		);
	
	/* Create the miner controller object. */
	minerController = new MinerController(GridTerminalSystem, IGC, stateWrapper, GetNTV);
}

void CreateRole()
{
	var b = new List<IMyProgrammableBlock>();
	GridTerminalSystem.GetBlocksOfType(b, pb => pb.CustomName.Contains("core") && pb.IsSameConstructAs(Me) && pb.Enabled);
	pillockCore = b.FirstOrDefault();	
	if (pillockCore != null)
		minerController.SetControlledUnit(pillockCore);
	else
	{
		coreUnit = new APckUnit(stateWrapper.PState, GridTerminalSystem, IGC, GetNTV);
		minerController.SetControlledUnit(coreUnit);
		minerController.ApckRegistry = new CommandRegistry(
			new Dictionary<string, Action<string[]>>
				{
					{
						"create-wp", (parts) => CreateWP(parts)
					},
					{
						"pillock-mode", (parts) => coreUnit?.TrySetState(parts[2])
					},
					{
						"request-docking", (parts) => {
							E.DebugLog("Embedded lone mode is not supported");
						}
					},
					{
						"request-depart", (parts) => {
							E.DebugLog("Embedded lone mode is not supported");
						}
					}
				}
			);
	}

	if (!string.IsNullOrEmpty(stateWrapper.PState.lastAPckCommand))
	{
		Scheduler.C.After(5000).RunCmd(() => minerController.CommandAutoPillock(stateWrapper.PState.lastAPckCommand));
	}

	Scheduler.C.RepeatWhile(() => !minerController.DispatcherId.HasValue).After(1000)
		.RunCmd(() => minerController.InitiateHandshake());

	if (stateWrapper.PState.miningEntryPoint.HasValue)
	{
		minerController.ResumeJobOnWorldLoad();
	}
}

static void AddUniqueItem<T>(T item, IList<T> c) where T : class
{
	if ((item != null) && !c.Contains(item))
		c.Add(item);
}

public void BroadcastToChannel<T>(string tag, T data)
{
	var channel = IGC.RegisterBroadcastListener(tag);
	IGC.SendBroadcastMessage(channel.Tag, data, TransmissionDistance.TransmissionDistanceMax);
}

public void Log(string msg)
{
	E.DebugLog(msg);
}

//////////////////// ignore section for MDK minifier
#region mdk preserve
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
	public PersistentState PState { get; private set; }

	public void ClearPersistentState()
	{
		var currentState = PState;
		PState = new PersistentState();
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
			PState.Save(stateSaver);
		}
		catch (Exception ex)
		{
			E.DebugLog("State save failed.");
			E.DebugLog(ex.ToString());
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
			E.DebugLog("State load failed.");
			E.DebugLog(ex.ToString());
		}
		return false;
	}
}

public class PersistentState
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

	public PersistentState Load(string storage)
	{
		if (!string.IsNullOrEmpty(storage))
		{
			E.Echo(storage);

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
#endregion

	public void Save(Action<string> store)
	{
		store(Serialize());
	}

	string Serialize()
	{
		string[] pairs = new string[]
		{
			"LifetimeAcceptedTasks=" + LifetimeAcceptedTasks,
			"LifetimeOperationTime=" + LifetimeOperationTime,
			"LifetimeWentToMaintenance=" + LifetimeWentToMaintenance,
			"LifetimeOreAmount=" + LifetimeOreAmount,
			"LifetimeYield=" + LifetimeYield,
			"bRecalled=" + bRecalled,
			"MinerState=" + MinerState,
			"miningPlaneNormal=" + (miningPlaneNormal.HasValue ? VectorOpsHelper.V3DtoBroadcastString(miningPlaneNormal.Value) : ""),
			"getAbovePt=" + (getAbovePt.HasValue ? VectorOpsHelper.V3DtoBroadcastString(getAbovePt.Value) : ""),
			"miningEntryPoint=" + (miningEntryPoint.HasValue ? VectorOpsHelper.V3DtoBroadcastString(miningEntryPoint.Value) : ""),
			"corePoint=" + (corePoint.HasValue ? VectorOpsHelper.V3DtoBroadcastString(corePoint.Value) : ""),
			"shaftRadius=" + shaftRadius,
	
			/* Airspace geometry. */
			"n_FL=" + VectorOpsHelper.V3DtoBroadcastString(n_FL),
			"p_FL=" + VectorOpsHelper.V3DtoBroadcastString(p_FL),
			
			/* Job parameters. */
			"maxDepth=" + maxDepth,
			"skipDepth=" + skipDepth,
			"leastDepth=" + leastDepth,
			"adaptiveMode=" + Toggle.C.Check("adaptive-mining"),
			"adjustAltitude=" + Toggle.C.Check("adjust-entry-by-elevation"),

			"currentWp=" +  (currentWp.HasValue ? VectorOpsHelper.V3DtoBroadcastString(currentWp.Value) : ""),			
			"lastFoundOreDepth=" + lastFoundOreDepth,
			"CurrentJobMaxShaftYield=" + CurrentJobMaxShaftYield,
			"minFoundOreDepth=" + minFoundOreDepth,
			"maxFoundOreDepth=" + maxFoundOreDepth,
			"CurrentShaftId=" + CurrentShaftId ?? "",
			"lastAPckCommand=" + lastAPckCommand
		};
		return string.Join("\n", pairs);
	}

	public override string ToString()
	{
		return Serialize();
	}
}

/////////

public void Save()
{
	stateWrapper.Save();
}

public Program()
{
	me = Me;
	Runtime.UpdateFrequency = UpdateFrequency.Update1;

	Ctor();
}

List<MyIGCMessage> uniMsgs = new List<MyIGCMessage>();
void Main(string param, UpdateType updateType)
{
	uniMsgs.Clear();
	while (IGC.UnicastListener.HasPendingMessage)
	{
		uniMsgs.Add(IGC.UnicastListener.AcceptMessage());
	}

	var commandChannel = IGC.RegisterBroadcastListener("miners.command");
	if (commandChannel.HasPendingMessage)
	{
		var msg = commandChannel.AcceptMessage();
		param = msg.Data.ToString();
		Log("Got miners.command: " + param);
	}

	StartOfTick(param);

	foreach (var m in uniMsgs)
	{
		if (m.Tag == "apck.ntv.update")
		{
			/* We have received telemtry (about the assigned docking port). */
			var igcDto = (MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>)m.Data;
			var name = igcDto.Item1.Item1;
			UpdateNTV(name, igcDto);
			if (minerController?.pCore != null)
			{
				IGC.SendUnicastMessage(minerController.pCore.EntityId, "apck.ntv.update", igcDto);
			}
		}
		else if (m.Tag == "apck.depart.complete")
		{
			if (minerController?.DispatcherId != null)
				IGC.SendUnicastMessage(minerController.DispatcherId.Value, "apck.depart.complete", "");
		}
		else if (m.Tag == "apck.docking.approach" || m.Tag == "apck.depart.approach")
		{
			/* We are cleared for departure/landing. */
			if (minerController?.pCore != null)
			{
				IGC.SendUnicastMessage(minerController.pCore.EntityId, m.Tag, (ImmutableArray<Vector3D>)m.Data);
			}
			else
			{
				if (m.Tag.Contains("depart"))
				{
					var f = new APckTask("fin", coreUnit.CurrentBH);
					f.TickLimit = 1;
					f.OnComplete = () => IGC.SendUnicastMessage(m.Source, "apck.depart.complete", "");
					coreUnit.CreateWP(f);
				}

				/* Disconnect from base (if departure). */
				coreUnit.docker.Disconnect();
			}

		}

	}

	E.Echo($"Version: {Ver}");
			
	minerController.Handle(uniMsgs);
	E.Echo("Min3r state: " + minerController.GetState());
	E.Echo("Dispatcher: " + minerController.DispatcherId);
	E.Echo("HoldingLock: " + minerController.ObtainedLock);
	E.Echo("WaitedSection: " + minerController.WaitedSection);
	E.Echo($"Estimated shaft radius: {Variables.Get<float>("circular-pattern-shaft-radius"):f2}");
	E.Echo("LifetimeAcceptedTasks: " + stateWrapper.PState.LifetimeAcceptedTasks);
	E.Echo("LifetimeOreAmount: " + FormatNumberToNeatString(stateWrapper.PState.LifetimeOreAmount));
	E.Echo("LifetimeOperationTime: " + TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime).ToString());
	E.Echo("LifetimeWentToMaintenance: " + stateWrapper.PState.LifetimeWentToMaintenance);

	if (coreUnit != null)
	{
		if (coreUnit.pc.Pip != Vector3D.Zero)
			EmitProjection("agent-dest", coreUnit.pc.Pip, "");
		if (coreUnit.pc.PosShift != Vector3D.Zero)
			EmitProjection("agent-vel", coreUnit.pc.PosShift, coreUnit.pc.DBG);
	}

	if (rawPanel != null)
	{
		SendFeedback($"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");
		SendFeedback($"LifetimeOreAmount: {FormatNumberToNeatString(stateWrapper.PState.LifetimeOreAmount)}");
		SendFeedback($"LifetimeOperationTime: {TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime)}");
		SendFeedback($"LifetimeWentToMaintenance: {stateWrapper.PState.LifetimeWentToMaintenance}");
		SendFeedback("\n");
		SendFeedback($"CurrentJobMaxShaftYield: {FormatNumberToNeatString(stateWrapper.PState.CurrentJobMaxShaftYield)}");
		SendFeedback($"CurrentShaftYield: " + minerController?.CurrentJob?.GetShaftYield());
		SendFeedback(minerController?.CurrentJob?.ToString());
		FlushFeedbackBuffer();
	}
			
	if (Toggle.C.Check("show-pstate"))
		E.Echo(stateWrapper.PState.ToString());

	EndOfTick();
	CheckExpireNTV();

	if (DbgIgc != 0)
		EmitFlush(DbgIgc);
	Dt = Math.Max(0.001, Runtime.TimeSinceLastRun.TotalSeconds);
	E.T += Dt;
	iCount = Math.Max(iCount, Runtime.CurrentInstructionCount);
	E.Echo($"InstructionCount (Max): {Runtime.CurrentInstructionCount} ({iCount})");
	E.Echo($"Processed in {Runtime.LastRunTimeMs:f3} ms");
}

int iCount;

List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
Vector3D? castedSurfacePoint;
Vector3D? castedNormal;
public void RaycastTaskHandler(string[] cmdString)
{
	var cam = cameras.Where(c => c.IsActive).FirstOrDefault();
	if (cam != null)
	{
		cam.CustomData = "";
		var pos = cam.GetPosition() + cam.WorldMatrix.Forward * Variables.Get<float>("ct-raycast-range");
		cam.CustomData += "GPS:dir0:" + VectorOpsHelper.V3DtoBroadcastString(pos) + ":\n";
		Log($"RaycastTaskHandler tries to raycast point GPS:create-task base point:{VectorOpsHelper.V3DtoBroadcastString(pos)}:");
		if (cam.CanScan(pos))
		{
			var dei = cam.Raycast(pos);
			if (!dei.IsEmpty())
			{
				castedSurfacePoint = dei.HitPosition.Value;
				Log($"GPS:Raycasted base point:{VectorOpsHelper.V3DtoBroadcastString(dei.HitPosition.Value)}:");
				cam.CustomData += "GPS:castedSurfacePoint:" + VectorOpsHelper.V3DtoBroadcastString(castedSurfacePoint.Value) + ":\n";

				IMyShipController gravGetter = minerController?.remCon;
				Vector3D pCent;
				if ((gravGetter != null) && gravGetter.TryGetPlanetPosition(out pCent))
				{
					castedNormal = Vector3D.Normalize(pCent - castedSurfacePoint.Value);
					E.DebugLog("Using mining-center-to-planet-center direction as a normal because we are in gravity");
				}
				else
				{
					var toBasePoint = castedSurfacePoint.Value - cam.GetPosition();
					var perp = Vector3D.Normalize(Vector3D.CalculatePerpendicularVector(toBasePoint));
					var p1 = castedSurfacePoint.Value + perp * Math.Min(10, toBasePoint.Length());
					var p2 = castedSurfacePoint.Value + Vector3D.Normalize(Vector3D.Cross(perp, toBasePoint)) * Math.Min(20, toBasePoint.Length());

					var pt1 = p1 + Vector3D.Normalize(p1 - cam.GetPosition()) * 500;
					var pt2 = p2 + Vector3D.Normalize(p2 - cam.GetPosition()) * 500;

					cam.CustomData += "GPS:target1:" + VectorOpsHelper.V3DtoBroadcastString(pt1) + ":\n";
					if (cam.CanScan(pt1))
					{
						var cast1 = cam.Raycast(pt1);
						if (!cast1.IsEmpty())
						{
							Log($"GPS:Raycasted aux point 1:{VectorOpsHelper.V3DtoBroadcastString(cast1.HitPosition.Value)}:");
							cam.CustomData += "GPS:cast1:" + VectorOpsHelper.V3DtoBroadcastString(cast1.HitPosition.Value) + ":\n";
							cam.CustomData += "GPS:target2:" + VectorOpsHelper.V3DtoBroadcastString(pt2) + ":\n";
							if (cam.CanScan(pt2))
							{
								var cast2 = cam.Raycast(pt2);
								if (!cast2.IsEmpty())
								{
									Log($"GPS:Raycasted aux point 2:{VectorOpsHelper.V3DtoBroadcastString(cast2.HitPosition.Value)}:");
									cam.CustomData += "GPS:cast2:" + VectorOpsHelper.V3DtoBroadcastString(cast2.HitPosition.Value) + ":";
									castedNormal = -Vector3D.Normalize(Vector3D.Cross(cast1.HitPosition.Value - castedSurfacePoint.Value,
											cast2.HitPosition.Value - castedSurfacePoint.Value));
								}
							}
						}
					}
				}

				if (castedNormal.HasValue && castedSurfacePoint.HasValue)
				{
					E.DebugLog("Successfully got mining center and mining normal");
					if (minerController != null)
					{
						if (minerController.DispatcherId.HasValue)
							IGC.SendUnicastMessage(minerController.DispatcherId.Value, "create-task",
									new MyTuple<float, Vector3D, Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),
									castedSurfacePoint.Value - castedNormal.Value * 10,
									castedNormal.Value));
					}
				}
				else
				{
					E.DebugLog($"RaycastTaskHandler failed to get castedNormal or castedSurfacePoint");
				}
			}
		}
		else
		{
			E.DebugLog($"RaycastTaskHandler couldn't raycast initial position. Camera '{cam.CustomName}' had {cam.AvailableScanRange} AvailableScanRange");
		}
	}
	else
	{
		throw new Exception($"No active cam, {cameras.Count} known");
	}
}

MinerController minerController;
public class MinerController
{
	TimerTriggerService tts;

	public MiningJob CurrentJob { get; private set; }

	public long? DispatcherId;
	public string ObtainedLock = "";
	public string WaitedSection = "";
	public bool WaitingForLock;

	public Vector3D GetMiningPlaneNormal()
	{
		if (!pState.miningPlaneNormal.HasValue)
		{
			var ng = remCon.GetNaturalGravity();
			if (ng == Vector3D.Zero)
				throw new Exception("Need either natural gravity or miningPlaneNormal");
			else
				return Vector3D.Normalize(ng);
		}
		return pState.miningPlaneNormal.Value;
	}

	public MinerState GetState()
	{
		return pState.MinerState;
	}

	public PersistentState pState
	{
		get
		{
			return stateWrapper.PState;
		}
	}

	Func<string, TargetTelemetry> ntv;
	StateWrapper stateWrapper;
	public MinerController(IMyGridTerminalSystem gts, IMyIntergridCommunicationSystem igc, StateWrapper stateWrapper,
			Func<string, TargetTelemetry> GetNTV)
	{
		ntv = GetNTV;
		this.gts = gts;
		IGC = igc;
		//CurrentJob = new MiningJob(this);
		this.stateWrapper = stateWrapper;

		fwReferenceBlock = GetSingleBlock<IMyGyro>(b => b.CustomName.Contains(ForwardGyroTag) && b.IsSameConstructAs(me));
		remCon = GetSingleBlock<IMyRemoteControl>(b => b.IsSameConstructAs(me));
		docker = GetSingleBlock<IMyShipConnector>(b => b.IsSameConstructAs(me));
		gts.GetBlocksOfType(drills, d => d.IsSameConstructAs(me));
		gts.GetBlocksOfType(allContainers, d => d.IsSameConstructAs(me) && d.HasInventory && ((d is IMyCargoContainer) || (d is IMyShipDrill) || (d is IMyShipConnector)));
		gts.GetBlocksOfType(batteries, b => b.IsSameConstructAs(me));
		gts.GetBlocksOfType(tanks, b => b.IsSameConstructAs(me));

		List<IMyTimerBlock> triggers = new List<IMyTimerBlock>();
		gts.GetBlocksOfType(triggers, b => b.IsSameConstructAs(me));
		tts = new TimerTriggerService(triggers);

		float maxR = 0;
		float padding = me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2f : 1.5f;
		foreach (var d in drills)
		{
			var r = Vector3D.Reject(d.GetPosition() - fwReferenceBlock.GetPosition(), fwReferenceBlock.WorldMatrix.Forward).Length();
			maxR = (float)Math.Max(r + padding, maxR);
		}
		Variables.Set("circular-pattern-shaft-radius", maxR);

		var bs = new List<IMyRadioAntenna>();
		gts.GetBlocksOfType(bs, b => b.IsSameConstructAs(me));
		antenna = bs.FirstOrDefault();

		var ls = new List<IMyLightingBlock>();
		gts.GetBlocksOfType(ls, b => b.IsSameConstructAs(me));
		refLight = ls.FirstOrDefault();

		gts.GetBlocksOfType(allFunctionalBlocks, b => b.IsSameConstructAs(me));
	}
	public void SetControlledUnit(IMyProgrammableBlock pCore)
	{
		this.pCore = pCore;
	}
	public void SetControlledUnit(APckUnit unit)
	{
		embeddedUnit = unit;
	}

	public MinerState PrevState { get; private set; }
	public void SetState(MinerState newState)
	{
		tts.TryTriggerNamedTimer(GetState() + ".OnExit");
		Log("SetState: " + GetState() + "=>" + newState);
		tts.TryTriggerNamedTimer(newState + ".OnEnter");

		PrevState = pState.MinerState;
		pState.MinerState = newState;

		if ((newState == MinerState.Disabled) || (newState == MinerState.Idle))
		{
			drills.ForEach(d => d.Enabled = false);
			CommandAutoPillock("command:pillock-mode:Inert", u => u.SetState(ApckState.Inert));
		}
	}

	public void Halt()
	{
		CheckBatteriesAndIntegrity(1, 1);
		CommandAutoPillock("command:pillock-mode:Disabled", u => u.pc.SetState(PillockController.State.Disabled));
		drills.ForEach(d => d.Enabled = false);
		stateWrapper.ClearPersistentState();
	}

	public void TrySetState(string stateName)
	{
		MinerState newState;
		if (Enum.TryParse(stateName, out newState))
			SetState(newState);
	}

	public T GetSingleBlock<T>(Func<IMyTerminalBlock, bool> pred) where T : class
	{
		var blocks = new List<IMyTerminalBlock>();
		gts.GetBlocksOfType(blocks, b => ((b is T) && pred(b)));
		return blocks.First() as T;
	}

	public void CreateTask()
	{
		var ng = remCon.GetNaturalGravity();
		if (ng != Vector3D.Zero)
			pState.miningPlaneNormal = Vector3D.Normalize(ng);
		else
			pState.miningPlaneNormal = fwReferenceBlock.WorldMatrix.Forward;

		double elevation;
		if (remCon.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation))
			pState.miningEntryPoint = fwReferenceBlock.WorldMatrix.Translation + pState.miningPlaneNormal.Value * (elevation - 5);
		else
			pState.miningEntryPoint = fwReferenceBlock.WorldMatrix.Translation;

		if (DispatcherId.HasValue)
		{
			IGC.SendUnicastMessage(DispatcherId.Value, "create-task",
				new MyTuple<float, Vector3D, Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"), pState.miningEntryPoint.Value,
				pState.miningPlaneNormal.Value));
		}
	}

	/** \brief Compiles a handshake message and broadcasts it. */
	public void InitiateHandshake() {
		/* Assemble a transponder message.*/
		var damBlck = allFunctionalBlocks.FirstOrDefault(b => !b.IsFunctional); // Damaged block, if exists.
		var report = new TransponderMsg();
		report.Id          = IGC.Me;
		report.WM          = fwReferenceBlock.WorldMatrix;
		report.v           = remCon.GetShipVelocities().LinearVelocity;
		report.f_bat       = batteryCharge_cached;
		report.f_bat_min   = Variables.Get<float>("battery-low-factor");
		report.f_fuel      = fuelLevel_cached;
		report.f_fuel_min  = Variables.Get<float>("gas-low-factor");
		report.damage      = (damBlck != null ? damBlck.CustomName : "");
		report.state       = pState.MinerState;
		report.f_cargo     = cargoFullness_cached;
		report.f_cargo_max = Variables.Get<float>("cargo-full-factor");
		report.bAdaptive   = Toggle.C.Check("adaptive-mining");
		report.bRecalled   = pState.bRecalled;
		report.t_shaft     = CurrentJob != null ? CurrentJob.GetCurrentDepth() : 0f;
		report.t_ore       = CurrentJob != null ? CurrentJob.lastFoundOreDepth.GetValueOrDefault(0f) : 0f;
		report.bUnload     = bUnloading;
		report.name        = me.CubeGrid.CustomName;
		CurrentJob?.UpdateReport(report, pState.MinerState);

		/* Assemble the data content for the handshake. */
		var data = new MyTuple<string,MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>, string>();
		data.Item1 = Variables.Get<string>("group-constraint");
		data.Item2 = report.ToIgc();
		data.Item3 = Ver;

		BroadcastToChannel("miners.handshake", data);
	}

	public void Handle(List<MyIGCMessage> uniMsgs)
	{
		/* Update some expensive quantities. */
		UpdateBatteryCharge(); // TODO: Maybe not necessary to update on every cycle
		UpdateFuelLevel();
		switch (pState.MinerState) {
		default:
			UpdateCargoFullness();
			break;
		case MinerState.Disabled:
		case MinerState.Idle:
		case MinerState.GoingToEntry:
		case MinerState.WaitingForDocking:
		case MinerState.ReturningToShaft:
		case MinerState.Takeoff:
		case MinerState.ReturningHome:
		case MinerState.Docking:
			break; // Cargo is definitely not changing in these states.
		}

		E.Echo(embeddedUnit != null ? "Embedded APck" : pCore.CustomName);
		embeddedUnit?.Handle(TickCount, E.Echo);

		if ((CurrentJob != null) && (!WaitingForLock))
		{
			if (DispatcherId.HasValue)
				CurrentJob.HandleState(pState.MinerState);
		}

		var j = CurrentJob;

		var minerChannel = IGC.RegisterBroadcastListener("miners");

		foreach (var msg in uniMsgs)
		{
			if (!msg.Tag.Contains("set-vectors"))
				LogMsg(msg, false);

			if ((msg.Tag == "miners.assign-shaft") && (msg.Data is MyTuple<int, Vector3D, Vector3D, MyTuple<float, float, float, bool, bool>>))
			{
				/* We have been assigned a new job (=shaft) to work on. */
				var data = (MyTuple<int, Vector3D, Vector3D, MyTuple<float, float, float, bool, bool>>)msg.Data;
				if (j != null)
				{
					j.SetShaftVectors(data.Item1, data.Item2, data.Item3);
					pState.maxDepth =                         data.Item4.Item1;
					pState.skipDepth =                        data.Item4.Item2;
					pState.leastDepth =                       data.Item4.Item3;
					Toggle.C.Set("adaptive-mining",           data.Item4.Item4);
					Toggle.C.Set("adjust-entry-by-elevation", data.Item4.Item5);
					Log("Got new ShaftVectors");
					Dispatch();
				}
			}

			//"miners.handshake.reply"

			if (msg.Tag == "miners.handshake.reply")
			{
				Log("Received reply from dispatcher " + msg.Source);
				DispatcherId = msg.Source;
			}

			if (msg.Tag == "miners.normal")
			{
				var normal = (Vector3D)msg.Data;
				Log("Was assigned a normal of " + normal);
				pState.miningPlaneNormal = normal;
			}

			if (msg.Tag == "miners.resume")
			{
				var normal = (Vector3D)msg.Data;
				Log("Received resume command. Clearing state, running MineCommandHandler, assigned a normal of " + normal);
				stateWrapper.ClearPersistentState();
				pState.miningPlaneNormal = normal;
				MineCommandHandler();
			}

			if (msg.Tag == "command")
			{
				if (msg.Data.ToString() == "force-finish")
					FinishAndDockHandler();
				if (msg.Data.ToString() == "mine")
					MineCommandHandler();
			}

			if (msg.Tag == "set-value")
			{
				var parts = ((string)msg.Data).Split(':');
				Log($"Set value '{parts[0]}' to '{parts[1]}'");
				Variables.Set(parts[0], parts[1]);
			}

			if (msg.Tag == "miners")
			{
				if (!(msg.Data is MyTuple<string, Vector3D, Vector3D>)) {
					Log("Ignoring granted lock with malformed data.");
					continue;
				}
				var data = (MyTuple<string, Vector3D, Vector3D>)msg.Data;
				pState.p_FL = data.Item2;
				pState.n_FL = data.Item3;

				if (!string.IsNullOrEmpty(ObtainedLock) && (ObtainedLock != data.Item1)) {
					//ReleaseLock(ObtainedLock);
					Log($"{data.Item1} common-airspace-lock hides current ObtainedLock {ObtainedLock}!");
				}
				ObtainedLock = data.Item1;
				Log(data.Item1 + " common-airspace-lock-granted");

				// can fly!
				// ("general" also covers "mining-site")
				if (   WaitedSection == data.Item1
					|| (WaitedSection == LOCK_NAME_MiningSection && data.Item1 == LOCK_NAME_GeneralSection))
					Dispatch();
			}

			if (msg.Tag == "report.request")
			{
				/* Progress report requested, compile and send the report. */
				var damBlck = allFunctionalBlocks.FirstOrDefault(b => !b.IsFunctional); // Damaged block, if exists.
				var report = new TransponderMsg();
				report.Id          = IGC.Me;
				report.WM          = fwReferenceBlock.WorldMatrix;
				report.v           = remCon.GetShipVelocities().LinearVelocity;
				report.f_bat       = batteryCharge_cached;
				report.f_bat_min   = Variables.Get<float>("battery-low-factor");
				report.f_fuel      = fuelLevel_cached;
				report.f_fuel_min  = Variables.Get<float>("gas-low-factor");
				report.damage      = (damBlck != null ? damBlck.CustomName : "");
				report.state       = pState.MinerState;
				report.f_cargo     = cargoFullness_cached;
				report.f_cargo_max = Variables.Get<float>("cargo-full-factor");
				report.bAdaptive   = Toggle.C.Check("adaptive-mining");
				report.bRecalled   = pState.bRecalled;
				report.t_shaft     = CurrentJob != null ? CurrentJob.GetCurrentDepth() : 0f;
				report.t_ore       = CurrentJob != null ? CurrentJob.lastFoundOreDepth.GetValueOrDefault(0f) : 0f;
				report.bUnload     = bUnloading;
				report.name        = me.CubeGrid.CustomName;
				CurrentJob?.UpdateReport(report, pState.MinerState);
				IGC.SendBroadcastMessage("miners.report", report.ToIgc());
			}
		}

		while (minerChannel.HasPendingMessage)
		{
			var msg = minerChannel.AcceptMessage();
			LogMsg(msg, false);
			// do some type checking
			//if ((msg.Data != null) && (msg.Data is Vector3D))
			if (msg.Data != null)
			{
				if (msg.Data.ToString().Contains("common-airspace-lock-released"))
				{
					var sectionName = msg.Data.ToString().Split(':')[1];
					Log("(Agent) received lock-released notification " + sectionName + " from " + msg.Source);
				}

				if (msg.Data.ToString() == "dispatcher-change")
				{
					DispatcherId = null;
					Scheduler.C.RepeatWhile(() => !DispatcherId.HasValue).After(1000).RunCmd(() => InitiateHandshake());
				}
			}
		}

	}

	Queue<Action<MinerController>> waitedActions = new Queue<Action<MinerController>>();
	public void WaitForDispatch(string sectionName, Action<MinerController> callback)
	{
		WaitingForLock = true;
		if (!string.IsNullOrEmpty(sectionName))
			WaitedSection = sectionName;
		waitedActions.Enqueue(callback);
		Log("WaitForDispatch section \"" + sectionName + "\", callback chain: " + waitedActions.Count);
	}

	public void Dispatch()
	{
		WaitingForLock = false;
		WaitedSection = "";
		var count = waitedActions.Count;
		if (count > 0)
		{
			Log("Dispatching, callback chain: " + count);
			var a = waitedActions.Dequeue();
			a.Invoke(this);
		}
		else
			Log("WARNING: empty Dispatch()");
	}

	public void BroadcastToChannel<T>(string tag, T data)
	{
		IGC.SendBroadcastMessage(tag, data, TransmissionDistance.TransmissionDistanceMax);
		LogMsg(data, true);
	}

	public void UnicastToDispatcher<T>(string tag, T data)
	{
		if (DispatcherId.HasValue)
			IGC.SendUnicastMessage(DispatcherId.Value, tag, data);
	}

	public void Log(object msg)
	{
		E.DebugLog($"MinerController -> {msg}");
	}

	public void LogMsg(object msg, bool outgoing)
	{
		string data = msg.GetType().Name;
		if (msg is string)
			data = (string)msg;
		else if ((msg is ImmutableArray<Vector3D>) || (msg is Vector3D))
			data = "some vector(s)";

		if (Toggle.C.Check("log-message"))
		{
			if (!outgoing)
				E.DebugLog($"MinerController MSG-IN -> {data}");
			else
				E.DebugLog($"MinerController MSG-OUT -> {data}");
		}
	}

	public Action InvalidateDockingDto;
	public IMyProgrammableBlock pCore;
	APckUnit embeddedUnit;
	public IMyGridTerminalSystem gts;
	public IMyIntergridCommunicationSystem IGC;
	public IMyRemoteControl remCon;
	public List<IMyTerminalBlock> allContainers = new List<IMyTerminalBlock>();
	public IMyTerminalBlock fwReferenceBlock;
	public List<IMyShipDrill> drills = new List<IMyShipDrill>();
	public IMyShipConnector docker;
	public IMyRadioAntenna antenna;
	public List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
	public List<IMyGasTank> tanks = new List<IMyGasTank>();
	public IMyLightingBlock refLight;
	public List<IMyTerminalBlock> allFunctionalBlocks = new List<IMyTerminalBlock>();

	public void ResumeJobOnWorldLoad()
	{
		CurrentJob = new MiningJob(this);
		CurrentJob.SessionStartedAt = DateTime.Now;
		// TODO: restore some stats stuff
	}

	public void MineCommandHandler()
	{
		CurrentJob = new MiningJob(this);
		CurrentJob.SessionStartedAt = DateTime.Now;
		pState.LifetimeAcceptedTasks++;
		if (!TryResumeFromDock())
		{
			/* This is the agent that was used for task designation.
			 * Start in state "ChangingShaft", because we are already
			 * at the mining site.                                    */
			CurrentJob.Start();
		}
	}

	public void SkipCommandHandler()
	{
		if (CurrentJob != null)
		{
			CurrentJob.SkipShaft();
		}
	}

	/**
	 * \brief Plan the agent's way home.
	 * \details To be called when the agent is above the shaft, at the intersection
	 * with its flight level. It has already been decided that the agent is going
	 * home (not changing shafts).
	 * \warning Only call, when the agent is on its flight level, or holds the
	 * local airspace lock!
	 */
	public void ArrangeDocking()
	{
		if (!DispatcherId.HasValue)
			return;
		
		// Multi-agent mode, dynamic docking, respect shared space
		// Release lock as we are safe at own echelon while sitting on WaitingForDocking
		ReleaseLock(ObtainedLock);
		InvalidateDockingDto?.Invoke();
		IGC.SendUnicastMessage(DispatcherId.Value, "apck.docking.request", docker.GetPosition());
		SetState(MinerState.WaitingForDocking);
	}

	/** \brief Processes the "force-finish" command.                           */
	public void FinishAndDockHandler()
	{
		if (docker.Status == MyShipConnectorStatus.Connected)
		{
			batteries.ForEach(b => b.ChargeMode = ChargeMode.Recharge);
			tanks.ForEach(b => b.Stockpile = true);
			pState.lastAPckCommand = "";
			SetState(MinerState.Disabled);
		}
		else {
			pState.bRecalled = true;
			if (WaitingForLock && WaitedSection == "")
				Dispatch(); // We are waiting for a new job. Abort the wait and return.
		}
	}

	public bool TryResumeFromDock()
	{
		if (docker.Status != MyShipConnectorStatus.Connected)
			return false; // We are the agent who designated the mining task, and are already at the mining site.
				
		if (pState.getAbovePt.HasValue)
		{
			/* We already have a job. */
			SetState(MinerState.Docked);
			return true;
		}
				
		/* We are docked and unemployed. */
		UnicastToDispatcher("request-new", "");
		WaitForDispatch("", mc => {
			mc.SetState(MinerState.Docked);
		});
		return true;
	}

	public void EnterSharedSpace(string sectionName, Action<MinerController> task)
	{
		if (ObtainedLock == sectionName) {
			/* We already hold the desired lock. */
			task.Invoke(this);
			return;
		}
		BroadcastToChannel("miners", "common-airspace-ask-for-lock:" + sectionName);
		WaitForDispatch(sectionName, task);
	}

	public void ReleaseLock(string sectionName)
	{
		if (ObtainedLock == sectionName)
		{
			ObtainedLock = null;
			BroadcastToChannel("miners", "common-airspace-lock-released:" + sectionName);
			Log($"Released lock: {sectionName}");
		}
		else
		{
			Log("Tried to release non-owned lock section " + sectionName);
		}
	}

	public CommandRegistry ApckRegistry;
	public void CommandAutoPillock(string cmd, Action<APckUnit> embeddedAction = null)
	{
		pState.lastAPckCommand = cmd;
		//E.DebugLog("CommandAutoPillock: " + cmd);
		if (embeddedUnit != null)
		{
			if (embeddedAction != null)
			{
				embeddedAction(embeddedUnit);
			}
			else
			{
				//Log($"'{cmd}' is not support for embedded unit yet");

				var cmds = cmd.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('[', ']')).ToList();
				foreach (var i in cmds)
				{
					string[] cmdParts = i.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
					if (cmdParts[0] == "command")
					{
						ApckRegistry.RunCommand(cmdParts[1], cmdParts);
					}
				}
			}
		}
		else
		{
			if (IGC.IsEndpointReachable(pCore.EntityId))
			{
				IGC.SendUnicastMessage(pCore.EntityId, "apck.command", cmd);
			}
			else
			{
				throw new Exception($"APck {pCore.EntityId} is not reachable");
			}
		}
		//if (!pCore.TryRun(cmd))
		//throw new Exception("APck failure");
	}


	DateTime lastCheckStamp;
	bool CheckBatteriesAndIntegrityThrottled(float desiredBatteryLevel, float desiredGasLevel)
	{
		var dtNow = DateTime.Now;
		if ((dtNow - lastCheckStamp).TotalSeconds > 60)
		{
			lastCheckStamp = dtNow;
			return CheckBatteriesAndIntegrity(desiredBatteryLevel, desiredGasLevel);
		}
		return true;
	}

	/**
	 * \brief Returns true, iff no damage, fueled and sufficient battery charge.
	 */
	bool CheckBatteriesAndIntegrity(float desiredBatteryLevel, float desiredGasLevel)
	{
		allFunctionalBlocks.ForEach(x => TagDamagedTerminalBlocks(x, GetMyTerminalBlockHealth(x), true)); //TODO: Call with "false", when bRecall is set?
		if (allFunctionalBlocks.Any(b => !b.IsFunctional)) {
			if (antenna != null)
				antenna.CustomName = antenna.CubeGrid.CustomName + "> Damaged. Fix me asap!";
			allFunctionalBlocks.Where(b => !b.IsFunctional).ToList().ForEach(b => E.DebugLog($"{b.CustomName} is damaged or destroyed"));
			return false;
		}

		if (tanks.Any() && (fuelLevel_cached < desiredGasLevel)) {
			if (antenna != null)
				antenna.CustomName = $"{antenna.CubeGrid.CustomName}> Maintenance. Gas level: {fuelLevel_cached:f2}/{desiredGasLevel:f2}";
			return false;
		}

		if (batteryCharge_cached < desiredBatteryLevel) {
			if (antenna != null)
				antenna.CustomName = $"{antenna.CubeGrid.CustomName}> Maintenance. Charge level: {batteryCharge_cached:f2}/{desiredBatteryLevel:f2}";
			return false;
		}
		
		return true;
	}

	float GetMyTerminalBlockHealth(IMyTerminalBlock block)
	{
		IMySlimBlock slimblock = block.CubeGrid.GetCubeBlock(block.Position);
		if (slimblock != null)
			return (slimblock.BuildIntegrity - slimblock.CurrentDamage) / slimblock.MaxIntegrity;
		else
			return 1f;
	}

	void TagDamagedTerminalBlocks(IMyTerminalBlock myTerminalBlock, float health, bool onlyNonFunctional)
	{
		string name = myTerminalBlock.CustomName;
		if ((health < 1f) && (!onlyNonFunctional || !myTerminalBlock.IsFunctional))
		{
			if (!(myTerminalBlock is IMyRadioAntenna) && !(myTerminalBlock is IMyBeacon))
			{
				myTerminalBlock.SetValue("ShowOnHUD", true);
			}
			string taggedName;
			if (name.Contains("||"))
			{
				string pattern = @"(?<=DAMAGED: )(?<label>\d+)(?=%)";
				System.Text.RegularExpressions.Regex r = new System.Text.RegularExpressions.Regex(pattern);
				taggedName = r.Replace(
					name,
					delegate (System.Text.RegularExpressions.Match m)
					{
						return (health * 100).ToString("F0");
					});
			}
			else
			{
				taggedName = string.Format("{0} || DAMAGED: {1}%", name, health.ToString("F0"));
				Log($"{name} was damaged. Showing on HUD.");
			}
			myTerminalBlock.CustomName = taggedName;
		}
		else
		{
			UntagAndHide(myTerminalBlock);
		}
	}

	void UntagAndHide(IMyTerminalBlock myTerminalBlock)
	{
		if (myTerminalBlock.CustomName.Contains("||"))
		{
			string name = myTerminalBlock.CustomName;
			myTerminalBlock.CustomName = name.Split('|')[0].Trim();
			if (!(myTerminalBlock is IMyRadioAntenna) && !(myTerminalBlock is IMyBeacon))
			{
				myTerminalBlock.SetValue("ShowOnHUD", false);
			}

			Log($"{myTerminalBlock.CustomName} was fixed.");
		}
	}

	/**
	 * \brief Updates the battery charge.
	 */
	void UpdateBatteryCharge() {
		float storedPower = 0;
		float maxPower = 0;
		foreach (var b in batteries) {
			maxPower += b.MaxStoredPower;
			storedPower += b.CurrentStoredPower;
		}
		batteryCharge_cached = (maxPower > 0 ? storedPower / maxPower : 1f);
	}

	/**
	 * \brief Updates the fuel level.
	 */
	void UpdateFuelLevel() {
		float storedFuel = 0;
		float fuelCapacity = 0;
		double gasAvg = 0;
		foreach (var b in tanks) {
			storedFuel   += b.Capacity * (float)b.FilledRatio; 
			fuelCapacity += b.Capacity;
			gasAvg += b.FilledRatio;
		}
		fuelLevel_cached = (fuelCapacity > 0 ? storedFuel / fuelCapacity : 1f);
	}

	/**
	 * \brief Updates the cargo fullness, a value in [0;1], and the cargo mass.
	 */
	void UpdateCargoFullness() {
		float spaceNominal = 0;
		float spaceOccupied = 0;
		cargoMass_cached = 0;
		for (int i = 0; i < allContainers.Count; i++) {
			var inv = allContainers[i].GetInventory(0);
			if (inv == null)
				continue;
			spaceNominal += (float)inv.MaxVolume;
			spaceOccupied += (float)inv.CurrentVolume;
			cargoMass_cached += (float)inv.CurrentMass;
		}
		cargoFullness_cached = (spaceNominal > 0 ? spaceOccupied / spaceNominal : 1f);
	}

	/* Cached values for some expensive-to-calculate quantities. */
	float batteryCharge_cached; ///< [-] Battery charge in [0;1].
	float fuelLevel_cached;     ///< [-] Fuel fill in [0;1].
	float cargoFullness_cached; ///< [-] Use instead of CalcCargoFullness. Will be kept up to date automatically.
	float cargoMass_cached;     ///< [kg] Mass in inventories.
	bool bUnloading;            ///< Is the agent still unloading cargo?

	/** \brief Returns true, iff the cargo is sufficiently full to return to base. */
	bool CargoIsFull() {
		return cargoFullness_cached >= Variables.Get<float>("cargo-full-factor");
	}

	public class MiningJob
	{
		protected MinerController c;

		bool CurrentWpReached(double tolerance)
		{
			if (c.pState.currentWp.HasValue) {
				double dist = (c.pState.currentWp.Value - c.fwReferenceBlock.WorldMatrix.Translation).Length();
				E.Echo($"ds_WP: {dist:f2}");
			}
			return (!c.pState.currentWp.HasValue || (c.pState.currentWp.Value - c.fwReferenceBlock.WorldMatrix.Translation).Length() <= tolerance);
		}

		public MiningJob(MinerController minerController)
		{
			c = minerController;
		}

		/**
		 * \brief Start mining job right here.
		 * \details To be called if we are already at the mining site, e.g. the agent
		 * who designated the mining task.
		 */
		public void Start()
		{
			c.UnicastToDispatcher("request-new", "");
			c.WaitForDispatch("", mc => {
				c.EnterSharedSpace(LOCK_NAME_MiningSection, x =>
				{
					x.SetState(MinerState.ChangingShaft);
					x.drills.ForEach(d => d.Enabled = false);

					/* We may not have been assigned a flight level at
					 * this point. Simply start 15 m above the shaft.  */
					//TODO: If we have received a lock (see above), then we have a flight level.
					var depth = -15;
					var pt = c.pState.miningEntryPoint.Value + c.GetMiningPlaneNormal() * depth;
					var entryBeh = $"command:create-wp:Name=ChangingShaft,Ng=Forward,UpNormal=1;0;0," +
						$"AimNormal={VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':', ';')}" +
						$":{VectorOpsHelper.V3DtoBroadcastString(pt)}";
					c.CommandAutoPillock(entryBeh);
					c.pState.currentWp = pt;
				});
			});
		}

		/**
		 * \brief To be called when the job is done, and the agent has ascended
		 * back to the top of the shaft.
		 * \details Will request a new job from the dispatcher.
		 */
		public void SkipShaft()
		{
			if (c.pState.CurrentJobMaxShaftYield < prevTickValCount + currentShaftValTotal - preShaftValTotal)
				c.pState.CurrentJobMaxShaftYield = prevTickValCount + currentShaftValTotal - preShaftValTotal;

			/* If no ore found on the job, tell the dispatcher "the direction is bad". */
			if (Toggle.C.Check("adaptive-mining"))
			{
				// can CurrentJobMaxShaftYield be zero?
				// does not work as the prevTickValCount reflects the whole ore amount, not only from the current shaft
				if (!lastFoundOreDepth.HasValue || ((prevTickValCount + currentShaftValTotal - preShaftValTotal) / c.pState.CurrentJobMaxShaftYield < 0.5f))
				{
					c.UnicastToDispatcher("ban-direction", c.pState.CurrentShaftId.Value);
				}
			}

			CargoIsGettingValuableOre(); // Account for all ore that has been accidentally picked up during ascent. (while drills were running)
			AccountChangeShaft();
			lastFoundOreDepth = null;

			/* Wait for a new job, then go into the ChangingShaft state. */
			var pt = CalcShaftAbovePoint(); // Remember the point above the old shaft.
			c.UnicastToDispatcher("shaft-complete-request-new", c.pState.CurrentShaftId.Value);
			c.WaitForDispatch("", mc => {
				c.EnterSharedSpace(LOCK_NAME_MiningSection, x =>
				{
					x.SetState(MinerState.ChangingShaft);
					x.drills.ForEach(d => d.Enabled = false);
					x.CommandAutoPillock("command:create-wp:Name=ChangingShaft (Ascent),Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
					c.pState.currentWp = pt;
				});
			});
		}

		public void SetShaftVectors(int id, Vector3D miningEntryPoint, Vector3D getAbovePt)
		{
			c.pState.miningEntryPoint = miningEntryPoint; ///< Center point on the shaft's upper face.
			c.pState.getAbovePt       = getAbovePt;       ///< Point above the shaft, depending on the get-above altitude. Used for approach/departure.
			c.pState.CurrentShaftId   = id;               ///< The job ID from the dispatcher.
		}

		/**
		 * \brief Calculates the intersection of the shaft axis with the flight
		 * level.
		 */
		public Vector3D CalcShaftAbovePoint()
		{
			double e = Vector3D.Dot(c.GetMiningPlaneNormal(), c.pState.n_FL);
			//TODO: Can the mining plane be orthogonal to the flight levels?
			//      Then is e == 0.
			double d = Vector3D.Dot(c.pState.p_FL - c.pState.miningEntryPoint.Value, c.pState.n_FL) / e;
			return c.pState.miningEntryPoint.Value + c.GetMiningPlaneNormal() * d;
		}

		/**
		 * \brief Calculates the intersection of the docking port axis with the
		 * flight level.
		 * \param[in] r_D A point on the docking port axis, e.g. the center of the
		 * docking connector.
		 * \param[in] n_D The unit normal vector pointing away from the connector.
		 */
		public Vector3D CalcDockAbovePoint(Vector3D r_D, Vector3D n_D)
		{
			double e = Vector3D.Dot(n_D, c.pState.n_FL);
			if (Math.Abs(e) < 1e-5) {
				//TODO: Docking axis and flight level are parallel! What to do?
				E.Echo("Error: Docking axis and flight level are parallel. Cannot compute flight plan.");
			}
			double d = Vector3D.Dot(c.pState.p_FL - r_D, c.pState.n_FL) / e; 
			return r_D + n_D * d;
		}

		/**
		 * \brief Processes the agent's state transitions.
		 */
		public void HandleState(MinerState state)
		{
			if (state == MinerState.GoingToEntry) {

				/* Have we been ordered back to base? */
				if (c.pState.bRecalled) {
					c.SetState(MinerState.GoingToUnload);
					c.drills.ForEach(d => d.Enabled = false);
					var pt = CalcShaftAbovePoint();
					c.CommandAutoPillock("command:create-wp:Name=GoingToUnload,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
					c.pState.currentWp = pt;
					return;
				}

				if (!CurrentWpReached(0.5f))
					return; // We are not there yet. Keep descending.

				/* We just left controlled airspace. Release the lock ("mining-site"
				 * or "general", whatever we have been granted).                      */
				c.ReleaseLock(c.ObtainedLock);

				/* Switch on the drills, if not running already. */
				c.drills.ForEach(d => d.Enabled = true);

				/* Descend into the shaft. */
				c.SetState(MinerState.Drilling);
				//c.CommandAutoPillock("command:create-wp:Name=drill,Ng=Forward,PosDirectionOverride=Forward,SpeedLimit=0.6:0:0:0");
				c.CommandAutoPillock("command:create-wp:Name=drill,Ng=Forward,PosDirectionOverride=Forward" +
					",AimNormal=" + VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':', ';') +
					",UpNormal=1;0;0,SpeedLimit=" + Variables.Get<float>("speed-drill") + ":0:0:0");

			} else if (state == MinerState.Drilling) {

				/* Update some reporting stuff. */
				float currentDepth = GetCurrentDepth();
				E.Echo($"Depth: current: {currentDepth:f1} skip: {c.pState.skipDepth:f1}");
				E.Echo($"Depth: least: {c.pState.leastDepth:f1} max: {c.pState.maxDepth:f1}");
				E.Echo($"Cargo: {c.cargoFullness_cached:f2} / " + Variables.Get<float>("cargo-full-factor").ToString("f2"));

				if (c.pState.bRecalled) {
					GetOutTheShaft(); // We have been ordered back to base.
					return;
				}
						
				if (currentDepth > c.pState.maxDepth) {
					GetOutTheShaft(); // We have reached max depth, job complete.
					return;
				}

				if (!c.CheckBatteriesAndIntegrityThrottled(Variables.Get<float>("battery-low-factor"), Variables.Get<float>("gas-low-factor"))) {
					GetOutTheShaft(); // We need to return to base for maintenance reasons. 
					return;          //TODO: Emit MAYDAY if docking port is damaged or we cannot make it back to base for other reasons.
				}

				if (c.CargoIsFull()) {
					GetOutTheShaft(); // Cargo full, return to base.
					return;
				}

				if (currentDepth <= c.pState.skipDepth) {
					c.drills.ForEach(d => d.UseConveyorSystem = false);
					return;
				}
				
				// skipped surface layer, checking for ore and caring about cargo level
				c.drills.ForEach(d => d.UseConveyorSystem = true);

				bool bValOre = CargoIsGettingValuableOre();
				if (bValOre)
					lastFoundOreDepth = Math.Max(currentDepth, lastFoundOreDepth ?? 0);

				if (currentDepth <= c.pState.leastDepth)
					return;

				if (bValOre) {
					if ((!MinFoundOreDepth.HasValue) || (MinFoundOreDepth > currentDepth))
						MinFoundOreDepth = currentDepth;
					if ((!MaxFoundOreDepth.HasValue) || (MaxFoundOreDepth < currentDepth))
						MaxFoundOreDepth = currentDepth;
					if (Toggle.C.Check("adaptive-mining")) {
						c.pState.skipDepth = MinFoundOreDepth.Value - 2f;
						c.pState.maxDepth = MaxFoundOreDepth.Value + 2f;
					}
				}
				else
				{
					/* Give up drilling 2 m below the last valuable ore. */
					if (lastFoundOreDepth.HasValue && (currentDepth - lastFoundOreDepth > 2)) {
						GetOutTheShaft(); // No more ore expected in this shaft, job complete.
					}
				}
			}
			
			if (state == MinerState.AscendingInShaft) {

				if (!CurrentWpReached(0.5f))
					return; // We have not reached the top of the shaft yet.
						
				// kinda expensive
				if (c.pState.bRecalled || c.CargoIsFull() || !c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-low-factor"), Variables.Get<float>("gas-low-factor")))
				{
					// we reached cargo limit
					c.EnterSharedSpace(LOCK_NAME_MiningSection, mc =>
					{
						mc.SetState(MinerState.GoingToUnload);
						mc.drills.ForEach(d => d.Enabled = false);
						var pt = CalcShaftAbovePoint();
						mc.CommandAutoPillock("command:create-wp:Name=GoingToUnload,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
						c.pState.currentWp = pt;
					});
				}
				else
					/* Current job is done, request a new one. */
					SkipShaft();

			} else if (state == MinerState.ChangingShaft) {

				// triggered 15m above old mining entry
				if (!CurrentWpReached(0.5f))
					return; // Not reached the point above the shaft yet. Keep flying.
				
				var pt = CalcShaftAbovePoint();
				c.CommandAutoPillock("command:create-wp:Name=ChangingShaft (Traverse),Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
				c.pState.currentWp = pt;
				c.SetState(MinerState.ReturningToShaft);
				c.ReleaseLock(c.ObtainedLock);
			}

			if (state == MinerState.Takeoff)
			{
				if (!CurrentWpReached(1.0f))
					return; // Not reached the point above the docking port yet. Keep flying.

				/* If received recall order while taking off, keep the lock and land again. */
				//TODO: ATC has probably re-assigned the docking port already.
				//      Have the dispatcher keep the docking port reserved, until the airspace lock is returned after takeoff.
				//if (c.pState.bRecalled) {
				//	return;
				//}

				/* Release the "general" or "base" airspace lock, if held. */
				if (c.ObtainedLock != null)
					c.ReleaseLock(c.ObtainedLock);

				//TODO: This is a quick fix, because we need ATC to assign a new docking port.
				//      Fix this, see above.
				if (c.pState.bRecalled) {
					c.ArrangeDocking();
					return;
				}

				/* Lay in a course to above the shaft. */

				/* Calculate the intersection of the shaft
				 * axis with the assigned flight level. */
				var aboveShaft = CalcShaftAbovePoint();
				c.CommandAutoPillock("command:create-wp:Name=xy,Ng=Forward,AimNormal=" 
				    + VectorOpsHelper.V3DtoBroadcastString( c.GetMiningPlaneNormal() ).Replace(':',';')
				    + ":" + VectorOpsHelper.V3DtoBroadcastString(aboveShaft));
				c.pState.currentWp = aboveShaft;
				c.SetState(MinerState.ReturningToShaft);
				return;

			} else if (state == MinerState.ReturningToShaft) {

				/* Have we been ordered back to base? */
				if (c.pState.bRecalled) {

					/* Have we already asked for a lock "mining-site"? If yes, cancel the request. */
					if (c.WaitingForLock) {
						c.BroadcastToChannel("miners", "common-airspace-ask-for-lock:");
						c.WaitingForLock = false;
						c.waitedActions.Clear();
					}

					/* Ask ATC for a free dockingport. */
					c.ArrangeDocking();

					/* Stop movement, don't keep flying to the mining site. */
					//TODO: In the meantime, APck keeps flying us towards the mining site. 
					//      Instead, we should bring the drone to a halt immediately.
					return;
				}

				if (!CurrentWpReached(1.0f))
					return; // Not reached the point above the shaft yet. Keep flying.

				/* Acquire "mining-site" airspace lock before descending into the shaft. */
				c.EnterSharedSpace(LOCK_NAME_MiningSection, mc =>
				{
					mc.SetState(MinerState.GoingToEntry);

					/* Start the drills. */
					c.drills.ForEach(d => d.Enabled = true);

					/* Command the auto pilot to descend to the shaft. */
					var entry = $"command:create-wp:Name=drill entry,Ng=Forward,UpNormal=1;0;0,AimNormal=" +
							$"{VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':', ';')}:";

					double elevation;
					if (Toggle.C.Check("adjust-entry-by-elevation") && c.remCon.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation))
					{
						Vector3D plCenter;
						c.remCon.TryGetPlanetPosition(out plCenter);
						var plNorm = Vector3D.Normalize(c.pState.miningEntryPoint.Value - plCenter);
						var h = (c.fwReferenceBlock.WorldMatrix.Translation - plCenter).Length() - elevation + 5f;

						var elevationAdjustedEntryPoint = plCenter + plNorm * h;
						mc.CommandAutoPillock(entry + VectorOpsHelper.V3DtoBroadcastString(elevationAdjustedEntryPoint));
						c.pState.currentWp = elevationAdjustedEntryPoint;
					}
					else
					{
						mc.CommandAutoPillock(entry + VectorOpsHelper.V3DtoBroadcastString(c.pState.miningEntryPoint.Value));
						c.pState.currentWp = c.pState.miningEntryPoint;
					}
				});
			}

			if (state == MinerState.GoingToUnload)
			{
				if (CurrentWpReached(0.5f))
				{
					c.ArrangeDocking();
				}

			} else if (state == MinerState.ReturningHome) {
						
				//TODO: Perform midcourse corrections (MCC) if docking port has moved by more than 1m.
				//      (e.g. issue a new APck command)

				if (!CurrentWpReached(1.0f))
					return; // Not reached the point above the docking port yet. Keep flying.

				c.EnterSharedSpace(LOCK_NAME_BaseSection, mc =>
				{
					TargetTelemetry dv = c.ntv("docking");

					/* Calculate the intersection of the docking port
					 * axis with the assigned flight level.           */
					var r_aboveDock = CalcDockAbovePoint(dv.Position.Value, dv.OrientationUnit.Value.Forward);

					/* Command the final approach to the AP. */
					c.CommandAutoPillock("command:create-wp:Name=DynamicDock.echelon,Ng=Forward,AimNormal="
					+ VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':', ';')
					+ ",TransformChannel=docking:"
					+ VectorOpsHelper.V3DtoBroadcastString(Vector3D.Transform(r_aboveDock, MatrixD.Invert(dv.OrientationUnit.Value)))
					+ ":command:pillock-mode:DockingFinal");
					c.SetState(MinerState.Docking);
				});

			} else if (state == MinerState.WaitingForDocking) {

				/* Get position (and other data) of the assigned docking port.
				 * (Updated live, because it could be moving.)               */
				TargetTelemetry dv = c.ntv("docking");
				if (!dv.Position.HasValue)
					return; // We have not yet received a position information for the docking port.
						
				/* Lay in a course to above the docking port. */
				var r_aboveDock = CalcDockAbovePoint(dv.Position.Value, dv.OrientationUnit.Value.Forward);
							
				//TODO: Set AimNormal to direction of docking port, not mining plane.
				c.CommandAutoPillock("command:create-wp:Name=xy,Ng=Forward,AimNormal=" + VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':',';')
					                + ":" + VectorOpsHelper.V3DtoBroadcastString(r_aboveDock));
				c.pState.currentWp = r_aboveDock;
				c.SetState(MinerState.ReturningHome);

			}

			if (state == MinerState.Docking) {
				/* Connect the connector, if not done already. */
				if (c.docker.Status != MyShipConnectorStatus.Connected)
					return;

				/* If just connected, disable autopilot and release airspace lock.
				 * Set fuel tanks to stockpile, and batteries to recharge.       */
				c.CommandAutoPillock("command:pillock-mode:Disabled");
				c.remCon.DampenersOverride = false;
				c.batteries.ForEach(b => b.ChargeMode = ChargeMode.Recharge);
				c.docker.OtherConnector.CustomData = "";
				c.InvalidateDockingDto?.Invoke();
				c.tanks.ForEach(b => b.Stockpile = true);

				/* We just left controlled airspace. Release the lock ("base"
				 * or "general", whatever we have been granted).              */
				if (c.ObtainedLock != null)
					c.ReleaseLock(c.ObtainedLock);

				c.SetState(MinerState.Docked);
			}

			if (state == MinerState.Docked)
			{
				E.Echo("Docking: Connected");
				if (c.bUnloading = !CargoFlush()) {
					/* Still unloading, remain docked. */
					E.Echo("Docking: still have items");
					return;
				}
						
				/* Wait until repaired, recharged and refuled. */
				if (!c.CheckBatteriesAndIntegrity(1f, 1f)) {
					/* We need to remain docked for maintenance. */
					c.SetState(MinerState.Maintenance);
					c.pState.LifetimeWentToMaintenance++;
					Scheduler.C.After(10000).RepeatWhile(() => c.GetState() == MinerState.Maintenance).RunCmd(() => {
						if (c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-full-factor"), 0.99f))
						{
							c.SetState(MinerState.Docked);
						}
					});
					return;
				}

				/* If recalled, go to "Disabled" mode. */
				if (c.pState.bRecalled) {
					c.SetState(MinerState.Disabled);
					c.pState.bRecalled = false;
					c.embeddedUnit?.UpdateHUD();
					AccountUnload();
					c.pState.LifetimeOperationTime += (int)(DateTime.Now - SessionStartedAt).TotalSeconds;
					c.stateWrapper.Save();
					c.CurrentJob = null; //TODO: Tell the dispatcher that the job will not be completed.
					return;
				}

				/* Unload and return to work. */
				c.EnterSharedSpace(LOCK_NAME_BaseSection, mc =>
				{
					/* Switch to internal power and open fuel tanks. */
					c.batteries.ForEach(b => b.ChargeMode = ChargeMode.Auto);
					c.tanks.ForEach(b => b.Stockpile = false);

					//c.docker.OtherConnector.CustomData = "";
					AccountUnload();

					/* Lay in a course to the point above
					 * docking port and engage autopilot. */
					HandleUnload(c.docker.OtherConnector);

					/* Disconnect and lift off. */
					c.docker.Disconnect();
					c.SetState(MinerState.Takeoff);
				});

			} else if (state == MinerState.Maintenance) {

				// for world reload
				if ((c.PrevState != MinerState.Docking) && (c.docker.Status == MyShipConnectorStatus.Connected))
				{
					c.CommandAutoPillock("command:pillock-mode:Disabled");
					Scheduler.C.After(10000).RepeatWhile(() => c.GetState() == MinerState.Maintenance).RunCmd(() => {
						if (c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-full-factor"), 0.99f))
						{
							c.SetState(MinerState.Docked);
						}
					});
				}
			}

		}

		/** \brief Unloads all cargo, returns true on success. */
		bool CargoFlush()
		{
			var outerContainers = new List<IMyCargoContainer>();
			c.gts.GetBlocksOfType(outerContainers, b => b.IsSameConstructAs(c.docker.OtherConnector) && b.HasInventory && b.IsFunctional && (b is IMyCargoContainer));

			var localInvs = c.allContainers.Select(c => c.GetInventory()).Where(i => i.ItemCount > 0);

			if (!localInvs.Any())
				return true; // All containers are already empty.
			
			E.Echo("Docking: still have items");
			foreach (var localInv in localInvs)
			{
				var items = new List<MyInventoryItem>();
				localInv.GetItems(items);
				for (int n = 0; n < items.Count; n++)
				{
					var itemToPush = items[n];
					IMyInventory destinationInv;

					var k = Variables.Get<string>("preferred-container");
					if (!string.IsNullOrEmpty(k))
						destinationInv = outerContainers.Where(x => x.CustomName.Contains(k)).Select(c => c.GetInventory()).FirstOrDefault();
					else
						destinationInv = outerContainers.Select(c => c.GetInventory()).Where(i => i.CanItemsBeAdded((MyFixedPoint)(1f), itemToPush.Type))
						.OrderBy(i => (float)i.CurrentVolume).FirstOrDefault();

					if (destinationInv != null)
					{
						//E.Echo("Docking: have outer invs to unload to");
						if (!localInv.TransferItemTo(destinationInv, items[n]))
						{
							E.Echo("Docking: failing to transfer from " + (localInv.Owner as IMyTerminalBlock).CustomName + " to "
								+ (destinationInv.Owner as IMyTerminalBlock).CustomName);
						}
					}
				}
			}
			return false;
		}

		
		/** 
		 * \brief To be called while in the MinerState.Drilling state. 
		 * \details Puts the agent in MinerState.AscendingInShaft and lets it
		 * carefully move towards the top of the shaft.
		 */
		void GetOutTheShaft()
		{
			c.SetState(MinerState.AscendingInShaft);

			var depth = Math.Min(8, (c.fwReferenceBlock.WorldMatrix.Translation - c.pState.miningEntryPoint.Value).Length());
			var pt = c.pState.miningEntryPoint.Value + c.GetMiningPlaneNormal() * depth;
			c.CommandAutoPillock("command:create-wp:Name=AscendingInShaft,Ng=Forward" +
				",AimNormal=" + VectorOpsHelper.V3DtoBroadcastString(c.GetMiningPlaneNormal()).Replace(':', ';') +
				",UpNormal=1;0;0,SpeedLimit=" + Variables.Get<float>("speed-clear") +
					":" + VectorOpsHelper.V3DtoBroadcastString(pt));
			c.pState.currentWp = pt;
		}


		public void UpdateReport(TransponderMsg report, MinerState state)
		{
			var b = ImmutableArray.CreateBuilder<MyTuple<string, string>>(10);
			//b.Add(new MyTuple<string, string>("Session\nore mined", SessionOreMined.ToString("f2")));
			b.Add(new MyTuple<string, string>("Lock\nrequested", c.WaitedSection));
			b.Add(new MyTuple<string, string>("Lock\nowned", c.ObtainedLock));
			report.KeyValuePairs = b.ToImmutableArray();
		}

		StringBuilder sb = new StringBuilder();
		public override string ToString()
		{
			sb.Clear();
			sb.AppendFormat("session uptime: {0}\n", (SessionStartedAt == default(DateTime) ? "-" : (DateTime.Now - SessionStartedAt).ToString()));
			sb.AppendFormat("session ore mass: {0}\n", SessionOreMined);
			sb.AppendFormat("cargoFullness: {0:f2}\n", c.cargoFullness_cached);
			sb.AppendFormat("cargoMass: {0:f2}\n", c.cargoMass_cached);
			sb.AppendFormat("cargoYield: {0:f2}\n", prevTickValCount);
			sb.AppendFormat("lastFoundOreDepth: {0}\n", lastFoundOreDepth.HasValue ? lastFoundOreDepth.Value.ToString("f2") : "-");
			sb.AppendFormat("minFoundOreDepth: {0}\n", MinFoundOreDepth.HasValue ? MinFoundOreDepth.Value.ToString("f2") : "-");
			sb.AppendFormat("maxFoundOreDepth: {0}\n", MaxFoundOreDepth.HasValue ? MaxFoundOreDepth.Value.ToString("f2") : "-");
			sb.AppendFormat("shaft id: {0}\n", c.pState.CurrentShaftId ?? -1);

			return sb.ToString();
		}

		public float SessionOreMined;
		public DateTime SessionStartedAt;

		public float? lastFoundOreDepth;  ///< Last depth at which ore has been found.

		float? MinFoundOreDepth
		{
			get
			{
				return c.pState.minFoundOreDepth;
			}
			set
			{
				c.pState.minFoundOreDepth = value;
			}
		}
		float? MaxFoundOreDepth
		{
			get
			{
				return c.pState.maxFoundOreDepth;
			}
			set
			{
				c.pState.maxFoundOreDepth = value;
			}
		}

		/**
		 * \brief Returns the current depth in the shaft.
		 * \details Negative value, if above the shaft.
		 */
		public float GetCurrentDepth()
		{
			if (   c.pState.MinerState == MinerState.Drilling
			    || c.pState.MinerState == MinerState.GoingToEntry
					|| c.pState.MinerState == MinerState.GoingToUnload
					|| c.pState.MinerState == MinerState.AscendingInShaft
					|| c.pState.MinerState == MinerState.ChangingShaft)
				return (float)Vector3D.Dot(c.fwReferenceBlock.WorldMatrix.Translation - c.pState.miningEntryPoint.Value, c.pState.miningPlaneNormal.Value);
			return 0f; // Agent is not in the shaft axis.
		}

		public float GetShaftYield()
		{
			return prevTickValCount + currentShaftValTotal - preShaftValTotal;
		}

		void AccountUnload()
		{
			SessionOreMined += c.cargoMass_cached;
			c.pState.LifetimeOreAmount += c.cargoMass_cached;
			c.pState.LifetimeYield += prevTickValCount;
			currentShaftValTotal += prevTickValCount - preShaftValTotal;
			preShaftValTotal = 0;
			prevTickValCount = 0;
		}

		void AccountChangeShaft()
		{
			preShaftValTotal = prevTickValCount;
			currentShaftValTotal = 0;
		}

		float currentShaftValTotal = 0;
		float preShaftValTotal = 0;
		float prevTickValCount = 0; // [l] On-board ore at last timestep. (For delta calculation.)

		/** \note May only be called once per cycle! */
		bool CargoIsGettingValuableOre()
		{
			/* Count all ore on board. */
			float totalAmount = 0;
			for (int i = 0; i < c.allContainers.Count; ++i) {
				var inv = c.allContainers[i].GetInventory(0);
				if (inv == null)
					continue;
				List<MyInventoryItem> items = new List<MyInventoryItem>();
				inv.GetItems(items);
				items.Where(ix => ix.Type.ToString().Contains("Ore") && !ix.Type.ToString().Contains("Stone")).ToList().ForEach(x => totalAmount += (float)x.Amount);
			}

			bool gain = ((prevTickValCount > 0) && (totalAmount > prevTickValCount));

			prevTickValCount = totalAmount;

			return gain;
		}

		void HandleUnload(IMyShipConnector otherConnector)
		{
			/* Calculate the intersection of the docking
			 * port axis with the assigned flight level. */
			var aboveDock = CalcDockAbovePoint(otherConnector.WorldMatrix.Translation, otherConnector.WorldMatrix.Forward);

			var seq = "[command:pillock-mode:Disabled],[command:create-wp:Name=Dock.Echelon,Ng=Forward:"
						+ VectorOpsHelper.V3DtoBroadcastString(aboveDock) + "]";

			c.CommandAutoPillock(seq);
			c.pState.currentWp = aboveDock;
		}
	}
}

///////////////////

static class VectorOpsHelper
{
	public static string V3DtoBroadcastString(params Vector3D[] vectors)
	{
		return string.Join(":", vectors.Select(v => string.Format("{0}:{1}:{2}", v.X, v.Y, v.Z)));
	}

	public static string MtrDtoBroadcastString(MatrixD mat)
	{
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				sb.Append(mat[i, j] + ":");
			}
		}
		return sb.ToString().TrimEnd(':');
	}

	public static Vector3D GetTangFreeDestinantion(MatrixD myMatrix, Vector3D pointTomove, BoundingSphereD dangerZone)
	{
		RayD r = new RayD(myMatrix.Translation, Vector3D.Normalize(pointTomove - myMatrix.Translation));
		double? collideDist = r.Intersects(dangerZone);

		if (collideDist.HasValue)
		{
			var toSphere = Vector3D.Normalize(dangerZone.Center - myMatrix.Translation);
			if (dangerZone.Contains(myMatrix.Translation) == ContainmentType.Contains)
				return dangerZone.Center - toSphere * dangerZone.Radius;
			var dzPerp = Vector3D.Cross(r.Direction, toSphere);
			Vector3D tangentR;
			if (dzPerp.Length() < double.Epsilon)
				toSphere.CalculatePerpendicularVector(out tangentR);
			else
				tangentR = Vector3D.Cross(dzPerp, -toSphere);
			return dangerZone.Center + Vector3D.Normalize(tangentR) * dangerZone.Radius;
		}
		return pointTomove;
	}


	public static Vector3D GetAnglesToPointMrot(Vector3D toP, MatrixD myFrameFw, MatrixD fwGyroDefault, Vector3D suggestedUpDir, ref Vector3D currentErr)
	{
			var up = myFrameFw.Up;
		var proj = Vector3D.ProjectOnPlane(ref toP, ref up);
		var angleYaw = -(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(myFrameFw.Forward, proj), up),
			Vector3D.Dot(myFrameFw.Forward, proj));

		up = myFrameFw.Right;
		proj = Vector3D.ProjectOnPlane(ref toP, ref up);
		var anglePitch = -(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(myFrameFw.Forward, proj), up),
			Vector3D.Dot(myFrameFw.Forward, proj));

		float angleRoll = 0;
		// to force same alignment among Agents. Roll is low priority.
		if ((suggestedUpDir != Vector3D.Zero) && (Math.Abs(angleYaw) < .05f) && (Math.Abs(anglePitch) < .05f))
		{
			up = myFrameFw.Forward;
			proj = Vector3D.ProjectOnPlane(ref suggestedUpDir, ref up);
			angleRoll = -(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(myFrameFw.Down, proj), up),
				Vector3D.Dot(myFrameFw.Up, proj));
		}

		var ctrl = new Vector3D(anglePitch, angleYaw, angleRoll * Variables.Get<float>("roll-power-factor"));
		currentErr = new Vector3D(Math.Abs(ctrl.X), Math.Abs(ctrl.Y), Math.Abs(ctrl.Z));

		var worldControlTorque = Vector3D.TransformNormal(ctrl, myFrameFw);
		var a = Vector3D.TransformNormal(worldControlTorque, MatrixD.Transpose(fwGyroDefault));
		a.X *= -1;

		return a;
	}

	// TODO: this is some BS control code, merge new from APck
	public static void SetOverrideX(IMyGyro gyro, Vector3 settings, Vector3D angV)
	{
		float yaw = settings.Y;
		float pitch = settings.X;
		float roll = settings.Z;

		var rm = IsLargeGrid ? 30 : 60;
		var acc = new Vector3D(1.92f, 1.92f, 1.92f);

		Func<double, double, double, double> rpmLimitSimple = (x, v, a) =>
		{
			var mag = Math.Abs(x);
			double r;
			if (mag > (v * v * 1.7) / (2 * a))
				r = rm * Math.Sign(x) * Math.Max(Math.Min(mag, 1), 0.002);
			else
			{
				r = -rm * Math.Sign(x) * Math.Max(Math.Min(mag, 1), 0.002);
			}
			return r * 0.6;
		};

		var realRPMyaw = (float)rpmLimitSimple(yaw, angV.Y, acc.Y); // max at 0.4165 (per tick), 5 per sec
		var realRPMpitch = (float)rpmLimitSimple(pitch, angV.X, acc.X);
		var realRPMroll = (float)rpmLimitSimple(roll, angV.Z, acc.Z);

		gyro.SetValue("Pitch", realRPMpitch);
		gyro.SetValue("Yaw", realRPMyaw);
		gyro.SetValue("Roll", realRPMroll);
	}

	public static void SetOverride(IMyGyro gyro, Vector3 settings, Vector3D deltaS, Vector3D angV)
	{
		if (Variables.Get<bool>("amp"))
		{
			var maxFactor = 5f;
			var curv = 2f;
			Func<double, double, double> ampF = (x, d) => x * (Math.Exp(-d * curv) + 0.8) * maxFactor / 2;
			deltaS /= Math.PI / 180f;
			if ((deltaS.X < 2) && (settings.X > 0.017))
				settings.X = (float)ampF(settings.X, deltaS.X);
			if ((deltaS.Y < 2) && (settings.Y > 0.017))
				settings.Y = (float)ampF(settings.Y, deltaS.Y);
			if ((deltaS.Z < 2) && (settings.Z > 0.017))
				settings.Z = (float)ampF(settings.Z, deltaS.Z);
		}

		SetOverrideX(gyro, settings, angV);
	}

	public static Vector3D GetPredictedImpactPoint(Vector3D meTranslation, Vector3D meVel, Vector3D targetCenter, Vector3D targetVelocity,
			Vector3D munitionVel, bool compensateOwnVel)
	{
		double munitionSpeed = Vector3D.Dot(Vector3D.Normalize(targetCenter - meTranslation), munitionVel);
		if (munitionSpeed < 30)
			munitionSpeed = 30;
		return GetPredictedImpactPoint(meTranslation, meVel, targetCenter, targetVelocity, munitionSpeed, compensateOwnVel);
	}

	public static Vector3D GetPredictedImpactPoint(Vector3D origin, Vector3D originVel, Vector3D targetCenter, Vector3D targetVelocity,
			double munitionSpeed, bool compensateOwnVel)
	{
		double currentDistance = Vector3D.Distance(origin, targetCenter);
		Vector3D target = targetCenter - origin;
		Vector3D targetNorm = Vector3D.Normalize(target);
		Vector3D assumedPosition = targetCenter;

		Vector3D velNorm;

		if (compensateOwnVel)
		{
			var rej = Vector3D.Reject(originVel, targetNorm);
			targetVelocity -= rej;
		}

		if (targetVelocity.Length() > float.Epsilon)
		{
			velNorm = Vector3D.Normalize(targetVelocity);
			var tA = Math.PI - Math.Acos(Vector3D.Dot(targetNorm, velNorm));
			var y = (targetVelocity.Length() * Math.Sin(tA)) / munitionSpeed;
			if (Math.Abs(y) <= 1)
			{
				var pipAngle = Math.Asin(y);
				var s = currentDistance * Math.Sin(pipAngle) / Math.Sin(tA + pipAngle);
				assumedPosition = targetCenter + velNorm * s;
			}
		}

		return assumedPosition;
	}
	public static string GetGPSString(string name, Vector3D p, Color c)
	{
		return $"GPS:{name}:{p.X}:{p.Y}:{p.Z}:#{c.R:X02}{c.G:X02}{c.B:X02}:";
	}

}

/// ///////////////////////////////

StringBuilder sbMain = new StringBuilder();
public void SendFeedback(string message)
{
	if (rawPanel != null)
	{
		sbMain.AppendLine(message);
	}
}

IMyTextPanel rawPanel;

public void FlushFeedbackBuffer()
{
	if (sbMain.Length > 0)
	{
		var s = sbMain.ToString();
		sbMain.Clear();
		rawPanel?.WriteText(s);
	}
}

string FormatNumberToNeatString(float value, string measure = "")
{
	string valueString;
	if (Math.Abs(value) >= 1000000)
	{
		if (!string.IsNullOrEmpty(measure))
			valueString = string.Format("{0:0.##} M{1}", value / 1000000, measure);
		else
			valueString = string.Format("{0:0.##}M", value / 1000000);
	}
	else if (Math.Abs(value) >= 1000)
	{
		if (!string.IsNullOrEmpty(measure))
			valueString = string.Format("{0:0.##} k{1}", value / 1000, measure);
		else
			valueString = string.Format("{0:0.##}k", value / 1000);
	}
	else
	{
		if (!string.IsNullOrEmpty(measure))
			valueString = string.Format("{0:0.##} {1}", value, measure);
		else
			valueString = string.Format("{0:0.##}", value);
	}
	return valueString;
}

public static class E
{
	static string debugTag = "";
	static Action<string> e;
	static IMyTextSurface p; // LCD of the current PB
	static IMyTextSurface l;
	public static double T;
	public static void Init(Action<string> echo, IMyGridTerminalSystem g)
	{
		e = echo;
		p = me.GetSurface(0);
		p.ContentType = ContentType.TEXT_AND_IMAGE;
		p.WriteText("");
	}
	public static void Echo(string s) { if ((debugTag == "") || (s.Contains(debugTag))) e(s); }

	static string buff = "";
	public static void DebugToPanel(string s)
	{
		buff += s + "\n";
	}
	static List<string> linesToLog = new List<string>();
	public static void DebugLog(string s)
	{
		p.WriteText($"{T:f2}: {s}\n", true);
		if (l != null)
		{
			linesToLog.Add(s);
		}
	}

	public static void AddLogger(IMyTextSurface s)
	{
		l = s;
	}

	public static void EndOfTick()
	{
		if (!string.IsNullOrEmpty(buff))
		{
			var h = UserCtrlTest.ctrls.Where(x => x.IsUnderControl).FirstOrDefault() as IMyTextSurfaceProvider;
			if ((h != null) && (h.SurfaceCount > 0))
				h.GetSurface(0).WriteText(buff);
			buff = "";
		}
		if (linesToLog.Any())
		{
			if (l != null)
			{
				linesToLog.Reverse();
				var t = string.Join("\n", linesToLog) + "\n" + l.GetText();
				var u = Variables.Get<int>("logger-char-limit");
				if (t.Length > u)
					t = t.Substring(0, u - 1);
				l.WriteText($"{T:f2}: {t}");
			}
			linesToLog.Clear();
		}
	}
}

class Scheduler
{
	static Scheduler inst = new Scheduler();
	Scheduler() { }

	public static Scheduler C
	{
		get
		{
			inst.delayForNextCmd = 0;
			inst.repeatCondition = null;
			return inst;
		}
	}

	class DelayedCommand
	{
		public DateTime TimeStamp;
		public Action Command;
		public Func<bool> repeatCondition;
		public long delay;
	}

	Queue<DelayedCommand> q = new Queue<DelayedCommand>();
	long delayForNextCmd;
	Func<bool> repeatCondition;

	public Scheduler After(int ms)
	{
		this.delayForNextCmd += ms;
		return this;
	}

	public Scheduler RunCmd(Action cmd)
	{
		q.Enqueue(new DelayedCommand { TimeStamp = DateTime.Now.AddMilliseconds(delayForNextCmd), Command = cmd, repeatCondition = repeatCondition, delay = delayForNextCmd });
		return this;
	}

	public Scheduler RepeatWhile(Func<bool> repeatCondition)
	{
		this.repeatCondition = repeatCondition;
		return this;
	}

	public void HandleTick()
	{
		if (q.Count > 0)
		{
			E.Echo("Scheduled actions count:" + q.Count);
			var c = q.Peek();
			if (c.TimeStamp < DateTime.Now)
			{
				if (c.repeatCondition != null)
				{
					if (c.repeatCondition.Invoke())
					{
						c.Command.Invoke();
						c.TimeStamp = DateTime.Now.AddMilliseconds(c.delay);
					}
					else
					{
						q.Dequeue();
					}
				}
				else
				{
					c.Command.Invoke();
					q.Dequeue();
				}
			}
		}
	}
	public void Clear()
	{
		q.Clear();
		delayForNextCmd = 0;
		repeatCondition = null;
	}
}

////////
///



APckUnit coreUnit;
public class APckUnit
{
	public PersistentState pState;   ///< Persistent state of the agent.
	public IMyShipConnector docker;  ///< Agent's docking port.
	public List<IMyWarhead> wh;

	IMyRadioAntenna antenna;

	FSM fsm;
	public PcBehavior CurrentBH;
	public Func<string, TargetTelemetry> _getTV;

	public IMyGyro G;
	public IMyGridTerminalSystem _gts;
	public IMyIntergridCommunicationSystem _igc;
	public IMyRemoteControl RC;
	public PillockController pc;
	public TimerTriggerService tts;

	public IMyProgrammableBlock Tp;
	public IMyProgrammableBlock TGP;

	HashSet<IMyTerminalBlock> coll = new HashSet<IMyTerminalBlock>();

	T GetCoreB<T>(string name, List<IMyTerminalBlock> set, bool required = false) where T : class, IMyTerminalBlock
	{
		T r;
		E.Echo("Looking for " + name);
		var f = set.Where(b => b is T && b.CustomName.Contains(name)).Cast<T>().ToList();
		r = required ? f.Single() : f.FirstOrDefault();
		if (r != null)
			coll.Add(r);
		return r;
	}

	List<T> GetCoreC<T>(List<IMyTerminalBlock> set, string n = null) where T : class, IMyTerminalBlock
	{
		var f = set.Where(b => b is T && ((n == null) || (b.CustomName == n))).Cast<T>().ToList();
		foreach (var b in f)
			coll.Add(b);
		return f;
	}

	public APckUnit(PersistentState ps, IMyGridTerminalSystem gts, IMyIntergridCommunicationSystem igc, Func<string, TargetTelemetry> gtt)
	{
		pState = ps;
		_gts = gts;
		_getTV = gtt;
		_igc = igc;

		//Func<IMyTerminalBlock, bool> f = b => true;
		Func<IMyTerminalBlock, bool> f = b => b.IsSameConstructAs(me);
		var subs = new List<IMyTerminalBlock>();
		gts.GetBlocks(subs);
		subs = subs.Where(b => f(b)).ToList();

		InitBS(subs);
	}

	public void InitBS(List<IMyTerminalBlock> subset)
	{
		var f = subset;

		E.DebugLog("subset: " + subset.Count);

				// svc
		Tp = GetCoreB<IMyProgrammableBlock>("a-thrust-provider", f);

		var rb = GetCoreC<IMyMotorStator>(f);
		var pbs = new List<IMyProgrammableBlock>();
		_gts.GetBlocksOfType(pbs, j => rb.Any(x => (x.Top != null) && x.Top.CubeGrid == j.CubeGrid));
		TGP = GetCoreB<IMyProgrammableBlock>("a-tgp", f) ?? pbs.FirstOrDefault(x => x.CustomName.Contains("a-tgp"));

		G = GetCoreB<IMyGyro>(ForwardGyroTag, f, true);

		var ctrls = GetCoreC<IMyShipController>(f);
		UserCtrlTest.Init(ctrls);

		antenna = GetCoreC<IMyRadioAntenna>(f).FirstOrDefault();
		docker = GetCoreC<IMyShipConnector>(f).First();

		wh = GetCoreC<IMyWarhead>(f);

		RC = GetCoreC<IMyRemoteControl>(f).First();
		RC.CustomData = "";

		var ts = GetCoreC<IMyTimerBlock>(f);
		tts = new TimerTriggerService(ts);

		thrusters = new List<IMyTerminalBlock>();
		thrusters.AddRange(GetCoreC<IMyThrust>(f));
		thrusters.AddRange(GetCoreC<IMyArtificialMassBlock>(f));

		string tag = Variables.Get<string>("ggen-tag");
		if (!string.IsNullOrEmpty(tag))
		{
			var g = new List<IMyGravityGenerator>();
			var gr = _gts.GetBlockGroupWithName(tag);
			if (gr != null)
				gr.GetBlocksOfType(g, b => f.Contains(b));
			foreach (var b in g)
				coll.Add(b);
			thrusters.AddRange(g);
		}
		else
			thrusters.AddRange(GetCoreC<IMyGravityGenerator>(f));

		pc = new PillockController(RC, tts, _igc, Tp, G, antenna, AllLocalThrusters, this, thrusters.Count > 5);

		fsm = new FSM(this, _getTV);

		SetState(ApckState.Standby);
		pc.SetState(PillockController.State.WP);
	}

	List<IMyTerminalBlock> thrusters;
	ThrusterSelector cached;
	int tsUpdatedStamp;
	public ThrusterSelector AllLocalThrusters()
	{
		if (cached == null)
			cached = new ThrusterSelector(G, thrusters);
		else if ((tick != tsUpdatedStamp) && (tick % 60 == 0))
		{
			tsUpdatedStamp = tick;
			if (thrusters.Any(x => !x.IsFunctional))
			{
				thrusters.RemoveAll(x => !x.IsFunctional);
				cached = new ThrusterSelector(G, thrusters);
			}
		}
		if (thrusters.Any(x => x is IMyThrust && (x as IMyThrust)?.MaxEffectiveThrust != (x as IMyThrust)?.MaxThrust))
			cached.CalculateForces();
		return cached;
	}

	public Vector3D? initialAlt;
	public Vector3D? plCenter;
	public bool UnderPlanetInfl()
	{
		if (pc.NG != null)
		{
			if (plCenter == null)
			{
				Vector3D planetPos;
				if (RC.TryGetPlanetPosition(out planetPos))
				{
					plCenter = planetPos;
					return true;
				}
			}
			return plCenter.HasValue;
		}
		return false;
	}

	public void TrySetState(string stateName)
	{
		ApckState s;
		if (Enum.TryParse(stateName, out s))
		{
			SetState(s);
		}
	}

	public void SetState(ApckState s)
	{
		if (fsm.TrySetState(s))
		{
			CurrentBH = fsm.GetCurrentBeh();
			UpdateHUD();
		}
	}

	/** \brief Updates the name of the antenna, which is shown in the HUD. */
	public void UpdateHUD()
	{
		if (antenna != null)
			antenna.CustomName = $"{G.CubeGrid.CustomName}"
			                   + (pState.bRecalled ? " [recalled]" : "")
			                   + $"> {fsm.GetCurrent().St} / {tPtr?.Value?.Name}";
	}

	public void BindBeh(ApckState st, PcBehavior b)
	{
		fsm.SetBehavior(st, b);
	}

	public PcBehavior GetBeh(ApckState st)
	{
		return fsm.GetXwrapper(st).BH;
	}

	public void CreateWP(APckTask t)
	{
		E.DebugLog("CreateWP " + t.Name);
		InsertTaskBefore(t);
	}

	public void Handle(int tick, Action<string> e)
	{
		this.tick = tick;

		HandleTasks();
		var task = tPtr?.Value;
		PcBehavior bh;
		if ((task != null) && (fsm.GetCurrent().St == task.PState))
			bh = task.BH ?? GetBeh(task.PState);
		else
			bh = CurrentBH;

		pc.HandleControl(tick, e, bh);
	}

	LinkedList<APckTask> tasks = new LinkedList<APckTask>();
	LinkedListNode<APckTask> tPtr;
	int tick;

	void HandleTasks()
	{
		if (tPtr != null)
		{
			var t = tPtr.Value;
			if (t.CheckCompl(tick, pc))
			{
				E.DebugLog($"TFin {t.Name}");
				ForceNext();
			}
		}
	}

	public APckTask GetCurrTask()
	{
		return tPtr?.Value;
	}

	public void InsertTaskBefore(APckTask t)
	{
		tasks.AddFirst(t);
		E.DebugLog($"Added {t.Name}, total: {tasks.Count}");
		tPtr = tasks.First;
		if (tPtr.Next == null)
			tPtr.Value.src = fsm.GetCurrent().St;
		t.Init(pc, tick);
		SetState(t.PState);
	}
	public void ForceNext()
	{
		var _p = tPtr;
		var c = _p.Value;
		c.OnComplete?.Invoke();
		//pc.TriggerService.TryTriggerNamedTimer(wp.Name + ".OnComplete");
		if (_p.Next != null)
		{
			c = _p.Next.Value;
			tPtr = _p.Next;
			c.Init(pc, tick);
			tasks.Remove(_p);
		}
		else
		{
			tPtr = null;
			tasks.Clear();
			SetState(_p.Value.src);
		}
	}
}

public class FSM
{
	APckUnit _u;

	Dictionary<ApckState, XState> stateBehs = new Dictionary<ApckState, XState>();

	public FSM(APckUnit unit, Func<string, TargetTelemetry> GetNTV)
	{
		_u = unit;
		var PC = _u.pc;

		var de = new PcBehavior { Name = "Default" };
		foreach (var s in Enum.GetValues(typeof(ApckState)))
		{
			stateBehs.Add((ApckState)s, new XState((ApckState)s, de));
		}

		stateBehs[ApckState.Standby].BH = new PcBehavior { Name = "Standby" };
		currentSt = stateBehs[ApckState.Standby];

		stateBehs[ApckState.Formation].BH = new PcBehavior
		{
			Name = "follow formation",
			AutoSwitchToNext = false,
			TargetFeed = () => GetNTV("wingman"),
			AimpointShifter = (tv) => PC.Fw.GetPosition() + GetNTV("wingman").OrientationUnit.Value.Forward * 5000,
			PositionShifter = p =>
			{
				var bs = new BoundingSphereD(GetNTV("wingman").OrientationUnit.Value.Translation, 30);
				return VectorOpsHelper.GetTangFreeDestinantion(PC.Fw.WorldMatrix, p, bs);
			},
			TranslationOverride = () => PC.Fw.GetPosition()
		};

		stateBehs[ApckState.Brake].BH = new PcBehavior
		{
			Name = "reverse",
			IgnoreTFeed = true,
			PositionShifter = tv => PC.CreateFromFwDir(-150),
			AimpointShifter = (tv) => PC.CreateFromFwDir(1),
			FlyThrough = true
		};

		stateBehs[ApckState.DockingAwait].BH = new PcBehavior
		{
			Name = "awaiting docking",
			AutoSwitchToNext = false,
			IgnoreTFeed = true,
			TargetFeed = () => GetNTV("wingman"),
			AimpointShifter = tv => PC.Fw.GetPosition() + PC.Fw.WorldMatrix.Forward,
			PositionShifter = tv => GetNTV("wingman").Position.HasValue ? GetNTV("wingman").Position.Value : PC.Fw.GetPosition(),
			DistanceHandler = (d, dx, c, wp, u) =>
			{
				if (GetNTV("docking").Position.HasValue && (_u.docker != null))
				{
					u.SetState(ApckState.DockingFinal);
				}
			}
		};

		stateBehs[ApckState.DockingFinal].BH = new PcBehavior
		{
			AimpointShifter = tv => _u.docker.GetPosition() - GetNTV("docking").OrientationUnit.Value.Forward * 10000,
			PositionShifter = p => p + GetNTV("docking").OrientationUnit.Value.Forward * (IsLargeGrid ? 1.25f : 0.5f),
			FwOverride = () => _u.docker.WorldMatrix,
			TranslationOverride = () => _u.docker.GetPosition(),
			TargetFeed = () => GetNTV("docking"),
			AutoSwitchToNext = false,
			ApproachVelocity = () => PC.Velocity,
			//FlyThrough = true, // fixes frav min3r docking
			DistanceHandler = (d, dx, c, wp, u) =>
			{
				if ((d < 20) && (c.AlignDelta.Length() < 0.8) && (_u.docker != null))
				{
					_u.docker.Connect();
					if (_u.docker.Status == MyShipConnectorStatus.Connected)
					{
						u.SetState(ApckState.Inert);
						_u.docker.OtherConnector.CustomData = "";
						c.RemCon.DampenersOverride = false;
					}
				}
			}
		};

		stateBehs[ApckState.Inert].OnEnter = s => unit.pc.SetState(PillockController.State.Inert);
		stateBehs[ApckState.Inert].OnExit = s => unit.pc.SetState(PillockController.State.WP);
	}

	public void SetBehavior(ApckState st, PcBehavior bh)
	{
		stateBehs[st].BH = bh;
	}

	public PcBehavior GetCurrentBeh()
	{
		return currentSt.BH;
	}

	public bool TrySetState(ApckState st)
	{
		if (st == currentSt.St)
			return true;
		var t = GetXwrapper(st);
		if (t != null)
		{
			var c = tConstraints.FirstOrDefault(x => x.Src.St == currentSt.St || x.Target.St == currentSt.St || x.Target.St == st || x.Src.St == st);
			if (c != null)
			{
				if (!(c.Src == currentSt && c.Target == t))
				{
					return false;
				}
			}

			var _s = currentSt;
			E.DebugLog($"{_s.St} -> {t.St}");
			currentSt = t;

			_s.OnExit?.Invoke(currentSt.St);
			c?.OnTransit?.Invoke();
			t.OnEnter?.Invoke(t.St);
			return true;
		}
		return false;
	}

	public XState GetXwrapper(ApckState st)
	{
		return stateBehs[st];
	}

	public XState GetCurrent()
	{
		return currentSt;
	}

	XState currentSt;
	List<XTrans> tConstraints = new List<XTrans>();

	public class XState
	{
		public ApckState St;
		public Action<ApckState> OnExit;
		public Action<ApckState> OnEnter;
		public PcBehavior BH;
		public XState(ApckState st, PcBehavior b, Action<ApckState> onExit = null, Action<ApckState> onEnter = null)
		{
			BH = b;
			St = st;
			OnExit = onExit;
			OnEnter = onEnter;
		}
	}

	public class XTrans
	{
		public XState Src;
		public XState Target;
		public Action OnTransit;
	}
}

public class PcBehavior
{
	public string Name = "Default";
	public bool AutoSwitchToNext = true;
	public Func<Vector3D, Vector3D> PositionShifter { get; set; }
	public Func<Vector3D, Vector3D> DestinationShifter { get; set; }
	public Func<Vector3D> SuggestedUpNorm { get; set; }
	public Action<double, double, PillockController, PcBehavior, APckUnit> DistanceHandler { get; set; }
	public Func<Vector3D> ApproachVelocity;
	public bool AllowFixedThrust = false;
	public Func<Vector3D, Vector3D> AimpointShifter = (tv) => tv;
	public Func<MatrixD> FwOverride;
	public Func<Vector3D> TranslationOverride;
	public bool fRoll;
	public float? SpeedLimit;
	public bool SelfVelocityAimCorrection = false;
	public bool FlyThrough = false;
	public bool IgnoreTFeed;
	public Func<TargetTelemetry> TargetFeed;
	public static PcBehavior FromGPS(Vector3D p, string name, Func<Vector3D> aim = null)
	{
		return new PcBehavior() { Name = name, IgnoreTFeed = true, PositionShifter = x => p, AimpointShifter = x => aim?.Invoke() ?? p };
	}
}


public class TimerTriggerService
{
	Dictionary<string, IMyTimerBlock> triggers = new Dictionary<string, IMyTimerBlock>();
	List<IMyTimerBlock> cached;
	public TimerTriggerService(List<IMyTimerBlock> triggers)
	{
		cached = triggers;
	}
	public bool TryTriggerNamedTimer(string name)
	{
		IMyTimerBlock b;
		if (!triggers.TryGetValue(name, out b))
		{
			b = cached.FirstOrDefault(c => c.CustomName.Contains(name));
			if (b != null)
				triggers.Add(name, b);
			else
				return false;
		}
		b.GetActionWithName("TriggerNow").Apply(b);
		return true;
	}
}


public class PillockController
{
	public enum State { Disabled = 0, Inert, WP }

	public bool VolThrust;

	State state = State.Disabled;
	public void SetState(State newState)
	{
		if (newState == State.WP)
			TakeControl();
		else if (newState == State.Inert)
			ReleaseControl(false);
		else if (newState == State.Disabled)
			ReleaseControl();
		state = newState;
	}

	public void TrySetState(string stateName)
	{
		State newState;
		if (Enum.TryParse(stateName, out newState))
			SetState(newState);
	}

	void TakeControl()
	{
		RemCon.DampenersOverride = false;
	}

	void ReleaseControl(bool damp = true)
	{
		forwardGyro.GyroOverride = false;
		_ts().Shutdown();
		RemCon.DampenersOverride = damp;
	}

	public PillockController(IMyRemoteControl remCon,
			TimerTriggerService timerTriggerService,
			IMyIntergridCommunicationSystem igc, IMyProgrammableBlock thrustProvider, IMyGyro fwGyro, IMyTerminalBlock antenna, Func<ThrusterSelector> ts,
			APckUnit au, bool volumetric)
	{
		RemCon = remCon;
		IGC = igc;
		forwardGyro = fwGyro;
		MainAntenna = antenna;
		TriggerService = timerTriggerService;
		tp = thrustProvider;
		_ts = ts;
		u = au;
		VolThrust = volumetric;
	}

	Vector3D prevCV;

	public string DBG;
	public Vector3D DeltaCV { get; private set; }
	public Vector3D AlignDelta { get; private set; }
	Vector3D prevAng = Vector3D.Zero;
	public double MisalignDot { get; private set; }
	public PcBehavior BH { get; private set; }
	public Vector3D Velocity { get { return RemCon.GetShipVelocities().LinearVelocity; } }
	Vector3D? nG;
	public Vector3D? NG { get { return (nG != Vector3D.Zero) ? nG : null; } }
	Vector3D mePrevVelocity { get; set; }
	public Vector3D meA { get; set; }
	public IMyRemoteControl RemCon;
	public TimerTriggerService TriggerService { get; private set; }
	IMyIntergridCommunicationSystem IGC;
	IMyProgrammableBlock tp;
	Func<ThrusterSelector> _ts;
	APckUnit u;
	int currentTick;
	IMyGyro forwardGyro;
	IMyTerminalBlock MainAntenna;
	public IMyTerminalBlock Fw { get { return forwardGyro; } }
	public Vector3D Destination;
	public Vector3D Pip;
	public Vector3D ThrustDest;
	public Vector3D PosShift;
	public Vector3D AimPoint;
	public void HandleControl(int tickCount, Action<string> echo, PcBehavior bh)
	{
		var elapsed = tickCount - currentTick;
		currentTick = tickCount;
		if (elapsed > 0)
			meA = (Velocity - mePrevVelocity) * 60f / elapsed;
		mePrevVelocity = Velocity;
		nG = RemCon.GetNaturalGravity();

		BH = bh;

		MyPlanetElevation dElevation = new MyPlanetElevation();
		double elevation;
		RemCon.TryGetPlanetElevation(dElevation, out elevation);
		Vector3D planetPos;
		RemCon.TryGetPlanetPosition(out planetPos);

		Func<Vector3D> approachVelocity = null; // for PIP
		bool fRoll = false;
		float? speedLimit = null;
		TargetTelemetry currentTargetVectors = null;

		switch (state)
		{
			case State.Disabled:
				return;
			case State.WP:
				try
				{
					var wp = bh;
					if (wp == null)
					{
						SetState(State.Disabled);
						return;
					}
					if (!VolThrust && !wp.AllowFixedThrust)
						return;

					var av = Vector3D.TransformNormal(RemCon.GetShipVelocities().AngularVelocity, MatrixD.Transpose(Fw.WorldMatrix));
					av = new Vector3D(Math.Abs(av.X), Math.Abs(av.Y), Math.Abs(av.Z));
					var anacc = (av - prevAng) / Dt;
					prevAng = av;

					approachVelocity = wp.ApproachVelocity;
					var aimpointShifter = wp.AimpointShifter;
					fRoll = wp.fRoll;
					speedLimit = wp.SpeedLimit;
					if (wp.TargetFeed != null)
						currentTargetVectors = wp.TargetFeed();

					if (wp.IgnoreTFeed || ((currentTargetVectors != null) && currentTargetVectors.Position.HasValue))
					{
						Vector3D point;
						Vector3D? targetVelocity = null;

						if ((currentTargetVectors != null) && (currentTargetVectors.Position.HasValue))
						{
							point = currentTargetVectors.Position.Value;
							if (targetVelocity.IsValid())
								targetVelocity = currentTargetVectors.Velocity;
							else
								E.DebugLog("Ivalid targetVelocity");
						}
						else
							point = Vector3D.Zero;

						if (wp.DestinationShifter != null)
							point = wp.DestinationShifter(point);

						var mePos = (wp.TranslationOverride != null) ? wp.TranslationOverride() : Fw.GetPosition();
						if ((approachVelocity != null) && (targetVelocity.HasValue) && (targetVelocity.Value.Length() > 0))
						{
							Vector3D targetCenter = point;
							Vector3D pp = VectorOpsHelper.GetPredictedImpactPoint(
								mePos,
								Velocity,
								targetCenter,
								targetVelocity.Value,
								approachVelocity(),
								wp.SelfVelocityAimCorrection
								);
							if ((point - pp).Length() < 2500)
							{
								point = pp;
							}
							Pip = pp;
						}
						Destination = point;
						if (wp.PositionShifter != null)
							PosShift = wp.PositionShifter(point);
						else
							PosShift = point;

						double origD = (point - mePos).Length();
						double shiftD = (PosShift - mePos).Length();

						if (DbgIgc != 0)
							DBG = $"origD: {origD:f1}\nshiftD: {shiftD:f1}";
						wp.DistanceHandler?.Invoke(origD, shiftD, this, wp, u);

						forwardGyro.GyroOverride = true;
						AimPoint = point;
						if (aimpointShifter != null)
							AimPoint = aimpointShifter(AimPoint);

						if ((Variables.Get<bool>("hold-thrust-on-rotation") && (prevCV.Length() > 1)) || Toggle.C.Check("suppress-transition-control"))
						{
							E.Echo($"prev cv: {prevCV.Length():f2} HOLD");
							CC(Fw.WorldMatrix.Translation, Fw.WorldMatrix.Translation, false, null, null, false);
						}
						else
						{
							E.Echo($"prev cv: {prevCV.Length():f2} OK");
							CC(mePos, PosShift, fRoll, targetVelocity, speedLimit, wp.FlyThrough);
						}

						var gridFov = (wp.FwOverride != null) ? wp.FwOverride() : Fw.WorldMatrix;
						if (!VolThrust && (ThrustDest != Fw.WorldMatrix.Translation))
							AimPoint = ThrustDest;

						Vector3D threeComponentCorrection = Vector3D.Zero;
						var toTarget = AimPoint - Fw.WorldMatrix.Translation;
						if (toTarget != Vector3D.Zero)
						{
							var ttN = Vector3D.Normalize(toTarget);
							var desM = MatrixD.CreateFromDir(ttN);
							Vector3D ctrlError = Vector3D.Zero;
							var up = wp.SuggestedUpNorm?.Invoke() ?? Vector3D.Zero;
							threeComponentCorrection = VectorOpsHelper.GetAnglesToPointMrot(ttN, gridFov, Fw.WorldMatrix, up, ref ctrlError);
							ctrlError.Z = 0;
							AlignDelta = ctrlError;
							DeltaCV = AlignDelta - prevCV;
							prevCV = AlignDelta;
							MisalignDot = Vector3D.Dot(ttN, gridFov.Forward);
						}

						if (!Toggle.C.Check("suppress-gyro-control"))
							VectorOpsHelper.SetOverride(forwardGyro, threeComponentCorrection, DeltaCV, prevAng);
					}
					else
					{
						forwardGyro.GyroOverride = false;
						if (Toggle.C.Check("damp-when-idle"))
							CC(Fw.WorldMatrix.Translation, Fw.WorldMatrix.Translation, false, null, 0, false);
						else
							CC(Fw.WorldMatrix.Translation, Fw.WorldMatrix.Translation, false, null, null, false);
					}
				}
				catch (Exception ex)
				{
					MainAntenna.CustomName += "HC Exception! See remcon cdata or PB screen";
					var r = RemCon;
					var e = $"HC EPIC FAIL\nNTV:{currentTargetVectors?.Name}\nBehavior:{BH.Name}\n{ex}";
					r.CustomData += e;
					E.DebugLog(e);
					SetState(State.Disabled);
					throw ex;
				}
				finally
				{
					E.EndOfTick();
				}
				break;
		}

	}

	void CC(Vector3D gridTrans, Vector3D interceptionPoint, bool fRoll, Vector3D? targetVel, float? speedLimit, bool flyThrough)
	{
		ThrustDest = interceptionPoint;
		if (state != State.WP)
			return;

		var pt = interceptionPoint;

		var zMatr = forwardGyro.WorldMatrix;
		zMatr.Translation = gridTrans;
		var toTarget = interceptionPoint - zMatr.Translation;

		var invMatrix = MatrixD.Transpose(zMatr);
		var localVel = Vector3D.TransformNormal(Velocity, invMatrix);
		var relativeVel = localVel;

		if (!VolThrust && (toTarget != Vector3D.Zero))
		{
			_ts().FacingBackward().SetPow(1f * Math.Max(0.2f, MisalignDot));
			if (Velocity != Vector3D.Zero)
				ThrustDest = Vector3D.Normalize(Vector3D.Reflect(Velocity, toTarget)) + gridTrans + Vector3D.Normalize(toTarget) * Velocity.Length() * 0.5f;
			return;
		}

		float mass = RemCon.CalculateShipMass().PhysicalMass;
		BoundingBoxD accCap = _ts().GetCapacityBB(mass);
		if (accCap.Volume == 0)
			return;

		Vector3D localGVector = Vector3D.Zero;
		if (NG != null)
		{
			localGVector = Vector3D.TransformNormal(NG.Value, invMatrix);
			accCap += -localGVector;
		}

		Vector3D dbgReject = Vector3D.Zero;
		Vector3D dbgTVbase = Vector3D.Zero;

		Vector3D overrideVector = new Vector3D();
		if (toTarget.Length() > double.Epsilon)
		{
			Vector3D zeroBasedTargetPoint = Vector3D.TransformNormal(toTarget, invMatrix);

			RayD rayToCenter = new RayD(-zeroBasedTargetPoint * (MaxAccelInProximity ? 1000 : 1), Vector3D.Normalize(zeroBasedTargetPoint));
			RayD rayToCenterInv = new RayD(zeroBasedTargetPoint * (MaxBrakeInProximity ? 1000 : 1), Vector3D.Normalize(-zeroBasedTargetPoint));

			var j = rayToCenterInv.Intersects(accCap);
			var i = rayToCenter.Intersects(accCap);

			if (!j.HasValue || !i.HasValue)
				throw new InvalidOperationException("Not enough thrust to compensate for gravity");

			var reversePoint = rayToCenterInv.Position + (Vector3D.Normalize(rayToCenterInv.Direction) * j.Value);
			var point = rayToCenter.Position + (Vector3D.Normalize(rayToCenter.Direction) * i.Value);

			var toOppositeTargetCapacity = reversePoint.Length();

			Vector3D reject = Vector3D.Reject(localVel, Vector3D.Normalize(zeroBasedTargetPoint));

			if (targetVel.HasValue)
			{
				var targetLocalVel = Vector3D.TransformNormal(targetVel.Value, invMatrix);
				relativeVel = localVel - targetLocalVel;
				reject = Vector3D.Reject(relativeVel, Vector3D.Normalize(zeroBasedTargetPoint));
			}
			else
			{
				relativeVel -= reject;
			}

			var relativeSpeed = Vector3D.Dot(relativeVel, Vector3D.Normalize(zeroBasedTargetPoint));

			bool closingDistance = relativeSpeed > 0;
			bool accelerate = true;

			var stoppingPathAtCurrentSpeed = Math.Pow(Math.Max(0, relativeSpeed), 2) / (2 * toOppositeTargetCapacity * StoppingPowerQuotient);
			var padding = toTarget.Length() - stoppingPathAtCurrentSpeed;

			if (DbgIgc != 0)
			{
				DBG += $"\nSTP: {stoppingPathAtCurrentSpeed:f2}\nRelSP: {relativeSpeed:f2}";
			}
			// TODO: how much capacity are we willing to give for reject compensation?
			if (closingDistance)
			{
				if (stoppingPathAtCurrentSpeed > toTarget.Length())
					accelerate = false;
				else if (MoreRejectDampening)
					reject /= Dt;
			}

			if (flyThrough || accelerate)
			{
				if (speedLimit.HasValue && (Vector3D.Dot(Vector3D.Normalize(toTarget), Velocity) >= speedLimit))
				{
					overrideVector = reversePoint;
					overrideVector *= (relativeSpeed - speedLimit.Value) / toOppositeTargetCapacity;
				}
				else
					overrideVector = point;
			}
			else
				overrideVector = reversePoint;

			if (accelerate)
			{
				var worldAprroachVel = Vector3D.Dot(Vector3D.Normalize(toTarget), Velocity);
				if (worldAprroachVel > MAX_SP - 0.001)
				{
					overrideVector = Vector3D.Zero;
				}
			}

			dbgTVbase = overrideVector;
			dbgReject = reject;
			if (reject.IsValid())
				overrideVector += reject;
		} // idle dampening. Dt atfter on-off-on wakeup -> inf
		else if (speedLimit.HasValue && (speedLimit == 0))
		{
			overrideVector += localVel / (MoreRejectDampening ? Dt : 1);
		}

		if (NG != null)
		{
			overrideVector += localGVector;
		}

		//E.DebugToPanel("applied V' vector: " + overrideVector.ToString("F3"));
		overrideVector -= UserCtrlTest.GetUserCtrlVector(Fw.WorldMatrix) * 1000;
		//E.DebugToPanel("user V' vector: " + UserCtrlTest.GetUserCtrlVector(Fw.WorldMatrix).ToString("F3"));

		if (overrideVector != Vector3D.Zero)
		{
			//overrideVector.Y *= -1;
			ThrustDest = Vector3D.TransformNormal(overrideVector, zMatr) + gridTrans;

			if (DbgIgc != 0)
			{
				var z = new List<MyTuple<Vector3D, Vector3D, Vector4>>();
				var c = Color.SeaGreen;
				c.A = 40;
				z.Add(new MyTuple<Vector3D, Vector3D, Vector4>(gridTrans, ThrustDest, c));

				var t = new MyTuple<string, Vector2, Vector3D, Vector3D, float, string>("Circle", Vector2.One * 4, ThrustDest, Vector3D.Zero, 1f,
						overrideVector.Length().ToString("f2"));
				IGC.SendUnicastMessage(DbgIgc, "draw-projection", t);

				c = Color.Blue;
				c.A = 40;
				z.Add(new MyTuple<Vector3D, Vector3D, Vector4>(gridTrans, Vector3D.TransformNormal(dbgReject, zMatr) + gridTrans, c));
				c = Color.Red;
				c.A = 40;
				z.Add(new MyTuple<Vector3D, Vector3D, Vector4>(gridTrans, Vector3D.TransformNormal(dbgTVbase, zMatr) + gridTrans, c));

				var rtc = new RayD(overrideVector * 1000, Vector3D.Normalize(-overrideVector));
				var i = rtc.Intersects(accCap);
				if (i.HasValue)
				{
					var thCap = rtc.Position + (Vector3D.Normalize(rtc.Direction) * i.Value);
					var pos = Vector3D.TransformNormal(thCap, zMatr) + gridTrans;
					var pr = new MyTuple<string, Vector2, Vector3D, Vector3D, float, string>("Circle", Vector2.One * 4, pos, Vector3D.Zero, 1f, thCap.Length().ToString("f2"));
					IGC.SendUnicastMessage(DbgIgc, "draw-projection", pr);
				}

				var rtc2 = new RayD(-overrideVector * 1000, Vector3D.Normalize(overrideVector));
				var i2 = rtc2.Intersects(accCap);
				if (i2.HasValue)
				{
					var thCap = rtc2.Position + (Vector3D.Normalize(rtc2.Direction) * i2.Value);
					var pos = Vector3D.TransformNormal(thCap, zMatr) + gridTrans;
					var pr = new MyTuple<string, Vector2, Vector3D, Vector3D, float, string>("Circle", Vector2.One * 4, pos, Vector3D.Zero, 1f, thCap.Length().ToString("f2"));
					IGC.SendUnicastMessage(DbgIgc, "draw-projection", pr);
				}

				IGC.SendUnicastMessage(DbgIgc, "draw-lines", z.ToImmutableArray());
			}
		}

		overrideVector.Y *= -1;
		//E.Echo($"OVR: {overrideVector.X:f2}:{overrideVector.Y:f2}:{overrideVector.Z:f2}");
		_ts().SetOverride(overrideVector, mass);
	}


	public Vector3D CreateFromFwDir(float meters)
	{
		return Fw.GetPosition() + Fw.WorldMatrix.Forward * meters;
	}
}


TargetTelemetry GetNTV(string key)
{
	TargetTelemetry r;
	if (NamedTeleData.TryGetValue(key, out r))
		return r;
	throw new InvalidOperationException("No TV named " + key);
}
void UpdateNTV(string key, MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> dto)
{
	NamedTeleData[key].ParseIgc(dto, TickCount);
}
void CheckExpireNTV()
{
	foreach (var x in NamedTeleData.Values.Where(v => v.Position.HasValue))
	{
		E.Echo(x.Name + ((x.Position.Value == Vector3D.Zero) ? " Zero!" : " OK"));
		x.CheckExpiration(TickCount);
	}
}

Dictionary<string, TargetTelemetry> NamedTeleData = new Dictionary<string, TargetTelemetry>(); ///< Live telemetry about the (potentially moving) docking port at the base.

public struct TeleDto
{
	public Vector3D? pos;
	public Vector3D? vel;
	public MatrixD? rot;
	public BoundingBoxD? bb;
	public MyDetectedEntityType? type;
}

public class TargetTelemetry
{
	public long TickStamp;
	int clock;
	public string Name;
	public long EntityId;
	public Vector3D? Position { get; private set; }
	public Vector3D? Velocity;
	public Vector3D? Acceleration;
	public MatrixD? OrientationUnit;
	public BoundingBoxD? BoundingBox;
	public int? ExpiresAfterTicks = 60;
	public MyDetectedEntityType? Type { get; set; }
	public delegate void InvalidatedHandler();
	public event InvalidatedHandler OnInvalidated;
	public TargetTelemetry(int clock, string name)
	{
		this.clock = clock;
		Name = name;
	}
	public void SetPosition(Vector3D pos, long tickStamp)
	{
		Position = pos;
		TickStamp = tickStamp;
	}
	public void CheckExpiration(int locTick)
	{
		if ((TickStamp != 0) && ExpiresAfterTicks.HasValue && (locTick - TickStamp > ExpiresAfterTicks.Value))
			Invalidate();
	}
	public void PredictPostion(int tick, int clock)
	{
		if ((Velocity.HasValue) && (Velocity.Value.Length() > double.Epsilon) && (tick - TickStamp) > 0)
		{
			Position += Velocity * (tick - TickStamp) * clock / 60;
		}
	}
	/////////
	public enum TeleMetaFlags : byte
	{
		HasVelocity = 1,
		HasOrientation = 2,
		HasBB = 4
	}

	bool HasFlag(TeleMetaFlags packed, TeleMetaFlags flag)
	{
		return (packed & flag) == flag;
	}

	public void ParseIgc(MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> igcDto, int localTick)
	{
		var meta = igcDto.Item1;
		EntityId = meta.Item2;
		//var tickStamp = meta.Item3;
		Type = (MyDetectedEntityType)meta.Item4;
		TeleMetaFlags tm = (TeleMetaFlags)meta.Item5;
		SetPosition(igcDto.Item2, localTick);
		if (HasFlag(tm, TeleMetaFlags.HasVelocity))
		{
			var newVel = igcDto.Item3;
			if (!Velocity.HasValue)
				Velocity = newVel;
			Acceleration = (newVel - Velocity.Value) * 60 / clock; // redo ffs
			Velocity = newVel;
		}
		if (HasFlag(tm, TeleMetaFlags.HasOrientation))
			OrientationUnit = igcDto.Item4;
		if (HasFlag(tm, TeleMetaFlags.HasBB))
			BoundingBox = igcDto.Item5;
	}

	public static TargetTelemetry FromIgc(MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> igcDto,
			Func<string[], TeleDto> parser, int localTick)
	{
		var t = new TargetTelemetry(1, igcDto.Item1.Item1);
		t.ParseIgc(igcDto, localTick);
		return t;
	}

	public MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> GetIgcDto()
	{
		var mask = 0 | (Velocity.HasValue ? 1 : 0) | (OrientationUnit.HasValue ? 2 : 0) | (BoundingBox.HasValue ? 4 : 0);
		var x = new MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>(
				new MyTuple<string, long, long, byte, byte>(Name, EntityId, DateTime.Now.Ticks, (byte)MyDetectedEntityType.LargeGrid, (byte)mask),
				Position.Value,
				Velocity ?? Vector3D.Zero,
				OrientationUnit ?? MatrixD.Identity,
				BoundingBox ?? new BoundingBoxD()
			);
		return x;
	}

	/////////
	public void Invalidate()
	{
		Position = null;
		Velocity = null;
		OrientationUnit = null;
		BoundingBox = null;
		var tmp = OnInvalidated;
		if (tmp != null)
			OnInvalidated();
	}
}


public class ThrusterSelector
{
	//public IMyGridTerminalSystem gts;

	List<IAccelerator> current;

	List<IAccelerator> _b;
	List<IAccelerator> _f;
	List<IAccelerator> _v;
	List<IAccelerator> _d;
	List<IAccelerator> _l;
	List<IAccelerator> _r;

	public double[] caps = new double[6];

	bool released;
	public void Shutdown()
	{
		if (!released)
		{
			_b.ForEach(a => a.Free());
			_f.ForEach(a => a.Free());
			_v.ForEach(a => a.Free());
			_d.ForEach(a => a.Free());
			_l.ForEach(a => a.Free());
			_r.ForEach(a => a.Free());
			released = true;
		}
	}
	public BoundingBoxD GetCapacityBB(float mass)
	{
		Vector3D min = new Vector3D(-caps[5], -caps[3], -caps[1]) / mass;
		Vector3D max = new Vector3D(caps[4], caps[2], caps[0]) / mass;

		return new BoundingBoxD(min, max);
	}
	public void CalculateForces()
	{
		caps[0] = FacingBackward().TotalForce();
		caps[1] = FacingForward().TotalForce();
		caps[2] = FacingVentral().TotalForce();
		caps[3] = FacingDorsal().TotalForce();
		caps[4] = FacingRight().TotalForce();
		caps[5] = FacingLeft().TotalForce();
	}
	public ThrusterSelector(IMyTerminalBlock forwardFacingBlock, List<IMyTerminalBlock> hw)
	{
		//this.gts = gts;
		MatrixD wm = forwardFacingBlock.WorldMatrix;

		Func<Vector3D, List<IAccelerator>> getT = fw =>
		{
			var r = hw.Where(b => b is IMyThrust && fw == b.WorldMatrix.Forward).Select(x => x as IMyThrust).ToList();
			return r.Select(t => new ThrusterAccelerator(t)).Cast<IAccelerator>().ToList();
		};

		_b = getT(wm.Backward);
		_f = getT(wm.Forward);
		_v = getT(wm.Down);
		_d = getT(wm.Up);
		_l = getT(wm.Left);
		_r = getT(wm.Right);

		var mms = hw.Where(b => b is IMyArtificialMassBlock).Cast<IMyArtificialMassBlock>().ToList();

		var ggs = hw.Where(b => b is IMyGravityGenerator).Cast<IMyGravityGenerator>().ToList();

		Func<Vector3D, bool, List<IAccelerator>> addGg = (fw, inv) =>
		{
			var g = ggs.Where(b => fw == b.WorldMatrix.Up);
			return g.Select(p => new GravPairAccelerator(p, mms, inv)).Cast<IAccelerator>().ToList();
		};

		_b.AddRange(addGg(wm.Forward, true));
		_f.AddRange(addGg(wm.Forward, false));

		_b.AddRange(addGg(wm.Backward, false));
		_f.AddRange(addGg(wm.Backward, true));

		_v.AddRange(addGg(wm.Up, true));
		_d.AddRange(addGg(wm.Up, false));

		_v.AddRange(addGg(wm.Down, false));
		_d.AddRange(addGg(wm.Down, true));

		_l.AddRange(addGg(wm.Right, true));
		_r.AddRange(addGg(wm.Right, false));

		_l.AddRange(addGg(wm.Left, false));
		_r.AddRange(addGg(wm.Left, true));

		CalculateForces();
	}

	public ThrusterSelector FacingBackward()
	{
		current = _b;
		return this;
	}
	public ThrusterSelector FacingForward()
	{
		current = _f;
		return this;
	}
	public ThrusterSelector FacingVentral()
	{
		current = _v;
		return this;
	}
	public ThrusterSelector FacingDorsal()
	{
		current = _d;
		return this;
	}
	public ThrusterSelector FacingLeft()
	{
		current = _l;
		return this;
	}
	public ThrusterSelector FacingRight()
	{
		current = _r;
		return this;
	}
	public void SetOverride(Vector3D v, float mass)
	{
		released = false;
		Func<IAccelerator, bool> filter = a => !(a is GravPairAccelerator);

		FacingForward().SetPow(-v.Z / caps[1] * mass);
		FacingBackward().SetPow(v.Z / caps[0] * mass);

		FacingDorsal().SetPow(-v.Y / caps[3] * mass);
		FacingVentral().SetPow(v.Y / caps[2] * mass);

		FacingLeft().SetPow(-v.X / caps[5] * mass);
		FacingRight().SetPow(v.X / caps[4] * mass);
	}
	public bool SetPow(double q, Func<IAccelerator, bool> filter = null)
	{
		if (current != null)
		{
			q = Math.Min(1, Math.Abs(q)) * Math.Sign(q);
			foreach (var accelerator in filter == null ? current : current.Where(filter))
			{
				accelerator.SetPow(q);
			}
		}
		current = null;
		return true;
	}
	public float TotalForce()
	{
		float result = 0;
		if (current != null)
		{
			foreach (var accelerator in current)
			{
				result += accelerator.EffectiveForce();
			}
		}
		current = null;
		return result;
	}
}
class GravPairAccelerator : IAccelerator
{
	IMyGravityGenerator g;
	List<IMyArtificialMassBlock> mss;
	bool negative;
	public GravPairAccelerator(IMyGravityGenerator g, List<IMyArtificialMassBlock> mss, bool negative)
	{
		this.g = g;
		this.mss = mss;
		this.negative = negative;
	}

	public void SetPow(double powerQuotient)
	{
		if (powerQuotient >= 0)
			g.GravityAcceleration = (float)(negative ? -powerQuotient : powerQuotient) * G;
	}

	public void Free()
	{
		g.GravityAcceleration = 0;
	}

	public float EffectiveForce()
	{
		return mss.Count * 50000 * G;
	}
}
class ThrusterAccelerator : IAccelerator
{
			IMyThrust t;
	public ThrusterAccelerator(IMyThrust t)
	{
		this.t = t;
	}
	public void SetPow(double powerQuotient)
	{
		if (powerQuotient <= 0)
			t.ThrustOverride = 0.00000001f;
		else
			t.ThrustOverride = (float)powerQuotient * t.MaxThrust;
	}
	public void Free()
	{
		t.ThrustOverride = 0;
		t.Enabled = true;
	}
	public float EffectiveForce()
	{
		return t.MaxEffectiveThrust;
	}
}
public interface IAccelerator
{
	void SetPow(double powerQuotient);
	float EffectiveForce();
	void Free();
}

static class UserCtrlTest
{
	public static List<IMyShipController> ctrls;
	public static void Init(List<IMyShipController> c)
	{
		if (ctrls == null)
			ctrls = c;
	}
	public static Vector3 GetUserCtrlVector(MatrixD fwRef)
	{
		Vector3 res = new Vector3();
		if (Toggle.C.Check("ignore-user-thruster"))
			return res;
		var c = ctrls.Where(x => x.IsUnderControl).FirstOrDefault();
		if (c != null && (c.MoveIndicator != Vector3.Zero))
			return Vector3D.TransformNormal(c.MoveIndicator, fwRef * MatrixD.Transpose(c.WorldMatrix));
		return res;
	}
}


public class APckTask
{
	public string Name;

	public Vector3D? Pos;
	public Func<Vector3D> TPos;

	double? proxLim;
	public int? TickLimit;

	public ApckState PState = ApckState.CwpTask;
	public PcBehavior BH;
	public Action OnComplete;

	int startTick;
	public ApckState src;
	public APckTask(string name, PcBehavior bh, int? tlim = null)
	{
		BH = bh;
		Name = name;
		TickLimit = tlim;
	}
	public APckTask(string name, ApckState st, int? tlim = null)
	{
		PState = st;
		Name = name;
		TickLimit = tlim;
	}

	public void Init(PillockController pc, int tick)
	{
		if (startTick == 0)
			startTick = tick;
		pc.TriggerService.TryTriggerNamedTimer(Name + ".OnStart");
	}

	public static APckTask CreateGPS(string name, Vector3D p, PcBehavior bh)
	{
		var t = new APckTask(name, bh);
		t.proxLim = 0.5;
		t.Pos = p;
		return t;
	}
	public static APckTask CreateRelP(string name, Func<Vector3D> pf, PcBehavior bh)
	{
		var t = new APckTask(name, bh);
		t.proxLim = 0.5;
		t.TPos = pf;
		return t;
	}
	public bool CheckCompl(int tick, PillockController pc)
	{
		if (TickLimit.HasValue && (tick - startTick > TickLimit))
		{
			return true;
		}
		if (proxLim.HasValue)
		{
			Vector3D p;
			var _p = BH.TranslationOverride?.Invoke() ?? pc.Fw.GetPosition();
			if (TPos != null)
				p = TPos();
			else
				p = Pos.Value;
			if ((_p - p).Length() < proxLim)
				return true;
		}
		return false;
	}
}


APckTask tempWp;
void CreateWP(string[] parts)
{
	//command:create-wp:Name=dr,Ng=Down,PosDirectionOverride=Forward,SpeedLimit=50:0:0:0
	FinalizeWP();

	var cu = coreUnit;
	var PC = cu.pc;

	var posCap = PC.Fw.GetPosition();
	var values = parts[2].Split(',').ToDictionary(s => s.Split('=')[0], s => s.Split('=')[1]);

	var bh = new PcBehavior() { Name = "Deserialized Behavior", IgnoreTFeed = true, AutoSwitchToNext = false };
	tempWp = new APckTask("twp", bh);

	float _d = 1;

	var vdtoArr = parts.Take(6).Skip(1).ToArray();
	var pos = new Vector3D(double.Parse(vdtoArr[2]), double.Parse(vdtoArr[3]), double.Parse(vdtoArr[4]));

	Func<Vector3D, Vector3D> sh = p => pos;
	tempWp.Pos = pos;

	Vector3D? n = null;
	if (values.ContainsKey("AimNormal"))
	{
		var v = values["AimNormal"].Split(';');
		n = new Vector3D(double.Parse(v[0]), double.Parse(v[1]), double.Parse(v[2]));
	}
	if (values.ContainsKey("UpNormal"))
	{
		var v = values["UpNormal"].Split(';');
		var up = Vector3D.Normalize(new Vector3D(double.Parse(v[0]), double.Parse(v[1]), double.Parse(v[2])));
		bh.SuggestedUpNorm = () => up;
	}

	if (values.ContainsKey("Name"))
		tempWp.Name = values["Name"];
	if (values.ContainsKey("FlyThrough"))
		bh.FlyThrough = true;
	if (values.ContainsKey("SpeedLimit"))
		bh.SpeedLimit = float.Parse(values["SpeedLimit"]);
	if (values.ContainsKey("TriggerDistance"))
		_d = float.Parse(values["TriggerDistance"]);
	if (values.ContainsKey("PosDirectionOverride") && (values["PosDirectionOverride"] == "Forward"))
	{
		if (n.HasValue)
		{
			sh = p => posCap + n.Value * ((PC.Fw.GetPosition() - posCap).Length() + 5);
		}
		else
			sh = p => PC.CreateFromFwDir(50000);
	}

	if (parts.Length > 6)
	{
		bh.DistanceHandler = (d, sh_d, pc, wp, u) =>
		{
			if (sh_d < _d)
			{
				FinalizeWP();
				minerController.ApckRegistry.RunCommand(parts[7], parts.Skip(6).ToArray());
			}
		};
	}

	if (values.ContainsKey("Ng"))
	{
		Func<MatrixD> fw = () => PC.Fw.WorldMatrix;
		if (values["Ng"] == "Down")
			bh.FwOverride = () => MatrixD.CreateFromDir(PC.Fw.WorldMatrix.Down, PC.Fw.WorldMatrix.Forward);
		if (cu.UnderPlanetInfl() && !values.ContainsKey("IgG"))
		{
			bh.AimpointShifter = p => cu.plCenter.Value;
			bh.PositionShifter = p => Vector3D.Normalize(sh(p) - cu.plCenter.Value) * (posCap - cu.plCenter.Value).Length() + cu.plCenter.Value;
		}
		else
		{
			if (n.HasValue)
			{
				bh.AimpointShifter = p => PC.Fw.GetPosition() + n.Value * 1000;
			}
			else
				bh.AimpointShifter = p => PC.Fw.GetPosition() + (bh.FwOverride ?? fw)().Forward * 1000;
		}
	}

	if (values.ContainsKey("TransformChannel"))
	{
		Func<Vector3D, Vector3D> trans = p => Vector3D.Transform(pos, GetNTV(values["TransformChannel"]).OrientationUnit.Value);
		bh.DestinationShifter = trans;
		tempWp.TPos = () => Vector3D.Transform(pos, GetNTV(values["TransformChannel"]).OrientationUnit.Value);
		bh.SelfVelocityAimCorrection = true;
		bh.TargetFeed = () => GetNTV(values["TransformChannel"]);
		bh.ApproachVelocity = () => PC.Velocity;
		bh.PositionShifter = null;
	}
	else
		bh.PositionShifter = sh;

	tempWp.PState = ApckState.CwpTask;
	cu.BindBeh(tempWp.PState, bh);

	cu.InsertTaskBefore(tempWp);

	//cu.WpMgr.AddWaypoint(tempWp);
	//cu.WpMgr.ForceSpecificWp(tempWp.Name);
}

void FinalizeWP()
{
	if (tempWp != null)
	{
		if (coreUnit.GetCurrTask() == tempWp)
			coreUnit.ForceNext();
		tempWp = null;
	}
}


/**
 * \brief Transponder message, to be broadcasted by an agent.
 * \details This message informs ATC about the status of the agent, used for
 * collaborative airspace control. Also, it can be used by the dispatcher for
 * progress monitoring.
 */
public class TransponderMsg
{
	public long       Id;         ///< Entity ID of the agent's PB.
	public string     name;       ///< Grid name of the agent.
	public MatrixD    WM;         ///< World matrix of the agent.
	public Vector3D   v;          ///< [m/s] Velocity of the agent.
	public float      f_bat;      ///< [-] Battery charge in [0;1].
	public float      f_bat_min;  ///< [-] Minimum operational charge.
	public float      f_fuel;     ///< [-] Fuel level in [0;1].
	public float      f_fuel_min; ///< [-] Minimum operational level.
	public string     damage;     ///< Name of damaged block, if exists.
	public MinerState state;      ///< Current state of the agent.
	public float      f_cargo;    ///< [-] Cargo fullness in [0;1].
	public float      f_cargo_max;///< [-] Threshold for returning to base.
	public bool       bAdaptive;  ///< Is the adaptive mode active?
	public bool       bRecalled;  ///< Has the agent been recalled?
	public float      t_shaft;    ///< [m] Current depth in shaft.
	public float      t_ore;      ///< [m] Depth at which ore has been found.
	public bool       bUnload;    ///< Is the agent unloading cargo?
	public ImmutableArray<MyTuple<string, string>> KeyValuePairs;

	// Note: Commented out, because only required by the receiver of this datagram.
	//public void UpdateFromIgc(MyTuple<
	//	MyTuple<long, string>,       // Id, name
	//	MyTuple<MatrixD, Vector3D>,  // WM, v
	//	MyTuple<byte, string, bool>, // state, damage, bUnload
	//	ImmutableArray<float>,       // f_bat, f_bat_min, f_fuel, f_fuel_min, f_cargo, f_cargo_max
	//	MyTuple<bool, bool, float, float>, // bAdaptive, bRecalled, t_shaft, t_ore
	//	ImmutableArray<MyTuple<string, string>>
	//> dto)
	//{
	//	Id            = dto.Item1.Item1;
	//	name          = dto.Item1.Item2;
	//	WM            = dto.Item2.Item1;
	//	v             = dto.Item2.Item2;
	//	state         = (MinerState)dto.Item3.Item1;
	//	damage        = dto.Item3.Item2;
	//	bUnload       = dto.Item3.Item3;
	//	f_bat         = dto.Item4[0];
	//	f_bat_min     = dto.Item4[1];
	//	f_fuel        = dto.Item4[2];
	//	f_fuel_min    = dto.Item4[3];
	//	f_cargo       = dto.Item4[4];
	//	f_cargo_max   = dto.Item4[5];
	//	bAdaptive     = dto.Item5.Item1;
	//	bRecalled     = dto.Item5.Item2;
	//	t_shaft       = dto.Item5.Item3;
	//	t_ore         = dto.Item5.Item4;
	//	KeyValuePairs = dto.Item6;
	//}

	public MyTuple<
		MyTuple<long, string>,       // Id, name
		MyTuple<MatrixD, Vector3D>,  // WM, v
		MyTuple<byte, string, bool>, // state, damage, bUnload
		ImmutableArray<float>,       // f_bat, f_bat_min, f_fuel, f_fuel_min, f_cargo, f_cargo_max
		MyTuple<bool, bool, float, float>, // bAdaptive, bRecalled, t_shaft, t_ore
		ImmutableArray<MyTuple<string, string>>
	> ToIgc()
	{
		var dto = new MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>();
		dto.Item1.Item1 = Id;
		dto.Item1.Item2 = name;
		dto.Item2.Item1 = WM;
		dto.Item2.Item2 = v;
		dto.Item3.Item1 = (byte)state;
		dto.Item3.Item2 = damage;
		dto.Item3.Item3 = bUnload;
		var arr = ImmutableArray.CreateBuilder<float>(6);
		arr.Add(f_bat);
		arr.Add(f_bat_min);
		arr.Add(f_fuel);
		arr.Add(f_fuel_min);
		arr.Add(f_cargo);
		arr.Add(f_cargo_max);
		dto.Item4 = arr.ToImmutableArray();
		dto.Item5.Item1 = bAdaptive;
		dto.Item5.Item2 = bRecalled;
		dto.Item5.Item3 = t_shaft;
		dto.Item5.Item4 = t_ore;
		dto.Item6 = KeyValuePairs;
		return dto;
	}
}

List<MyTuple<string, Vector3D, ImmutableArray<string>>> prjs = new List<MyTuple<string, Vector3D, ImmutableArray<string>>>();
void EmitProjection(string tag, Vector3D p, params string[] s)
{
	prjs.Add(new MyTuple<string, Vector3D, ImmutableArray<string>>(tag, p, s.ToImmutableArray()));
}
void EmitFlush(long addr)
{
	IGC.SendUnicastMessage(addr, "hud.apck.proj", prjs.ToImmutableArray());
	prjs.Clear();
}

} // class Program

} // namespace IngameScript
