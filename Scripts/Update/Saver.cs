using System; // partial
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot;
using Rynchodon.Settings;
using Rynchodon.Utility;
using Rynchodon.Weapons.Guided;
using Rynchodon.Weapons.SystemDisruption;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace Rynchodon.Update
{
	/// <summary>
	/// Saves/loads persistent data to/from a file.
	/// </summary>
	/// <remarks>
	/// Path is used as unique identifier for saving. Name is updated after using "Save As".
	/// Path is saved as a variable inside the save file so that if "Save As" is used from main menu, data can be loaded from previous save.
	/// </remarks>
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Saver : MySessionComponentBase
	{

		[Serializable]
		public class Builder_ArmsData
		{
			[XmlAttribute]
			public int ModVersion = Settings.ServerSettings.latestVersion;
			[XmlAttribute]
			public long SaveTime = Globals.ElapsedTime.Ticks;
			public NetworkStorage.Builder_NetworkStorage[] AntennaStorage;
			public Disruption.Builder_Disruption[] SystemDisruption;
			public ShipAutopilot.Builder_Autopilot[] Autopilot;
			public ProgrammableBlock.Builder_ProgrammableBlock[] ProgrammableBlock;
			public TextPanel.Builder_TextPanel[] TextPanel;
		}

		private const string SaveIdString = "ARMS save file id";

		public static Saver Instance;

		private readonly Logger m_logger;
		private FileMaster m_fileMaster;

		public Saver()
		{
			this.m_logger = new Logger(GetType().Name);
			Instance = this;
		}

		/// <summary>
		/// Load data from a file. Shall be called after mod is fully initialized.
		/// </summary>
		public void DoLoad()
		{
			try
			{
				if (!MyAPIGateway.Multiplayer.IsServer)
					return;

				m_fileMaster = new FileMaster("SaveDataMaster.txt", "SaveData - ", ServerSettings.GetSetting<int>(ServerSettings.SettingName.iMaxSaveKeep));

				string saveId_fromPath = GetSaveIdFromPath();
				string saveId_fromWorld;
				System.IO.TextReader reader = m_fileMaster.GetTextReader(saveId_fromPath);

				// if file from path exists, it should match stored value
				if (reader != null)
				{
					if (!MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
					{
						m_logger.alwaysLog("Save exists for path but save id could not be retrieved from world. From path: " + saveId_fromPath, Logger.severity.ERROR);
					}
					else if (saveId_fromPath != saveId_fromWorld)
					{
						m_logger.alwaysLog("Save id from path does not match save id from world. From path: " + saveId_fromPath + ", from world: " + saveId_fromWorld, Logger.severity.ERROR);

						// prefer from world
						System.IO.TextReader reader2 = m_fileMaster.GetTextReader(saveId_fromWorld);
						if (reader2 != null)
							reader = reader2;
						else
							m_logger.alwaysLog("Save id from world does not match a save. From world: " + saveId_fromWorld, Logger.severity.ERROR);
					}
				}
				else
				{
					if (MyAPIGateway.Utilities.GetVariable(SaveIdString, out saveId_fromWorld))
					{
						reader = m_fileMaster.GetTextReader(saveId_fromWorld);
						if (reader != null)
							m_logger.alwaysLog("Save is a copy, loading from old world: " + saveId_fromWorld, Logger.severity.DEBUG);
						else
						{
							m_logger.alwaysLog("Cannot load world, save id does not match any save: " + saveId_fromWorld, Logger.severity.DEBUG);
							return;
						}
					}
					else
					{
						m_logger.alwaysLog("Cannot load world, no save id found", Logger.severity.DEBUG);
						return;
					}
				}

				Builder_ArmsData data = MyAPIGateway.Utilities.SerializeFromXML<Builder_ArmsData>(reader.ReadToEnd());

				// network

				Dictionary<Message.Builder_Message, Message> messages = new Dictionary<Message.Builder_Message, Message>();
				SerializableGameTime.Adjust = new TimeSpan(data.SaveTime);
				foreach (NetworkStorage.Builder_NetworkStorage bns in data.AntennaStorage)
				{
					NetworkNode node;
					if (!Registrar.TryGetValue(bns.PrimaryNode, out node))
					{
						m_logger.alwaysLog("Failed to get node for: " + bns.PrimaryNode, Logger.severity.WARNING);
						continue;
					}
					NetworkStorage store = node.Storage;
					if (store == null) // probably always true
					{
						node.ForceCreateStorage();
						store = node.Storage;
						if (store == null)
						{
							m_logger.debugLog("failed to create storage for " + node.LoggingName, Logger.severity.WARNING);
							continue;
						}
					}

					foreach (LastSeen.Builder_LastSeen bls in bns.LastSeenList)
					{
						LastSeen ls = new LastSeen(bls);
						if (ls.IsValid)
							store.Receive(ls);
						else
							m_logger.debugLog("failed to create a valid last seen from builder", Logger.severity.WARNING);
					}

					m_logger.debugLog("added " + bns.LastSeenList.Length + " last seen to " + store.PrimaryNode.LoggingName, Logger.severity.DEBUG);

					foreach (Message.Builder_Message bm in bns.MessageList)
					{
						Message msg;
						if (!messages.TryGetValue(bm, out msg))
						{
							msg = new Message(bm);
							messages.Add(bm, msg);
						}
						else
						{
							m_logger.debugLog("found linked message", Logger.severity.TRACE);
						}
						if (msg.IsValid)
							store.Receive(msg);
						else
							m_logger.debugLog("failed to create a valid message from builder", Logger.severity.WARNING);
					}

					m_logger.debugLog("added " + bns.MessageList.Length + " message to " + store.PrimaryNode.LoggingName, Logger.severity.DEBUG);
				}

				// system disruption

				foreach (Disruption.Builder_Disruption bd in data.SystemDisruption)
				{
					Disruption disrupt;
					switch (bd.Type)
					{
						case "AirVentDepressurize":
							disrupt = new AirVentDepressurize();
							break;
						case "CryoChamberMurder":
							disrupt = new CryoChamberMurder();
							break;
						case "DisableTurret":
							disrupt = new DisableTurret();
							break;
						case "DoorLock":
							disrupt = new DoorLock();
							break;
						case "EMP":
							disrupt = new EMP();
							break;
						case "GravityReverse":
							disrupt = new GravityReverse();
							break;
						case "JumpDriveDrain":
							disrupt = new JumpDriveDrain();
							break;
						case "MedicalRoom":
							disrupt = new MedicalRoom();
							break;
						case "TraitorTurret":
							disrupt = new TraitorTurret();
							break;
						default:
							m_logger.alwaysLog("Unknown disruption: " + bd.Type, Logger.severity.WARNING);
							continue;
					}
					disrupt.Start(bd);
				}

				// autopilot

				if(data.Autopilot != null)
					foreach (ShipAutopilot.Builder_Autopilot ba in data.Autopilot)
					{
						ShipAutopilot autopilot;
						if (Registrar.TryGetValue(ba.AutopilotBlock, out autopilot))
							autopilot.Resume = ba;
						else
							m_logger.alwaysLog("failed to find autopilot block " + ba.AutopilotBlock, Logger.severity.WARNING);
					}

				// programmable block

				if (data.ProgrammableBlock != null)
					foreach (ProgrammableBlock.Builder_ProgrammableBlock bpa in data.ProgrammableBlock)
					{
						ProgrammableBlock pb;
						if (Registrar.TryGetValue(bpa.BlockId, out pb))
							pb.ResumeFromSave(bpa);
						else
							m_logger.alwaysLog("failed to find programmable block " + bpa.BlockId, Logger.severity.WARNING);
					}

				// text panel

				if (data.TextPanel != null)
					foreach (TextPanel.Builder_TextPanel btp in data.TextPanel)
					{
						TextPanel panel;
						if (Registrar.TryGetValue(btp.BlockId, out panel))
							panel.ResumeFromSave(btp);
						else
							m_logger.alwaysLog("failed to find text panel " + btp.BlockId, Logger.severity.WARNING);
					}

				m_logger.debugLog("Loaded from " + saveId_fromWorld, Logger.severity.INFO);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.notify("ARMS: failed to load data", 60000, Logger.severity.ERROR);
			}
		}

		/// <summary>
		/// Saves data to a file.
		/// </summary>
		public override void SaveData()
		{
			if (!MyAPIGateway.Multiplayer.IsServer || m_fileMaster == null)
				return;

			// critical this happens before SE saves variables (has not been an issue)
			string fileId = GetSaveIdFromPath();
			MyAPIGateway.Utilities.SetVariable(SaveIdString, fileId);

			try
			{
				// fetching data needs to happen on game thread as not every script has locks

				Builder_ArmsData data = new Builder_ArmsData();

				// network data

				Dictionary<long, NetworkStorage.Builder_NetworkStorage> storages = new Dictionary<long, NetworkStorage.Builder_NetworkStorage>();

				Registrar.ForEach<NetworkNode>(node => {
					if (node.Storage != null && !storages.ContainsKey(node.Storage.PrimaryNode.EntityId))
					{
						NetworkStorage.Builder_NetworkStorage bns = node.Storage.GetBuilder();
						storages.Add(bns.PrimaryNode, bns);
					}
				});

				data.AntennaStorage = storages.Values.ToArray();

				// disruption

				List<Disruption.Builder_Disruption> systemDisrupt = new List<Disruption.Builder_Disruption>();
				foreach (Disruption disrupt in Disruption.AllDisruptions)
					systemDisrupt.Add(disrupt.GetBuilder());

				data.SystemDisruption = systemDisrupt.ToArray();

				// autopilot

				List<ShipAutopilot.Builder_Autopilot> buildAuto = new List<ShipAutopilot.Builder_Autopilot>();
				Registrar.ForEach<ShipAutopilot>(autopilot => {
					ShipAutopilot.Builder_Autopilot builder = autopilot.GetBuilder();
					if (builder != null)
						buildAuto.Add(builder);
				});

				data.Autopilot = buildAuto.ToArray();

				// programmable block

				List<ProgrammableBlock.Builder_ProgrammableBlock> buildProgram = new List<ProgrammableBlock.Builder_ProgrammableBlock>();
				Registrar.ForEach<ProgrammableBlock>(program => {
					ProgrammableBlock.Builder_ProgrammableBlock builder = program.GetBuilder();
					if (builder != null)
						buildProgram.Add(builder);
				});

				data.ProgrammableBlock = buildProgram.ToArray();

				// text panel

				List<TextPanel.Builder_TextPanel> buildPanel = new List<TextPanel.Builder_TextPanel>();
				Registrar.ForEach<TextPanel>(panel => {
					TextPanel.Builder_TextPanel builder = panel.GetBuilder();
					if (builder != null)
						buildPanel.Add(builder);
				});

				data.TextPanel = buildPanel.ToArray();

				var writer = m_fileMaster.GetTextWriter(fileId);
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(data));
				writer.Close();

				m_logger.debugLog("Saved to " + fileId, Logger.severity.INFO);
			}
			catch (Exception ex)
			{
				m_logger.alwaysLog("Exception: " + ex, Logger.severity.ERROR);
				Logger.notify("ARMS: failed to save data", 60000, Logger.severity.ERROR);
			}
		}

		private string GetSaveIdFromPath()
		{
			string path = MyAPIGateway.Session.CurrentPath;
			return path.Substring(path.LastIndexOfAny(new char[] { '/', '\\' }) + 1) + ".xml";
		}

	}
}
