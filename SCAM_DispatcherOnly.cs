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
const bool   ClearDocksOnReload = false;  ///< Clear assignment between agents and docking ports.
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
	static bool           bClearLCD = false;               ///< Clear LCD contents on next cycle.
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
		switch (lvl) {
		case LogLevel.Critical:
			linesToLog.Add($"{simT:f1}: CRITICAL: {msg}");
			break;
		case LogLevel.Warning:
			linesToLog.Add($"{simT:f1}: Warning: {msg}");
			break;
		default:
			linesToLog.Add($"{simT:f1}: {msg}");
			break;
		}
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
		bClearLCD = true;
	}

	public static void EndOfTick() {
		if (linesToLog.Any())
		{
			if (lcd != null)
			{
				if (bClearLCD) {
					lcd.WriteText("");
					bClearLCD = false;
				}
				linesToLog.Reverse();
				var t = string.Join("\n", linesToLog) + "\n" + lcd.GetText();
				var u = Variables.Get<int>("logger-char-limit");
				if (t.Length > u)
					t = t.Substring(0, u - 1);
				lcd.WriteText(t);
				linesToLog.Clear();
			} else
				/* Drop excess messages, if they cannot be written to an LCD.
				 * (Prevent large buildup of memory over time.)             */
				while (linesToLog.Count() > 100)
					linesToLog.RemoveAt(0);
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
	/* If this is the first cycle, and the game engine has loaded
	 * all the blocks, then we need to do some more initialisation. */
	if (pendingInitSequence && string.IsNullOrEmpty(arg))
	{
		pendingInitSequence = false;
		
		/* Announce that a new dispatcher just went online.
		 * Agents may want to handshake.                   */
		BroadcastToChannel("miners", "dispatcher-change");

		/* Process commands from the startup script. */
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
							p.ContentType = ContentType.TEXT_AND_IMAGE;
							rawPanel = p;
						}
					}
				},
				{
					"add-gui-controller", (parts) => {
						List<IMyShipController> b = new List<IMyShipController>();
						GridTerminalSystem.GetBlocksOfType(b, x => x.IsSameConstructAs(Me) && x.CustomName.Contains(parts[2]));
						guiSeat                   = b.FirstOrDefault();
						dispatcherService.guiSeat = b.FirstOrDefault();
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
					"init-airspace", (parts) => {
						Log("Warning: Initizing airspace by force. Possible undefined behaviour.", E.LogLevel.Warning);
						dispatcherService?.InitializeAirspace();
					}
				},
				{
					"recall", (parts) => dispatcherService?.Recall()
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

	/* Create the main dispatcher object. */
	dispatcherService = new Dispatcher(Me, GridTerminalSystem, IGC, stateWrapper, guiSeat);

	if (stateWrapper.PState.ShaftStates.Count > 0)
	{
		var cap = stateWrapper.PState.ShaftStates;
		dispatcherService.CreateTask(
			stateWrapper.PState.shaftRadius.Value,
			stateWrapper.PState.corePoint.Value,
			stateWrapper.PState.miningPlaneNormal.Value,
			stateWrapper.PState.layout_cur,
			stateWrapper.PState.maxGen_cur,
			stateWrapper.PState.CurrentTaskGroup,
			stateWrapper.PState.bDense_cur
		);

		/* Load the shaft states, e.g. in progress, planned, ... */
		for (int n = 0; n < Math.Min(dispatcherService.CurrentTask.Shafts.Count, cap.Count()); n++)
			dispatcherService.CurrentTask.Shafts[n].State = (ShaftState)cap[n];
		stateWrapper.PState.ShaftStates = dispatcherService.CurrentTask.Shafts.Select(x => (byte)x.State).ToList();
		E.Log($"Restored task from pstate, shaft count: {cap.Count}");
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
	Drilling              = 3, ///< Descending into the shaft, until there is a reason to leave.
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


/** \brief A slice of the sky. */
public class FlightLevelLease {
	public int  h0;   ///< [m] Lower altitude, measured from the get-above altitude.
	public int  h1;   ///< [m] Higher altitude, measured from the get-above altitude.
	public long agent;///< ID of the owning agent's PB.
	public FlightLevelLease(int _h0, int _h1, long _id) { h0 = _h0; h1 = _h1; agent = _id; }
	public override string ToString() { return $"{h0},{h1},{agent}"; }
	public static FlightLevelLease Parse(string s) {
		var t = s.Split(',');
		return new FlightLevelLease(int.Parse(t[0]), int.Parse(t[1]), long.Parse(t[2]));
	}
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

	public long logLCD;                            ///< Entity ID of the logging screen.
	public List<LockRequest> airspaceLockRequests  ///< Airspace lock queue.
	                         = new List<LockRequest>();
	public List<FlightLevelLease> flightLevels     ///< Assigned flight levels, ordered in ascending order.
	                         = new List<FlightLevelLease>();
	public Vector3D? miningPlaneNormal;
	public Vector3D? corePoint;
	public float? shaftRadius;

	/* Airspace geometry. */
	public Vector3D n_Airspace;  ///< Normal vector of flight planes.
	public Vector3D p_Airspace;  ///< Point at the minimum safe altitude (MSA).
	public float h_msa;          ///< Minimum safe altitude (MSA), set value.
	public float h_msa_cur;      ///< Minimum safe altitude (MSA), value immediately before docking manager re-init.
	public int flightLevelHeight;///< [m] Thickness of newly granted flight level leases.

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
			else if (typeof(T) == typeof(List<FlightLevelLease>))
			{
				var d = res.Split(':');
				var q = new List<FlightLevelLease>();
				foreach (var f in d)
					q.Add(FlightLevelLease.Parse(f));
				return (T)(object)(q);
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

		logLCD               = ParseValue<long>             (values, "logLCD");
		airspaceLockRequests = ParseValue<List<LockRequest>>(values, "airspaceLockRequests") ?? new List<LockRequest>();
		flightLevels         = ParseValue<List<FlightLevelLease>>(values, "flightLevels") ?? new List<FlightLevelLease>();
		miningPlaneNormal    = ParseValue<Vector3D?>        (values, "miningPlaneNormal");
		corePoint = ParseValue<Vector3D?>(values, "corePoint");
		shaftRadius = ParseValue<float?>(values, "shaftRadius");

		/* Airspace geometry. */
		n_Airspace = ParseValue<Vector3D>   (values, "n_Airspace");
		p_Airspace = ParseValue<Vector3D>   (values, "p_Airspace");
		h_msa      = ParseValue<float>      (values, "h_msa");
		h_msa_cur  = ParseValue<float>      (values, "h_msa_cur");
		flightLevelHeight = ParseValue<int> (values, "flightLevelHeight");

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

		/* If some values are not found, set them to defaults. */
		if (flightLevelHeight == 0)
			flightLevelHeight = 14;
		if (maxDepth == 0)
			maxDepth = 10;

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
			"logLCD=" + logLCD,
			"airspaceLockRequests=" + string.Join(":", airspaceLockRequests),
			"flightLevels=" + string.Join(":", flightLevels),
			"miningPlaneNormal=" + (miningPlaneNormal.HasValue ? VectorOpsHelper.V3DtoBroadcastString(miningPlaneNormal.Value) : ""),
			"corePoint=" + (corePoint.HasValue ? VectorOpsHelper.V3DtoBroadcastString(corePoint.Value) : ""),
			"shaftRadius=" + shaftRadius,

			/* Airspace Geometry. */
			"n_Airspace=" + VectorOpsHelper.V3DtoBroadcastString(n_Airspace),
			"p_Airspace=" + VectorOpsHelper.V3DtoBroadcastString(p_Airspace),
			"h_msa=" + h_msa,
			"h_msa_cur=" + h_msa_cur,
			"flightLevelHeight=" + flightLevelHeight,

			/* Task Parameters. */
			"layout=" + layout,
			"layout_cur=" + layout_cur,
			"bDense=" + bDense,
			"bDense_cur=" + bDense_cur,
			"maxGen=" + maxGen,
			"maxGen_cur=" + maxGen_cur,

			/* Job parameters. */
			"maxDepth="  + maxDepth,
			"skipDepth=" + skipDepth,
			"leastDepth=" + leastDepth,
			"adaptiveMode=" + Toggle.C.Check("adaptive-mining"),
			"adjustAltitude=" + Toggle.C.Check("adjust-entry-by-elevation"),
			"safetyDist=" + safetyDist,

			"CurrentTaskGroup=" + CurrentTaskGroup,
			"ShaftStates=" + string.Join(":", ShaftStates),
		};
		//return string.Join("\n", pairs);
		string s = string.Join("\n", pairs);
		Log("Serialised persistent state: " + s, E.LogLevel.Debug);
		return s;
	}

	public override string ToString()
	{
		return Serialize();
	}
}

/////////

public void Save()
{
	/* Remember the MSA, so that we can adjust the MSA set value on reload. 
	 * (Additional docking ports may be added, which are higher or lower.)  */
	stateWrapper.PState.h_msa_cur = dispatcherService.CalcGetAboveAltitude();

	stateWrapper.Save();
}


public Program()
{
	Runtime.UpdateFrequency = UpdateFrequency.Update1;
	Ctor();
}


List<MyIGCMessage> uniMsgs = new List<MyIGCMessage>();
int _cycle_counter = 0;


void Main(string param, UpdateType updateType)
{
	/* Execute only every 4th cycle. 
	 * We need at least this frequency for the mouse
	 * cursor to move smoothly on the GUI screen. */
	_cycle_counter = (++_cycle_counter % 4);
	if (_cycle_counter != 0)
		return;

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
			dispatcherService.dockHost.DepartComplete(m.Source.ToString());
		}
		else if (m.Tag == "apck.depart.request") //TODO: Never used???
		{
			dispatcherService.dockHost.RequestDocking(m.Source, (Vector3D)m.Data, true);
		}
		else if (m.Tag == "apck.docking.request")
		{
			dispatcherService.dockHost.RequestDocking(m.Source, (Vector3D)m.Data);
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
	E.Echo(dispatcherService.ToString());
	dispatcherService.HandleIGC(uniMsgs);

	/* Clean stale flight levels.
	 * (Don't do this immediately after start, because there may be some handshaking in progress.) */
	//FIXME: This might be a performance problem. It is sufficient to only check every 1 s or so.
	if (TickCount > 100)
		dispatcherService.CleanFlightLevelLeases();
	
	/* Adjust get-above altitude.
	 * (Don't do this immediately after start, because there may be some handshaking in progress.) */
	//FIXME: This might be a performance problem. It is sufficient to only check every 1 s or so.
	dispatcherService.AdjustGetAboveAltitude();

	/* Check if we can grant airspace locks.
	 * (Don't do this immediately after start, because there may be some handshaking in progress.) */
	//FIXME: This might be a performance problem. It is sufficient to only check every 1 s or so.
	if (TickCount > 100)
		dispatcherService.GrantAirspaceLocks();

	/* Request GUI data from the agents. */
	if (guiH != null)
	{
		foreach (var s in dispatcherService.subordinates)
			IGC.SendUnicastMessage(s.Id, "report.request", "");
	}

	/* Update the GUI. */
	guiH?.UpdateTaskSummary(dispatcherService);

	/* Handle docking port related stuff. */
	dispatcherService.dockHost.Handle(IGC, TickCount);

	if (rawPanel != null)
	{
		if (guiSeat != null)
		{
			if (guiH == null)
			{
				/* GUI does not yet exist. Create it! */
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
	/* Can only start task when all agents are at base. */
	if (dispatcherService.subordinates.Any(s => s.Report.state != MinerState.Disabled && s.Report.state != MinerState.Idle)) {
		E.Log("Cannot start new task: All agents must be in Idle or Disabled state.");
		return;
	}

	/* Prepare the airspace (ATC). */
	dispatcherService.InitializeAirspace();

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
			n = -dispatcherService.dockHost.GetNormal();
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
				n = -dispatcherService.dockHost.GetNormal();
				E.Log("Using 'first dock connector Backward' as a normal");
			}
		}
	}
	var c = Variables.Get<string>("group-constraint");
	if (!string.IsNullOrEmpty(c))
	{
		dispatcherService.CreateTask(
			Variables.Get<float>("circular-pattern-shaft-radius"),
			pos,
			n,
			stateWrapper.PState.layout,
			stateWrapper.PState.maxGen,
			c,
			stateWrapper.PState.bDense
		);
		dispatcherService.BroadcastStart(c);
	}
	else
		E.Log("To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");
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
							/* Can only start task when all agents are at base. */
							if (dispatcherService.subordinates.Any(s => s.Report.state != MinerState.Disabled && s.Report.state != MinerState.Idle)) {
								E.Log("Cannot start new task: All agents must be in Idle or Disabled state.");
								return;
							}

							/* Prepare the airspace (ATC). */
							dispatcherService.InitializeAirspace();

							dispatcherService.BroadcastStart(c);
							dispatcherService.CreateTask(
								Variables.Get<float>("circular-pattern-shaft-radius"),
								castedSurfacePoint.Value - castedNormal.Value * 10,
								castedNormal.Value,
								stateWrapper.PState.layout,
								stateWrapper.PState.maxGen,
								c,
								stateWrapper.PState.bDense
							);
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
	public DockHost          dockHost; ///< Docking port manager for ATC duties.
	public List<Subordinate> subordinates = new List<Subordinate>();

	public Action<MiningTask> OnTaskUpdate;

	public class Subordinate
	{
		public long Id;               ///< Handle of the agent's programmable block.
		public string ObtainedLock;
		public string Group;
		public TransponderMsg Report; ///< Last received transponder status (position, velocity, ...).
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
	StateWrapper                    stateWrapper;
	public IMyShipController        guiSeat;

	public Dispatcher(
		IMyProgrammableBlock me,
		IMyGridTerminalSystem gts,
		IMyIntergridCommunicationSystem igc,
		StateWrapper stateWrapper,
		IMyShipController guiSeat
	)
	{
		IGC = igc;
		this.stateWrapper = stateWrapper;
		this.guiSeat      = guiSeat;
		
		/* Create a docking port manager. */
		dockHost = new DockHost(me, this, gts);

		/* Adjust the MSA set value.
		 * (Assuming this is called on recompile / world reload only.) */
		float delta = stateWrapper.PState.h_msa_cur - CalcGetAboveAltitude();
		if (delta < 30f) // If the delta has moved more than 30 m, it is carrier which has moved to the next site. (Not newly created docking ports.)
			stateWrapper.PState.h_msa -= delta;
	}

	/**
	 * \brief Enqueues a request from an agent for an airspace lock.
	 * \details The lock must be currently in use by another agent. When the other
	 * agent releases that lock (cooperatively!), it is granted to the applicant.
	 * If a request exists for the applicant it is updated. Requesting an empty
	 * string cancels a request.
	 * \param[in] src The ID of the applicants PB. It is used to send the answer via IGC.
	 * \param[in] lockName The name of the requested lock.
	 */
	private void EnqueueLockRequest(long src, string lockName)
	{
		int idx = stateWrapper.PState.airspaceLockRequests.FindIndex(s => s.id == src);
		if (idx < 0) {
			stateWrapper.PState.airspaceLockRequests.Add(new LockRequest(src, lockName));
			Log("Airspace permission request added to requests queue: " + GetSubordinateName(src) + " / " + lockName, E.LogLevel.Debug);
			return;
		}

		/* Update an existing request. */
		if (lockName == "") {
			/* Request cancelled. */
			stateWrapper.PState.airspaceLockRequests.RemoveAt(idx);
			Log("Airspace permission request by " + GetSubordinateName(src) + " was cancelled.");
		} else {
			stateWrapper.PState.airspaceLockRequests.RemoveAt(idx);
			stateWrapper.PState.airspaceLockRequests.Add(new LockRequest(src, lockName));
			Log("Airspace permission request by " + GetSubordinateName(src) + " was changed: " + lockName);
		}
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
				case MinerState.AscendingInShaft:// Agent is loitering in its shaft.
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
				default:                            // Something went wrong. No experiments. //TODO: Better log an error message!
					return false;
				case MinerState.WaitingForDocking:  // Agent is loitering on its flight plane, or reversing course because recalled.
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
					int idx_fl_applicant = stateWrapper.PState.flightLevels.FindIndex(s => s.agent == agentId);
					int idx_fl_other     = stateWrapper.PState.flightLevels.FindIndex(s => s.agent == sb.Id);
					if (idx_fl_applicant >= 0 && idx_fl_applicant < idx_fl_other)
						continue;

					/* If the other agent is holding position and
					 * waiting for a lock too, no problem (prevents deadlocks).  */
					if (sb.Report.v.Length() <= 0.1 && stateWrapper.PState.airspaceLockRequests.Any(s => s.id == sb.Id))
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
			
		/* Find the applicant with the most urgent need. */
		int         pref    = -1;                  // Index of preferred applicant. (negative means none)
		Subordinate pref_sb = new Subordinate();   // Data of the preferred applicant.
		for (int i = 0; i < stateWrapper.PState.airspaceLockRequests.Count(); ++i) {

			/* Get some information about the applicant. */
			var sb = subordinates.FirstOrDefault(s => s.Id == stateWrapper.PState.airspaceLockRequests[i].id);
			if (sb == null) {
				/* Drop requests from obsolete agents. */
				Log($"Agent {stateWrapper.PState.airspaceLockRequests[i].id} is no longer a subordinate. Dropping its request for airspace lock.", E.LogLevel.Warning);
				stateWrapper.PState.airspaceLockRequests.RemoveAt(i);
				--i;
				continue;
			}

			/* If the lock is not granteable at the moment, try next. */
			if (!IsLockGranteable(stateWrapper.PState.airspaceLockRequests[i].lockName, stateWrapper.PState.airspaceLockRequests[i].id))
				continue;

			/* This lock is granteable. */
			if (pref < 0) {
				/* No other applicant yet, this is our favuorite for now. */
				pref    = i;
				pref_sb = sb;
				continue;
			}

			/* Is the current applicant better than the previous one? */
			if (pref_sb.Report.state == MinerState.Docked && sb.Report.state != MinerState.Docked) {
				/* The applicant is in the air, the other one isn't. */
				pref    = i;
				pref_sb = sb;
				continue;
			}

			/* Take the one which has least fuel/power. */
			float urgency      = sb.Report.Urgency();
			float pref_urgency = pref_sb.Report.Urgency();
			if (urgency > pref_urgency) {
				/* Applicant is shorter on fuel/power than the other one. */
				pref    = i;
				pref_sb = sb;
				continue;
			}

			/* Keep the old favourite. */
		}

		if (pref < 0)
			return; // No lock granteable.
			
		/* Requested lock is not held by any other agent.
		 * Grant immediately to the applicant.
		 *
		 * Also, make sure the agent knows its flight level,
		 * because it is entering controlled airspace.     */
		var cand = stateWrapper.PState.airspaceLockRequests[pref];
		stateWrapper.PState.airspaceLockRequests.RemoveAt(pref);
		
		var sub = subordinates.First(s => s.Id == cand.id);
		sub.ObtainedLock = cand.lockName;

		var data = new MyTuple<string, Vector3D, Vector3D>(
			cand.lockName,        // granted lock
			stateWrapper.PState.p_Airspace // point on the flight level
			+ stateWrapper.PState.n_Airspace * ReserveFlightLevel(cand.id),
			stateWrapper.PState.n_Airspace // normal vector of the flight level
		);

		IGC.SendUnicastMessage(cand.id, "miners", data);
		Log(cand.lockName + " granted to " + GetSubordinateName(cand.id), E.LogLevel.Debug);
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
			if (!(msg.Data is MyTuple<string,MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>, string>)) {
				Log("Ignoring handshake broadcast with malformed transponder data.", E.LogLevel.Warning);
				continue; // Corrupt/malformed message. (Or wrong s/w version on agent.)
			}
				
			var data = (MyTuple<string,MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>, string>)msg.Data;
			if (data.Item3 != Ver) {
				Log($"Ignoring handshake broadcast by {data.Item2.Item1.Item2}: Wrong s/w version {data.Item3} (vs {Ver}).", E.LogLevel.Warning);
				continue;
			}
	
			Log($"Initiated handshake by {data.Item2.Item1.Item2}, group tag: {data.Item1}", E.LogLevel.Notice);

			Subordinate sb;
			if (!subordinates.Any(s => s.Id == msg.Source))
			{
				sb = new Subordinate { Id = msg.Source, Group = data.Item1 };
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

			/* If there is a task, inform the new agent about the normal vector. */
			if (stateWrapper.PState.miningPlaneNormal.HasValue)
			{
				IGC.SendUnicastMessage(msg.Source, "miners.normal", stateWrapper.PState.miningPlaneNormal.Value);
			}
		}

		/* Process agent's status reports. */
		var minerReportChannel = IGC.RegisterBroadcastListener("miners.report");
		while (minerReportChannel.HasPendingMessage)
		{
			var msg = minerReportChannel.AcceptMessage();
			var sub = subordinates.FirstOrDefault(s => s.Id == msg.Source);
			if (sub == null)
				continue; // None of our subordinates.
			var data = (MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>)msg.Data;
			sub.Report.UpdateFromIgc(data);
		}

		/* Finally, process unicast messages.*/
		foreach (var msg in uniMsgs)
		{
			//Log("Dispatcher has received private message from " + msg.Source);
			if (msg.Tag == "create-task")
			{
				/* Can only start task when all other agents are at base. */
				if (subordinates.Any(s => s.Id != msg.Source && s.Report.state != MinerState.Disabled && s.Report.state != MinerState.Idle)) {
					Log("Cannot start new task: All agents must be in Idle or Disabled state.");
					continue;
				}

				/* Prepare the airspace (ATC). */
				InitializeAirspace();

				var data = (MyTuple<float, Vector3D, Vector3D>)msg.Data;

				var sub = subordinates.First(s => s.Id == msg.Source);
				Log("Got new mining task from agent " + sub.Report.name);
				sub.ObtainedLock = LOCK_NAME_GeneralSection;
				CreateTask(
					data.Item1,
					data.Item2,
					data.Item3,
					stateWrapper.PState.layout,
					stateWrapper.PState.maxGen,
					sub.Group,
					stateWrapper.PState.bDense
				);
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
					IGC.SendUnicastMessage(msg.Source, "miners.assign-shaft", new MyTuple<int, Vector3D, Vector3D, MyTuple<float, float, float, bool, bool>>(
						shId,
						entry.Value,
						getabove.Value,
						new MyTuple<float, float, float, bool, bool>(
							stateWrapper.PState.maxDepth,
							stateWrapper.PState.skipDepth,
							stateWrapper.PState.leastDepth,
							Toggle.C.Check("adaptive-mining"),
							Toggle.C.Check("adjust-entry-by-elevation")
						)
					));
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
			var fl = stateWrapper.PState.flightLevels.FirstOrDefault(f => f.agent == s.Id);
			string s_fl = (fl == null ? "n/a" : $"({fl.h0},{fl.h1})");
			E.Echo(s.Report.name + ": F/L=" + s_fl + ", LCK: " + s.ObtainedLock);
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

	/** \brief Recalls all subordinates. */
	public void Recall()
	{
		IGC.SendBroadcastMessage("miners.command", "command:force-finish");
		Log($"Broadcasting Recall");
	}

	/** 
	 * \brief Recalls the n'th subordinate.
	 * \details If the subordinate does not exist, then this method will have
	 * no effect.
	 */
	public void Recall(int n)
	{
		if (n >= 0 && n < subordinates.Count())
			IGC.SendUnicastMessage(subordinates[n].Id, "command", "force-finish");
	}

	public void PurgeLocks()
	{
		IGC.SendBroadcastMessage("miners.command", "command:dispatch");
		stateWrapper.PState.airspaceLockRequests.Clear();
		subordinates.ForEach(x => x.ObtainedLock = "");
		Log($"WARNING! Purging Locks, green light for everybody...");
	}


	public MiningTask CurrentTask;

	/**
	 * \brief Resolves the task into job (shafts).
	 */
	public class MiningTask
	{
		public float R { get; private set; }
		public Vector3D miningPlaneNormal { get; private set; }
		public Vector3D corePoint { get; private set; }
		public Vector3D planeXunit { get; private set; }
		public Vector3D planeYunit { get; private set; }

		public string GroupConstraint { get; private set; }

		public List<MiningShaft> Shafts; // List of of jobs (shafts).

		public MiningTask(
			TaskLayout layout,
			int maxGenerations,
			float shaftRadius,
			Vector3D coreP,
			Vector3D normal,
			string groupConstraint,
			bool bDense
		)
		{
			R = shaftRadius;
			GroupConstraint = groupConstraint;
			miningPlaneNormal = normal;
			corePoint = coreP;
			planeXunit = Vector3D.Normalize(Vector3D.Cross(coreP, normal)); // just any perp to normal will do
			planeYunit = Vector3D.Cross(planeXunit, normal);

			/* Generate the todo list of jobs (shafts). */
			if (layout == TaskLayout.Circle) {
				var radInterval = R * 2f * 0.866f   // 2 cos(30) * R
				                * (bDense ? 0.866f : 1f);
				Shafts = new List<MiningShaft>(maxGenerations * 6 + 1); //TODO: Probably wrong size.
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
			} else if (layout == TaskLayout.Hexagon) {
				Shafts = new List<MiningShaft>(maxGenerations * 6 + 1); //TODO: Probably wrong size.
				Shafts.Add(new MiningShaft());
				int id = 1;
				float d = R * 2f // [m] Distance between two shafts.
				        * (bDense ? 0.866f : 1f);
				Vector2[] _dir = new Vector2[] {
					new Vector2(-0.50000f, 0.86603f), // 120 deg
					new Vector2(-1.00000f, 0.00000f), // 180 deg
					new Vector2(-0.50000f,-0.86603f), // 240 deg
					new Vector2( 0.50000f,-0.86603f), // 300 deg
					new Vector2( 1.00000f, 0.00000f), // 360 deg
					new Vector2( 0.50000f, 0.86603f)  //  60 deg
				};
				for (int g = 1; g < maxGenerations; ++g) {
					var p = new Vector2(d * g, 0f);
					for (int i = 0; i < g * 6; ++i) {
						Shafts.Add(new MiningShaft() { Point = p, Id = id++ });
						p += _dir[i / g] * d;
					}
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

		/** 
		 * \brief Returns a new job, and sets it to "in progres".
		 * \param[out] entry Intersection of the shaft's axis with the mining plane.
		 * \param[out] getAbove Intersection of the shaft's axis with the flight level's airspace lower boundary.
		 * \param[out] id The shaft id.
		 * \param[in] d_safty A minimum safety distance to shafts that are currently in progress.
		 */
		public bool RequestShaft(
			ref Vector3D? entry,
			ref Vector3D? getAbove,
			ref int id,
			float d_safety = 0
		)
		{
			/* Find the first planned shaft. */
			var sh = Shafts.FirstOrDefault(x => x.State == ShaftState.Planned
			     &&  Shafts.All(other =>                     // All other shafts ...
			             other.State != ShaftState.InProgress // ... must be either not in progress ...
			         || (other.Point - x.Point).Length() >= d_safety )); // ... or have the proper safety distance.
			if (sh == null)
				return false; // No more shafts planned.

			/* Transform the entry point into global coordinates. */
			entry = corePoint + planeXunit * sh.Point.X + planeYunit * sh.Point.Y;
			getAbove = entry.Value - miningPlaneNormal * 10.0/*stateWrapper.PState.h_msa*/;//TODO: Probably unused now.
			id = sh.Id;
			sh.State = ShaftState.InProgress;
			return true;
		}

		public class MiningShaft
		{
			public ShaftState State = ShaftState.Planned;
			public Vector2 Point; ///< [m] Point in the mining plane
			public int Id;
		}
	}

	/**
	 * \brief Constructs a local airspace geometry w.r.t. world coordinates.
	 * \note To be called when all agents are either disabled or idle.
	 */
	public void InitializeAirspace()
	{
		Vector3D _n;
		if (guiSeat != null && guiSeat.TryGetPlanetPosition(out _n)) {
			Log("We are on a planet.");//TODO: Remove
			_n = -Vector3D.Normalize(_n - guiSeat.WorldMatrix.Translation); // We are in a planetary gravity.
		} else {
			Log("We are in space.");//TODO: Remove
			_n = dockHost.GetNormal(); // We are outside of planetary gravity.
		}
		stateWrapper.PState.n_Airspace = _n;
		stateWrapper.PState.p_Airspace = dockHost.GetBasePoint() + _n * stateWrapper.PState.h_msa;

		/* Invalidate all old flight levels (if existent). */
		stateWrapper.PState.flightLevels.Clear();

		Log("Airspace initialised to current dispatcher location.", E.LogLevel.Notice);
	}

	/** \brief Calculates the actual get-above altitude. */
	public int CalcGetAboveAltitude()
	{
		Vector3D _n = stateWrapper.PState.n_Airspace;
		Vector3D _d = stateWrapper.PState.p_Airspace - dockHost.GetBasePoint();
		return (int)Math.Round( Vector3D.Dot(_n, _d), 0 );
	}

	/** \brief Checks if the get-above altitude is to be adjusted. */
	public void AdjustGetAboveAltitude()
	{
		int h_is  = CalcGetAboveAltitude();
		int h_set = (int)Math.Round(stateWrapper.PState.h_msa, 0);
		if (h_is == h_set)
			return; // Nothing to be done.

		/* Transform coordinates of all existing flight levels. */
		int d = h_set - h_is; // [m] Amount by which to shift up. (Negativ means down.)
		if (stateWrapper.PState.flightLevels.Count() > 0) {
			d = Math.Min(d, stateWrapper.PState.flightLevels.First().h0);
			foreach (var fl in stateWrapper.PState.flightLevels) {
				fl.h0 -= d;
				fl.h1 -= d;
			}
		}
		stateWrapper.PState.p_Airspace += stateWrapper.PState.n_Airspace * d;
	}

	/**
	 * \brief Reserves a flight level for a subordinate.
	 * \details If the subordinate already has a flight level, it is returned,
	 * regardless of its size.
	 */
	public float ReserveFlightLevel(long id)
	{
		/* Does the subordinate already have a lease? */
		int ex = stateWrapper.PState.flightLevels.FindIndex(s => s.agent == id);
		if (ex >= 0) {
			var fl = stateWrapper.PState.flightLevels[ex];
			Log($"Existing flight level [{fl.h0}, {fl.h1}] for " + GetSubordinateName(fl.agent), E.LogLevel.Debug);
			return 0.5f * (float)(fl.h0 + fl.h1);
		}

		/* Try to find an available space between two leases. */
		int d  = stateWrapper.PState.flightLevelHeight; // [m] Thickness of a flight plane.
		int h0 = 0; // [m] lower boundary
		int i  = 0; // Position where to insert the new lease.
		for (; i < stateWrapper.PState.flightLevels.Count(); ++i) {
			if (stateWrapper.PState.flightLevels[i].h0 - h0 >= d)
				break;
			h0 = stateWrapper.PState.flightLevels[i].h1;
		}

		/* Reserve the space. */
		var fll = new FlightLevelLease(h0, h0 + d, id);
		stateWrapper.PState.flightLevels.Insert(i, fll);
		Log($"Reserved flight level [{fll.h0}, {fll.h1}] for " + GetSubordinateName(id), E.LogLevel.Debug);

		/* Return center of the reserved airspace slice. */
		return .5f * (float)(fll.h0 + fll.h1);
	}

	/**
	 * \brief Cleans all outdated flight level leases.
	 */
	public void CleanFlightLevelLeases()
	{
		for (int i = 0; i < stateWrapper.PState.flightLevels.Count(); ++i) {
			var lease  = stateWrapper.PState.flightLevels[i];
			int idx_sb = subordinates.FindIndex(s => s.Id == lease.agent);
			if (idx_sb < 0)
				continue; // Lease granted to a non-subordinate. (?)
			switch (subordinates[idx_sb].Report.state) {
			case MinerState.Disabled:
			case MinerState.Idle:
			case MinerState.Drilling:
			case MinerState.Docked:
			case MinerState.Maintenance:
			case MinerState.Docking:
				Log($"Withdraw flight level [{lease.h0}, {lease.h1}] for " + subordinates[idx_sb].Report.name, E.LogLevel.Debug);
				stateWrapper.PState.flightLevels.RemoveAt(i);
				--i;
				break;
			}
		}
	}

	/**
	 * \note Call only when all agents are docked!
	 */
	public void CreateTask(
		float r,
		Vector3D corePoint,
		Vector3D miningPlaneNormal,
		TaskLayout layout,
		int maxGenerations,
		string groupConstraint,
		bool bDense
	)
	{
		CurrentTask = new MiningTask(
			layout,
			maxGenerations,
			r,
			corePoint,
			miningPlaneNormal,
			groupConstraint,
			bDense
		);
		OnTaskUpdate?.Invoke(CurrentTask);
		
		stateWrapper.PState.layout_cur        = stateWrapper.PState.layout;
		stateWrapper.PState.bDense_cur        = stateWrapper.PState.bDense;
		stateWrapper.PState.maxGen_cur        = stateWrapper.PState.maxGen;
		stateWrapper.PState.corePoint         = corePoint;
		stateWrapper.PState.shaftRadius       = r;
		stateWrapper.PState.miningPlaneNormal = miningPlaneNormal;
		stateWrapper.PState.ShaftStates       = CurrentTask.Shafts.Select(x => (byte)x.State).ToList();
		stateWrapper.PState.CurrentTaskGroup  = groupConstraint;

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
		bool res = CurrentTask.RequestShaft(ref entry, ref getAbove, ref id, stateWrapper.PState.shaftRadius.Value * 2.0f * stateWrapper.PState.safetyDist);
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
		sb.AppendLine($"Lock queue: {stateWrapper.PState.airspaceLockRequests.Count}");
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


IMyTextPanel rawPanel;     ///< The LCD screen onto which the GUI is drawn.
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


/**
 * \brief Docking port manager.
 */
public class DockHost
{
	Dispatcher             dispatcher;
	List<IMyShipConnector> ports;      ///< All managed docking ports.
	Dictionary<IMyShipConnector, Vector3D> pPositions = new Dictionary<IMyShipConnector, Vector3D>(); // Positions of `ports` during last timestep.

	/**
	 * \brief Detects all available docking ports for the dispatcher.
	 * \param[in] me The programmable block of the dispatcher.
	 */
	public DockHost(IMyProgrammableBlock me, Dispatcher disp, IMyGridTerminalSystem gts)
	{
		dispatcher = disp;
	
		/* Find all assigned docking ports ("docka-min3r"). */
		ports = new List<IMyShipConnector>();
		gts.GetBlocksOfType(ports, c => c.IsSameConstructAs(me) && c.CustomName.Contains(DockHostTag));
		if (ports.Count == 0) {
			E.Log($"Error: No available docking ports. (Name must contain {DockHostTag}.)", E.LogLevel.Critical);
			return;
		}

		/* Verify that all docking ports are colinear. */
		Vector3D _n = ports.First().WorldMatrix.Forward; // Direction of docking ports.
		if (ports.Any(p => Math.Abs(Vector3.Dot(p.WorldMatrix.Forward, _n) - 1) > 1e-4)) {
			E.Log("Error: Docking ports are not colinear.", E.LogLevel.Critical);
			ports.Clear();
			return;
		}
		 
		/* Clear docks, if so requested. */
		if (ClearDocksOnReload)
			ports.ForEach(d => d.CustomData = "");

		/* Cache the docking port's positions. */
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

	/**
	 * \brief Returns the unit direction vector pointing away from the
	 * connectors.
	 */
	public Vector3D GetNormal()
	{
		return ports.First().WorldMatrix.Forward;
	}

	/**
	 * \brief Returns the location of the highest connector.
	 */
	public Vector3D GetBasePoint()
	{
		Vector3D p_base = ports.First().GetPosition();
		Vector3D _n     = this.GetNormal();
		ports.ForEach(port => p_base = (Vector3D.Dot(_n, port.GetPosition() - p_base) > 0 ? port.GetPosition() : p_base));
		return p_base;
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
	List<ActiveElement> controls   = new List<ActiveElement>();
	List<ActiveElement> recallBtns = new List<ActiveElement>(); ///< Buttons for recalling individual agents.
	Dispatcher _dispatcher;
	StateWrapper _stateWrapper;
	Vector2 viewPortSize;
	int current_page = 2; ///< The page that is currently being displayed.
	const int agents_per_page = 8; ///< [-] Number of agents that can be listed per page. 


	public GuiHandler(IMyTextSurface p, Dispatcher dispatcher, StateWrapper stateWrapper)
	{
		_dispatcher = dispatcher;
		_stateWrapper = stateWrapper;
		viewPortSize = p.TextureSize;

		Vector2 btnSize = new Vector2(84, 40);
		float y_btn = 0.85f * viewPortSize.Y; // [px] y-position of button ribbon at 85%
		int   x_btn = 20;

		x_btn += 20;

		var bNextPage = CreateButton(-1, p, new Vector2(40,40), new Vector2(x_btn, y_btn), "<", 1.2f);
		bNextPage.OnClick = xy => {
			current_page = (current_page > 0 ? current_page - 1 : PageCount() - 1);
		};
		AddTipToAe(bNextPage, "Previous page ...");
		controls.Add(bNextPage);

		x_btn += 20 + 10 + 42;

		var bRecall = CreateButton(2, p, btnSize, new Vector2(x_btn, y_btn), "Recall");
		bRecall.OnClick = xy => {
			dispatcher.Recall();
		};
		AddTipToAe(bRecall, "Finish work (broadcast command:force-finish)");
		controls.Add(bRecall);
		
		x_btn += 42 + 10 + 42;

		var bResume = CreateButton(2, p, btnSize, new Vector2(x_btn, y_btn), "Resume");
		bResume.OnClick = xy => {
			dispatcher.BroadcastResume();
		};
		AddTipToAe(bResume, "Resume work (broadcast 'miners.resume' message)");
		controls.Add(bResume);
		
		x_btn += 42 + 10 + 42;

		var bClearLog = CreateButton(2, p, btnSize, new Vector2(x_btn, y_btn), "Clear log");
		bClearLog.OnClick = xy => {
			E.ClearLog();
		};
		controls.Add(bClearLog);
		
		x_btn += 42 + 10 + 42;

		var bPurgeLocks = CreateButton(2, p, btnSize, new Vector2(x_btn, y_btn), "Purge locks");
		bPurgeLocks.OnClick = xy => {
			dispatcher.PurgeLocks();
		};
		AddTipToAe(bPurgeLocks, "Clear lock ownership. Last resort in case of deadlock");
		controls.Add(bPurgeLocks);
		
		x_btn += 42 + 10 + 42;

		var bHalt = CreateButton(2, p, btnSize, new Vector2(x_btn, y_btn), "EMRG HALT");
		bHalt.OnClick = xy => {
			dispatcher.BroadCastHalt();
		};
		AddTipToAe(bHalt, "Halt all activity, restore overrides, release control, clear states");
		controls.Add(bHalt);
		
		x_btn += 42 + 10 + 20;

		var bPrevPage = CreateButton(-1, p, new Vector2(40,40), new Vector2(x_btn, y_btn), ">", 1.2f);
		bPrevPage.OnClick = xy => {
			current_page = (current_page + 1) % PageCount();
		};
		AddTipToAe(bPrevPage, "Next page ...");
		controls.Add(bPrevPage);

		/* Butons for recalling individual drones. */
		for (int i = 0; i < 8; ++i) {
			var bRec = CreateButton(-1, p, new Vector2(30, 30), new Vector2(25, 85 + i * 40), "-", 1.2f);
			int idx = i;
			bRec.OnClick = xy => {
				dispatcher.Recall(idx);
			};
			AddTipToAe(bRec, "Recalls that individual agent only.");
			recallBtns.Add(bRec);
		}

		/* Buttons for the Task/Job parameters page. */

		var bLayout = CreateButton(0, p, new Vector2(110, 30), new Vector2(300, 55), _stateWrapper.PState.layout.ToString(), 0.6f);
		bLayout.OnClick = xy => {
			_stateWrapper.PState.layout = (TaskLayout)(((byte)_stateWrapper.PState.layout + 1) % 2);
			bLayout.fgSprite.Data = _stateWrapper.PState.layout.ToString();
		};
		AddTipToAe(bLayout, "Change the arrangement of the shafts for the next task.");
		controls.Add(bLayout);

		{
			var chkDense = CreateCheckbox(0, new Vector2(30, 30), new Vector2(300,90));
			chkDense.bChecked = _stateWrapper.PState.bDense;
			chkDense.OnClick = xy => {
				chkDense.bChecked = (_stateWrapper.PState.bDense = !_stateWrapper.PState.bDense);
			};
			AddTipToAe(chkDense, "Toggle dense (overlapping) shafts for next task.");
			controls.Add(chkDense);
		}

		var bIncMaxGen = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 125), "+", 1.2f);
		bIncMaxGen.OnClick = xy => {
			++_stateWrapper.PState.maxGen;
		};
		AddTipToAe(bIncMaxGen, "Increase size of next task. (0 is single shaft only)");
		controls.Add(bIncMaxGen);

		var bDecMaxGen = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 125), "-", 1.2f);
		bDecMaxGen.OnClick = xy => {
			_stateWrapper.PState.maxGen = Math.Max(0, --_stateWrapper.PState.maxGen);
		};
		AddTipToAe(bDecMaxGen, "Decrease size of next task. (0 is single shaft only)");
		controls.Add(bDecMaxGen);

		var bIncDepthLimit = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 195), "+", 1.2f);
		bIncDepthLimit.OnClick = xy => {
			_stateWrapper.PState.maxDepth += 5f;
		};
		AddTipToAe(bIncDepthLimit, "Increase depth limit by 5 m");
		controls.Add(bIncDepthLimit);

		var bDecDepthLimit = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 195), "-", 1.2f);
		bDecDepthLimit.OnClick = xy => {
			_stateWrapper.PState.maxDepth = Math.Max(_stateWrapper.PState.leastDepth, _stateWrapper.PState.maxDepth - 5f);
		};
		AddTipToAe(bDecDepthLimit, "Decrease depth limit by 5 m");
		controls.Add(bDecDepthLimit);

		var bIncSkipDepth = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 230), "+", 1.2f);
		bIncSkipDepth.OnClick = xy => {
			_stateWrapper.PState.skipDepth = Math.Min(_stateWrapper.PState.maxDepth, _stateWrapper.PState.skipDepth + 5f);
		};
		AddTipToAe(bIncSkipDepth, "Increase skip-depth by 5 m");
		controls.Add(bIncSkipDepth);

		var bDecSkipDepth = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 230), "-", 1.2f);
		bDecSkipDepth.OnClick = xy => {
			_stateWrapper.PState.skipDepth = Math.Max(0f, _stateWrapper.PState.skipDepth - 5f);
		};
		AddTipToAe(bDecSkipDepth, "Decrease skip-depth by 5 m");
		controls.Add(bDecSkipDepth);

		var bIncLeastDepth = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 265), "+", 1.2f);
		bIncLeastDepth.OnClick = xy => {
			_stateWrapper.PState.leastDepth = Math.Min(_stateWrapper.PState.maxDepth, _stateWrapper.PState.leastDepth + 5f);
		};
		AddTipToAe(bIncLeastDepth, "Increase least-depth by 5 m");
		controls.Add(bIncLeastDepth);

		var bDecLeastDepth = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 265), "-", 1.2f);
		bDecLeastDepth.OnClick = xy => {
			_stateWrapper.PState.leastDepth = Math.Max(0f, _stateWrapper.PState.leastDepth - 5f);
		};
		AddTipToAe(bDecLeastDepth, "Decrease least-depth by 5 m");
		controls.Add(bDecLeastDepth);

		{
			var chkAdaptive = CreateCheckbox(0, new Vector2(30, 30), new Vector2(300,300));
			chkAdaptive.bChecked = Toggle.C.Check("adaptive-mining");
			chkAdaptive.OnClick = xy => {
				chkAdaptive.bChecked = Toggle.C.Invert("adaptive-mining");
			};
			AddTipToAe(chkAdaptive, "Toggle adaptive mining");
			controls.Add(chkAdaptive);
		}

		{
			var chkAdjEntry = CreateCheckbox(0, new Vector2(30, 30), new Vector2(300,335));
			chkAdjEntry.bChecked = Toggle.C.Check("adjust-entry-by-elevation");
			chkAdjEntry.OnClick = xy => {
				chkAdjEntry.bChecked = Toggle.C.Invert("adjust-entry-by-elevation");
			};
			AddTipToAe(chkAdjEntry, "Toggle automatic entry point adjustment");
			controls.Add(chkAdjEntry);
		}
		
		var bIncSafetyDist = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 370), "+", 1.2f);
		bIncSafetyDist.OnClick = xy => {
			_stateWrapper.PState.safetyDist += 0.2f;
		};
		AddTipToAe(bIncSafetyDist, "Increase safety distance by 0.2 (multiple of the shaft diameter).");
		controls.Add(bIncSafetyDist);

		var bDecSafetyDist = CreateButton(0, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 370), "-", 1.2f);
		bDecSafetyDist.OnClick = xy => {
			_stateWrapper.PState.safetyDist = Math.Max(1f, _stateWrapper.PState.safetyDist - 0.2f);
		};
		AddTipToAe(bDecSafetyDist, "Decrease safety distance by 0.2 (multiple of shaft diameter).");
		controls.Add(bDecSafetyDist);
		
		/* Buttons for the Airspace page. */

		var bIncFlH = CreateButton(1, p, new Vector2(30, 30), new Vector2(300 + 55 - 15, 55), "+", 1.2f);
		bIncFlH.OnClick = xy => {
			++_stateWrapper.PState.flightLevelHeight;
		};
		AddTipToAe(bIncFlH, "Increase vertical distance between flight levels.");
		controls.Add(bIncFlH);

		var bDecFlH = CreateButton(1, p, new Vector2(30, 30), new Vector2(300 - 55 + 15, 55), "-", 1.2f);
		bDecFlH.OnClick = xy => {
			_stateWrapper.PState.maxGen = Math.Max(1, --_stateWrapper.PState.flightLevelHeight);
		};
		AddTipToAe(bDecFlH, "Decrease vertical distance between flight levels.");
		controls.Add(bDecFlH);
		
		var bIncGetAbove = CreateButton(1, p, new Vector2(30, 30), new Vector2(700 + 55 - 15, 55), "+", 1.2f);
		bIncGetAbove.OnClick = xy => {
			_stateWrapper.PState.h_msa += 10f;
		};
		AddTipToAe(bIncGetAbove, "Increase minimum safe altitude.");
		controls.Add(bIncGetAbove);

		var bDecGetAbove = CreateButton(1, p, new Vector2(30, 30), new Vector2(700 - 55 + 15, 55), "-", 1.2f);
		bDecGetAbove.OnClick = xy => {
			_stateWrapper.PState.h_msa = Math.Max(0f, _stateWrapper.PState.h_msa - 10f);
		};
		AddTipToAe(bDecGetAbove, "Decrease minimum safe altitude.");
		controls.Add(bDecGetAbove);

		shaftTip = new MySprite(SpriteType.TEXT, "", new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y * 0.9f),
			null, Color.White, "Debug", TextAlignment.CENTER, 0.5f);
		buttonTip = new MySprite(SpriteType.TEXT, "", bRecall.Min - Vector2.UnitY * 17,
			null, Color.White, "Debug", TextAlignment.LEFT, 0.5f);
		taskSummary = new MySprite(SpriteType.TEXT, "No active task", new Vector2(viewPortSize.X / 1.2f, viewPortSize.Y / 20f),
			null, Color.White, "Debug", TextAlignment.CENTER, 0.5f);
	}

	/** \brief Returns the number of pages. */
	int PageCount() {
		return 2 // 1x task/job parameters, 1x flight levels
		     + (_dispatcher.subordinates.Count() + agents_per_page - 1) / agents_per_page;
	}

	/**
	 * \brief Creates a check box control.
	 * \param[in] page The page on which this button will exist. Negative value means "all pages".
	 */
	ActiveElement CreateCheckbox(
		int page,
		Vector2 size,
		Vector2 pos
	)
	{
		/* Create the background sprites. */
		var checkBox = new ActiveElement(page, size, pos);
		checkBox.bkSprite0 = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0, 0), size, Color.CornflowerBlue);
		checkBox.bkSprite1 = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0, 0), size, Color.Black);
		checkBox.fgSprite  = new MySprite(SpriteType.TEXTURE, "Cross",        new Vector2(0, 0), size * 0.8f, Color.DarkKhaki);
		return checkBox;
	}

	/**
	 * \param[in] page The page on which this button will exist. Negative value means "all pages".
	 * \param[in] btnSize The size of the button in [px]. Must be at least 1x1, or undefined behaviour.
	 * \param[in] fsize The font size/scale.
	 */
	ActiveElement CreateButton(
		int page,
		IMyTextSurface p,
		Vector2 btnSize,
		Vector2 posN,
		string label,
		float fsize = 0.5f
	)
	{
		/* Determine the right Y-position for the text. */
		var lbl_ypos = Vector2.Zero;
		lbl_ypos.Y = -p.MeasureStringInPixels(new StringBuilder(label), "Debug", fsize + 0.2f).Y / btnSize.Y;

		var btn = new ActiveElement(page, btnSize, posN);
		btn.bkSprite0 = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0, 0), btnSize, Color.CornflowerBlue);
		btn.bkSprite1 = new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(0, 0), btnSize, Color.Black);
		btn.fgSprite  = new MySprite(SpriteType.TEXT, label, lbl_ypos, Vector2.One, Color.White, "Debug", TextAlignment.CENTER, fsize);
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
			mOffset.X += r.Y;
			mOffset.Y += r.X;
			mOffset = Vector2.Clamp(mOffset, -panel.TextureSize / 2, panel.TextureSize / 2);
		}
		var cursP = mOffset + panel.TextureSize / 2;

		/* Enable/disable buttons for controlling individual drones. */
		for (int i = 0; i < 8; ++i)
			//TODO: Take into account multiple pages of agents.
			recallBtns[i].Visible = (current_page == 2
			                      && i < _dispatcher.subordinates.Count()
														&& _dispatcher.subordinates[i].Report.state != MinerState.Disabled
														&& _dispatcher.subordinates[i].Report.state != MinerState.Idle
														&& !_dispatcher.subordinates[i].Report.bRecalled);

		/* Update the GUI screen contents.*/

		using (var frame = panel.DrawFrame())
		{
			/* Render the agent status table. */
			if (current_page == 2)
				DrawReportRepeater(frame);

			foreach (var ae in controls.Union(shaftControls).Union(recallBtns).Where(x => x.Visible && (x.page == current_page || x.page < 0)))
			{
				if (ae.CheckHover(cursP))
				{
					if (clickE)
						ae.OnClick?.Invoke(cursP);
				}
			}

			/* Render the active elements. */
			foreach (var ae in controls.Union(shaftControls).Union(recallBtns).Where(x => x.Visible && (x.page == current_page || x.page < 0)))
				frame.AddRange(ae.GetSprites());

			if (current_page == 2)
				frame.Add(shaftTip);

			frame.Add(buttonTip);

			if (current_page == 2) {
				frame.Add(taskSummary);

				/* Draw the agent icons. */
				DrawAgents(frame);
			} else if (current_page == 0)
				DrawDispatcherParameters(frame);
			else if (current_page == 1)
				DrawAirspace(frame);

			/* Render mouse cursor. */
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

	/**
	 * \brief Renders the agent icons on the GUI screen.
	 */
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

				var btnSpr = new MySprite(SpriteType.TEXTURE, "AH_BoreSight", posViewport + new Vector2(0, 5), btnSize * 0.8f, Color.Orange);
				btnSpr.RotationOrScale = (float)Math.PI / 2f;
				var btnSprBack = new MySprite(SpriteType.TEXTURE, "Textures\\FactionLogo\\Miners\\MinerIcon_3.dds", posViewport, btnSize * 1.2f, Color.Black);

				frame.Add(btnSprBack);
				frame.Add(btnSpr);
			}
		}
	}

	/**
	 * \brief Renders the dispatcher parameter page.
	 */
	void DrawDispatcherParameters(MySpriteDrawFrame frame) {
		int offY = 0, startY = 20;
		int offX = 0, startX = 65;

		offX += 145;
		frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY + offY    ), new Vector2(290 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Task Parameters", new Vector2(startX + offX, startY + offY - 9), null, Color.White, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 145;
		
		offY += 35;
		offX  = 0;
		
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Task layout",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
		
		offY += 35;
		offX  = 0;
		
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Dense shaft layout", new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offY += 35;
		offX  = 0;
	
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Size (# shaft rings)", new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offX += 55;
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.maxGen.ToString(),  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY += 35;
		offX  = 0;

		offX += 145;
		frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY + offY    ), new Vector2(290 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Job Parameters",  new Vector2(startX + offX, startY + offY - 9), null, Color.White, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 145;

		offY += 35;
		offX  = 0;

		offX += 90;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX,      startY + offY    ), new Vector2(180 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Depth Limit",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offX += 55;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX, startY + offY), new Vector2(110 - 4, 30), Color.DarkGray));
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.maxDepth.ToString("f0") + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY += 35;
		offX  = 0;

		offX += 90;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX,      startY + offY    ), new Vector2(180 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Skip depth",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offX += 55;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX, startY + offY), new Vector2(110 - 4, 30), Color.DarkGray));
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.skipDepth.ToString("f0") + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY += 35;
		offX  = 0;

		offX += 90;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX,      startY + offY    ), new Vector2(180 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Least depth",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offX += 55;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(startX + offX, startY + offY), new Vector2(110 - 4, 30), Color.DarkGray));
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.leastDepth.ToString("f0") + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY += 35;
		offX  = 0;

		offX += 90;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX,      startY + offY    ), new Vector2(180 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Adaptive mining", new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
	
		offY += 35;
		offX  = 0;

		offX += 90;
		//frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",       new Vector2(startX + offX,      startY + offY    ), new Vector2(180 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Adjust entry altitude", new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
		
		offY += 35;
		offX  = 0;
		
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Safety distance", new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;

		offX += 55;
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.safetyDist.ToString("f1"),  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

	}
	
	/**
	 * \brief Renders the airspace page.
	 */
	void DrawAirspace(MySpriteDrawFrame frame) {
		int offY = 0, startY = 20;
		int offX = 0, startX = 65;

		offX += 145;
		frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY + offY    ), new Vector2(290 - 4, 30), Color.Black));
		frame.Add(new MySprite(SpriteType.TEXT, "Airspace", new Vector2(startX + offX, startY + offY - 9), null, Color.White, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 145;
		
		offY += 35;
		offX  = 0;
		
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Flight Level Stride",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
		
		offX += 55;
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.flightLevelHeight + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY = 0;
		offX = 0;
		startX += 400; // Right column
		
		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Min. Safe Altitude (MSA) (Is)",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
		
		offX += 55;
		frame.Add(new MySprite(SpriteType.TEXT, _dispatcher.CalcGetAboveAltitude() + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		offY += 35;
		offX  = 0;

		offX += 90;
		frame.Add(new MySprite(SpriteType.TEXT, "Min. Safe Altitude (MSA) (Set)",      new Vector2(startX + offX + 70, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.RIGHT, 0.6f));
		offX += 90;
		
		offX += 55;
		frame.Add(new MySprite(SpriteType.TEXT, _stateWrapper.PState.h_msa.ToString("f0") + " m",  new Vector2(startX + offX, startY + offY - 9), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.6f));
		offX += 55;

		/* Flight level diagram. */
		int W = (int) viewPortSize.X - 40;          // [px] Width of the diagram
		int H = (int)(viewPortSize.Y * 0.85f) - 120; // [px] Height of the diagram
		frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",  new Vector2(20 + W / 2, 80 + H/2), new Vector2(W, H), Color.DarkGray));
		if (_stateWrapper.PState.flightLevels.Count() == 0)
			return;

		Vector3D _n = _dispatcher.dockHost.GetNormal();
		Vector3D _p = _dispatcher.dockHost.GetBasePoint();
		double l;
		if (_dispatcher.CurrentTask != null) {
			Vector3D _q = _dispatcher.CurrentTask.corePoint;
			l = Vector3D.Cross(_q - _p, _n).Length()
			  + _dispatcher.CurrentTask.R;
		} else
			l = 1;
		
		double dw = W / l; // [px/m] X-scale
		int dh = Math.Min(4, H / _stateWrapper.PState.flightLevels.Last().h1); // [px/m] Y-scale
		bool bBright = false;
		foreach (var fl in _stateWrapper.PState.flightLevels) {
		
			int h = (fl.h1 - fl.h0) * dh; // [px]
			int y = H - fl.h0 * dh - h/2; // [px]
			int h_agent = (int)((float)h * .9f); 

			frame.Add(new MySprite(
				SpriteType.TEXTURE,
				"SquareSimple",
				new Vector2(20 + W / 2, 80 + y),
				new Vector2(W, h),
				(bBright ? Color.Darken(Color.CornflowerBlue, 0.4f) : Color.Darken(Color.CornflowerBlue, 0.5f))));

			frame.Add(new MySprite(
				SpriteType.TEXT,
				_dispatcher.GetSubordinateName(fl.agent),
				new Vector2(20 + W / 2, 80 + y),
				null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.5f));
			
			frame.Add(new MySprite(
				SpriteType.TEXT,
				$"+[{fl.h0} - {fl.h1}] m",
				new Vector2(20 + 10, 80 + y),
				null, Color.DarkKhaki, "Debug", TextAlignment.LEFT, 0.5f));
			
			bBright = !bBright; // Alternating colors.

			/* Subordinate position. */
			var sb = _dispatcher.subordinates.FirstOrDefault(s => s.Id == fl.agent);
			if (sb == null)
				continue; // Lease holder is not a subordinate.

			double d = Vector3D.Cross(sb.Report.WM.Translation - _p, _n).Length(); // [m]
			int    x = (int)(d * dw); // [px]

			var btnSpr = new MySprite(
				SpriteType.TEXTURE,
				"AH_BoreSight",
				new Vector2(20 + x, 80 + y),
				new Vector2(h_agent, h_agent), Color.Orange);
			btnSpr.RotationOrScale = (float)Math.PI / 2f;
			var btnSprBack = new MySprite(
				SpriteType.TEXTURE,
				"Textures\\FactionLogo\\Miners\\MinerIcon_3.dds",
				new Vector2(20 + x, 80 + y),
				new Vector2(h_agent, h_agent), Color.Black);
			frame.Add(btnSprBack);
			frame.Add(btnSpr);

		}
	}

	/**
	 * \brief Renders the agent table on the GUI screen.
	 */
	void DrawReportRepeater(MySpriteDrawFrame frame)
	{
		bool madeHeader = false;
		int offY = 0, startY = 30;
		const float fontHeight = 15; // [px] vertical distance between 2 lines
		foreach (var su in _dispatcher.subordinates)
		{
			int offX = 0, startX = 45;
			int interval = 75; ///< Stride between two columns.

			/* Before first row, render the table header. */
			if (!madeHeader) {
					
				/* System columns. */
				offX += 55;
				frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(110 - 4, 40), Color.Black));
				frame.Add(new MySprite(SpriteType.TEXT, "Name\nState",     new Vector2(startX + offX, startY - 16), null, Color.White, "Debug", TextAlignment.CENTER, 0.5f));
				offX += 55;

				offX += 22;
				frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX,      startY), new Vector2(44 - 4, 40), Color.Black));
				frame.Add(new MySprite(SpriteType.TEXTURE, "IconEnergy",   new Vector2(startX + offX - 7, startY - 7), new Vector2(18, 18), Color.White));
				frame.Add(new MySprite(SpriteType.TEXTURE, "IconHydrogen", new Vector2(startX + offX + 7, startY + 7), new Vector2(16, 16), Color.White));
				offX += 22;

				offX += 22;
				frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(44 - 4, 40), Color.Black));
				frame.Add(new MySprite(SpriteType.TEXTURE, "MyObjectBuilder_Ore/Stone",   new Vector2(startX + offX, startY), new Vector2(40, 40), Color.White));
				offX += 22;

				offX += 22;
				frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(44 - 4, 40), Color.Black));
				frame.Add(new MySprite(SpriteType.TEXTURE, "Arrow",   new Vector2(startX + offX, startY), new Vector2(40, 40), Color.White, "", TextAlignment.CENTER, (float)Math.PI));
				offX += 22;

				offX += 75;
				frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(150 - 4, 40), Color.Black));
				frame.Add(new MySprite(SpriteType.TEXT, "Flags\nDamage",     new Vector2(startX + offX, startY - 16), null, Color.White, "Debug", TextAlignment.CENTER, 0.5f));
				offX += 75;

				/* Dynamic columns. */
				offX += interval / 2;
				if (!su.Report.KeyValuePairs.IsDefault)
					foreach (var kvp in su.Report.KeyValuePairs) {
						frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(startX + offX, startY), new Vector2(interval - 5, 40), Color.Black));
						frame.Add(new MySprite(SpriteType.TEXT, kvp.Item1, new Vector2(startX + offX, startY - 16), null, Color.White, "Debug", TextAlignment.CENTER, 0.5f));
						offX += interval;
					}
				madeHeader = true;
				offY += 40;
			}

			/* Row contents: 1 Row per subordinate */
			offX = 0;

			/* System columns. */
			offX += 55;
			frame.Add(new MySprite(SpriteType.TEXT, su.Report.name + "\n" + su.Report.state.ToString(), new Vector2(startX + offX, startY + offY), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.5f));
			offX += 55;
			offX += 22;
			frame.Add(new MySprite(SpriteType.TEXT,
				Math.Truncate(su.Report.f_bat * 100f).ToString("f0") + "%",
				new Vector2(startX + offX, startY + offY), null, (su.Report.f_bat > su.Report.f_bat_min ? Color.DarkKhaki : Color.DarkRed), "Debug", TextAlignment.CENTER, 0.5f));
			frame.Add(new MySprite(SpriteType.TEXT,
				Math.Truncate(su.Report.f_fuel * 100f).ToString("f0") + "%",
				new Vector2(startX + offX, startY + offY + fontHeight), null, (su.Report.f_fuel > su.Report.f_fuel_min ? Color.DarkKhaki : Color.DarkRed), "Debug", TextAlignment.CENTER, 0.5f));
			offX += 22;
			offX += 22;
			frame.Add(new MySprite(SpriteType.TEXT,
				(su.Report.f_cargo * 100f).ToString("f0") + "%",
				new Vector2(startX + offX, startY + offY), null, (su.Report.f_cargo <= su.Report.f_cargo_max ? Color.DarkKhaki : Color.DarkOrange), "Debug", TextAlignment.CENTER, 0.5f));
			if (su.Report.bUnload)
				frame.Add(new MySprite(SpriteType.TEXTURE, "Danger", new Vector2(startX + offX, startY + offY + 24), new Vector2(22, 22), Color.Red));
			offX += 22;
			offX += 22;
			frame.Add(new MySprite(SpriteType.TEXT,
				su.Report.t_shaft.ToString("f2") + " m",
				new Vector2(startX + offX, startY + offY), null, (su.Report.t_shaft <= _stateWrapper.PState.maxDepth ? Color.DarkKhaki : Color.DarkRed), "Debug", TextAlignment.CENTER, 0.5f));
			frame.Add(new MySprite(SpriteType.TEXT,
				"(" + su.Report.t_ore.ToString("f2") + " m)",
				new Vector2(startX + offX, startY + offY + fontHeight), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.5f));
			offX += 22;
			offX += 75;
			List<string> s_flags = new List<string>();
			if (su.Report.bAdaptive)
				s_flags.Add("A");
			if (su.Report.bRecalled)
				s_flags.Add("R");
			frame.Add(new MySprite(SpriteType.TEXT,
				string.Join(" ", s_flags),
				new Vector2(startX + offX, startY + offY), null, Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.5f));
			frame.Add(new MySprite(SpriteType.TEXT,
				su.Report.damage,
				new Vector2(startX + offX, startY + offY + fontHeight), null, Color.DarkRed, "Debug", TextAlignment.CENTER, 0.5f));
			offX += 75;

			/* Dynamic columns. */
			offX += interval / 2;
			if (!su.Report.KeyValuePairs.IsDefault)
				foreach (var kvp in su.Report.KeyValuePairs) {
					frame.Add(new MySprite(SpriteType.TEXT, kvp.Item2, new Vector2(startX + offX, startY + offY), null,
						Color.DarkKhaki, "Debug", TextAlignment.CENTER, 0.5f));
					offX += interval;
				}
			offY += 40;
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
			/* Convert from [m] to [px] (and from RHS to LHS!). */
			var pos = bPos + new Vector2(t.Point.X, -t.Point.Y) * scale;

			Color mainCol = Color.White;
			if (t.State == ShaftState.Planned)
				mainCol = Color.Darken(Color.CornflowerBlue, 0.1f);
			else if (t.State == ShaftState.Complete)
				mainCol = Color.Darken(Color.CornflowerBlue, 0.4f);
			else if (t.State == ShaftState.InProgress)
				mainCol = Color.Lighten(Color.CornflowerBlue, 0.2f);
			else if (t.State == ShaftState.Cancelled)
				mainCol = Color.Darken(Color.DarkSlateGray, 0.2f);

			var hoverColor = Color.Red;
			
			var btn = new ActiveElement(2, btnSize, pos);
			btn.bkSprite0 = new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0, 0), btnSize, mainCol);
			btn.bkSprite1 = new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(0, 0), btnSize, hoverColor);

			btn.OnHover = p => shaftTip.Data = $"id: {t.Id}, {t.State}";

			btn.OnMouseOut = () =>
			{
				shaftTip.Data = "Hover over shaft for more info,\n tap E to cancel it";
			};

			btn.OnClick = x => OnShaftClick?.Invoke(t.Id);

			shaftControls.Add(btn);
		}
	}

	public void UpdateTaskSummary(Dispatcher d)
	{
		if (d?.CurrentTask != null)
			taskSummary.Data = $"Layout: {_stateWrapper.PState.layout}"
			 + (_stateWrapper.PState.bDense_cur ? ", dense" : "")
			 + $"\nShafts: {d.CurrentTask.Shafts.Count}\nRadius: {d.CurrentTask.R:f2}\n" +
					$"Group: {d.CurrentTask.GroupConstraint}";
	}

	/**
	 * \brief This is an interactive dialog control.
   * \details The element interacts with the mouse cursor, e.g. on click or on
	 * mouse-over.
	 */
	class ActiveElement
	{
		public int  page    = 0;      ///< Page to which this element belongs. Negative values mean "all pages". 
		public bool Visible = true;   ///< An invisible element does not interact with the user.
		public bool bChecked;         ///< Is the checkbox checked (if checkbox).
		//TODO: Should be private:
		//private Vector2 Min;         ///< Upper left corner.
		public Vector2 Min;           ///< Upper left corner.
		private Vector2 Max;          ///< Lower right corner.
		private Vector2 Center;       ///< Center coordinates

		/* The sprites visually representing the element. */
		public MySprite bkSprite0;    ///< Background sprite.
		public MySprite bkSprite1;    ///< Background sprite on mouse over.
		public MySprite fgSprite;     ///< Foreground sprite.
		public Vector2 SizePx;

		/* Event handlers. */
		public Action OnMouseIn { get; set; }
		public Action OnMouseOut { get; set; }
		public Action<Vector2> OnHover { get; set; }
		public Action<Vector2> OnClick { get; set; }
		
		private bool bHover { get; set; }  ///< Is the mouse currently hovering above the element?

		/** \brief Creates an active element in absolute coordinates. */
		public ActiveElement(int _p, Vector2 sizeN, Vector2 posN)
		{
			page = _p;
			SizePx = sizeN;
			Center = posN;
			Min = Center - SizePx / 2f;
			Max = Center + SizePx / 2f;
			bChecked = true;
		}

		/** 
		 * \brief Checks if the mouse cursor is currently hovering above the element.
		 * \details To be called on every cycle. Will trigger OnMouseIn and OnMouseOut
		 * actions.
		 */
		public bool CheckHover(Vector2 cursorPosition)
		{
			bool res = (cursorPosition.X > Min.X) && (cursorPosition.X < Max.X)
			        && (cursorPosition.Y > Min.Y) && (cursorPosition.Y < Max.Y);
			if (res)
			{
				if (!bHover)
					OnMouseIn?.Invoke();
				bHover = true;
				OnHover?.Invoke(cursorPosition);
			}
			else 
			{
				if (bHover)
					OnMouseOut?.Invoke();
				bHover = false;
			}

			return res;
		}

		/** 
		 * \brief Transforms (translate & scale) the sprites to the element's
		 * coordinates, and returns the transformed sprites.
		 * \details To be called on every frame for rendering.
		 */
		public IEnumerable<MySprite> GetSprites()
		{
			if (bHover) {
				var retval = bkSprite1;
				retval.Position = Center + SizePx / 2f * bkSprite1.Position;
				yield return retval;
			} else {
				var retval = bkSprite0;
				retval.Position = Center + SizePx / 2f * bkSprite0.Position;
				yield return retval;
			}
			if (bChecked) {
				var retval = fgSprite;
				retval.Position = Center + SizePx / 2f * fgSprite.Position;
				yield return retval;
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

	public void UpdateFromIgc(MyTuple<
		MyTuple<long, string>,       // Id, name
		MyTuple<MatrixD, Vector3D>,  // WM, v
		MyTuple<byte, string, bool>, // state, damage, bUnload
		ImmutableArray<float>,       // f_bat, f_bat_min, f_fuel, f_fuel_min, f_cargo, f_cargo_max
		MyTuple<bool, bool, float, float>, // bAdaptive, bRecalled, t_shaft, t_ore
		ImmutableArray<MyTuple<string, string>>
	> dto)
	{
		Id            = dto.Item1.Item1;
		name          = dto.Item1.Item2;
		WM            = dto.Item2.Item1;
		v             = dto.Item2.Item2;
		state         = (MinerState)dto.Item3.Item1;
		damage        = dto.Item3.Item2;
		bUnload       = dto.Item3.Item3;
		f_bat         = dto.Item4[0];
		f_bat_min     = dto.Item4[1];
		f_fuel        = dto.Item4[2];
		f_fuel_min    = dto.Item4[3];
		f_cargo       = dto.Item4[4];
		f_cargo_max   = dto.Item4[5];
		bAdaptive     = dto.Item5.Item1;
		bRecalled     = dto.Item5.Item2;
		t_shaft       = dto.Item5.Item3;
		t_ore         = dto.Item5.Item4;
		KeyValuePairs = dto.Item6;
	}

	// Note: Commented out, because only required by the sender of this datagram.
	//public MyTuple<
	//	MyTuple<long, string>,       // Id, name
	//	MyTuple<MatrixD, Vector3D>,  // WM, v
	//	MyTuple<byte, string, bool>, // state, damage, bUnload
	//	ImmutableArray<float>,       // f_bat, f_bat_min, f_fuel, f_fuel_min, f_cargo, f_cargo_max
	//	MyTuple<bool, bool, float, float>, // bAdaptive, bRecalled, t_shaft, t_ore
	//	ImmutableArray<MyTuple<string, string>>
	//> ToIgc()
	//{
	//	var dto = new MyTuple<MyTuple<long, string>, MyTuple<MatrixD, Vector3D>, MyTuple<byte, string, bool>, ImmutableArray<float>, MyTuple<bool, bool, float, float>, ImmutableArray<MyTuple<string, string>>>();
	//	dto.Item1.Item1 = Id;
	//	dto.Item1.Item2 = name;
	//	dto.Item2.Item1 = WM;
	//	dto.Item2.Item2 = v;
	//	dto.Item3.Item1 = (byte)state;
	//	dto.Item3.Item2 = damage;
	//	dto.Item3.Item3 = bUnload;
	//	var arr = ImmutableArray.CreateBuilder<float>(6);
	//	arr.Add(f_bat);
	//	arr.Add(f_bat_min);
	//	arr.Add(f_fuel);
	//	arr.Add(f_fuel_min);
	//	arr.Add(f_cargo);
	//	arr.Add(f_cargo_max);
	//	dto.Item4 = arr.ToImmutableArray();
	//	dto.Item5.Item1 = bAdaptive;
	//	dto.Item5.Item2 = bRecalled;
	//	dto.Item5.Item3 = t_shaft;
	//	dto.Item5.Item4 = t_ore;
	//	dto.Item6 = KeyValuePairs;
	//	return dto;
	//}

	/** 
	 * \brief Calculates a measure for the need to go home.
	 * \details Positive values indicate that the agent is running out of fuel (or power).
	 */
	public float Urgency() {
		return -Math.Min(f_bat  - f_bat_min,
                         f_fuel - f_fuel_min);
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
