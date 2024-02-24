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
const string Ver = "0.9.68"; // Must be the same on dispatcher and agents.

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

Action<IMyTextPanel> outputPanelInitializer = x =>
{
	x.ContentType = ContentType.TEXT_AND_IMAGE;
};


/** \brief The configuration state of the PB. */
static class Variables
{
	static Dictionary<string, object> v = new Dictionary<string, object> {
		{ "depth-limit", new Variable<float> { value = 80, parser = s => float.Parse(s) } },
		{ "max-generations", new Variable<int> { value = 7, parser = s => int.Parse(s) } },
		{ "circular-pattern-shaft-radius", new Variable<float> { value = 3.6f, parser = s => float.Parse(s) } },
		{ "echelon-offset", new Variable<float> { value = 12f, parser = s => float.Parse(s) } },
		{ "getAbove-altitude", new Variable<float> { value = 20, parser = s => float.Parse(s) } },
		{ "skip-depth", new Variable<float> { value = 0, parser = s => float.Parse(s) } },
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

bool pendingInitSequence; // Do first-cycle initialsation?
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


/**
 * \brief Logging facility.
 */
public static class E
{
	public enum LogLevel {
		Critical = 0, ///< Something went wrong.
		Warning  = 1, ///< Something is odd.
		Notice   = 2, ///< Something the operator should be aware of.
		Debug    = 3  ///< Maximum verbosity.
	}

	static string debugTag = "";
	static Action<string> echoClbk;
	static IMyTextSurface p;
	static IMyTextSurface lcd;                             ///< The LCD screen used for displaying the log messages.
	public static double  simT;                            ///< [s] Simulation elapsed time.
	static List<string>   linesToLog = new List<string>(); ///< List of all log messages, which have not yet been written to the LCD screen.
	static LogLevel       filterLevel = LogLevel.Notice;   ///< All message with a higher log level are filtered out.

	/**
	 * \brief Initializes the logging modle.
	 * \note To be called in the script's constructor, as early as possible.
	 * \param[out] echo A callback function, which will receive messages sent to the "Echo"
	 * method. (Used for debugging output only.) 
	 */
	public static void Init(Action<string> echo, IMyGridTerminalSystem g, IMyProgrammableBlock me)
	{
		echoClbk = echo;
		p = me.GetSurface(0);
		p.ContentType = ContentType.TEXT_AND_IMAGE;
		p.WriteText("");

		if (!Enum.TryParse(logLevel, out filterLevel))
			Log("Invalid log-level: \"" + logLevel + "\". Defaulting to \"Notice\".", LogLevel.Notice);
	}

	public static void Echo(string s) {
		if ((debugTag == "") || (s.Contains(debugTag)))
			echoClbk(s);
	}
	
	/** 
	 * \brief Writes a message to the log.
	 * \details The message is automatically timestamped.
	 * \param[in] msg The message to be logged.
	 * \param[in] lvl The severity of the message.
	 */
	public static void Log(string msg, LogLevel lvl = LogLevel.Notice)
	{
		if (lvl > filterLevel)
			return; // Ignore message.
		p.WriteText($"{simT:f1}: {msg}\n", true);
		linesToLog.Add($"{simT:f1}: {msg}");
	}

	/** \brief Clears the log, including the LCD screen. */
	public static void ClearLog() {
		lcd?.WriteText("");
		linesToLog.Clear();
	}

	/** \brief Sets the LCD display for logging mesages. */
	public static void SetLCD(IMyTextSurface s) {
		lcd = s;
		/* Set some parameters of the LCD panel. */
		lcd.ContentType = ContentType.TEXT_AND_IMAGE;
		lcd.FontColor = new Color(r: 0, g: 255, b: 116);
		lcd.Font = "Monospace"; // Time stamps will always have the same length.
		lcd.FontSize = 0.65f;
		/* Clear LCD contents. */
		lcd.WriteText("");
	}

	public static void EndOfTick() {
		if (linesToLog.Any())
		{
			if (lcd != null)
			{
				linesToLog.Reverse();
				var t = string.Join("\n", linesToLog) + "\n" + lcd.GetText();
				var u = Variables.Get<int>("logger-char-limit");
				if (t.Length > u)
					t = t.Substring(0, u - 1);
				lcd.WriteText(t);
			}
			if (simT > 5) // Don't drop messages of early initialisation.
				linesToLog.Clear();
		}
	}
}


/** \brief Abbreviation for E.Log(). */
public static void Log(string msg, E.LogLevel lvl = E.LogLevel.Notice)
{
	E.Log(msg, lvl);
}


static int TickCount;

/**
 * \brief Processes commands.
 */
void StartOfTick(string arg)
{
	/* On first cycle, load initialiation script from custom data. */
	if (pendingInitSequence && string.IsNullOrEmpty(arg))
	{
		pendingInitSequence = false;
		
		CreateRole("Dispatcher"); //TODO: Resolve

		arg = string.Join(",", Me.CustomData.Trim('\n').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith("//"))
				.Select(s => "[" + s + "]"));
	}

	if (string.IsNullOrEmpty(arg) || !arg.Contains(":"))
		return; // No commands to process.
			
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

void EndOfTick()
{
	Scheduler.C.HandleTick();
	E.EndOfTick();
}

/**
 * \brief Initialisation of the PB.
 */
void Ctor()
{
	if (!string.IsNullOrEmpty(Me.CustomData))
		pendingInitSequence = true;

	/* Initialise logging subsystem first. */
	E.Init(Echo, GridTerminalSystem, Me);

	/* Initialise the toggles. */
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
					//var cd = minerController?.fwReferenceBlock;
					//if (cd != null)
					//	cd.CustomData = "";
					break;
			}
		}
	);

	NamedTeleData.Add("docking", new TargetTelemetry(1, "docking"));

	/* Load persistent state. */
	stateWrapper = new StateWrapper(s => Storage = s);
	if (!stateWrapper.TryLoad(Storage))
	{
		E.Echo("State load failed, clearing Storage now");
		stateWrapper.Save();
		Runtime.UpdateFrequency = UpdateFrequency.None;
	} else {
		/* Persistent state successfully loaded. */
		var p = (IMyTextPanel)GridTerminalSystem.GetBlockWithId(stateWrapper.PState.logLCD);
		if (p != null)
			E.SetLCD(p);
	}

	GridTerminalSystem.GetBlocksOfType(cameras, c => c.IsSameConstructAs(Me));
	cameras.ForEach(c => c.EnableRaycast = true);

	IsLargeGrid = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;

	/* Initialise the command handlers. */
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
							E.Log($"Added {p.CustomName} as GUI panel");
							outputPanelInitializer(p);
							rawPanel = p;
						}
					}
				},
				{
					"add-gui-controller", (parts) => {
						List<IMyShipController> b = new List<IMyShipController>();
						GridTerminalSystem.GetBlocksOfType(b, x => x.IsSameConstructAs(Me) && x.CustomName.Contains(parts[2]));
						guiSeat = b.FirstOrDefault();
						if (guiSeat != null)
							E.Log($"Added {guiSeat.CustomName} as GUI controller");
					}
				},
				{
					"add-logger", (parts) => {
						List<IMyTextPanel> b = new List<IMyTextPanel>();
						GridTerminalSystem.GetBlocksOfType(b, x => x.IsSameConstructAs(Me) && x.CustomName.Contains(parts[2]));
						var p = b.FirstOrDefault();
						if (p == null)
							return; // LCD panel not found.
						E.SetLCD(p);
						E.Log("Set logging LCD screen to: " + p.CustomName, E.LogLevel.Notice);
						stateWrapper.PState.logLCD = p.EntityId;
					}
				},
				{
					"set-role", (parts) => Log("command:set-role is deprecated.", E.LogLevel.Warning)
				},
				{
					"low-update-rate", (parts) => Runtime.UpdateFrequency = UpdateFrequency.Update10
				},
				{
					"create-task-raycast", (parts) => RaycastTaskHandler(parts)
				},
				{
					"create-task-gps", (parts) => GPStaskHandler(parts)
				},
				{
					"recall", (parts) => dispatcherService?.Recall()
				},
				{
					"clear-storage-state", (parts) => stateWrapper?.ClearPersistentState()
				},
				{
					"save", (parts) => stateWrapper?.Save()
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
}

void CreateRole(string role)
{
	Role newRole;
	if (!Enum.TryParse(role, out newRole))
		throw new Exception("Failed to parse role in command:set-role.");

	CurrentRole = newRole;
	E.Log("Assigned role: " + newRole);

	if (newRole == Role.Dispatcher)
	{
		dispatcherService = new Dispatcher(IGC, stateWrapper);

		/* Find all assigned docking ports ("docka-min3r"). */
		var dockingPoints = new List<IMyShipConnector>();
		GridTerminalSystem.GetBlocksOfType(dockingPoints, c => c.IsSameConstructAs(Me) && c.CustomName.Contains(DockHostTag));
		if (ClearDocksOnReload)
			dockingPoints.ForEach(d => d.CustomData = "");

		/* Create a docking port manager. */
		dockHost = new DockHost(dispatcherService, dockingPoints, GridTerminalSystem);

		if (stateWrapper.PState.ShaftStates.Count > 0)
		{
			var cap = stateWrapper.PState.ShaftStates;
			dispatcherService.CreateTask(stateWrapper.PState.shaftRadius.Value, stateWrapper.PState.corePoint.Value,
					stateWrapper.PState.miningPlaneNormal.Value, stateWrapper.PState.MaxGenerations, stateWrapper.PState.CurrentTaskGroup);
			for (int n = 0; n < dispatcherService.CurrentTask.Shafts.Count; n++)
			{
				dispatcherService.CurrentTask.Shafts[n].State = (ShaftState)cap[n];
			}
			stateWrapper.PState.ShaftStates = dispatcherService.CurrentTask.Shafts.Select(x => (byte)x.State).ToList();
			E.Log($"Restored task from pstate, shaft count: {cap.Count}");
		}

		BroadcastToChannel("miners", "dispatcher-change");
	}
	else
	{
		throw new Exception("This script is for dispatcher only (command:set-role:dispatcher).");
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



		//////////////////// ignore section for MDK minifier
#region mdk preserve
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
	WaitingForLockInShaft = 9, ///< Slowly ascending in the shaft after drilling. Waiting for permission to enter airspace above shaft.
	ChangingShaft        = 10,
	Maintenance          = 11,
	ForceFinish          = 12,
	Takeoff              = 13, ///< Ascending from docking port, through shared airspace, into assigned flight level.
	ReturningHome        = 14, ///< Traveling from the point above the shaft to the base on a reserved flight level.
	Docking              = 15  ///< Descending to the docking port through shared airspace. (docking final approach)
}

public enum ShaftState { Planned, InProgress, Complete, Cancelled }

Role CurrentRole; // Current role, always Role.Dispatcher (after config load)
public enum Role : byte { None = 0, Dispatcher, Agent, Lone }

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
		PState.StaticDockOverride = currentState.StaticDockOverride;
		PState.LifetimeAcceptedTasks = currentState.LifetimeAcceptedTasks;
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

public class PersistentState
{
	public int LifetimeAcceptedTasks = 0;

	// cleared by specific command
	public Vector3D? StaticDockOverride { get; set; }

	// cleared by clear-storage-state (task-dependent)
	public long logLCD;                ///< Entity ID of the logging screen.
	public Vector3D? miningPlaneNormal;
	public Vector3D? corePoint;
	public float? shaftRadius;

	public float? maxDepth;
	public float? skipDepth;

	public List<byte> ShaftStates = new List<byte>();
	public int MaxGenerations;
	public string CurrentTaskGroup;

	// banned directions?

	T ParseValue<T>(Dictionary<string, string> values, string key)
	{
		string res;
		if (values.TryGetValue(key, out res) && !string.IsNullOrEmpty(res))
		{
			if (typeof(T) == typeof(String))
				return (T)(object)res;
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
		if (string.IsNullOrEmpty(storage))
			return this;
		
		var values = storage.Split('\n').ToDictionary(s => s.Split('=')[0], s => string.Join("=", s.Split('=').Skip(1)));
		foreach (var v in values)
			E.Log("Load: " + v.Key + " <-- " + v.Value , E.LogLevel.Debug);

		LifetimeAcceptedTasks = ParseValue<int>(values, "LifetimeAcceptedTasks");

		StaticDockOverride = ParseValue<Vector3D?>(values, "StaticDockOverride");
		logLCD             = ParseValue<long>     (values, "logLCD");
		miningPlaneNormal  = ParseValue<Vector3D?>(values, "miningPlaneNormal");
		corePoint = ParseValue<Vector3D?>(values, "corePoint");
		shaftRadius = ParseValue<float?>(values, "shaftRadius");

		maxDepth = ParseValue<float?>(values, "maxDepth");
		skipDepth = ParseValue<float?>(values, "skipDepth");

		MaxGenerations = ParseValue<int>(values, "MaxGenerations");
		CurrentTaskGroup = ParseValue<string>(values, "CurrentTaskGroup");

		ShaftStates = ParseValue<List<byte>>(values, "ShaftStates") ?? new List<byte>();
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
			"StaticDockOverride=" + (StaticDockOverride.HasValue ? VectorOpsHelper.V3DtoBroadcastString(StaticDockOverride.Value) : ""),
			"logLCD=" + logLCD,
			"miningPlaneNormal=" + (miningPlaneNormal.HasValue ? VectorOpsHelper.V3DtoBroadcastString(miningPlaneNormal.Value) : ""),
			"corePoint=" + (corePoint.HasValue ? VectorOpsHelper.V3DtoBroadcastString(corePoint.Value) : ""),
			"shaftRadius=" + shaftRadius,
			"maxDepth=" + maxDepth,
			"skipDepth=" + skipDepth,
			"MaxGenerations=" + MaxGenerations,
			"CurrentTaskGroup=" + CurrentTaskGroup,
			"ShaftStates=" + string.Join(":", ShaftStates)
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
	Runtime.UpdateFrequency = UpdateFrequency.Update1;
	Ctor();
}

List<MyIGCMessage> uniMsgs = new List<MyIGCMessage>();
void Main(string param, UpdateType updateType)
{
	/* Fetch all new unicast messages since the last cycle. */
	uniMsgs.Clear();
	while (IGC.UnicastListener.HasPendingMessage)
	{
		uniMsgs.Add(IGC.UnicastListener.AcceptMessage());
	}

	/* Fetch one broadcast message since the last cycle. */
	//TODO: Do dispatchers receive broadcase messages on miners.command???
	//TODO: Why not multiple messages? Can there be only one broadcast per cycle?
	var commandChannel = IGC.RegisterBroadcastListener("miners.command");
	if (commandChannel.HasPendingMessage)
	{
		var msg = commandChannel.AcceptMessage();
		param = msg.Data.ToString();
		Log("Got miners.command: " + param);
	}

	TickCount++;
	Echo("Run count: " + TickCount);

	/* Process command, if broadcast received, manually passed by the user or from custom data (1st cycle). */
	StartOfTick(param);

	/* Process the unicast messages. */
	foreach (var m in uniMsgs)
	{
		if (m.Tag == "apck.ntv.update")
		{
			var igcDto = (MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>)m.Data;
			var name = igcDto.Item1.Item1;
			UpdateNTV(name, igcDto);
			//if (minerController?.pCore != null)
			//{
			//	IGC.SendUnicastMessage(minerController.pCore.EntityId, "apck.ntv.update", igcDto);
			//}
		}
		else if (m.Tag == "apck.depart.complete")
		{
			dockHost?.DepartComplete(m.Source.ToString());
		}
		else if (m.Tag == "apck.depart.request") //TODO: Never used???
		{
			dockHost.RequestDocking(m.Source, (Vector3D)m.Data, true);
		}
		else if (m.Tag == "apck.docking.request")
		{
			dockHost.RequestDocking(m.Source, (Vector3D)m.Data);
		}
		//else if (m.Tag == "apck.depart.complete") //TODO: Never executed, because same condition is already above.
		//{
			//if (minerController?.DispatcherId != null)
			//	IGC.SendUnicastMessage(minerController.DispatcherId.Value, "apck.depart.complete", "");
		//}
		else if (m.Tag == "apck.docking.approach" || m.Tag == "apck.depart.approach")
		{
			throw new Exception("Dispatcher cannot process incoming approach paths.");
		}

	}

	E.Echo($"Version: {Ver}");
	E.Echo("Min3r role: " + CurrentRole);
	if (CurrentRole == Role.Dispatcher)
	{
		E.Echo(dispatcherService.ToString());
		dispatcherService.HandleIGC(uniMsgs);

		/* Check if we can grant airspace locks. */
		//FIXME: This might be a performance problem. It is sufficient to only check every 1 s or so.
		dispatcherService.GrantAirspaceLocks();

		/* Request GUI data from the agents. */
		if (guiH != null)
		{
			foreach (var s in dispatcherService.subordinates)
				IGC.SendUnicastMessage(s.Id, "report.request", "");
		}

		/* Update the GUI. */
		guiH?.UpdateTaskSummary(dispatcherService);
		dockHost.Handle(IGC, TickCount);

		if (rawPanel != null)
		{
			if (guiSeat != null)
			{
				if (guiH == null)
				{
					guiH = new GuiHandler(rawPanel, dispatcherService, stateWrapper);
					dispatcherService.OnTaskUpdate = guiH.UpdateMiningScheme;
					guiH.OnShaftClick = id => dispatcherService.CancelShaft(id);

					if (dispatcherService.CurrentTask != null)
						dispatcherService.OnTaskUpdate.Invoke(dispatcherService.CurrentTask); // restore from pstate
				}
				else
					guiH.Handle(rawPanel, guiSeat);
			}
		}
	}
	if (Toggle.C.Check("show-pstate"))
		E.Echo(stateWrapper.PState.ToString());

	EndOfTick();
	CheckExpireNTV();

	if (DbgIgc != 0)
		EmitFlush(DbgIgc);
	Dt = Math.Max(0.001, Runtime.TimeSinceLastRun.TotalSeconds);
	E.simT += Dt;
	iCount = Math.Max(iCount, Runtime.CurrentInstructionCount);
	E.Echo($"InstructionCount (Max): {Runtime.CurrentInstructionCount} ({iCount})");
	E.Echo($"Processed in {Runtime.LastRunTimeMs:f3} ms");
}

int iCount;

public void GPStaskHandler(string[] cmdString)
{
	// add Lone role local disp
	if (dispatcherService != null)
	{
		var vdtoArr = cmdString.Skip(2).ToArray();
		var pos = new Vector3D(double.Parse(vdtoArr[0]), double.Parse(vdtoArr[1]), double.Parse(vdtoArr[2]));
		Vector3D n;
		if (vdtoArr.Length > 3)
		{
			n = Vector3D.Normalize(pos - new Vector3D(double.Parse(vdtoArr[3]), double.Parse(vdtoArr[4]), double.Parse(vdtoArr[5])));
		}
		else
		{
			if (guiSeat == null)
			{
				E.Log("WARNING: the normal was not supplied and there is no Control Station available to check if we are in gravity");
				n = -dockHost.GetFirstNormal();
				E.Log("Using 'first dock connector Backward' as a normal");
			}
			else
			{
				Vector3D pCent;
				if (guiSeat.TryGetPlanetPosition(out pCent))
				{
					n = Vector3D.Normalize(pCent - pos);
					E.Log("Using mining-center-to-planet-center direction as a normal because we are in gravity");
				}
				else
				{
					n = -dockHost.GetFirstNormal();
					E.Log("Using 'first dock connector Backward' as a normal");
				}
			}
		}
		var c = Variables.Get<string>("group-constraint");
		if (!string.IsNullOrEmpty(c))
		{
			dispatcherService.CreateTask(Variables.Get<float>("circular-pattern-shaft-radius"), pos, n, Variables.Get<int>("max-generations"), c);
			dispatcherService.BroadcastStart(c);
		}

		else
			E.Log("To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");
	}
	else
		E.Log("GPStaskHandler is intended for Dispatcher role");
}

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

				//IMyShipController gravGetter = minerController?.remCon ?? guiSeat;
				IMyShipController gravGetter = guiSeat; //This is always a dispatcher, never a drone.
				Vector3D pCent;
				if ((gravGetter != null) && gravGetter.TryGetPlanetPosition(out pCent))
				{
					castedNormal = Vector3D.Normalize(pCent - castedSurfacePoint.Value);
					E.Log("Using mining-center-to-planet-center direction as a normal because we are in gravity");
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
					E.Log("Successfully got mining center and mining normal");
					if (dispatcherService != null)
					{
						var c = Variables.Get<string>("group-constraint");
						if (!string.IsNullOrEmpty(c))
						{
							dispatcherService.BroadcastStart(c);
							dispatcherService.CreateTask(Variables.Get<float>("circular-pattern-shaft-radius"),
									castedSurfacePoint.Value - castedNormal.Value * 10, castedNormal.Value, Variables.Get<int>("max-generations"), c);
						}
						else
							Log("To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");
					}
				}
				else
				{
					E.Log($"RaycastTaskHandler failed to get castedNormal or castedSurfacePoint");
				}
			}
		}
		else
		{
			E.Log($"RaycastTaskHandler couldn't raycast initial position. Camera '{cam.CustomName}' had {cam.AvailableScanRange} AvailableScanRange");
		}
	}
	else
	{
		throw new Exception($"No active cam, {cameras.Count} known");
	}
}


Dispatcher dispatcherService;

/**
 * \brief Implements job dispatching and air traffic control (ATC).
 */
public class Dispatcher
{
	/** \brief Request for an airspace lock. */
	public struct LockRequest {
		public long   id;      ///< ID of the requesting PB. Send answer there.
		public string lockName;///< Name of the requested lock.
		public LockRequest(long _id, string _ln) { id = _id; lockName = _ln; }
	}

	public List<Subordinate> subordinates = new List<Subordinate>();
	Queue<LockRequest> sectionsLockRequests = new Queue<LockRequest>(); ///< Lock requests to be served ASAP. An agent(=id) can only be queued once.

	public Action<MiningTask> OnTaskUpdate;

	public class Subordinate
	{
		public long Id;               ///< Handle of the agent's programmable block.
		public string ObtainedLock;
		public float Echelon;
		public string Group;
		public TransponderMsg Report; ///< Last received transponder status (position, velocity, ...).
		//TODO: Add software version for compatibility check.
	}

	/**
	 * \brief Returns the subordinate's name from the entity ID of its PB.
	 * \return The name of the subordinate, or "n/a" if the subordinate has no name
	 * or the subordinate does not exist.
	 */
	public string GetSubordinateName(long id) {
		var sb = subordinates.FirstOrDefault(s => s.Id == id);
		return sb == null ? "n/a" : sb.Report.name;
	}

	IMyIntergridCommunicationSystem IGC;

	StateWrapper stateWrapper;

	public Dispatcher(IMyIntergridCommunicationSystem igc, StateWrapper stateWrapper)
	{
		IGC = igc;
		this.stateWrapper = stateWrapper;
	}

	/**
	 * \brief Enqueues a request from an agent for an airspace lock.
	 * \details The lock must be currently in use by another agent. When the other
	 * agent releases that lock (cooperatively!), it is granted to the applicant.
	 * \param[in] src The ID of the applicants PB. It is used to send the answer via IGC.
	 * \param[in] lockName The name of the requested lock.
	 */
	private void EnqueueLockRequest(long src, string lockName)
	{
		if (!sectionsLockRequests.Any(s => s.id == src))
			sectionsLockRequests.Enqueue(new LockRequest(src, lockName));
		//else
			; //TODO: Update existing lock request with new lockName
		Log("Airspace permission request added to requests queue: " + GetSubordinateName(src) + " / " + lockName, E.LogLevel.Debug);
	}

	/**
	 * \brief Tests, if a lock is granteable to an applicant.
	 * \param[in] lockName The name of the lock to grant.
	 * \param[in] agentId The entity ID of the applicant agent's PB. Must
	 * be a subordinate of this dispatcher.
	 */
	public bool IsLockGranteable(string lockName, long agentId) {
		if (subordinates.Any(s => s.ObtainedLock == lockName && s.Id != agentId))
			return false; // Requested lock is held by another agent.
		if (lockName == LOCK_NAME_MiningSection && subordinates.Any(s => s.ObtainedLock == LOCK_NAME_GeneralSection && s.Id != agentId))
			return false; // Cannot grant "mining-site" lock, because "general" lock is currently granted.
		if (lockName == LOCK_NAME_BaseSection && subordinates.Any(s => s.ObtainedLock == LOCK_NAME_GeneralSection && s.Id != agentId))
			return false; // Cannot grant "base" lock, because "general" lock is currently granted.

		/* For local locks, ensure that no other aircraft flying in the surrounding. */
		if (lockName == LOCK_NAME_MiningSection || lockName == LOCK_NAME_BaseSection) {

			var applicant = subordinates.FirstOrDefault(s => s.Id == agentId);
	
			foreach (var sb in subordinates) {
				if (sb.Id == agentId)
					continue; // Check only the _other_ agents.
				//FIXME: We should check the age of the sb.Report (introduce timestamping!
				//       If the report is older than 2s, don't grant permission and emit log message.
				//       If the report is older than 30s, assume the agent is gone for good, and grant permission.
				//       (Don't keep airspace locked forever, just because of 1 uncooperative drone.)
				switch (sb.Report.state)
				{
				case MinerState.Disabled:     // Passive agents will not enter airspace without asking permission first.
				case MinerState.Idle:         // Passive agents will not enter airspace without asking permission first.
				case MinerState.Drilling:     // Drilling agents will request lock before moving out of their shaft and into airspace.
				case MinerState.WaitingForLockInShaft:// Agent is loitering in its shaft.
				case MinerState.Maintenance:  // Agent is docked to base, and will not enter airspace without permission.
				case MinerState.Docked:      // Agent is docked to base, and will not enter airspace without permission.
					continue;
				case MinerState.GoingToEntry: // Agent is descending to its shaft.
				case MinerState.GoingToUnload:// Agent is ascending from its shaft. // TODO: If the agent has arrived at the hold position on its flight level, and waiting for permission to dock, it would not be a problem.
				case MinerState.ChangingShaft:// Agent is moving above the mining site.
					if (lockName == LOCK_NAME_MiningSection)
						return false;
					else
						continue;
				case MinerState.Docking:      // Agent is descending to its docking port.
				case MinerState.Takeoff:      // Agent is ascending from its docking port.
					if (lockName == LOCK_NAME_BaseSection)
						return false;
					else
						continue;
				case MinerState.WaitingForDocking:  //TODO: How to handle this case?
				case MinerState.ForceFinish:        // Maybe a MAYDAY or recall. No experiments.
				default:                            // Something went wrong. No experiments. //TODO: Better log an error message!
					return false;
				case MinerState.ReturningToShaft:
				case MinerState.ReturningHome:

					/* The agent is moving on its flight level, but
					 * potentially through the local protected airspaces. */

					Vector3D dist  = sb.Report.WM.Translation - applicant.Report.WM.Translation; // applicant --> other
					Vector3D v_rel = sb.Report.v - applicant.Report.v;

					/* If the other agent moving away and has at least 50 m
					 * distance, then it is not a problem.                   */
					if (   Vector3D.Dot(sb.Report.v, applicant.Report.v) > 0
						&& dist.Length() > 50.0                                )
						continue;

					/* If the other agent is at least 6s away, then it should
					   not be a problem. (Assuming applicant can vertically traverse
					   all flight levels in 6s or less.                           */
					if (dist.Length() > 600.0) // Even of other agent is closing in at 100m/s, it would take 6s to reach.
						continue;

					/* If the other agent is traveling on a higher flight level, no problem. */
					if (applicant.Echelon < sb.Echelon)
						continue;

					/* If the other agent is holding position and
					 * waiting for a lock too, no problem (prevents deadlocks).  */
					if (sb.Report.v.Length() <= 0.1 && sectionsLockRequests.Any(s => s.id == sb.Id))
						continue;

					return false; // There is in fact another drone with collision risk. Cannot grant airspace lock.
				}
			}
		}

		return true; // The lock can safely be granted.
	}

	/**
	 * \brief Grants granteable locks.
	 * \details To be called periodically. The method will observe priorities and queues.
	 */
	public void GrantAirspaceLocks() {
		while (sectionsLockRequests.Count > 0) {

			//TODO: Prefer agents which are in the air (=not docked). No necessarily the first one.
			//TODO: Prefer agents with low fuel or low battery.
			LockRequest cand = sectionsLockRequests.Peek();

			if (!IsLockGranteable(cand.lockName, cand.id))
				break;

			/* Requested lock is not held by any other agent.
			 * Grant immediately to the applicant.             */
			sectionsLockRequests.Dequeue();
			subordinates.First(s => s.Id == cand.id).ObtainedLock = cand.lockName;
			IGC.SendUnicastMessage(cand.id, "miners", "common-airspace-lock-granted:" + cand.lockName);
			Log(cand.lockName + " granted to " + GetSubordinateName(cand.id), E.LogLevel.Debug);
		}
	}

	public void HandleIGC(List<MyIGCMessage> uniMsgs)
	{
		/* First process broadcasted messages. */
		var minerChannel = IGC.RegisterBroadcastListener("miners");
		while (minerChannel.HasPendingMessage)
		{
			var msg = minerChannel.AcceptMessage();
			if (msg.Data == null)
				continue;
						
			if (msg.Data.ToString().Contains("common-airspace-ask-for-lock"))
			{
				var sectionName = msg.Data.ToString().Split(':')[1];
				EnqueueLockRequest(msg.Source, sectionName);
			}

			if (msg.Data.ToString().Contains("common-airspace-lock-released"))
			{
				var sectionName = msg.Data.ToString().Split(':')[1];
				Log("(Dispatcher) received lock-released notification " + sectionName + " from " + GetSubordinateName(msg.Source), E.LogLevel.Debug);
				subordinates.Single(s => s.Id == msg.Source).ObtainedLock = "";
			}
		}

		/* Second, process broadcasted handshakes. */
		var minerHandshakeChannel = IGC.RegisterBroadcastListener("miners.handshake");
		while (minerHandshakeChannel.HasPendingMessage)
		{
			var msg = minerHandshakeChannel.AcceptMessage();
			if (!(msg.Data is MyTuple<string,MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>>, string>))
				continue; // Corrupt/malformed message. (Or wrong s/w version on agent.)
				
			var data = (MyTuple<string,MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>>, string>)msg.Data;
			if (data.Item3 != Ver) {
				Log($"Ignoring handshake broadcast by {data.Item2.Item1.Item2}: Wrong s/w version {data.Item3} (vs {Ver}).", E.LogLevel.Warning);
				continue;
			}
	
			Log($"Initiated handshake by {data.Item2.Item1.Item2}, group tag: {data.Item1}", E.LogLevel.Notice);

			Subordinate sb;
			if (!subordinates.Any(s => s.Id == msg.Source))
			{
				sb = new Subordinate { Id = msg.Source, Echelon = (subordinates.Count + 1) * Variables.Get<float>("echelon-offset") + 10f, Group = data.Item1 };
				subordinates.Add(sb);
				sb.Report = new TransponderMsg();
			}
			else
			{
				sb = subordinates.Single(s => s.Id == msg.Source);
				sb.Group = data.Item1;
			}
			sb.Report.UpdateFromIgc(data.Item2);

			IGC.SendUnicastMessage(msg.Source, "miners.handshake.reply", IGC.Me);
			IGC.SendUnicastMessage(msg.Source, "miners.echelon", sb.Echelon);

			/* If there is a task, inform the new agent about the normal vector. */
			if (stateWrapper.PState.miningPlaneNormal.HasValue)
			{
				IGC.SendUnicastMessage(msg.Source, "miners.normal", stateWrapper.PState.miningPlaneNormal.Value);
			}
			var vals = new string[] { "skip-depth", "depth-limit", "getAbove-altitude" };
			Scheduler.C.After(500).RunCmd(() => {
				foreach (var v in vals)
				{
					Log($"Propagating set-value:'{v}' to " + sb.Report.name, E.LogLevel.Debug);
					IGC.SendUnicastMessage(msg.Source, "set-value", $"{v}:{Variables.Get<float>(v)}");
				}
			});
		}

		/* Process agent's status reports. */
		var minerReportChannel = IGC.RegisterBroadcastListener("miners.report");
		while (minerReportChannel.HasPendingMessage)
		{
			var msg = minerReportChannel.AcceptMessage();
			var sub = subordinates.FirstOrDefault(s => s.Id == msg.Source);
			if (sub == null)
				continue; // None of our subordinates.
			var data = (MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>>)msg.Data;
			sub.Report.UpdateFromIgc(data);
		}

		/* Finally, process unicast messages.*/
		foreach (var msg in uniMsgs)
		{
			//Log("Dispatcher has received private message from " + msg.Source);
			if (msg.Tag == "create-task")
			{
				var data = (MyTuple<float, Vector3D, Vector3D>)msg.Data;

				var sub = subordinates.First(s => s.Id == msg.Source);
				Log("Got new mining task from agent " + sub.Report.name);
				sub.ObtainedLock = LOCK_NAME_GeneralSection;
				CreateTask(data.Item1, data.Item2, data.Item3, Variables.Get<int>("max-generations"), sub.Group);
				BroadcastStart(sub.Group);
			}

			if (msg.Tag.Contains("request-new"))
			{
				if (msg.Tag == "shaft-complete-request-new")
				{
					CompleteShaft((int)msg.Data);
					Log(GetSubordinateName(msg.Source) + $" reports shaft {msg.Data} complete.", E.LogLevel.Notice);
				}

				// assign and send new shaft points
				Vector3D? entry = Vector3D.Zero;
				Vector3D? getabove = Vector3D.Zero;
				int shId = 0;
				if ((CurrentTask != null) && AssignNewShaft(ref entry, ref getabove, ref shId))
				{
					IGC.SendUnicastMessage(msg.Source, "miners.assign-shaft", new MyTuple<int, Vector3D, Vector3D>(shId, entry.Value, getabove.Value));
					E.Log($"Assigned new shaft {shId} to " + GetSubordinateName(msg.Source) + '.', E.LogLevel.Notice);
				}
				else
				{
					IGC.SendUnicastMessage(msg.Source, "command", "force-finish");
				}
			}
			if (msg.Tag == "ban-direction")
			{
				BanDirectionByPoint((int)msg.Data);
			}
		}

		foreach (var s in subordinates)
		{
			E.Echo(s.Id + ": echelon = " + s.Echelon + " lock: " + s.ObtainedLock);
		}
	}

	/** \brief Sends the miners.resume message to all agents in the current task group. */
	public void BroadcastResume()
	{
		var g = stateWrapper.PState.CurrentTaskGroup;
		if (string.IsNullOrEmpty(g))
			return;
				
		Log($"Broadcasting task resume for mining group '{g}'");
		foreach (var s in subordinates.Where(x => x.Group == g))
		{
			IGC.SendUnicastMessage(s.Id, "miners.resume", stateWrapper.PState.miningPlaneNormal.Value);
		}
	}

	public void BroadCastHalt()
	{
		Log($"Broadcasting global Halt & Clear state");
		IGC.SendBroadcastMessage("miners.command", "command:halt");
	}

	public void BroadcastStart(string group)
	{
		Log($"Preparing start for mining group '{group}'");

		/* Reset the internal state of all agents, so that they are in a well-defined state. */
		IGC.SendBroadcastMessage("miners.command", "command:clear-storage-state");

		/* Inform the agents about the orientation of the mining plane (same for all shafts). */
		Scheduler.C.Clear();
		stateWrapper.PState.LifetimeAcceptedTasks++;
		Scheduler.C.After(500).RunCmd(() => {
			foreach (var s in subordinates.Where(x => x.Group == group))
			{
				IGC.SendUnicastMessage(s.Id, "miners.normal", stateWrapper.PState.miningPlaneNormal.Value);
			}
		});

		/* Instruct the agents to start mining. They will then ask for a shaft. */
		Scheduler.C.After(1000).RunCmd(() => {
			Log($"Broadcasting start for mining group '{group}'");
			foreach (var s in subordinates.Where(x => x.Group == group))
			{
				IGC.SendUnicastMessage(s.Id, "command", "mine");
			}
		});
	}

	public void Recall()
	{
		IGC.SendBroadcastMessage("miners.command", "command:force-finish");
		Log($"Broadcasting Recall");
	}

	public void PurgeLocks()
	{
		IGC.SendBroadcastMessage("miners.command", "command:dispatch");
		sectionsLockRequests.Clear();
		subordinates.ForEach(x => x.ObtainedLock = "");
		Log($"WARNING! Purging Locks, green light for everybody...");
	}


	public MiningTask CurrentTask;
	public class MiningTask
	{
		public float R { get; private set; }
		public Vector3D miningPlaneNormal { get; private set; }
		public Vector3D corePoint { get; private set; }
		public Vector3D planeXunit { get; private set; }
		public Vector3D planeYunit { get; private set; }

		public string GroupConstraint { get; private set; }

		public List<MiningShaft> Shafts; // List of of jobs (shafts).

		public MiningTask(int maxGenerations, float shaftRadius, Vector3D coreP, Vector3D normal, string groupConstraint)
		{
			R = shaftRadius;
			GroupConstraint = groupConstraint;
			miningPlaneNormal = normal;
			corePoint = coreP;
			planeXunit = Vector3D.Normalize(Vector3D.Cross(coreP, normal)); // just any perp to normal will do
			planeYunit = Vector3D.Cross(planeXunit, normal);

			/* Generate the todo list of jobs (shafts). */
			var radInterval = R * 2f * 0.866f; // 2 cos(30) * R
			Shafts = new List<MiningShaft>(maxGenerations * 6 + 1);
			Shafts.Add(new MiningShaft());
			int id = 1;
			for (int g = 1; g < maxGenerations; g++) // For each concentric circle ...
			{
				for (int i = 0; i < g * 6; i++) // For each shaft on the circle ...
				{
					double angle = 60f / 180f * Math.PI * i / g; // 6 * N circles per N diameter generation, i.e. generation
					var p = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radInterval * g;
					Shafts.Add(new MiningShaft() { Point = p, Id = id++ });
				}
			}
		}

		public void BanDirection(int id)
		{
			var pt = Shafts.First(x => x.Id == id).Point;
			foreach (var sh in Shafts)
			{
				if (Vector2.Dot(Vector2.Normalize(sh.Point), Vector2.Normalize(pt)) > .8f)
					sh.State = ShaftState.Cancelled;
			}
		}

		public void SetShaftState(int id, ShaftState state)
		{
			var sh = Shafts.First(x => x.Id == id);
			var current = sh.State;
			if (current == ShaftState.Cancelled && current == state)
				state = ShaftState.Planned;
			sh.State = state;
		}

		public bool RequestShaft(ref Vector3D? entry, ref Vector3D? getAbove, ref int id)
		{
			var sh = Shafts.FirstOrDefault(x => x.State == ShaftState.Planned);
			if (sh != null)
			{
				entry = corePoint + planeXunit * sh.Point.X + planeYunit * sh.Point.Y;
				getAbove = entry.Value - miningPlaneNormal * Variables.Get<float>("getAbove-altitude");
				id = sh.Id;
				sh.State = ShaftState.InProgress;
				return true;
			}
			return false;
		}

		public class MiningShaft
		{
			public ShaftState State = ShaftState.Planned;
			public Vector2 Point;
			public int Id;
		}
	}

	public void CreateTask(float r, Vector3D corePoint, Vector3D miningPlaneNormal, int maxGenerations, string groupConstraint)
	{
		CurrentTask = new MiningTask(maxGenerations, r, corePoint, miningPlaneNormal, groupConstraint);
		OnTaskUpdate?.Invoke(CurrentTask);

		stateWrapper.ClearPersistentState();
		stateWrapper.PState.corePoint = corePoint;
		stateWrapper.PState.shaftRadius = r;
		stateWrapper.PState.miningPlaneNormal = miningPlaneNormal;
		stateWrapper.PState.MaxGenerations = maxGenerations;
		stateWrapper.PState.ShaftStates = CurrentTask.Shafts.Select(x => (byte)x.State).ToList();
		stateWrapper.PState.CurrentTaskGroup = groupConstraint;

		Log($"Creating task...");
		Log(VectorOpsHelper.GetGPSString("min3r.task.P", corePoint, Color.Red));
		Log(VectorOpsHelper.GetGPSString("min3r.task.Np", corePoint - miningPlaneNormal, Color.Red));
		Log($"shaftRadius: {r}");
		Log($"maxGenerations: {maxGenerations}");
		Log($"shafts: {CurrentTask.Shafts.Count}");
		Log($"groupConstraint: {CurrentTask.GroupConstraint}");
		Log($"Task created");
	}

	public void CancelShaft(int id)
	{
		var s = ShaftState.Cancelled;
		CurrentTask?.SetShaftState(id, s);
		stateWrapper.PState.ShaftStates[id] = (byte)s;
		OnTaskUpdate?.Invoke(CurrentTask);
	}

	public void CompleteShaft(int id)
	{
		var s = ShaftState.Complete;
		CurrentTask?.SetShaftState(id, ShaftState.Complete);
		stateWrapper.PState.ShaftStates[id] = (byte)s;
		OnTaskUpdate?.Invoke(CurrentTask);
	}

	public void BanDirectionByPoint(int id)
	{
		CurrentTask?.BanDirection(id);
		OnTaskUpdate?.Invoke(CurrentTask);
	}

	public bool AssignNewShaft(ref Vector3D? entry, ref Vector3D? getAbove, ref int id)
	{
		Log("CurrentTask.RequestShaft", E.LogLevel.Debug);
		bool res = CurrentTask.RequestShaft(ref entry, ref getAbove, ref id);
		stateWrapper.PState.ShaftStates[id] = (byte)ShaftState.InProgress;
		OnTaskUpdate?.Invoke(CurrentTask);
		return res;
	}

	StringBuilder sb = new StringBuilder();
	public override string ToString()
	{
		sb.Clear();
		sb.AppendLine($"CircularPattern radius: {stateWrapper.PState.shaftRadius:f2}");
		sb.AppendLine($" ");
		sb.AppendLine($"Total subordinates: {subordinates.Count}");
		sb.AppendLine($"Lock queue: {sectionsLockRequests.Count}");
		sb.AppendLine($"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");
		return sb.ToString();
	}
}


static class VectorOpsHelper
{
	public static string V3DtoBroadcastString(params Vector3D[] vectors)
	{
		return string.Join(":", vectors.Select(v => string.Format("{0}:{1}:{2}", v.X, v.Y, v.Z)));
	}
			
	public static string GetGPSString(string name, Vector3D p, Color c)
	{
		return $"GPS:{name}:{p.X}:{p.Y}:{p.Z}:#{c.R:X02}{c.G:X02}{c.B:X02}:";
	}
}


IMyTextPanel rawPanel;
IMyShipController guiSeat;


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


Dictionary<string, TargetTelemetry> NamedTeleData = new Dictionary<string, TargetTelemetry>();

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
	public long TickStamp;                          ///< Timestamp in ticks (TODO: tick timer is reset on reload!)
	int clock;
	public string Name;                             ///< Purpose for which the data is sent, e.g. "docking".
	public long EntityId;
	public Vector3D? Position { get; private set; } ///< Interface position of the docking port. (centerline intersects docking ring)
	public Vector3D? Velocity;                      ///< [m/s] Velocity of docking port.
	public Vector3D? Acceleration;
	public MatrixD? OrientationUnit;                ///< World matrix of docking port.
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


DockHost dockHost;

/**
 * \brief Docking port manager. (dispatcher only)
 */
public class DockHost
{
	Dispatcher             dispatcher;
	List<IMyShipConnector> ports;
	Dictionary<IMyShipConnector, Vector3D> pPositions = new Dictionary<IMyShipConnector, Vector3D>(); // Positions of `ports`.

	public DockHost(Dispatcher disp, List<IMyShipConnector> docks, IMyGridTerminalSystem gts)
	{
		dispatcher = disp;
		ports = docks;
		ports.ForEach(x => pPositions.Add(x, x.GetPosition()));
	}

	public void Handle(IMyIntergridCommunicationSystem i, int t)
	{
		var z = new List<MyTuple<Vector3D, Vector3D, Vector4>>();
		//foreach (var n in nodes.Where(x => x.Pa != null))
		//{
		//	var gr = ports.First().CubeGrid;
		//	z.Add(new MyTuple<Vector3D, Vector3D, Vector4>(gr.GridIntegerToWorld(n.loc), gr.GridIntegerToWorld(n.Pa.loc), Color.SeaGreen.ToVector4()));
		//}

		i.SendUnicastMessage(DbgIgc, "draw-lines", z.ToImmutableArray());

		foreach (var s in dockRequests)
			E.Echo(s + " awaits docking");
		foreach (var s in depRequests)
			E.Echo(s + " awaits dep");

		/* Process pending docking requests. */
		if (dockRequests.Any())
		{
			/* Search for a free docking port. */
			var fd = ports.FirstOrDefault(d =>
				(string.IsNullOrEmpty(d.CustomData) || (d.CustomData == dockRequests.Peek().ToString())) && (d.Status == MyShipConnectorStatus.Unconnected));
			if (fd != null)
			{
				/* Found a port. Serve the docking request. */
				var id = dockRequests.Dequeue();

				/* Store the ID of the agent's PB in the docking port's custom data. */
				fd.CustomData = id.ToString();
				E.Log("Assigned docking port " + fd.CustomName + " to " + dispatcher.GetSubordinateName(id), E.LogLevel.Notice);
				i.SendUnicastMessage(id, "apck.docking.approach", ""); //TODO: NEcessary?
			}
		}

		/* Process pending departure requests. */
		if (depRequests.Any())
		{
			foreach (var s in depRequests)
			{
				E.Echo(s + " awaits departure");
			}
			var r = depRequests.Peek();
			var bd = ports.FirstOrDefault(d => d.CustomData == r.ToString());
			if (bd != null)
			{
				depRequests.Dequeue();

				/* Calculate the departure path and send it to the agent. */
				E.Log($"Sent 0-node departure path");
				i.SendUnicastMessage(r, "apck.depart.approach", "");//TODO: Necessary?
			}
		}

		/* For all reserved docking ports, sent the position to the incoming agent. */
		foreach (var d in ports.Where(d => !string.IsNullOrEmpty(d.CustomData)))
		{
			long id;
			if (!long.TryParse(d.CustomData, out id))
				continue;
					
			E.Echo($"Channeling DV to {id}");
			var x = new TargetTelemetry(1, "docking");
			var m = d.WorldMatrix;
			x.SetPosition(m.Translation + m.Forward * (d.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 1.25 : 0.5), t);

			/* Calculate velocity of docking port. */
			if (pPositions[d] != Vector3D.Zero)
				x.Velocity = (d.GetPosition() - pPositions[d]) / Dt;
			pPositions[d] = d.GetPosition();

			x.OrientationUnit = m;
			var k = x.GetIgcDto();
			i.SendUnicastMessage(id, "apck.ntv.update", k);
		}

	}

	/**
	 * \brief Marks a docking port as available again.
	 * \param[in] id Handle of the departed agent's PB, which is still stored
	 * in the docking port's custom data.
	 */
	public void DepartComplete(string id)
	{
		/* Clear the docking port's custom data. */
		ports.First(x => x.CustomData == id).CustomData = "";
	}

	/**
	 * \param[in] id Handle of the requesting agent's PB.
	 */
	public void RequestDocking(long id, Vector3D d, bool depart = false)
	{
		if (depart)
		{
			if (!depRequests.Contains(id))
				depRequests.Enqueue(id);
		}
		else
		{
			if (!dockRequests.Contains(id))
				dockRequests.Enqueue(id);
		}
		dests[id] = d;
	}

	Dictionary<long, Vector3D> dests = new Dictionary<long, Vector3D>(); ///< Position of the agent's docking port at the time of the last docking/departure request.
	Queue<long> dockRequests = new Queue<long>(); ///< Requesting agent's PB handles.
	Queue<long> depRequests = new Queue<long>();  ///< Requesting agent's PB handles.

	public Vector3D GetFirstNormal()
	{
		return ports.First().WorldMatrix.Forward;
	}

	public List<IMyShipConnector> GetPorts()
	{
		return ports;
	}

}


GuiHandler guiH;
public class GuiHandler
{
	Vector2 mOffset;
	List<ActiveElement> controls = new List<ActiveElement>();
	Dispatcher _dispatcher;
	StateWrapper _stateWrapper;
	Vector2 viewPortSize;

	public GuiHandler(IMyTextSurface p, Dispatcher dispatcher, StateWrapper stateWrapper)
	{
		_dispatcher = dispatcher;
		_stateWrapper = stateWrapper;
		viewPortSize = p.TextureSize;

		float interval = 0.18f;
		Vector2 btnSize = new Vector2(85, 40);
		float b_X = -0.967f;

		var bRecall = CreateButton(p, btnSize, new Vector2(b_X + interval, 0.85f), "Recall", Color.Black);
		bRecall.OnClick = xy => {
			dispatcher.Recall();
		};
		AddTipToAe(bRecall, "Finish work (broadcast command:force-finish)");
		controls.Add(bRecall);

		var bResume = CreateButton(p, btnSize, new Vector2(b_X + interval * 2, 0.85f), "Resume", Color.Black);
		bResume.OnClick = xy => {
			dispatcher.BroadcastResume();
		};
		AddTipToAe(bResume, "Resume work (broadcast 'miners.resume' message)");
		controls.Add(bResume);

		var bClearState = CreateButton(p, btnSize, new Vector2(b_X + interval * 3, 0.85f), "Clear state", Color.Black);
		bClearState.OnClick = xy => {
			stateWrapper?.ClearPersistentState();
		};
		AddTipToAe(bClearState, "Clear Dispatcher state");
		controls.Add(bClearState);

		var bClearLog = CreateButton(p, btnSize, new Vector2(b_X + interval * 4, 0.85f), "Clear log", Color.Black);
		bClearLog.OnClick = xy => {
			E.ClearLog();
		};
		controls.Add(bClearLog);

		var bPurgeLocks = CreateButton(p, btnSize, new Vector2(b_X + interval * 5, 0.85f), "Purge locks", Color.Black);
		bPurgeLocks.OnClick = xy => {
			dispatcher.PurgeLocks();
		};
		AddTipToAe(bPurgeLocks, "Clear lock ownership. Last resort in case of deadlock");
		controls.Add(bPurgeLocks);

		var bHalt = CreateButton(p, btnSize, new Vector2(b_X + interval * 6, 0.85f), "EMRG HALT", Color.Black);
		bHalt.OnClick = xy => {
			dispatcher.BroadCastHalt();
		};
		AddTipToAe(bHalt, "Halt all activity, restore overrides, release control, clear states");
		controls.Add(bHalt);

		shaftTip = new MySprite(SpriteType.TEXT, "", new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y * 0.9f),
			null, Color.White, "Debug", TextAlignment.CENTER, 0.5f);
		buttonTip = new MySprite(SpriteType.TEXT, "", bRecall.Min - Vector2.UnitY * 17,
			null, Color.White, "Debug", TextAlignment.LEFT, 0.5f);
		taskSummary = new MySprite(SpriteType.TEXT, "No active task", new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y / 20f),
			null, Color.White, "Debug", TextAlignment.CENTER, 0.5f);
	}

	ActiveElement CreateButton(IMyTextSurface p, Vector2 btnSize, Vector2 posN, string label, Color? hoverColor = null)
	{
		var textureSize = p.TextureSize;
		var btnSpr = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0, 0),
						btnSize, Color.CornflowerBlue);
		var lblHeight = Vector2.Zero;
		// norm relative to parent widget...
		if (btnSize.Y > 1)
			lblHeight.Y = -p.MeasureStringInPixels(new StringBuilder(label), "Debug", 0.5f).Y / btnSize.Y;

		var lbl = new MySprite(SpriteType.TEXT, label, lblHeight, Vector2.One, Color.White, "Debug", TextAlignment.CENTER, 0.5f);
		var sprites = new List<MySprite>() { btnSpr, lbl };
		var btn = new ActiveElement(sprites, btnSize, posN, textureSize);

		if (hoverColor != null)
		{
			btn.OnMouseIn = () =>
			{
				btn.TransformSprites(spr => { var s1 = spr; s1.Color = hoverColor; s1.Size = btnSize * 1.05f; return spr.Type == SpriteType.TEXTURE ? s1 : spr; });
			};
			btn.OnMouseOut = () =>
			{
				btn.TransformSprites(spr => spr.Type == SpriteType.TEXTURE ? btnSpr : spr);
			};
		}
		return btn;
	}

	void AddTipToAe(ActiveElement ae, string tip)
	{
		ae.OnMouseIn += () => buttonTip.Data = tip;
		ae.OnMouseOut += () => buttonTip.Data = "";
	}

	bool eDown;
	public void Handle(IMyTextPanel panel, IMyShipController seat)
	{
		bool needsUpdate = true;
		Vector2 r = Vector2.Zero;
		bool clickE = false;
		if (seat.IsUnderControl && Toggle.C.Check("cc"))
		{
			r = seat.RotationIndicator;
			var roll = seat.RollIndicator;

			var eDownUpdate = (roll > 0);
			if (!eDownUpdate && eDown)
				clickE = true;
			eDown = eDownUpdate;
		}
		if (r.LengthSquared() > 0 || clickE)
		{
			needsUpdate = true;
			mOffset.X += r.Y;
			mOffset.Y += r.X;
			mOffset = Vector2.Clamp(mOffset, -panel.TextureSize / 2, panel.TextureSize / 2);
		}
		var cursP = mOffset + panel.TextureSize / 2;

		if (needsUpdate)
		{
			using (var frame = panel.DrawFrame())
			{
				DrawReportRepeater(frame);

				foreach (var ae in controls.Where(x => x.Visible).Union(shaftControls))
				{
					if (ae.CheckHover(cursP))
					{
						if (clickE)
							ae.OnClick?.Invoke(cursP);
					}
				}

				foreach (var ae in controls.Where(x => x.Visible).Union(shaftControls))
				{
					frame.AddRange(ae.GetSprites());
				}

				frame.Add(shaftTip);
				frame.Add(buttonTip);
				frame.Add(taskSummary);

				DrawAgents(frame);

				var cur = new MySprite(SpriteType.TEXTURE, "Triangle", cursP, new Vector2(7f, 10f), Color.White);
				cur.RotationOrScale = 6f;
				frame.Add(cur);
			}

			if (r.LengthSquared() > 0)
			{
				panel.ContentType = ContentType.TEXT_AND_IMAGE;
				panel.ContentType = ContentType.SCRIPT;
			}
		}
	}

	void DrawAgents(MySpriteDrawFrame frame)
	{
		var z = new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y / 2f);
		foreach (var ag in _dispatcher.subordinates)
		{
			var pos = ag.Report.WM.Translation;
			var task = _dispatcher.CurrentTask;
			if (task != null)
			{
				var posLoc = Vector3D.Transform(pos, worldToScheme);
				float scale = 3.5f;
				var posViewport = z + new Vector2((float)posLoc.Y, (float)posLoc.X) * scale;
				var btnSize = Vector2.One * scale * task.R * 2;

				var btnSpr = new MySprite(SpriteType.TEXTURE, "AH_BoreSight", posViewport + new Vector2(0, 5), btnSize * 0.8f, ag.Report.ColorTag);
				btnSpr.RotationOrScale = (float)Math.PI / 2f;
				var btnSprBack = new MySprite(SpriteType.TEXTURE, "Textures\\FactionLogo\\Miners\\MinerIcon_3.dds", posViewport, btnSize * 1.2f, Color.Black);

				frame.Add(btnSprBack);
				frame.Add(btnSpr);
			}
		}
	}

	void DrawReportRepeater(MySpriteDrawFrame frame)
	{
		bool madeHeader = false;
		int offY = 0, startY = 30;
		foreach (var su in _dispatcher.subordinates)
		{
			if (!su.Report.KeyValuePairs.IsDefault)
			{
				int offX = 0, startX = 100, interval = 75;
				if (!madeHeader)
				{
					foreach (var kvp in su.Report.KeyValuePairs)
					{
						frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(interval - 5, 40), Color.Black));
						frame.Add(new MySprite(SpriteType.TEXT, kvp.Item1, new Vector2(startX + offX, startY - 16), null, Color.White, "Debug", TextAlignment.CENTER, 0.5f));
						offX += interval;
					}
					madeHeader = true;
					offY += 40;
				}

				offX = 0;
				foreach (var kvp in su.Report.KeyValuePairs)
				{
					frame.Add(new MySprite(SpriteType.TEXT, kvp.Item2, new Vector2(startX + offX, startY + offY), null,
						su.Report.ColorTag, "Debug", TextAlignment.CENTER, 0.5f));
					offX += interval;
				}
				offY += 40;
			}
		}
	}

	List<ActiveElement> shaftControls = new List<ActiveElement>();
	public Action<int> OnShaftClick;
	MySprite shaftTip;
	MySprite buttonTip;
	MySprite taskSummary;

	MatrixD worldToScheme;

	internal void UpdateMiningScheme(Dispatcher.MiningTask obj)
	{
		worldToScheme = MatrixD.Invert(MatrixD.CreateWorld(obj.corePoint, obj.miningPlaneNormal, obj.planeXunit));

		shaftControls = new List<ActiveElement>();

		Vector2 bPos = new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y / 2f);
		float scale = 3.5f;
		Vector2 btnSize = Vector2.One * scale * obj.R * 1.6f;

		foreach (var t in obj.Shafts)
		{
			var pos = bPos + t.Point * scale;

			Color mainCol = Color.White;
			if (t.State == ShaftState.Planned)
				mainCol = Color.CornflowerBlue;
			else if (t.State == ShaftState.Complete)
				mainCol = Color.Darken(Color.CornflowerBlue, 0.4f);
			else if (t.State == ShaftState.InProgress)
				mainCol = Color.Lighten(Color.CornflowerBlue, 0.2f);
			else if (t.State == ShaftState.Cancelled)
				mainCol = Color.DarkSlateGray;

			var btnSpr = new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0, 0), btnSize, mainCol);
			var sprites = new List<MySprite>() { btnSpr };
			var btn = new ActiveElement(sprites, btnSize, pos, viewPortSize);

			var hoverColor = Color.Red;

			btn.OnHover = p => shaftTip.Data = $"id: {t.Id}, {t.State}";

			btn.OnMouseIn = () =>
			{
				btn.TransformSprites(spr => { var s1 = spr; s1.Color = hoverColor; s1.Size = btnSize * 1.05f; return spr.Type == SpriteType.TEXTURE ? s1 : spr; });
			};
			btn.OnMouseOut = () =>
			{
				btn.TransformSprites(spr => spr.Type == SpriteType.TEXTURE ? btnSpr : spr);
				shaftTip.Data = "Hover over shaft for more info,\n tap E to cancel it";
			};

			btn.OnClick = x => OnShaftClick?.Invoke(t.Id);

			shaftControls.Add(btn);
		}
	}

	public void UpdateTaskSummary(Dispatcher d)
	{
		if (d?.CurrentTask != null)
			taskSummary.Data = $"Kind: SpiralLayout\nShafts: {d.CurrentTask.Shafts.Count}\nRadius: {d.CurrentTask.R:f2}\n" +
					$"Group: {d.CurrentTask.GroupConstraint}";
	}

	class ActiveElement
	{
		public Vector2 Min, Max, Center;
		public List<MySprite> Sprites;
		public Vector2 SizePx;
		public Action OnMouseIn { get; set; }
		public Action OnMouseOut { get; set; }
		public Action<Vector2> OnHover { get; set; }
		public Action<Vector2> OnClick { get; set; }
		public bool Hover { get; set; }
		public bool Visible = true;
		Vector2 ContainerSize;

		public ActiveElement(List<MySprite> sprites, Vector2 sizeN, Vector2 posN, Vector2 deviceSize)
		{
			Sprites = sprites;
			if (Math.Abs(posN.X) > 1)
				posN.X = posN.X / (deviceSize.X * 0.5f) - 1;
			if (Math.Abs(posN.Y) > 1)
				posN.Y = 1 - posN.Y / (deviceSize.Y * 0.5f);
			ContainerSize = deviceSize;
			SizePx = new Vector2(sizeN.X > 1 ? sizeN.X : sizeN.X * ContainerSize.X, sizeN.Y > 1 ? sizeN.Y : sizeN.Y * ContainerSize.Y);
			Center = deviceSize / 2f * (Vector2.One + posN);
			Min = Center - SizePx / 2f;
			Max = Center + SizePx / 2f;
		}

		public bool CheckHover(Vector2 cursorPosition)
		{
			bool res = (cursorPosition.X > Min.X) && (cursorPosition.X < Max.X)
						&& (cursorPosition.Y > Min.Y) && (cursorPosition.Y < Max.Y);
			if (res)
			{
				if (!Hover)
				{
					OnMouseIn?.Invoke();
				}
				Hover = true;
				OnHover?.Invoke(cursorPosition);
			}
			else
			{
				if (Hover)
				{
					OnMouseOut?.Invoke();
				}
				Hover = false;
			}

			return res;
		}

		public void TransformSprites(Func<MySprite, MySprite> f)
		{
			for (int n = 0; n < Sprites.Count; n++)
			{
				Sprites[n] = f(Sprites[n]);
			}
		}

		public IEnumerable<MySprite> GetSprites()
		{
			foreach (var x in Sprites)
			{
				var rect = SizePx;

				var x1 = x;
				x1.Position = Center + SizePx / 2f * x.Position;
				var sz = x.Size.Value;
				x1.Size = new Vector2(sz.X > 1 ? sz.X : sz.X * rect.X, sz.Y > 1 ? sz.Y : sz.Y * rect.Y);

				yield return x1;
			}
		}
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
	public long      Id;   ///< Entity ID of the agent's PB.
	public string    name; ///< Grid name of the agent.
	public MatrixD   WM;   ///< World matrix of the agent.
	public Vector3D  v;    ///< [m/s] Velocity of the agent.
	public MinerState state;///< Current state of the agent.
	public Color ColorTag;
	public ImmutableArray<MyTuple<string, string>> KeyValuePairs;

	public void UpdateFromIgc(MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>> dto)
	{
		Id       = dto.Item1.Item1;
		name     = dto.Item1.Item2;
		WM       = dto.Item2;
		v        = dto.Item3;
		state    = (MinerState)dto.Item4;
		ColorTag = dto.Item5;
		KeyValuePairs = dto.Item6;
	}

	// Note: Commented out, because only required by the sender of this datagram.
	//public MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>> ToIgc()
	//{
	//	var dto = new MyTuple<MyTuple<long, string>, MatrixD, Vector3D, byte, Vector4, ImmutableArray<MyTuple<string, string>>>();
	//	dto.Item1.Item1 = Id;
	//	dto.Item1.Item2 = name;
	//	dto.Item2 = WM;
	//	dto.Item3 = v;
	//	dto.Item4 = (byte)state;
	//	dto.Item5 = ColorTag.ToVector4();
	//	dto.Item6 = KeyValuePairs;
	//	return dto;
	//}
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
