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

namespace ConsoleApplication1.UtilityPillockMonolith
{
	class Program : MyGridProgram
	{
#region mdk preserve
		string Ver = "0.9.68";

		//static bool WholeAirspaceLocking = false;
		static long DbgIgc = 0;
		//static long DbgIgc = 76932813351402441; // pertam
		//static long DbgIgc = 141426525525683227; // space
		static bool IsLargeGrid;
		static double Dt = 1 / 60f;
		static float MAX_SP = 104.38f;
		const float G = 9.81f;
		const string DockHostTag = "docka-min3r";
		const string ForwardGyroTag = "forward-gyro";
		bool ClearDocksOnReload = false;

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

		Action<IMyTextPanel> logPanelInitializer = x =>
		{
			x.ContentType = ContentType.TEXT_AND_IMAGE;
			x.FontColor = new Color(r: 0, g: 255, b: 116);
			x.FontSize = 0.65f;
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
									E.DebugLog($"Added {p.CustomName} as GUI panel");
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
									E.DebugLog($"Added {guiSeat.CustomName} as GUI controller");
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
							"set-role", (parts) => CreateRole(parts[2])
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
			E.DebugLog("Assigned role: " + newRole);

			if (newRole == Role.Dispatcher)
			{
				dispatcherService = new Dispatcher(IGC, stateWrapper);

				/* Find all assigned docking ports ("docka-min3r"). */
				var dockingPoints = new List<IMyShipConnector>();
				GridTerminalSystem.GetBlocksOfType(dockingPoints, c => c.IsSameConstructAs(Me) && c.CustomName.Contains(DockHostTag));
				if (ClearDocksOnReload)
					dockingPoints.ForEach(d => d.CustomData = "");

				/* Create a docking port manager. */
				dockHost = new DockHost(dockingPoints, stateWrapper.PState, GridTerminalSystem);

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
					E.DebugLog($"Restored task from pstate, shaft count: {cap.Count}");
				}

				BroadcastToChannel("miners", "dispatcher-change");
			}
			else
			{
				throw new Exception("This script is for dispatcher only (command:set-role:dispatcher).");
				//var b = new List<IMyProgrammableBlock>();
				//GridTerminalSystem.GetBlocksOfType(b, pb => pb.CustomName.Contains("core") && pb.IsSameConstructAs(Me) && pb.Enabled);
				//pillockCore = b.FirstOrDefault();
				//minerController = new MinerController(newRole, GridTerminalSystem, IGC, stateWrapper, GetNTV, Me);
				//if (pillockCore != null)
				//{
				//	minerController.SetControlledUnit(pillockCore);
				//}
				//else
				//{
				//	coreUnit = new APckUnit(Me, stateWrapper.PState, GridTerminalSystem, IGC, GetNTV);
				//	minerController.SetControlledUnit(coreUnit);
				//	minerController.ApckRegistry = new CommandRegistry(
				//		new Dictionary<string, Action<string[]>>
				//			{
				//				{
				//					"create-wp", (parts) => CreateWP(parts)
				//				},
				//				{
				//					"pillock-mode", (parts) => coreUnit?.TrySetState(parts[2])
				//				},
				//			}
				//		);
				//}
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
		/** \note Must have same values in the agent script!  */
		public enum MinerState : byte
		{
			Disabled              = 0,
			Idle                  = 1, 
			GoingToEntry          = 2, ///< Descending to shaft, through shared airspace.
			Drilling              = 3, ///< Descending into the shaft, until there is a reasong to leave.
			GettingOutTheShaft    = 4, 
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

			// cleared by specific command
			public Vector3D? StaticDockOverride { get; set; }

			// cleared by clear-storage-state (task-dependent)
			public MinerState MinerState = MinerState.Idle;
			public Vector3D? miningPlaneNormal;
			public Vector3D? corePoint;
			public float? shaftRadius;

			public float? maxDepth;
			public float? skipDepth;

			public float? lastFoundOreDepth;
			public float CurrentJobMaxShaftYield;

			public float? minFoundOreDepth;
			public float? maxFoundOreDepth;
			public float? prevTickValCount = 0;

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
				if (!string.IsNullOrEmpty(storage))
				{
					E.Echo(storage);

					var values = storage.Split('\n').ToDictionary(s => s.Split('=')[0], s => string.Join("=", s.Split('=').Skip(1)));

					LifetimeAcceptedTasks = ParseValue<int>(values, "LifetimeAcceptedTasks");
					LifetimeOperationTime = ParseValue<int>(values, "LifetimeOperationTime");
					LifetimeWentToMaintenance = ParseValue<int>(values, "LifetimeWentToMaintenance");
					LifetimeOreAmount = ParseValue<float>(values, "LifetimeOreAmount");
					LifetimeYield = ParseValue<float>(values, "LifetimeYield");

					StaticDockOverride = ParseValue<Vector3D?>(values, "StaticDockOverride");
					MinerState = ParseValue<MinerState>(values, "MinerState");
					miningPlaneNormal = ParseValue<Vector3D?>(values, "miningPlaneNormal");
					corePoint = ParseValue<Vector3D?>(values, "corePoint");
					shaftRadius = ParseValue<float?>(values, "shaftRadius");

					maxDepth = ParseValue<float?>(values, "maxDepth");
					skipDepth = ParseValue<float?>(values, "skipDepth");

					lastFoundOreDepth = ParseValue<float?>(values, "lastFoundOreDepth");
					CurrentJobMaxShaftYield = ParseValue<float>(values, "CurrentJobMaxShaftYield");

					minFoundOreDepth = ParseValue<float?>(values, "minFoundOreDepth");
					maxFoundOreDepth = ParseValue<float?>(values, "maxFoundOreDepth");

					MaxGenerations = ParseValue<int>(values, "MaxGenerations");
					CurrentTaskGroup = ParseValue<string>(values, "CurrentTaskGroup");

					ShaftStates = ParseValue<List<byte>>(values, "ShaftStates") ?? new List<byte>();
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
					"StaticDockOverride=" + (StaticDockOverride.HasValue ? VectorOpsHelper.V3DtoBroadcastString(StaticDockOverride.Value) : ""),
					"MinerState=" + MinerState,
					"miningPlaneNormal=" + (miningPlaneNormal.HasValue ? VectorOpsHelper.V3DtoBroadcastString(miningPlaneNormal.Value) : ""),
					"corePoint=" + (corePoint.HasValue ? VectorOpsHelper.V3DtoBroadcastString(corePoint.Value) : ""),
					"shaftRadius=" + shaftRadius,
					"maxDepth=" + maxDepth,
					"skipDepth=" + skipDepth,
					"lastFoundOreDepth=" + lastFoundOreDepth,
					"CurrentJobMaxShaftYield=" + CurrentJobMaxShaftYield,
					"minFoundOreDepth=" + minFoundOreDepth,
					"maxFoundOreDepth=" + maxFoundOreDepth,
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
					//if (minerController?.pCore != null)
					//{
					//	IGC.SendUnicastMessage(minerController.pCore.EntityId, m.Tag, (ImmutableArray<Vector3D>)m.Data);
					//}
					//else
					//{
					//	if (m.Tag.Contains("depart"))
					//	{
					//		var f = new APckTask("fin", coreUnit.CurrentBH);
					//		f.TickLimit = 1;
					//		f.OnComplete = () => IGC.SendUnicastMessage(m.Source, "apck.depart.complete", "");
					//		coreUnit.CreateWP(f);
					//	}
					//	coreUnit.docker.Disconnect();
					//	var path = (ImmutableArray<Vector3D>)m.Data;
					//	if (path.Length > 0)
					//	{
					//		foreach (var p in path)
					//		{
					//			// race c?
					//			Func<Vector3D> trans = () => Vector3D.Transform(p, GetNTV("docking").OrientationUnit.Value);
					//			var bh = new PcBehavior()
					//			{
					//				Name = "r",
					//				IgnoreTFeed = true,
					//				PositionShifter = x => trans(),
					//				TranslationOverride = () => coreUnit.docker.GetPosition(),
					//				AimpointShifter = tv => coreUnit.docker.GetPosition() - GetNTV("docking").OrientationUnit.Value.Forward * 10000,
					//				FwOverride = () => coreUnit.docker.WorldMatrix
					//			};
					//			coreUnit.CreateWP(APckTask.CreateRelP("r", trans, bh));
					//		}
					//	}

					//}

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
			else if ((CurrentRole == Role.Agent) || (CurrentRole == Role.Lone))
			{
				//	minerController.Handle(uniMsgs);
				//	E.Echo("Min3r state: " + minerController.GetState());
				//	E.Echo("Static dock override: " + (stateWrapper.PState.StaticDockOverride.HasValue ? "ON" : "OFF"));
				//	E.Echo("Dispatcher: " + minerController.DispatcherId);
				//	E.Echo("Echelon: " + minerController.Echelon);
				//	E.Echo("HoldingLock: " + minerController.ObtainedLock);
				//	E.Echo("WaitedSection: " + minerController.WaitedSection);
				//	E.Echo($"Estimated shaft radius: {Variables.Get<float>("circular-pattern-shaft-radius"):f2}");
				//	E.Echo("LifetimeAcceptedTasks: " + stateWrapper.PState.LifetimeAcceptedTasks);
				//	E.Echo("LifetimeOreAmount: " + FormatNumberToNeatString(stateWrapper.PState.LifetimeOreAmount));
				//	E.Echo("LifetimeOperationTime: " + TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime).ToString());
				//	E.Echo("LifetimeWentToMaintenance: " + stateWrapper.PState.LifetimeWentToMaintenance);

				//	if (coreUnit != null)
				//	{
				//		if (coreUnit.pc.Pip != Vector3D.Zero)
				//			EmitProjection("agent-dest", coreUnit.pc.Pip, "");
				//		if (coreUnit.pc.PosShift != Vector3D.Zero)
				//			EmitProjection("agent-vel", coreUnit.pc.PosShift, coreUnit.pc.DBG);
				//	}

				//	if (rawPanel != null)
				//	{
				//		SendFeedback($"Version: {Ver}");
				//		SendFeedback($"LifetimeAcceptedTasks: {stateWrapper.PState.LifetimeAcceptedTasks}");
				//		SendFeedback($"LifetimeOreAmount: {FormatNumberToNeatString(stateWrapper.PState.LifetimeOreAmount)}");
				//		SendFeedback($"LifetimeOperationTime: {TimeSpan.FromSeconds(stateWrapper.PState.LifetimeOperationTime)}");
				//		SendFeedback($"LifetimeWentToMaintenance: {stateWrapper.PState.LifetimeWentToMaintenance}");
				//		SendFeedback("\n");
				//		SendFeedback($"CurrentJobMaxShaftYield: {FormatNumberToNeatString(stateWrapper.PState.CurrentJobMaxShaftYield)}");
				//		SendFeedback($"CurrentShaftYield: " + minerController?.CurrentJob?.GetShaftYield());
				//		SendFeedback(minerController?.CurrentJob?.ToString());
				//		FlushFeedbackBuffer();
				//	}
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
						E.DebugLog("WARNING: the normal was not supplied and there is no Control Station available to check if we are in gravity");
						n = -dockHost.GetFirstNormal();
						E.DebugLog("Using 'first dock connector Backward' as a normal");
					}
					else
					{
						Vector3D pCent;
						if (guiSeat.TryGetPlanetPosition(out pCent))
						{
							n = Vector3D.Normalize(pCent - pos);
							E.DebugLog("Using mining-center-to-planet-center direction as a normal because we are in gravity");
						}
						else
						{
							n = -dockHost.GetFirstNormal();
							E.DebugLog("Using 'first dock connector Backward' as a normal");
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
					E.DebugLog("To use this mode specify group-constraint value and make sure you have intended circular-pattern-shaft-radius");
			}
			else
				E.DebugLog("GPStaskHandler is intended for Dispatcher role");
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
							//else if (minerController != null)
							//{
								//if (minerController.LocalDispatcher != null)
								//{
								//	dispatcherService.CreateTask(Variables.Get<float>("circular-pattern-shaft-radius"),
								//			castedSurfacePoint.Value - castedNormal.Value * 10, castedNormal.Value,
								//			Variables.Get<int>("max-generations"), "LocalDispatcher");
								//	minerController.MineCommandHandler();
								//}
								//else if (minerController.DispatcherId.HasValue)
								//	IGC.SendUnicastMessage(minerController.DispatcherId.Value, "create-task",
								//			new MyTuple<float, Vector3D, Vector3D>(Variables.Get<float>("circular-pattern-shaft-radius"),
								//			castedSurfacePoint.Value - castedNormal.Value * 10,
								//			castedNormal.Value));
							//}
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

			void Log(string msg)
			{
				E.DebugLog(msg);
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
				Log("Airspace permission request added to requests queue: " + GetSubordinateName(src) + " / " + lockName);
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
						case MinerState.GettingOutTheShaft: // (Can never happen, because only used by Lone mode.)
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
					Log(cand.lockName + " granted to " + GetSubordinateName(cand.id));
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
						Log("(Dispatcher) received lock-released notification " + sectionName + " from " + GetSubordinateName(msg.Source));
						subordinates.Single(s => s.Id == msg.Source).ObtainedLock = "";
					}
				}

				/* Second, process broadcasted handshakes. */
				var minerHandshakeChannel = IGC.RegisterBroadcastListener("miners.handshake");
				while (minerHandshakeChannel.HasPendingMessage)
				{
					var msg = minerHandshakeChannel.AcceptMessage();
					if (!(msg.Data is string))
						continue; // Corrupt/malformed message.
					
					var data = (string)msg.Data;

					//TODO: Register the agent's script version, so that we can keep them parked.

					Log($"Initiated handshake by {msg.Source}, group tag: {data}");

					Subordinate sb;
					if (!subordinates.Any(s => s.Id == msg.Source))
					{
						sb = new Subordinate { Id = msg.Source, Echelon = (subordinates.Count + 1) * Variables.Get<float>("echelon-offset") + 10f, Group = data };
						subordinates.Add(sb);
						sb.Report = new TransponderMsg() { Id = sb.Id, ColorTag = Color.White };
					}
					else
					{
						sb = subordinates.Single(s => s.Id == msg.Source);
						sb.Group = data;
					}

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
							Log($"Propagating set-value:'{v}' to {msg.Source}");
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
							E.DebugLog($"Shaft {msg.Data} complete");
						}

						// assign and send new shaft points
						Vector3D? entry = Vector3D.Zero;
						Vector3D? getabove = Vector3D.Zero;
						int shId = 0;
						if ((CurrentTask != null) && AssignNewShaft(ref entry, ref getabove, ref shId))
						{
							IGC.SendUnicastMessage(msg.Source, "miners.assign-shaft", new MyTuple<int, Vector3D, Vector3D>(shId, entry.Value, getabove.Value));
							E.DebugLog($"AssignNewShaft with id {shId} sent");
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
				Log($"CurrentTask.RequestShaft");
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

		//MinerController minerController;//TODO: Only used by drones/agents. (Always NULL)
		public class MinerController
		{
			//	public Dispatcher LocalDispatcher { get; set; }
			//	TimerTriggerService tts;

			//	public MiningJob CurrentJob { get; private set; }

			//	public Role CurrentRole;
			//	public long? DispatcherId;
			//	public float? Echelon;
			//	public string ObtainedLock = "";
			//	public string WaitedSection = "";
			//	public bool WaitingForLock;

			//	public bool DockingHandled;

			//	public PersistentState pState
			//	{
			//		get
			//		{
			//			return stateWrapper.PState;
			//		}
			//	}

			//	Func<string, TargetTelemetry> ntv;
			//	StateWrapper stateWrapper;
			//	public MinerController(Role role, IMyGridTerminalSystem gts, IMyIntergridCommunicationSystem igc, StateWrapper stateWrapper, 
			//			Func<string, TargetTelemetry> GetNTV, IMyTerminalBlock me)
			//	{
			//		ntv = GetNTV;
			//		this.CurrentRole = role;
			//		this.gts = gts;
			//		IGC = igc;
			//CurrentJob = new MiningJob(this);
			//		this.stateWrapper = stateWrapper;

			//		fwReferenceBlock = GetSingleBlock<IMyGyro>(b => b.CustomName.Contains(ForwardGyroTag) && b.IsSameConstructAs(me));
			//		remCon = GetSingleBlock<IMyRemoteControl>(b => b.IsSameConstructAs(me));
			//		docker = GetSingleBlock<IMyShipConnector>(b => b.IsSameConstructAs(me));
			//		gts.GetBlocksOfType(drills, d => d.IsSameConstructAs(me));
			//		gts.GetBlocksOfType(allContainers, d => d.IsSameConstructAs(me) && d.HasInventory && ((d is IMyCargoContainer) || (d is IMyShipDrill) || (d is IMyShipConnector)));
			//		gts.GetBlocksOfType(batteries, b => b.IsSameConstructAs(me));
			//		gts.GetBlocksOfType(tanks, b => b.IsSameConstructAs(me));

			//		List<IMyTimerBlock> triggers = new List<IMyTimerBlock>();
			//		gts.GetBlocksOfType(triggers, b => b.IsSameConstructAs(me));
			//		tts = new TimerTriggerService(triggers);

			//		float maxR = 0;
			//		float padding = me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2f : 1.5f;
			//		foreach (var d in drills)
			//		{
			//			var r = Vector3D.Reject(d.GetPosition() - fwReferenceBlock.GetPosition(), fwReferenceBlock.WorldMatrix.Forward).Length();
			//			maxR = (float)Math.Max(r + padding, maxR);
			//		}
			//		Variables.Set("circular-pattern-shaft-radius", maxR);

			//		var bs = new List<IMyRadioAntenna>();
			//		gts.GetBlocksOfType(bs, b => b.IsSameConstructAs(me));
			//		antenna = bs.FirstOrDefault();

			//		var ls = new List<IMyLightingBlock>();
			//		gts.GetBlocksOfType(ls, b => b.IsSameConstructAs(me));
			//		refLight = ls.FirstOrDefault();

			//		gts.GetBlocksOfType(allFunctionalBlocks, b => b.IsSameConstructAs(me));
			//	}
			//	public void SetControlledUnit(IMyProgrammableBlock pCore)
			//	{
			//		this.pCore = pCore;
			//	}
			//	public void SetControlledUnit(APckUnit unit)
			//	{
			//		embeddedUnit = unit;
			//	}

			//	public void SetState()
			//	{
			//		tts.TryTriggerNamedTimer(GetState() + ".OnExit");
			//		Log("SetState: " + GetState() + "=>" + newState);
			//		tts.TryTriggerNamedTimer(newState + ".OnEnter");

			//	}

			//	public void Halt()
			//	{
			//		CheckBatteriesAndIntegrity(1, 1);
			//		CommandAutoPillock("command:pillock-mode:Disabled", u => u.pc.SetState(PillockController.State.Disabled));
			//		drills.ForEach(d => d.Enabled = false);
			//		stateWrapper.ClearPersistentState();
			//	}

			//	public T GetSingleBlock<T>(Func<IMyTerminalBlock, bool> pred) where T : class
			//	{
			//		var blocks = new List<IMyTerminalBlock>();
			//		gts.GetBlocksOfType(blocks, b => ((b is T) && pred(b)));
			//		return blocks.First() as T;
			//	}

			//	public void CreateTask()
			//	{
			//		var ng = remCon.GetNaturalGravity();
			//		if (ng != Vector3D.Zero)
			//			pState.miningPlaneNormal = Vector3D.Normalize(ng);
			//		else
			//			pState.miningPlaneNormal = fwReferenceBlock.WorldMatrix.Forward;

			//		double elevation;
			//		if (remCon.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation))

			//		}
			//	}

			//	public void Handle(List<MyIGCMessage> uniMsgs)
			//	{
			//		E.Echo(embeddedUnit != null ? "Embedded APck" : pCore.CustomName);
			//		embeddedUnit?.Handle(TickCount, E.Echo);

			//		if ((CurrentJob != null) && (!WaitingForLock))
			//		{
			//		}

			//		var j = CurrentJob;

			//		var minerChannel = IGC.RegisterBroadcastListener("miners");

			//		foreach (var msg in uniMsgs)
			//		{
			//			if (!msg.Tag.Contains("set-vectors"))
			//				LogMsg(msg, false);
			//			if ((msg.Tag == "miners.assign-shaft") && (msg.Data is MyTuple<int, Vector3D, Vector3D>) && (CurrentRole == Role.Agent))
			//			{
			//				var data = (MyTuple<int, Vector3D, Vector3D>)msg.Data;
			//				if (j != null)
			//				{
			//					j.SetShaftVectors(data.Item1, data.Item2, data.Item3);
			//					Log("Got new ShaftVectors");
			//					Dispatch();
			//				}
			//			}

			//"miners.handshake.reply"

			//			if (msg.Tag == "miners.handshake.reply")
			//			{
			//				Log("Received reply from dispatcher " + msg.Source);
			//				DispatcherId = msg.Source;
			//			}

			//			if (msg.Tag == "miners.echelon")
			//			{
			//				Log("Was assigned an echelon of " + msg.Data);
			//				Echelon = (float)msg.Data;
			//			}

			//			if (msg.Tag == "miners.normal")
			//			{
			//				var normal = (Vector3D)msg.Data;
			//				Log("Was assigned a normal of " + normal);
			//				pState.miningPlaneNormal = normal;
			//			}

			//			if (msg.Tag == "miners.resume")
			//			{
			//				var normal = (Vector3D)msg.Data;
			//				Log("Received resume command. Clearing state, running MineCommandHandler, assigned a normal of " + normal);
			//				stateWrapper.ClearPersistentState();
			//				pState.miningPlaneNormal = normal;
			//				MineCommandHandler();
			//			}

			//			if (msg.Tag == "command")
			//			{
			//				if (msg.Data.ToString() == "force-finish")
			//					FinishAndDockHandler();
			//				if (msg.Data.ToString() == "mine")
			//					MineCommandHandler();
			//			}

			//			if (msg.Tag == "set-value")
			//			{
			//				var parts = ((string)msg.Data).Split(':');
			//				Log($"Set value '{parts[0]}' to '{parts[1]}'");
			//				Variables.Set(parts[0], parts[1]);
			//			}

			//			if (msg.Data.ToString().Contains("common-airspace-lock-granted"))
			//			{
			//				var sectionName = msg.Data.ToString().Split(':')[1];

			//				if (!string.IsNullOrEmpty(ObtainedLock) && (ObtainedLock != sectionName))
			//				{
			//					//ReleaseLock(ObtainedLock);
			//					Log($"{sectionName} common-airspace-lock hides current ObtainedLock {ObtainedLock}!");
			//				}
			//				ObtainedLock = sectionName;
			//				Log(sectionName + " common-airspace-lock-granted");

			// can fly!
			//				if (WaitedSection == sectionName)
			//					Dispatch();
			//			}

			//			if (msg.Tag == "report.request")
			//			{
			//				var report = new TransponderMsg();
			//				report.Id = IGC.Me;
			//				report.WM = fwReferenceBlock.WorldMatrix;
			//				report.ColorTag = refLight?.Color ?? Color.White;
			//				CurrentJob?.UpdateReport(report);
			//				IGC.SendBroadcastMessage("miners.report", report.ToIgc());
			//			}
			//		}

			//		while (minerChannel.HasPendingMessage)
			//		{
			//			var msg = minerChannel.AcceptMessage();
			//			LogMsg(msg, false);
			// do some type checking
			//if ((msg.Data != null) && (msg.Data is Vector3D))
			//			if (msg.Data != null)
			//			{
			//				if (msg.Data.ToString().Contains("common-airspace-lock-released"))
			//				{
			//					var sectionName = msg.Data.ToString().Split(':')[1];
			//					if (CurrentRole == Role.Agent)
			//					{
			//						Log("(Agent) received lock-released notification " + sectionName + " from " + msg.Source);
			//					}
			//				}

			//				if (CurrentRole == Role.Agent)
			//				{
			//					if (msg.Data.ToString() == "dispatcher-change")
			//					{
			//						DispatcherId = null;
			//						Scheduler.C.RepeatWhile(() => !DispatcherId.HasValue).After(1000).RunCmd(() => 
			//								BroadcastToChannel("miners.handshake", Variables.Get<string>("group-constraint")));
			//					}
			//				}
			//			}
			//		}

			//	}

			//Action<MinerController> callback;
			//	Queue<Action<MinerController>> waitedActions = new Queue<Action<MinerController>>();
			//	public void WaitForDispatch(string sectionName, Action<MinerController> callback)
			//	{
			//		WaitingForLock = true;
			//		if (!string.IsNullOrEmpty(sectionName))
			//			WaitedSection = sectionName;
			//		waitedActions.Enqueue(callback);
			//		Log("WaitForDispatch section \"" + sectionName + "\", callback chain: " + waitedActions.Count);
			//	}

			//	public void Dispatch()
			//	{
			//		WaitingForLock = false;
			//		WaitedSection = "";
			//		var count = waitedActions.Count;
			//		if (count > 0)
			//		{
			//			Log("Dispatching, callback chain: " + count);
			//			var a = waitedActions.Dequeue();
			//			a.Invoke(this);
			//		}
			//		else
			//			Log("WARNING: empty Dispatch()");
			//	}

			//	public void BroadcastToChannel<T>(string tag, T data)
			//	{
			//		IGC.SendBroadcastMessage(tag, data, TransmissionDistance.TransmissionDistanceMax);
			//		LogMsg(data, true);
			//	}

			//	public void UnicastToDispatcher<T>(string tag, T data)
			//	{
			//		if (DispatcherId.HasValue)
			//			IGC.SendUnicastMessage(DispatcherId.Value, tag, data);
			//	}

			//	public void Log(object msg)
			//	{
			//		E.DebugLog($"MinerController -> {msg}");
			//	}

			//	public void LogMsg(object msg, bool outgoing)
			//	{
			//		string data = msg.GetType().Name;
			//		if (msg is string)
			//			data = (string)msg;
			//		else if ((msg is ImmutableArray<Vector3D>) || (msg is Vector3D))
			//			data = "some vector(s)";

			//		if (Toggle.C.Check("log-message"))
			//		{
			//			if (!outgoing)
			//				E.DebugLog($"MinerController MSG-IN -> {data}");
			//			else
			//				E.DebugLog($"MinerController MSG-OUT -> {data}");
			//		}
			//	}

			//	public Action InvalidateDockingDto;
			//	public IMyProgrammableBlock pCore;
			//	APckUnit embeddedUnit;
			//	public IMyGridTerminalSystem gts;
			//	public IMyIntergridCommunicationSystem IGC;
			//	public IMyRemoteControl remCon;
			//	public List<IMyTerminalBlock> allContainers = new List<IMyTerminalBlock>();
			//	public IMyTerminalBlock fwReferenceBlock;
			//	public List<IMyShipDrill> drills = new List<IMyShipDrill>();
			//	public IMyShipConnector docker;
			//	public IMyRadioAntenna antenna;
			//	public List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
			//	public List<IMyGasTank> tanks = new List<IMyGasTank>();
			//	public IMyLightingBlock refLight;
			//	public List<IMyTerminalBlock> allFunctionalBlocks = new List<IMyTerminalBlock>();

			//	public void ResumeJobOnWorldLoad()
			//	{
			//		CurrentJob = new MiningJob(this);
			//		CurrentJob.SessionStartedAt = DateTime.Now;
			//		// TODO: restore some stats stuff
			//	}

			//	public void MineCommandHandler()
			//	{
			//		CurrentJob = new MiningJob(this);
			//		CurrentJob.SessionStartedAt = DateTime.Now;
			//		pState.LifetimeAcceptedTasks++;
			//		pState.maxDepth = Variables.Get<float>("depth-limit");
			//		pState.skipDepth = Variables.Get<float>("skip-depth");
			//		if (!TryResumeFromDock())
			//		{
			//			CurrentJob.Start();
			//		}
			//	}

			//	public void SkipCommandHandler()
			//	{
			//		if (CurrentJob != null)
			//		{
			//			CurrentJob.SkipShaft();
			//		}
			//	}

			//	public void SetStaticDockOverrideHandler(string[] cmdString)
			//	{
			//		if ((cmdString.Length > 2) && (cmdString[2] == "clear"))
			//			pState.StaticDockOverride = null;
			//		else
			//			pState.StaticDockOverride = fwReferenceBlock.WorldMatrix.Translation;
			//	}

			//	public Vector3D AddEchelonOffset(Vector3D pt, Vector3D normal)
			//	{
			//		if (Echelon.HasValue)
			//		{
			//			return pt - normal * Echelon.Value;
			//		}
			//		return pt;
			//	}

			//	public bool TryUsingStaticDock(bool generateApproachWp)
			//	{
			//		if (pState.StaticDockOverride.HasValue)
			//		{
			//			string dFinal = "command:create-wp:Name=StaticDock,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pState.StaticDockOverride.Value);

			//			if (!WholeAirspaceLocking)
			//				ReleaseLock(LOCK_NAME_GeneralSection);

			//			if (Echelon.HasValue)
			//			{
			//				dFinal = "command:create-wp:Name=StaticDock.echelon,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(AddEchelonOffset(pState.StaticDockOverride.Value)) 
			//					+ ":" + dFinal;
			//			}

			//			if (generateApproachWp)
			//			{
			//				else
			//				{
			/*
			double elevaton;
			remCon.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevaton);
			*/
			// elevation below surface is Abs
			//					var dockP = pState.StaticDockOverride.Value;
			//					Vector3D plCenter;
			//					remCon.TryGetPlanetPosition(out plCenter);
			//					var dockAlt = dockP - plCenter;
			//					var myAlt = (fwReferenceBlock.GetPosition() - plCenter);
			//					var plNorm = Vector3D.Normalize(myAlt);
			//					var alt = dockAlt.Length() > myAlt.Length() ? dockAlt : myAlt;
			//					var approachP = plCenter + plNorm * (alt.Length() + 100f);
			//					CommandAutoPillock("command:create-wp:Name=StaticDock.approachP,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(approachP) + ":" + dFinal);
			//				}
			//			}
			//			else
			//			{
			//				CommandAutoPillock(dFinal);
			//			}

			//			return true;
			//		}
			//		return false;
			//	}

			//	public void ArrangeDocking()
			//	{
			//		if (pState.StaticDockOverride.HasValue)
			//		{
			//			if (TryUsingStaticDock(finishSession))
			//			{
			// Static docking, we are safe at own echelon
			//				if (!WholeAirspaceLocking)
			//					ReleaseLock(LOCK_NAME_GeneralSection);
			//			}
			//		}
			//		else
			//		{
			//			if (DispatcherId.HasValue)
			//			{
			// Multi-agent mode, dynamic docking, respect shared space
			// Release lock as we are safe at own echelon while sitting on WaitingForDocking
			//				if (!WholeAirspaceLocking)
			//					ReleaseLock(LOCK_NAME_GeneralSection);
			//				InvalidateDockingDto?.Invoke();
			//				IGC.SendUnicastMessage(DispatcherId.Value, "apck.docking.request", docker.GetPosition());
			//			}
			//			else
			//			{
			// Lone mode, dynamic docking, don't care about shared space. Docking is arranged by APck.
			//				if (!finishSession)
			//				{
			//					CommandAutoPillock("command:request-docking");
			//				}
			//			
			//			}
			//		}
			//	}

			//	public void FinishAndDockHandler()
			//	{
			//		if (docker.Status == MyShipConnectorStatus.Connected)
			//		{
			//			batteries.ForEach(b => b.ChargeMode = ChargeMode.Recharge);
			//			tanks.ForEach(b => b.Stockpile = true);
			//		}
			//		else
			//		{
			//			drills.ForEach(dr => dr.Enabled = false);
			//ReleaseLock(LOCK_NAME_GeneralSection);
			//			WaitedSection = "";
			//			waitedActions.Clear();
			//			EnterSharedSpace(LOCK_NAME_ForceFinishSection, (mc) =>
			//			{
			//				CurrentJob = CurrentJob ?? new MiningJob(this);
			//				ArrangeDocking();
			//			});
			//		}
			//	}

			//	public bool TryResumeFromDock()
			//	{
			//		if (docker.Status == MyShipConnectorStatus.Connected)
			//		{
			//			
			//		}
			//		return false;
			//	}

			//	public void EnterSharedSpace(string sectionName, Action<MinerController> task)
			//	{
			//		if (CurrentRole == Role.Agent)
			//		{
			//			BroadcastToChannel("miners", "common-airspace-ask-for-lock:" + sectionName);
			//			WaitForDispatch(sectionName, task);
			//		}
			//		else
			//		{
			//			task(this);
			//		}
			//	}

			//	public void ReleaseLock(string sectionName)
			//	{
			//		if (ObtainedLock == sectionName)
			//		{
			//			ObtainedLock = null;
			//			if (CurrentRole == Role.Agent)
			//			{
			//				BroadcastToChannel("miners", "common-airspace-lock-released:" + sectionName);
			//				Log($"Released lock: {sectionName}");
			//			}
			//		}
			//		else
			//		{
			//			Log("Tried to release non-owned lock section " + sectionName);
			//		}
			//	}

			//	public CommandRegistry ApckRegistry;
			//	public void CommandAutoPillock(string cmd, Action<APckUnit> embeddedAction = null)
			//	{
			//		E.DebugLog("CommandAutoPillock: " + cmd);
			//		if (embeddedUnit != null)
			//		{
			//			if (embeddedAction != null)
			//			{
			//				embeddedAction(embeddedUnit);
			//			}
			//			else
			//			{
			//Log($"'{cmd}' is not support for embedded unit yet");

			//				var cmds = cmd.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('[', ']')).ToList();
			//				foreach (var i in cmds)
			//				{
			//					string[] cmdParts = i.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
			//					if (cmdParts[0] == "command")
			//					{
			//						ApckRegistry.RunCommand(cmdParts[1], cmdParts);
			//					}
			//				}
			//			}
			//		}
			//		else
			//		{
			//			if (IGC.IsEndpointReachable(pCore.EntityId))
			//			{
			//				IGC.SendUnicastMessage(pCore.EntityId, "apck.command", cmd);
			//			}
			//			else
			//			{
			//				throw new Exception($"APck {pCore.EntityId} is not reachable");
			//			}
			//		}
			//if (!pCore.TryRun(cmd))
			//throw new Exception("APck failure");
			//	}


			//	DateTime lastCheckStamp;
			//	bool CheckBatteriesAndIntegrityThrottled(float desiredBatteryLevel, float desiredGasLevel)
			//	{
			//		var dtNow = DateTime.Now;
			//		if ((dtNow - lastCheckStamp).TotalSeconds > 60)
			//		{
			//			lastCheckStamp = dtNow;
			//			return CheckBatteriesAndIntegrity(desiredBatteryLevel, desiredGasLevel);
			//		}
			//		return true;
			//	}

			//	bool CheckBatteriesAndIntegrity(float desiredBatteryLevel, float desiredGasLevel)
			//	{
			//		allFunctionalBlocks.ForEach(x => TagDamagedTerminalBlocks(x, GetMyTerminalBlockHealth(x)));
			//		if (allFunctionalBlocks.Any(b => !b.IsFunctional))
			//		{
			//			if (antenna != null)
			//				antenna.CustomName = antenna.CubeGrid.CustomName + "> Damaged. Fix me asap!";
			//			allFunctionalBlocks.Where(b => !b.IsFunctional).ToList().ForEach(b => E.DebugLog($"{b.CustomName} is damaged or destroyed"));
			//			return false;
			//		}
			//		float storedPower = 0;
			//		float maxPower = 0;
			//		foreach (var b in batteries)
			//		{
			//			maxPower += b.MaxStoredPower;
			//			storedPower += b.CurrentStoredPower;
			//		}
			//		double gasAvg = 0;
			//		foreach (var b in tanks)
			//		{
			//			gasAvg += b.FilledRatio;
			//		}

			//		if (tanks.Any() && (gasAvg / tanks.Count < desiredGasLevel))
			//		{
			//			if (antenna != null)
			//				antenna.CustomName = $"{antenna.CubeGrid.CustomName}> Maintenance. Gas level: {gasAvg / tanks.Count:f2}/{desiredGasLevel:f2}";
			//			return false;
			//		}
			//		else if (storedPower / maxPower < desiredBatteryLevel)
			//		{
			//			if (antenna != null)
			//				antenna.CustomName = $"{antenna.CubeGrid.CustomName}> Maintenance. Charge level: {storedPower / maxPower:f2}/{desiredBatteryLevel:f2}";
			//			return false;
			//		}
			//		else
			//		{
			//			return true;
			//		}
			//	}

			//	float GetMyTerminalBlockHealth(IMyTerminalBlock block)
			//	{
			//		IMySlimBlock slimblock = block.CubeGrid.GetCubeBlock(block.Position);
			//		if (slimblock != null)
			//			return (slimblock.BuildIntegrity - slimblock.CurrentDamage) / slimblock.MaxIntegrity;
			//		else
			//			return 1f;
			//	}

			//	void TagDamagedTerminalBlocks(IMyTerminalBlock myTerminalBlock, float health, bool onlyNonFunctional)
			//	{
			//		string name = myTerminalBlock.CustomName;
			//		if ((health < 1f) && (!onlyNonFunctional || !myTerminalBlock.IsFunctional))
			//		{
			//			if (!(myTerminalBlock is IMyRadioAntenna) && !(myTerminalBlock is IMyBeacon))
			//			{
			//				myTerminalBlock.SetValue("ShowOnHUD", true);
			//			}
			//			string taggedName;
			//			if (name.Contains("||"))
			//			{
			//				string pattern = @"(?<=DAMAGED: )(?<label>\d+)(?=%)";
			//				System.Text.RegularExpressions.Regex r = new System.Text.RegularExpressions.Regex(pattern);
			//				taggedName = r.Replace(
			//					name,
			//					delegate (System.Text.RegularExpressions.Match m)
			//					{
			//						return (health * 100).ToString("F0");
			//					});
			//			}
			//			else
			//			{
			//				taggedName = string.Format("{0} || DAMAGED: {1}%", name, health.ToString("F0"));
			//				Log($"{name} was damaged. Showing on HUD.");
			//			}
			//			myTerminalBlock.CustomName = taggedName;
			//		}
			//		else
			//		{
			//			UntagAndHide(myTerminalBlock);
			//		}
			//	}

			//	void UntagAndHide(IMyTerminalBlock myTerminalBlock)
			//	{
			//		if (myTerminalBlock.CustomName.Contains("||"))
			//		{
			//			string name = myTerminalBlock.CustomName;
			//			myTerminalBlock.CustomName = name.Split('|')[0].Trim();
			//			if (!(myTerminalBlock is IMyRadioAntenna) && !(myTerminalBlock is IMyBeacon))
			//			{
			//				myTerminalBlock.SetValue("ShowOnHUD", false);
			//			}

			//			Log($"{myTerminalBlock.CustomName} was fixed.");
			//		}
			//	}

			//	public class MiningJob
			//	{
			//		protected MinerController c;

			//		public MiningJob(MinerController minerController)
			//		{
			//			c = minerController;
			//		}

			//		public void Start()
			//		{
			//			if (c.CurrentRole == Role.Agent)
			//			{
			//				c.UnicastToDispatcher("request-new", "");
			//				c.WaitForDispatch("", mc => {
			//					c.EnterSharedSpace(LOCK_NAME_GeneralSection, x =>
			//					{
			//						x.drills.ForEach(d => d.Enabled = false);
			//						var depth = -15;
			//						
			//						c.CommandAutoPillock(entryBeh);
			//					});
			//				});
			//			}
			//			else if (c.CurrentRole == Role.Lone)
			//			{
			//				c.pState.maxDepth = Variables.Get<float>("depth-limit");
			//				c.pState.skipDepth = Variables.Get<float>("skip-depth");

			//				c.CommandAutoPillock(entryBeh);
			//			}
			//		}

			//		public void SkipShaft()
			//		{
			//			if (c.pState.CurrentJobMaxShaftYield < prevTickValCount + currentShaftValTotal - preShaftValTotal)
			//				c.pState.CurrentJobMaxShaftYield = prevTickValCount + currentShaftValTotal - preShaftValTotal;
			//
			//			if (Toggle.C.Check("adaptive-mining"))
			//			{
			// can CurrentJobMaxShaftYield be zero?
			// does not work as the prevTickValCount reflects the whole ore amount, not only from the current shaft
			//				if (!lastFoundOreDepth.HasValue || ((prevTickValCount + currentShaftValTotal - preShaftValTotal) / c.pState.CurrentJobMaxShaftYield < 0.5f))
			//				{
			//				}
			//			}

			//			AccountChangeShaft();
			//			lastFoundOreDepth = null;

			//			var depth = -15;

			//			if (c.CurrentRole == Role.Agent)
			//			{
			// WaitingForLockInShaft -> ChangingShaft

			//c.SetState(State.WaitingForDispatch);

			//				c.WaitForDispatch("", mc => {
			//					c.EnterSharedSpace(LOCK_NAME_GeneralSection, x =>
			//					{
			//						x.drills.ForEach(d => d.Enabled = false);
			//						x.CommandAutoPillock("command:create-wp:Name=ChangingShaft,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
			//					});
			//				});
			//			}
			//			else if (c.CurrentRole == Role.Lone)
			//			{
			//				int newShaftId = 0;
			//				{
			//					c.drills.ForEach(d => d.Enabled = false);

			//					c.CommandAutoPillock("command:create-wp:Name=ChangingShaft,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
			//				}

			//			}

			//		}

			//		public void HandleState()
			//		{
			//			

			//			if ()
			//			{
			//				E.Echo($"Depth: current: {currentDepth:f1} skip: {c.pState.skipDepth:f1}");
			//				if (c.pState.maxDepth.HasValue && (currentDepth > c.pState.maxDepth.Value) 
			//					|| !c.CheckBatteriesAndIntegrityThrottled(Variables.Get<float>("battery-low-factor"), Variables.Get<float>("gas-low-factor")))
			//				{
			//					GetOutTheShaft();
			//				}
			//
			//				if ((!c.pState.skipDepth.HasValue) || (currentDepth > c.pState.skipDepth))
			//				{
			//					// skipped surface layer, checking for ore and caring about cargo level
			//					c.drills.ForEach(d => d.UseConveyorSystem = true);

			//					if (CargoIsGettingValuableOre())
			//					{
			//						// lastFoundOreDepth = currentDepth;
			//						// this causes early shaft abandon when lastFoundOreDepth gets occasional inputs from shaft walls during consequental shaft entries
			//						// lastFoundOreDepth scope is current shaft
			//						lastFoundOreDepth = Math.Max(currentDepth, lastFoundOreDepth ?? 0);
			//						// test this
			//						if ((!MinFoundOreDepth.HasValue) || (MinFoundOreDepth > currentDepth))
			//							MinFoundOreDepth = currentDepth;
			//						if ((!MaxFoundOreDepth.HasValue) || (MaxFoundOreDepth < currentDepth))
			//							MaxFoundOreDepth = currentDepth;
			//						if (Toggle.C.Check("adaptive-mining"))
			//						{
			//							c.pState.skipDepth = MinFoundOreDepth.Value - 2f;
			//							c.pState.maxDepth = MaxFoundOreDepth.Value + 2f;
			//						}
			//					}
			//					else
			//					{
			//						if (lastFoundOreDepth.HasValue && (currentDepth - lastFoundOreDepth > 2))
			//						{
			//							GetOutTheShaft();
			//						}
			//					}
			//
			//					if (CargoIsFull())
			//					{
			//						GetOutTheShaft();
			//					}
			//				}
			//				else
			//				{
			//					c.drills.ForEach(d => d.UseConveyorSystem = false);
			//				}
			//			}

			//			if ()
			//			{
			//				if (CurrentWpReached(0.5f))
			//				{
			// kinda expensive
			//					if (CargoIsFull() || !c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-low-factor"), Variables.Get<float>("gas-low-factor")))
			//					{
			// we reached cargo limit
			//						c.EnterSharedSpace(LOCK_NAME_GeneralSection, mc =>
			//						{
			//							mc.drills.ForEach(d => d.Enabled = false);
			//							mc.CommandAutoPillock("command:create-wp:Name=GoingToUnload,Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
			//						});
			//					}
			//					else
			//					{
			// we reached depth limit
			//						SkipShaft();
			//					}
			//				}
			//			}

			//			if ()
			//			{
			// triggered 15m above old mining entry
			//				if (CurrentWpReached(0.5f))
			//				{
			//					var depth = -15;
			//					c.AddEchelonOffset(pt);
			//					c.CommandAutoPillock("command:create-wp:Name=GoingToEntry (ChangingShaft),Ng=Forward:" + VectorOpsHelper.V3DtoBroadcastString(pt));
			//c.SetState(State.GoingToEntry);
	
			//				}
			//			}

		
			//			{
			//				if (CurrentWpReached(1))
			//				{
			//					c.EnterSharedSpace(LOCK_NAME_GeneralSection, mc =>
			//					{
	
			//						c.drills.ForEach(d => d.Enabled = true);

			//						double elevation;
			//						if (Toggle.C.Check("adjust-entry-by-elevation") && c.remCon.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation))
			//						{
			//							Vector3D plCenter;
			//							c.remCon.TryGetPlanetPosition(out plCenter);
			//							var h = (c.fwReferenceBlock.WorldMatrix.Translation - plCenter).Length() - elevation + 5f;
			//
			//							var elevationAdjustedEntryPoint = plCenter + plNorm * h;
			//							mc.CommandAutoPillock(entry + VectorOpsHelper.V3DtoBroadcastString(elevationAdjustedEntryPoint));
			//						}
			//						else
			//						{
			//						}
			//					});
			//				}
			//			}


			//			{
			//				var dv = c.ntv("docking");
			//				if (dv.Position.HasValue)
			//				{
			//					{
			//						Vector3D dockingTransEntryPt;
			//						{
			//							dockingTransEntryPt = c.AddEchelonOffset();
			//						}
			//						else // assuming we didn't mine and just want to RTB
			//						{
			//							var dockPosDiff = dv.Position.Value - c.docker.GetPosition();
			//							var n = dv.OrientationUnit.Value.Backward;
			//							var altDiff = Vector3D.ProjectOnVector(ref dockPosDiff, ref n);
			//							dockingTransEntryPt = c.AddEchelonOffset(c.docker.GetPosition() + altDiff, n);
			//						}

			// releasing that when we leave mining area zone
			//c.EnterSharedSpace("general", mc =>
			//{
	
			//									VectorOpsHelper.V3DtoBroadcastString(dockingTransEntryPt)
			//							+ ":command:create-wp:Name=ForceFinish.dock-echelon,Ng=Forward,TransformChannel=docking:"
			//							+ VectorOpsHelper.V3DtoBroadcastString(Vector3D.Transform(
			//									c.AddEchelonOffset(dv.Position.Value, dv.OrientationUnit.Value.Backward) -
			//											dv.OrientationUnit.Value.Backward * Variables.Get<float>("getAbove-altitude"),
			//											MatrixD.Invert(dv.OrientationUnit.Value)))
			//							+ ":command:pillock-mode:DockingFinal");
			//});
			//					}
			//					else
			//					{
			//c.EnterSharedSpace("general", mc =>
			//{
			//							c.CommandAutoPillock("command:create-wp:Name=DynamicDock.echelon,Ng=Forward,AimNormal="
			//							+ ",TransformChannel=docking:"
			//							+ VectorOpsHelper.V3DtoBroadcastString(Vector3D.Transform(
			//								c.AddEchelonOffset(dv.Position.Value, dv.OrientationUnit.Value.Backward) -
			//										dv.OrientationUnit.Value.Backward * Variables.Get<float>("getAbove-altitude"),
			//										MatrixD.Invert(dv.OrientationUnit.Value)))
			//							+ ":command:pillock-mode:DockingFinal");
			//						//});
			//					}
			//				}
			//			}

			//			{
			//				if (c.docker.Status == MyShipConnectorStatus.Connected)
			//				{
			//					if (!c.DockingHandled)
			//					{
			//						c.DockingHandled = true;
			//						E.DebugLog("Regular docking handled");

			//						c.CommandAutoPillock("command:pillock-mode:Disabled");
			//						c.remCon.DampenersOverride = false;
			//						c.batteries.ForEach(b => b.ChargeMode = ChargeMode.Recharge);
			//						c.docker.OtherConnector.CustomData = "";
			//						c.InvalidateDockingDto?.Invoke();
			//						c.tanks.ForEach(b => b.Stockpile = true);

			//						if (c.ObtainedLock == LOCK_NAME_GeneralSection)
			//							c.ReleaseLock(LOCK_NAME_GeneralSection);
			//					}

			//					E.Echo("Docking: Connected");
			//					if (!CargoFlush())
			//					{
			//						E.Echo("Docking: still have items");
			//					}
			//					else
			//					{
			//						// Docking => Maintenance => Disabled => Docking
			//						if (c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-low-factor"), Variables.Get<float>("gas-low-factor")))
			//						{
			//							c.EnterSharedSpace(LOCK_NAME_GeneralSection, mc =>
			//							{
			//								c.batteries.ForEach(b => b.ChargeMode = ChargeMode.Auto);
			//								c.tanks.ForEach(b => b.Stockpile = false);
			//								//c.docker.OtherConnector.CustomData = "";
			//								AccountUnload();
			//								HandleUnload(c.docker.OtherConnector);
			//								c.docker.Disconnect();
			//							});
			//						}
			//						else
			//						{
			//							c.pState.LifetimeWentToMaintenance++;
			//							Scheduler.C.After(10000).).RunCmd(() => {
			//								if (c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-full-factor"), 0.99f))
			//								{
		
			//								}
			//							});
			//						}
			//					}
			//				}
			//				else
			//				{
			//					if (c.DockingHandled)
			//						c.DockingHandled = false;
			//					if (c.pState.StaticDockOverride.HasValue)
			//						c.docker.Connect();
			//				}
			//			}

			//			{
			// for world reload
			//				if (() && (c.docker.Status == MyShipConnectorStatus.Connected))
			//				{
			//					c.CommandAutoPillock("command:pillock-mode:Disabled");
			//					Scheduler.C.After(10000).RepeatWhile(() => c.).RunCmd(() => {
			//						if (c.CheckBatteriesAndIntegrity(Variables.Get<float>("battery-full-factor"), 0.99f))
			//						{
			//						
			//						}
			//					});
			//				}
			//			}

			//			{
			//				if (c.docker.Status == MyShipConnectorStatus.Connected)
			//				{
			//					if (!c.DockingHandled)
			//					{
			//						c.DockingHandled = true;
			//						E.DebugLog("ForceFinish docking handled");

			//						c.CommandAutoPillock("command:pillock-mode:Disabled");

			//						c.remCon.DampenersOverride = false;
			//						c.batteries.ForEach(b => b.ChargeMode = ChargeMode.Recharge);
			//						c.docker.OtherConnector.CustomData = "";
			//						c.InvalidateDockingDto?.Invoke();
			//						c.tanks.ForEach(b => b.Stockpile = true);

			//						c.ObtainedLock = LOCK_NAME_ForceFinishSection; // facepalm
			//						c.ReleaseLock(LOCK_NAME_ForceFinishSection);
			//						c.ObtainedLock = LOCK_NAME_GeneralSection; // facepalm
			//						c.ReleaseLock(LOCK_NAME_GeneralSection);
			//					}

			//					if (!CargoFlush())
			//					{
			//						E.Echo("ForceFinish: still have items");
			//					}
			//					else
			//					{
			//						AccountUnload();

			//						c.stateWrapper.Save();

			// CurrentJob is recreated at command:mine or ResumeFromDock
			//						c.CurrentJob = null;
			// probably it's better to clear state only explicitly or when creating new mining task
			// in this case a worker can ResumeFromDock via command:mine
			/*
			c.CurrentJob = null;
			MaxFoundOreDepth = null;
			MinFoundOreDepth = null;
			*/
			//					}
			//				}
			//				else
			//				{
			//					if (c.DockingHandled)
			//						c.DockingHandled = false;
			//					if (c.pState.StaticDockOverride.HasValue)
			//						c.docker.Connect();
			//				}
			//			}
			//		}

			//		bool CargoFlush()
			//		{
			//			var outerContainers = new List<IMyCargoContainer>();
			//			c.gts.GetBlocksOfType(outerContainers, b => b.IsSameConstructAs(c.docker.OtherConnector) && b.HasInventory && b.IsFunctional && (b is IMyCargoContainer));

			//			var localInvs = c.allContainers.Select(c => c.GetInventory()).Where(i => i.ItemCount > 0);

			//			if (localInvs.Any())
			//			{
			//				E.Echo("Docking: still have items");
			//				foreach (var localInv in localInvs)
			//				{
			//					var items = new List<MyInventoryItem>();
			//					localInv.GetItems(items);
			//					for (int n = 0; n < items.Count; n++)
			//					{
			//						var itemToPush = items[n];
			//						IMyInventory destinationInv;

			//						var k = Variables.Get<string>("preferred-container");
			//						if (!string.IsNullOrEmpty(k))
			//							destinationInv = outerContainers.Where(x => x.CustomName.Contains(k)).Select(c => c.GetInventory()).FirstOrDefault();
			//						else
			//							destinationInv = outerContainers.Select(c => c.GetInventory()).Where(i => i.CanItemsBeAdded((MyFixedPoint)(1f), itemToPush.Type))
			//							.OrderBy(i => (float)i.CurrentVolume).FirstOrDefault();

			//						if (destinationInv != null)
			//						{
			//E.Echo("Docking: have outer invs to unload to");
			//							if (!localInv.TransferItemTo(destinationInv, items[n]))
			//							{
			//								E.Echo("Docking: failing to transfer from " + (localInv.Owner as IMyTerminalBlock).CustomName + " to "
			//									+ (destinationInv.Owner as IMyTerminalBlock).CustomName);
			//							}
			//						}
			//					}
			//				}
			//				return false;
			//			}
			//			return true;
			//		}

			//		public void UpdateReport(TransponderMsg report)
			//		{
			//			var b = ImmutableArray.CreateBuilder<MyTuple<string, string>>(10);
			//			b.Add(new MyTuple<string, string>("State", state.ToString()));
			//			b.Add(new MyTuple<string, string>("Adaptive\nmode", Toggle.C.Check("adaptive-mining") ? "Y" : "N"));
			//			b.Add(new MyTuple<string, string>("Session\nore mined", SessionOreMined.ToString("f2")));
			//			b.Add(new MyTuple<string, string>("Last found\nore depth", (lastFoundOreDepth ?? 0f).ToString("f2")));
			//			b.Add(new MyTuple<string, string>("Cargo\nfullness", cargoFullness.ToString("f2")));
			//			b.Add(new MyTuple<string, string>("Current\ndepth", currentDepth.ToString("f2")));
			//			b.Add(new MyTuple<string, string>("Lock\nrequested", c.WaitedSection));
			//			b.Add(new MyTuple<string, string>("Lock\nowned", c.ObtainedLock));
			//			report.KeyValuePairs = b.ToImmutableArray();
			//		}

			//		StringBuilder sb = new StringBuilder();
			//		public override string ToString()
			//		{
			//			sb.Clear();
			//			sb.AppendFormat("session uptime: {0}\n", (SessionStartedAt == default(DateTime) ? "-" : (DateTime.Now - SessionStartedAt).ToString()));
			//			sb.AppendFormat("session ore mass: {0}\n", SessionOreMined);
			//			sb.AppendFormat("cargoFullness: {0:f2}\n", cargoFullness);
			//			sb.AppendFormat("cargoMass: {0:f2}\n", cargoMass);
			//			sb.AppendFormat("cargoYield: {0:f2}\n", prevTickValCount);
			//			sb.AppendFormat("lastFoundOreDepth: {0}\n", lastFoundOreDepth.HasValue ? lastFoundOreDepth.Value.ToString("f2") : "-");
			//			sb.AppendFormat("minFoundOreDepth: {0}\n", MinFoundOreDepth.HasValue ? MinFoundOreDepth.Value.ToString("f2") : "-");
			//			sb.AppendFormat("maxFoundOreDepth: {0}\n", MaxFoundOreDepth.HasValue ? MaxFoundOreDepth.Value.ToString("f2") : "-");

			//			return sb.ToString();
			//		}

			//		float currentDepth;
			//		public float SessionOreMined;
			//		public DateTime SessionStartedAt;

			//		float? lastFoundOreDepth;

			//		float? MinFoundOreDepth
			//		{
			//			get
			//			{
			//				return c.pState.minFoundOreDepth;
			//			}
			//			set
			//			{
			//				c.pState.minFoundOreDepth = value;
			//			}
			//		}
			//		float? MaxFoundOreDepth
			//		{
			//			get
			//			{
			//				return c.pState.maxFoundOreDepth;
			//			}
			//			set
			//			{
			//				c.pState.maxFoundOreDepth = value;
			//			}
			//		}

			//		public float GetShaftYield()
			//		{
			//			return prevTickValCount + currentShaftValTotal - preShaftValTotal;
			//		}

			//		void AccountUnload()
			//		{
			//			SessionOreMined += cargoMass;
			//			c.pState.LifetimeOreAmount += cargoMass;
			//			c.pState.LifetimeYield += prevTickValCount;
			//			currentShaftValTotal += prevTickValCount - preShaftValTotal;
			//			preShaftValTotal = 0;
			//			prevTickValCount = 0;
			//		}

			//		void AccountChangeShaft()
			//		{
			//			preShaftValTotal = prevTickValCount;
			//			currentShaftValTotal = 0;
			//		}

			//		float currentShaftValTotal = 0;
			//		float preShaftValTotal = 0;
			//		float prevTickValCount = 0;
			//		bool CargoIsGettingValuableOre()
			//		{
			//			float totalAmount = 0;
			//			for (int i = 0; i < c.allContainers.Count; i++)
			//			{
			//				var inv = c.allContainers[i].GetInventory(0);
			//				if (inv == null)
			//					continue;
			//				List<MyInventoryItem> items = new List<MyInventoryItem>();
			//				inv.GetItems(items);
			//				items.Where(ix => ix.Type.ToString().Contains("Ore") && !ix.Type.ToString().Contains("Stone")).ToList().ForEach(x => totalAmount += (float)x.Amount);
			//			}

			//			bool gain = false;
			//			if ((prevTickValCount > 0) && (totalAmount > prevTickValCount))
			//			{
			//				gain = true;
			//			}

			//			prevTickValCount = totalAmount;

			//			return gain;
			//		}

			//		float cargoFullness;
			//		float cargoMass;
			//		bool CargoIsFull()
			//		{
			//			float spaceNominal = 0;
			//			float spaceOccupied = 0;
			//			cargoMass = 0;
			//			for (int i = 0; i < c.allContainers.Count; i++)
			//			{
			//				var inv = c.allContainers[i].GetInventory(0);
			//				if (inv == null)
			//					continue;
			//				spaceNominal += (float)inv.MaxVolume;
			//				spaceOccupied += (float)inv.CurrentVolume;
			//				cargoMass += (float)inv.CurrentMass;
			//			}
			//			cargoFullness = spaceOccupied / spaceNominal;
			//			return cargoFullness >= Variables.Get<float>("cargo-full-factor");
			//		}

			//		void HandleUnload(IMyShipConnector otherConnector)
			//		{

			//			var aboveDock = c.AddEchelonOffset(otherConnector.WorldMatrix.Translation, otherConnector.WorldMatrix.Backward) -
			//										otherConnector.WorldMatrix.Backward * Variables.Get<float>("getAbove-altitude");


			//			c.CommandAutoPillock(seq);
			//		}
			//	}
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
		IMyShipController guiSeat;

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
			static IMyTextSurface p;
			static IMyTextSurface l;
			public static double T;
			public static void Init(Action<string> echo, IMyGridTerminalSystem g, IMyProgrammableBlock me)
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
			public static void ClearLog()
			{
				l?.WriteText("");
				linesToLog.Clear();
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



		//APckUnit coreUnit;
		//public class APckUnit
		//{
		//	public IMyShipConnector docker;
		//	public List<IMyWarhead> wh;

		//	IMyRadioAntenna antenna;

		//	FSM fsm;
		//	public PcBehavior CurrentBH;
		//	public Func<string, TargetTelemetry> _getTV;

		//	public IMyGyro G;
		//	public IMyGridTerminalSystem _gts;
		//	public IMyIntergridCommunicationSystem _igc;
		//	public IMyRemoteControl RC;
		//	public PillockController pc;
		//	public TimerTriggerService tts;

		//	public IMyProgrammableBlock Tp;
		//	public IMyProgrammableBlock TGP;

		//	HashSet<IMyTerminalBlock> coll = new HashSet<IMyTerminalBlock>();

		//	T GetCoreB<T>(string name, List<IMyTerminalBlock> set, bool required = false) where T : class, IMyTerminalBlock
		//	{
		//		T r;
		//		E.Echo("Looking for " + name);
		//		var f = set.Where(b => b is T && b.CustomName.Contains(name)).Cast<T>().ToList();
		//		r = required ? f.Single() : f.FirstOrDefault();
		//		if (r != null)
		//			coll.Add(r);
		//		return r;
		//	}

		//	List<T> GetCoreC<T>(List<IMyTerminalBlock> set, string n = null) where T : class, IMyTerminalBlock
		//	{
		//		var f = set.Where(b => b is T && ((n == null) || (b.CustomName == n))).Cast<T>().ToList();
		//		foreach (var b in f)
		//			coll.Add(b);
		//		return f;
		//	}

		//	public APckUnit(IMyProgrammableBlock me, PersistentState ps, IMyGridTerminalSystem gts, IMyIntergridCommunicationSystem igc, Func<string, TargetTelemetry> gtt)
		//	{
		//		_gts = gts;
		//		_getTV = gtt;
		//		_igc = igc;

				//Func<IMyTerminalBlock, bool> f = b => true;
		//		Func<IMyTerminalBlock, bool> f = b => b.IsSameConstructAs(me);
		//		var subs = new List<IMyTerminalBlock>();
		//		gts.GetBlocks(subs);
		//		subs = subs.Where(b => f(b)).ToList();

		//		InitBS(subs);
		//	}

		//	public void InitBS(List<IMyTerminalBlock> subset)
		//	{
		//		var f = subset;

		//		E.DebugLog("subset: " + subset.Count);

				// svc
		//		Tp = GetCoreB<IMyProgrammableBlock>("a-thrust-provider", f);

		//		var rb = GetCoreC<IMyMotorStator>(f);
		//		var pbs = new List<IMyProgrammableBlock>();
		//		_gts.GetBlocksOfType(pbs, j => rb.Any(x => (x.Top != null) && x.Top.CubeGrid == j.CubeGrid));
		//		TGP = GetCoreB<IMyProgrammableBlock>("a-tgp", f) ?? pbs.FirstOrDefault(x => x.CustomName.Contains("a-tgp"));

		//		G = GetCoreB<IMyGyro>(ForwardGyroTag, f, true);

		//		var ctrls = GetCoreC<IMyShipController>(f);
		//		UserCtrlTest.Init(ctrls);

		//		antenna = GetCoreC<IMyRadioAntenna>(f).FirstOrDefault();
		//		docker = GetCoreC<IMyShipConnector>(f).First();

		//		wh = GetCoreC<IMyWarhead>(f);

		//		RC = GetCoreC<IMyRemoteControl>(f).First();
		//		RC.CustomData = "";

		//		var ts = GetCoreC<IMyTimerBlock>(f);
		//		tts = new TimerTriggerService(ts);

		//		thrusters = new List<IMyTerminalBlock>();
		//		thrusters.AddRange(GetCoreC<IMyThrust>(f));
		//		thrusters.AddRange(GetCoreC<IMyArtificialMassBlock>(f));

		//		string tag = Variables.Get<string>("ggen-tag");
		//		if (!string.IsNullOrEmpty(tag))
		//		{
		//			var g = new List<IMyGravityGenerator>();
		//			var gr = _gts.GetBlockGroupWithName(tag);
		//			if (gr != null)
		//				gr.GetBlocksOfType(g, b => f.Contains(b));
		//			foreach (var b in g)
		//				coll.Add(b);
		//			thrusters.AddRange(g);
		//		}
		//		else
		//			thrusters.AddRange(GetCoreC<IMyGravityGenerator>(f));

		//		pc = new PillockController(RC, tts, _igc, Tp, G, antenna, AllLocalThrusters, this, thrusters.Count > 5);

		//		fsm = new FSM(this, _getTV);

		//		SetState(ApckState.Standby);
		//		pc.SetState(PillockController.State.WP);
		//	}

		//	List<IMyTerminalBlock> thrusters;
		//	ThrusterSelector cached;
		//	int tsUpdatedStamp;
		//	public ThrusterSelector AllLocalThrusters()
		//	{
		//		if (cached == null)
		//			cached = new ThrusterSelector(G, thrusters);
		//		else if ((tick != tsUpdatedStamp) && (tick % 60 == 0))
		//		{
		//			tsUpdatedStamp = tick;
		//			if (thrusters.Any(x => !x.IsFunctional))
		//			{
		//				thrusters.RemoveAll(x => !x.IsFunctional);
		//				cached = new ThrusterSelector(G, thrusters);
		//			}
		//		}
		//		if (thrusters.Any(x => x is IMyThrust && (x as IMyThrust)?.MaxEffectiveThrust != (x as IMyThrust)?.MaxThrust))
		//			cached.CalculateForces();
		//		return cached;
		//	}

		//	public Vector3D? initialAlt;
		//	public Vector3D? plCenter;
		//	public bool UnderPlanetInfl()
		//	{
		//		if (pc.NG != null)
		//		{
		//			if (plCenter == null)
		//			{
		//				Vector3D planetPos;
		//				if (RC.TryGetPlanetPosition(out planetPos))
		//				{
		//					plCenter = planetPos;
		//					return true;
		//				}
		//			}
		//			return plCenter.HasValue;
		//		}
		//		return false;
		//	}

		//	public void TrySetState(string stateName)
		//	{
		//		ApckState s;
		//		if (Enum.TryParse(stateName, out s))
		//		{
		//			SetState(s);
		//		}
		//	}

		//	public void SetState(ApckState s)
		//	{
		//		if (fsm.TrySetState(s))
		//		{
		//			CurrentBH = fsm.GetCurrentBeh();
		//			if (antenna != null)
		//				antenna.CustomName = $"{G.CubeGrid.CustomName}> {fsm.GetCurrent().St} / {tPtr?.Value?.Name}";
		//		}
		//	}

		//	public void BindBeh(ApckState st, PcBehavior b)
		//	{
		//		fsm.SetBehavior(st, b);
		//	}

		//	public PcBehavior GetBeh(ApckState st)
		//	{
		//		return fsm.GetXwrapper(st).BH;
		//	}

		//	public void CreateWP(APckTask t)
		//	{
		//		E.DebugLog("CreateWP " + t.Name);
		//		InsertTaskBefore(t);
		//	}

		//	public void Handle(int tick, Action<string> e)
		//	{
		//		this.tick = tick;

		//		HandleTasks();
		//		var task = tPtr?.Value;
		//		PcBehavior bh;
		//		if ((task != null) && (fsm.GetCurrent().St == task.PState))
		//			bh = task.BH ?? GetBeh(task.PState);
		//		else
		//			bh = CurrentBH;

		//		pc.HandleControl(tick, e, bh);
		//	}

		//	LinkedList<APckTask> tasks = new LinkedList<APckTask>();
		//	LinkedListNode<APckTask> tPtr;
		//	int tick;

		//	void HandleTasks()
		//	{
		//		if (tPtr != null)
		//		{
		//			var t = tPtr.Value;
		//			if (t.CheckCompl(tick, pc))
		//			{
		//				E.DebugLog($"TFin {t.Name}");
		//				ForceNext();
		//			}
		//		}
		//	}

		//	public APckTask GetCurrTask()
		//	{
		//		return tPtr?.Value;
		//	}

		//	public void InsertTaskBefore(APckTask t)
		//	{
		//		tasks.AddFirst(t);
		//		E.DebugLog($"Added {t.Name}, total: {tasks.Count}");
		//		tPtr = tasks.First;
		//		if (tPtr.Next == null)
		//			tPtr.Value.src = fsm.GetCurrent().St;
		//		t.Init(pc, tick);
		//		SetState(t.PState);
		//	}
		//	public void ForceNext()
		//	{
		//		var _p = tPtr;
		//		var c = _p.Value;
		//		c.OnComplete?.Invoke();
		//		//pc.TriggerService.TryTriggerNamedTimer(wp.Name + ".OnComplete");
		//		if (_p.Next != null)
		//		{
		//			c = _p.Next.Value;
		//			tPtr = _p.Next;
		//			c.Init(pc, tick);
		//			tasks.Remove(_p);
		//		}
		//		else
		//		{
		//			tPtr = null;
		//			tasks.Clear();
		//			SetState(_p.Value.src);
		//		}
		//	}
		//}

		//public class FSM
		//{
		//	APckUnit _u;

		//	Dictionary<ApckState, XState> stateBehs = new Dictionary<ApckState, XState>();

		//	public FSM(APckUnit unit, Func<string, TargetTelemetry> GetNTV)
		//	{
		//		_u = unit;
		//		var PC = _u.pc;

		//		var de = new PcBehavior { Name = "Default" };
		//		foreach (var s in Enum.GetValues(typeof(ApckState)))
		//		{
		//			stateBehs.Add((ApckState)s, new XState((ApckState)s, de));
		//		}

		//		stateBehs[ApckState.Standby].BH = new PcBehavior { Name = "Standby" };
		//		currentSt = stateBehs[ApckState.Standby];

		//		stateBehs[ApckState.Formation].BH = new PcBehavior
		//		{
		//			Name = "follow formation",
		//			AutoSwitchToNext = false,
		//			TargetFeed = () => GetNTV("wingman"),
		//			AimpointShifter = (tv) => PC.Fw.GetPosition() + GetNTV("wingman").OrientationUnit.Value.Forward * 5000,
		//			PositionShifter = p =>
		//			{
		//				var bs = new BoundingSphereD(GetNTV("wingman").OrientationUnit.Value.Translation, 30);
		//				return VectorOpsHelper.GetTangFreeDestinantion(PC.Fw.WorldMatrix, p, bs);
		//			},
		//			TranslationOverride = () => PC.Fw.GetPosition()
		//		};

		//		stateBehs[ApckState.Brake].BH = new PcBehavior
		//		{
		//			Name = "reverse",
		//			IgnoreTFeed = true,
		//			PositionShifter = tv => PC.CreateFromFwDir(-150),
		//			AimpointShifter = (tv) => PC.CreateFromFwDir(1),
		//			FlyThrough = true
		//		};

		//		stateBehs[ApckState.DockingAwait].BH = new PcBehavior
		//		{
		//			Name = "awaiting docking",
		//			AutoSwitchToNext = false,
		//			IgnoreTFeed = true,
		//			TargetFeed = () => GetNTV("wingman"),
		//			AimpointShifter = tv => PC.Fw.GetPosition() + PC.Fw.WorldMatrix.Forward,
		//			PositionShifter = tv => GetNTV("wingman").Position.HasValue ? GetNTV("wingman").Position.Value : PC.Fw.GetPosition(),
		//			DistanceHandler = (d, dx, c, wp, u) =>
		//			{
		//				if (GetNTV("docking").Position.HasValue && (_u.docker != null))
		//				{
		//					u.SetState(ApckState.DockingFinal);
		//				}
		//			}
		//		};

		//		stateBehs[ApckState.DockingFinal].BH = new PcBehavior
		//		{
		//			AimpointShifter = tv => _u.docker.GetPosition() - GetNTV("docking").OrientationUnit.Value.Forward * 10000,
		//			PositionShifter = p => p + GetNTV("docking").OrientationUnit.Value.Forward * (IsLargeGrid ? 1.25f : 0.5f),
		//			FwOverride = () => _u.docker.WorldMatrix,
		//			TranslationOverride = () => _u.docker.GetPosition(),
		//			TargetFeed = () => GetNTV("docking"),
		//			AutoSwitchToNext = false,
		//			ApproachVelocity = () => PC.Velocity,
		//			//FlyThrough = true, // fixes frav min3r docking
		//			DistanceHandler = (d, dx, c, wp, u) =>
		//			{
		//				if ((d < 20) && (c.AlignDelta.Length() < 0.8) && (_u.docker != null))
		//				{
		//					_u.docker.Connect();
		//					if (_u.docker.Status == MyShipConnectorStatus.Connected)
		//					{
		//						u.SetState(ApckState.Inert);
		//						_u.docker.OtherConnector.CustomData = "";
		//						c.RemCon.DampenersOverride = false;
		//					}
		//				}
		//			}
		//		};

		//		stateBehs[ApckState.Inert].OnEnter = s => unit.pc.SetState(PillockController.State.Inert);
		//		stateBehs[ApckState.Inert].OnExit = s => unit.pc.SetState(PillockController.State.WP);
		//	}

		//	public void SetBehavior(ApckState st, PcBehavior bh)
		//	{
		//		stateBehs[st].BH = bh;
		//	}

		//	public PcBehavior GetCurrentBeh()
		//	{
		//		return currentSt.BH;
		//	}

		//	public bool TrySetState(ApckState st)
		//	{
		//		if (st == currentSt.St)
		//			return true;
		//		var t = GetXwrapper(st);
		//		if (t != null)
		//		{
		//			var c = tConstraints.FirstOrDefault(x => x.Src.St == currentSt.St || x.Target.St == currentSt.St || x.Target.St == st || x.Src.St == st);
		//			if (c != null)
		//			{
		//				if (!(c.Src == currentSt && c.Target == t))
		//				{
		//					return false;
		//				}
		//			}

		//			var _s = currentSt;
		//			E.DebugLog($"{_s.St} -> {t.St}");
		//			currentSt = t;

		//			_s.OnExit?.Invoke(currentSt.St);
		//			c?.OnTransit?.Invoke();
		//			t.OnEnter?.Invoke(t.St);
		//			return true;
		//		}
		//		return false;
		//	}

		//	public XState GetXwrapper(ApckState st)
		//	{
		//		return stateBehs[st];
		//	}

		//	public XState GetCurrent()
		//	{
		//		return currentSt;
		//	}

		//	XState currentSt;
		//	List<XTrans> tConstraints = new List<XTrans>();

		//	public class XState
		//	{
		//		public ApckState St;
		//		public Action<ApckState> OnExit;
		//		public Action<ApckState> OnEnter;
		//		public PcBehavior BH;
		//		public XState(ApckState st, PcBehavior b, Action<ApckState> onExit = null, Action<ApckState> onEnter = null)
		//		{
		//			BH = b;
		//			St = st;
		//			OnExit = onExit;
		//			OnEnter = onEnter;
		//		}
		//	}

		//	public class XTrans
		//	{
		//		public XState Src;
		//		public XState Target;
		//		public Action OnTransit;
		//	}
		//}

		//public class PcBehavior
		//{
		//	public string Name = "Default";
		//	public bool AutoSwitchToNext = true;
		//	public Func<Vector3D, Vector3D> PositionShifter { get; set; }
		//	public Func<Vector3D, Vector3D> DestinationShifter { get; set; }
		//	public Func<Vector3D> SuggestedUpNorm { get; set; }
		//	public Action<double, double, PillockController, PcBehavior, APckUnit> DistanceHandler { get; set; }
		//	public Func<Vector3D> ApproachVelocity;
		//	public bool AllowFixedThrust = false;
		//	public Func<Vector3D, Vector3D> AimpointShifter = (tv) => tv;
		//	public Func<MatrixD> FwOverride;
		//	public Func<Vector3D> TranslationOverride;
		//	public bool fRoll;
		//	public float? SpeedLimit;
		//	public bool SelfVelocityAimCorrection = false;
		//	public bool FlyThrough = false;
		//	public bool IgnoreTFeed;
		//	public Func<TargetTelemetry> TargetFeed;
		//	public static PcBehavior FromGPS(Vector3D p, string name, Func<Vector3D> aim = null)
		//	{
		//		return new PcBehavior() { Name = name, IgnoreTFeed = true, PositionShifter = x => p, AimpointShifter = x => aim?.Invoke() ?? p };
		//	}
		//}


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
			List<IMyShipConnector> ports;
			Dictionary<IMyShipConnector, Vector3D> pPositions = new Dictionary<IMyShipConnector, Vector3D>(); // Positions of `ports`.

			public DockHost(List<IMyShipConnector> docks, PersistentState ps, IMyGridTerminalSystem gts)
			{
				ports = docks;
				ports.ForEach(x => pPositions.Add(x, x.GetPosition()));
			}

			public void Handle(IMyIntergridCommunicationSystem i, int t)
			{
				var z = new List<MyTuple<Vector3D, Vector3D, Vector4>>();
				foreach (var n in nodes.Where(x => x.Pa != null))
				{
					var gr = ports.First().CubeGrid;
					z.Add(new MyTuple<Vector3D, Vector3D, Vector4>(gr.GridIntegerToWorld(n.loc), gr.GridIntegerToWorld(n.Pa.loc), Color.SeaGreen.ToVector4()));
				}

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

						/* Calculate the approach path and send it to the agent. */
						var inv = MatrixD.Transpose(fd.WorldMatrix);
						var a = GetPath(dests[id]).Reverse().Select(x => Vector3D.TransformNormal(x - fd.GetPosition(), inv)).ToImmutableArray();
						E.DebugLog($"Sent {a.Length}-node approach path");
						i.SendUnicastMessage(id, "apck.docking.approach", a);
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
						var inv = MatrixD.Transpose(bd.WorldMatrix);
						var a = GetPath(dests[r]).Select(x => Vector3D.TransformNormal(x - bd.GetPosition(), inv)).ToImmutableArray();
						E.DebugLog($"Sent {a.Length}-node departure path");
						i.SendUnicastMessage(r, "apck.depart.approach", a);
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
			 * \brief Generates a departure path from a docking port.
			 * \details For approach, simply reverse the order of points.
			 * \param[in] o The current location of the agent's docking port.
			 */
			public IEnumerable<Vector3D> GetPath(Vector3D o)
			{
				var gr = ports.First().CubeGrid; // (For conversion of grid coordinates to world coordinates.)

				/* Find the node that is closest to the agent. (Its docking port, to be precise.) */
				//TODO: What is the x.Ch.Count == 0 condition?
				var n = nodes.Where(x => x.Ch.Count == 0).OrderBy(x => (gr.GridIntegerToWorld(x.loc) - o).LengthSquared()).FirstOrDefault();

				if (n != null) {
				
					//TODO: This actually looks like a for-loop.
					var c = n;
					do
					{
						yield return gr.GridIntegerToWorld(c.loc);
						c = c.Pa;
					} while (c != null);
				}
			}

			public Vector3D GetFirstNormal()
			{
				return ports.First().WorldMatrix.Forward;
			}

			public List<IMyShipConnector> GetPorts()
			{
				return ports;
			}

			//NavMeshNode root;
			public List<NavMeshNode> nodes = new List<NavMeshNode>();
			public class NavMeshNode
			{
				public NavMeshNode Pa;
				public List<NavMeshNode> Ch = new List<NavMeshNode>();
				public char[] tags;
				public Vector3I loc;
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

	}
}
