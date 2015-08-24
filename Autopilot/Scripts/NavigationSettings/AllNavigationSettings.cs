﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rynchodon.Autopilot.NavigationSettings
{
	public class AllNavigationSettings
	{
		[Flags]
		public enum PathfinderPermissions : byte
		{
			None = 0,
			ChangeCourse = 1 << 0,
			All = ChangeCourse
		}

		[Flags]
		public enum MovementType : byte
		{
			None = 0,
			Rotate = 1 << 0,
			Move = 1 << 1,
			All = Rotate | Move
		}

		public class SettingsLevel
		{
			private SettingsLevel parent;

			private PathfinderPermissions? m_pathPerm;
			private MovementType? m_allowedMovement;

			private float? m_maxSpeed, m_minSpeed;

			private bool? m_ignoreAsteroid, m_jumpToDest;

			/// <summary>
			/// Creates the top-level SettingLevel, which has defaults set.
			/// </summary>
			internal SettingsLevel()
			{
				m_allowedMovement = MovementType.All;
				m_pathPerm = PathfinderPermissions.All;

				m_maxSpeed = Settings.GetSetting<float>(Settings.SettingName.fMaxSpeed);
				m_minSpeed = 0.5f;

				m_ignoreAsteroid = false;
				m_jumpToDest = false;
			}

			/// <summary>
			/// Creates a SettingLevel with a parent. Where values are not present, value from parent will be used.
			/// </summary>
			internal SettingsLevel(SettingsLevel parent)
			{ this.parent = parent; }

			public PathfinderPermissions PathPerm
			{
				get { return m_pathPerm ?? parent.PathPerm; }
				set { m_pathPerm = value; }
			}

			public MovementType AllowedMovement
			{
				get { return m_allowedMovement ?? parent.AllowedMovement; }
				set { m_allowedMovement = value; }
			}

			public float MaxSpeed
			{
				get { return m_maxSpeed ?? parent.MaxSpeed; }
				set { m_maxSpeed = value; }
			}

			public float MinSpeed
			{
				get { return m_minSpeed ?? parent.MinSpeed; }
				set { m_minSpeed = value; }
			}

			public bool IgnoreAsteroid
			{
				get { return m_ignoreAsteroid ?? parent.IgnoreAsteroid; }
				set { m_ignoreAsteroid = value; }
			}

			public bool JumpToDest
			{
				get { return m_jumpToDest ?? parent.JumpToDest; }
				set { m_jumpToDest = value; }
			}
		}

		///// <summary>Settings that are reset when Autopilot gains control. Settings should be written here but not read.</summary>
		//public SettingsLevel Settings_GainControl { get; private set; }

		/// <summary>Settings that are reset at the start of commands. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Commands { get; private set; }

		/// <summary>Settings that are reset when a task is completed. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task { get; private set; }

		/// <summary>Settings that are reset when subtask is completed. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Subtask { get; private set; }

		///// <summary>Settings that are reset every time autopilot is updated. Settings should be written here but not read.</summary>
		//public SettingLevel MySettings_Update { get; private set; }

		/// <summary>Reflects the current state of autopilot. Settings should be read here but not written.</summary>
		public SettingsLevel CurrentSettings { get { return Settings_Subtask; } }

		public AllNavigationSettings()
		{ OnStartOfCommands(); }

		//public void OnGainControl()
		//{
		//	Settings_GainControl = new SettingsLevel();
		//	OnStartOfCommands();
		//}

		public void OnStartOfCommands()
		{
			Settings_Commands = new SettingsLevel();
			OnTaskComplete();
		}

		public void OnTaskComplete()
		{
			Settings_Task = new SettingsLevel(Settings_Commands);
			OnSubtaskComplete();
		}

		public void OnSubtaskComplete()
		{
			Settings_Subtask = new SettingsLevel(Settings_Task);
			//OnUpdate();
		}

		//public void OnUpdate()
		//{
		//	MySettings_Update = new SettingLevel(MySettings_Subtask);
		//}

	}
}