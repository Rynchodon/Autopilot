﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Sandbox.ModAPI;

namespace Rynchodon.Settings
{
	/// <summary>
	/// Some of the settings will be made available to clients.
	/// Clients should still not be able to affect balance in any way, only to update hud, GPS, etc.
	/// </summary>
	public static class ServerSettings
	{
		public enum SettingName : byte
		{
			bAllowAutopilot, bAllowRadar, bAllowWeaponControl, bUseRemoteControl, bUseColourState,
			yParallelPathfinder,
			fDefaultSpeed, fMaxSpeed, fMaxWeaponRange,
			sWeaponCommandsNPC
		}

		private const ushort ModID = 54310; // no idea what this is supposed to be

		private static readonly Dictionary<SettingName, Setting> AllSettings = new Dictionary<SettingName, Setting>();

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static T GetSetting<T>(SettingName name) where T : struct
		{
			SettingSimple<T> set = AllSettings[name] as SettingSimple<T>;
			return set.Value;
		}

		private static void SetSetting<T>(SettingName name, T value) where T : struct
		{
			SettingSimple<T> set = AllSettings[name] as SettingSimple<T>;
			set.Value = value;
			myLogger.alwaysLog("Setting " + name + " = " + value, "SetSetting()", Logger.severity.INFO);
		}

		/// <exception cref="NullReferenceException">if setting does not exist or is of a different type</exception>
		public static string GetSettingString(SettingName name)
		{
			SettingString set = AllSettings[name] as SettingString;
			return set.Value;
		}

		private const string modName = "Autopilot";
		private const string settings_file_name = "AutopilotSettings.txt";
		private static System.IO.TextWriter settingsWriter;

		private static readonly string strVersion = "Version";
		public static readonly int latestVersion = 41; // in sequence of updates on steam
		public static readonly int fileVersion;

		private static readonly Logger myLogger = new Logger("ServerSettings");

		public static bool ServerSettingsLoaded { get; private set; }

		static ServerSettings()
		{
			buildSettings();

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				ServerSettingsLoaded = true;
				MyAPIGateway.Multiplayer.RegisterMessageHandler(ModID, Server_ReceiveMessage);

				fileVersion = readAll();
				if (fileVersion != latestVersion)
					Logger.debugNotify(modName + " has been updated to version " + latestVersion, 10000, Logger.severity.INFO);
				myLogger.alwaysLog("file version: " + fileVersion + ", latest version: " + latestVersion, "static Constructor", Logger.severity.INFO);

				writeAll(); // writing immediately decreases user errors & whining
			}
			else
			{
				MyAPIGateway.Multiplayer.RegisterMessageHandler(ModID, Client_ReceiveMessage);

				byte[] message = new byte[8];
				int pos = 0;
				ByteConverter.AppendBytes(MyAPIGateway.Session.Player.SteamUserId, message, ref pos);

				if (MyAPIGateway.Multiplayer.SendMessageToServer(ModID, message))
					myLogger.debugLog("Sent request to server", "ServerSettings()", Logger.severity.INFO);
				else
					myLogger.alwaysLog("Failed to send request to server", "ServerSettings()", Logger.severity.ERROR);
			}
		}

		private static void Server_ReceiveMessage(byte[] message)
		{
			try
			{
				int pos = 0;
				ulong SteamUserId = ByteConverter.GetUlong(message, ref pos);

				message = new byte[17];
				pos = 0;

				ByteConverter.AppendBytes(GetSetting<bool>(SettingName.bAllowAutopilot), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<bool>(SettingName.bAllowRadar), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<bool>(SettingName.bAllowWeaponControl), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<bool>(SettingName.bUseColourState), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<bool>(SettingName.bUseRemoteControl), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<float>(SettingName.fDefaultSpeed), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<float>(SettingName.fMaxSpeed), message, ref pos);
				ByteConverter.AppendBytes(GetSetting<float>(SettingName.fMaxWeaponRange), message, ref pos);

				if (MyAPIGateway.Multiplayer.SendMessageTo(ModID, message, SteamUserId))
					myLogger.debugLog("Sent settings to " + SteamUserId, "Server_ReceiveMessage()", Logger.severity.INFO);
				else
					myLogger.alwaysLog("Failed to send settings to " + SteamUserId, "Server_ReceiveMessage()", Logger.severity.ERROR);
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, "Server_ReceiveMessage()", Logger.severity.ERROR); }
		}

		private static void Client_ReceiveMessage(byte[] message)
		{
			try
			{
				myLogger.debugLog("Received settings from server", "Client_ReceiveMessage()");

				int pos = 0;
		
				SetSetting<bool>(SettingName.bAllowAutopilot, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowRadar, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bAllowWeaponControl, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bUseColourState, ByteConverter.GetBool(message, ref pos));
				SetSetting<bool>(SettingName.bUseRemoteControl, ByteConverter.GetBool(message, ref pos));
				SetSetting<float>(SettingName.fDefaultSpeed, ByteConverter.GetFloat(message, ref pos));
				SetSetting<float>(SettingName.fMaxSpeed, ByteConverter.GetFloat(message, ref pos));
				SetSetting<float>(SettingName.fMaxWeaponRange, ByteConverter.GetFloat(message, ref pos));

				ServerSettingsLoaded = true;
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Exception: " + ex, "Client_ReceiveMessage()", Logger.severity.ERROR); }
		}

		/// <summary>
		/// put each setting into AllSettings with its default value
		/// </summary>
		private static void buildSettings()
		{
			AllSettings.Add(SettingName.bAllowAutopilot, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowRadar, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bUseRemoteControl, new SettingSimple<bool>(false));
			AllSettings.Add(SettingName.bUseColourState, new SettingSimple<bool>(true));
			AllSettings.Add(SettingName.bAllowWeaponControl, new SettingSimple<bool>(true));

			AllSettings.Add(SettingName.yParallelPathfinder, new SettingMinMax<byte>(1, 100, 4));

			AllSettings.Add(SettingName.fDefaultSpeed, new SettingMinMax<float>(1, float.MaxValue, 100));
			AllSettings.Add(SettingName.fMaxSpeed, new SettingMinMax<float>(10, float.MaxValue, float.MaxValue));
			AllSettings.Add(SettingName.fMaxWeaponRange, new SettingMinMax<float>(100, float.MaxValue, 800));

			AllSettings.Add(SettingName.sWeaponCommandsNPC, new SettingString("[(Warhead, Turret, Rocket, Gatling, Reactor, Battery, Solar) ; Range 800 ; AllGrid ; Destroy ]"));
		}

		/// <summary>
		/// Read all settings from file
		/// </summary>
		/// <returns>version of file</returns>
		private static int readAll()
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(settings_file_name, typeof(ServerSettings)))
				return -1; // no file

			TextReader settingsReader = null;
			try
			{
				settingsReader = MyAPIGateway.Utilities.ReadFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				string[] versionLine = settingsReader.ReadLine().Split('=');
				if (versionLine.Length != 2 || !versionLine[0].Equals(strVersion))
					return -2; // first line is not version
				int fileVersion;
				if (!int.TryParse(versionLine[1], out fileVersion))
					return -3; // could not parse version

				// read settings
				while (true)
				{
					string line = settingsReader.ReadLine();
					if (line == null)
						break;
					parse(line);
				}

				return fileVersion;
			}
			catch (Exception ex)
			{
				myLogger.alwaysLog("Failed to read settings from " + settings_file_name + ": " + ex, "readAll()", Logger.severity.WARNING);
				return -4; // exception while reading
			}
			finally
			{
				if (settingsReader != null)
					settingsReader.Close();
			}
		}

		/// <summary>
		/// Write all settings to file. Only server should call this!
		/// </summary>
		private static void writeAll()
		{
			try
			{
				settingsWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(settings_file_name, typeof(ServerSettings));

				write(strVersion, latestVersion.ToString()); // must be first line

				// write settings
				foreach (KeyValuePair<SettingName, Setting> pair in AllSettings)
					write(pair.Key.ToString(), pair.Value.ValueAsString());

				settingsWriter.Flush();
			}
			catch (Exception ex)
			{ myLogger.alwaysLog("Failed to write settings to " + settings_file_name + ": " + ex, "writeAll()", Logger.severity.WARNING); }
			finally
			{
				if (settingsWriter != null)
				{
					settingsWriter.Close();
					settingsWriter = null;
				}
			}
		}

		/// <summary>
		/// write a single setting to file, format is name=value
		/// </summary>
		/// <param name="name">name of setting</param>
		/// <param name="value">value of setting</param>
		private static void write(string name, string value)
		{
			StringBuilder toWrite = new StringBuilder();
			toWrite.Append(name);
			toWrite.Append('=');
			toWrite.Append(value);
			settingsWriter.WriteLine(toWrite);
		}

		/// <summary>
		/// convert a line of format name=value into a setting and apply it
		/// </summary>
		private static void parse(string line)
		{
			string[] split = line.Split('=');

			if (split.Length != 2)
			{
				myLogger.alwaysLog("split wrong length: " + split.Length + ", line: " + line, "parse()", Logger.severity.WARNING);
				return;
			}

			SettingName name;
			if (Enum.TryParse<SettingName>(split[0], out name))
				try
				{
					if (AllSettings[name].ValueFromString(split[1]))
						myLogger.alwaysLog("Setting " + name + " = " + split[1], "parse()", Logger.severity.INFO);
				}
				catch (Exception)
				{ myLogger.alwaysLog("failed to parse: " + split[1] + " for " + name, "parse()", Logger.severity.WARNING); }
			else
				myLogger.alwaysLog("Setting does not exist: " + split[0], "parse()", Logger.severity.WARNING);
		}

	}
}