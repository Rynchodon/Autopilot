using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.AntennaRelay;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Pathfinding;
using Rynchodon.Settings;
using Rynchodon.Utility;
using Rynchodon.Weapons;
using Sandbox.Common.ObjectBuilders;
using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator
{
	/// <summary>
	/// Uses weapons to attack enemy ship
	/// </summary>
	public class Fighter : NavigatorMover, IEnemyResponse, IDisposable
	{

		private const float FinalAltitude = -50f, InitialAltitude = 200f;

		private static readonly MyObjectBuilderType[] TurretWeaponTypes = new MyObjectBuilderType[] { typeof(MyObjectBuilder_LargeGatlingTurret), typeof(MyObjectBuilder_LargeMissileTurret), typeof(MyObjectBuilder_InteriorTurret) };
		private static readonly TargetType[] CumulativeTypes = new TargetType[] { TargetType.SmallGrid, TargetType.LargeGrid, TargetType.Station };

		private readonly CachingList<FixedWeapon> m_weapons_fixed = new CachingList<FixedWeapon>();
		private readonly CachingList<WeaponTargeting> m_weapons_all = new CachingList<WeaponTargeting>();
		private readonly Dictionary<TargetType, BlockTypeList> m_cumulative_targeting = new Dictionary<TargetType, BlockTypeList>();
		private WeaponTargeting m_weapon_primary;
		private PseudoBlock m_weapon_primary_pseudo;
		private LastSeen m_currentTarget;
		private Orbiter m_orbiter;
		private float m_finalOrbitAltitude;
		private float m_weaponRange_min;
		private bool m_weaponArmed = false;
		private bool m_destroySet = false;
		private bool m_weaponDataDirty = true;

		private Logable Log
		{
			get { return new Logable(m_controlBlock.CubeGrid); }
		}

		public Fighter(Pathfinder pathfinder, AllNavigationSettings navSet)
			: base(pathfinder)
		{
			Arm();
		}

		~Fighter()
		{
			Dispose();
		}

		public void Dispose()
		{
			Disarm();
		}

		public bool CanRespond()
		{
			if (!m_weaponArmed)
			{
				Arm();
				if (!m_weaponArmed)
				{
					m_navSet.Settings_Commands.Complaint |= InfoString.StringId.FighterUnarmed;
					return false;
				}
			}

			GetPrimaryWeapon();
			if (m_weapon_primary == null)
			{
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.FighterNoPrimary;
				return false;
			}
			m_navSet.Settings_Task_NavEngage.NavigationBlock = m_weapon_primary_pseudo;

			if (m_weaponDataDirty)
				UpdateWeaponData();

			//Log.DebugLog("weapon count: " + m_weapons_all.Count, "CanRespond()"); 

			if (m_weapons_all.Count == 0)
			{
				m_navSet.Settings_Commands.Complaint |= InfoString.StringId.FighterNoWeapons;
				return false;
			}

			return true;
		}

		public void UpdateTarget(LastSeen enemy)
		{
			if (enemy == null)
			{
				Log.DebugLog("lost target", Logger.severity.DEBUG, condition: m_currentTarget != null);
				m_currentTarget = null;
				m_orbiter = null;
				return;
			}

			if (m_currentTarget == null || m_currentTarget.Entity != enemy.Entity)
			{
				Log.DebugLog("new target: " + enemy.Entity.getBestName(), Logger.severity.DEBUG);
				m_currentTarget = enemy;
				m_navSet.Settings_Task_NavEngage.DestinationEntity = m_currentTarget.Entity;
			}
		}

		public bool CanTarget(IMyCubeGrid grid)
		{
			CubeGridCache cache = null;
			if (m_destroySet)
			{
				cache = CubeGridCache.GetFor(grid);
				if (cache.TerminalBlocks > 0)
				{
					Log.DebugLog("destoy: " + grid.DisplayName);
					return true;
				}
				else
					Log.DebugLog("destroy set but no terminal blocks found: " + grid.DisplayName); 
			}

			if (m_currentTarget != null && grid == m_currentTarget.Entity && m_weapon_primary.CurrentTarget.TType != TargetType.None)
				return true;

			TargetType gridType = grid.GridSizeEnum == MyCubeSize.Small ? TargetType.SmallGrid
				: grid.IsStatic ? TargetType.Station
				: TargetType.LargeGrid;

			BlockTypeList targetBlocks;
			if (m_cumulative_targeting.TryGetValue(gridType, out targetBlocks))
			{
				if (cache == null)
					cache = CubeGridCache.GetFor(grid);
				foreach (IMyCubeBlock block in targetBlocks.Blocks(cache))
					return true;
			}
			else
				Log.DebugLog("no targeting at all for grid type of: " + grid.DisplayName);

			return false;
		}

		public override void Move()
		{
			if (!m_weaponArmed)
			{
				m_mover.StopMove();
				return;
			}

			if (m_weapon_primary == null || m_weapon_primary.CubeBlock.Closed)
			{
				Log.DebugLog("no primary weapon");
				m_mover.StopMove();
				return;
			}

			if (m_currentTarget == null)
			{
				m_mover.StopMove();
				return;
			}

			if (m_orbiter == null)
			{
				if (m_navSet.DistanceLessThan(m_weaponRange_min * 2f))
				{
					// we give orbiter a lower distance, so it will calculate orbital speed from that
					m_orbiter = new Orbiter(m_pathfinder, m_navSet, m_currentTarget.Entity, m_weaponRange_min + FinalAltitude, m_currentTarget.HostileName());
					// start further out so we can spiral inwards
					m_finalOrbitAltitude = m_orbiter.Altitude;
					m_orbiter.Altitude = m_finalOrbitAltitude + InitialAltitude - FinalAltitude;
					Log.DebugLog("weapon range: " + m_weaponRange_min + ", final orbit altitude: " + m_finalOrbitAltitude + ", initial orbit altitude: " + m_orbiter.Altitude, Logger.severity.DEBUG);
				}
				else
				{
					m_mover.Thrust.Update();
					Vector3 direction = m_mover.SignificantGravity() ?
						(Vector3)m_mover.Thrust.WorldGravity / -m_mover.Thrust.GravityStrength :
						Vector3.CalculatePerpendicularVector(Vector3.Normalize(m_weapon_primary_pseudo.WorldPosition - m_currentTarget.GetPosition()));
					Vector3 offset = direction * (m_weaponRange_min + InitialAltitude);

					m_pathfinder.MoveTo(m_currentTarget, offset);
					return;
				}
			}

			Target current = m_weapon_primary.CurrentTarget;
			if ((current == null || current.Entity == null) && m_orbiter.Altitude > m_finalOrbitAltitude && m_navSet.DistanceLessThan(m_orbiter.OrbitSpeed * 0.5f))
			{
				Log.DebugLog("weapon range: " + m_weaponRange_min + ", final orbit altitude: " + m_finalOrbitAltitude + ", initial orbit altitude: " + m_orbiter.Altitude +
					", dist: " + m_navSet.Settings_Current.Distance + ", orbit speed: " + m_orbiter.OrbitSpeed, Logger.severity.TRACE);
				m_orbiter.Altitude -= 10f;
			}

			m_orbiter.Move();

			////Log.DebugLog("moving to " + (m_currentTarget.predictPosition() + m_currentOffset), "Move()");
			//m_mover.CalcMove(m_weapon_primary_pseudo, m_currentTarget.GetPosition() + m_currentOffset, m_currentTarget.GetLinearVelocity());
		}

		public void Rotate()
		{
			if (!m_weaponArmed)
			{
				m_mover.StopRotate();
				return;
			}

			if (m_weapon_primary == null || m_weapon_primary.CubeBlock.Closed)
			{
				Log.DebugLog("no primary weapon");
				Disarm();
				m_mover.StopRotate();
				return;
			}
			Vector3? FiringDirection = m_weapon_primary.CurrentTarget.FiringDirection;
			if (!FiringDirection.HasValue)
			{
				if (m_orbiter != null)
					m_orbiter.Rotate();
				else
					m_mover.CalcRotate();
				return;
			}
			//Log.DebugLog("facing target at " + firingDirection.Value, "Rotate()");

			RelativeDirection3F upDirect = null;
			if (m_mover.SignificantGravity())
				upDirect = RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, -m_mover.Thrust.WorldGravity.vector);

			m_mover.CalcRotate(m_weapon_primary_pseudo, RelativeDirection3F.FromWorld(m_weapon_primary_pseudo.Grid, FiringDirection.Value), UpDirect: upDirect, targetEntity: m_weapon_primary.CurrentTarget.Entity);
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_orbiter != null)
			{
				if (m_mover.ThrustersOverWorked())
					customInfo.AppendLine("Fighter cannot stabilize in gravity");
				m_orbiter.AppendCustomInfo(customInfo);
			}
			else
			{
				customInfo.Append("Fighter moving to: ");
				customInfo.AppendLine(m_currentTarget.HostileName());
			}
		}

		private void Arm()
		{
			if (!ServerSettings.GetSetting<bool>(ServerSettings.SettingName.bAllowWeaponControl))
			{
				Log.DebugLog("Cannot arm, weapon control is disabled.", Logger.severity.WARNING);
				return;
			}

			Log.DebugLog("Arming", Logger.severity.DEBUG);

			Log.DebugLog("Fixed weapons has not been cleared", Logger.severity.FATAL, condition: m_weapons_fixed.Count != 0);
			Log.DebugLog("All weapons has not been cleared", Logger.severity.FATAL, condition: m_weapons_all.Count != 0);

			m_weaponRange_min = float.MaxValue;

			CubeGridCache cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);

			foreach (FixedWeapon weapon in Registrar.Scripts<FixedWeapon, WeaponTargeting>())
			{
				if (weapon.CubeBlock.CubeGrid == m_controlBlock.CubeGrid)
				{
					if (weapon.EngagerTakeControl())
					{
						Log.DebugLog("Took control of " + weapon.CubeBlock.DisplayNameText);
						m_weapons_fixed.Add(weapon);
						m_weapons_all.Add(weapon);

						weapon.CubeBlock.OnClosing += Weapon_OnClosing;
					}
					else
						Log.DebugLog("failed to get control of: " + weapon.CubeBlock.DisplayNameText);
				}
				if (weapon.MotorTurretBaseGrid() == m_controlBlock.CubeGrid)
				{
					Log.DebugLog("Active motor turret: " + weapon.CubeBlock.DisplayNameText);
					m_weapons_all.Add(weapon);

					weapon.CubeBlock.OnClosing += Weapon_OnClosing;
				}
			}

			foreach (MyObjectBuilderType weaponType in TurretWeaponTypes)
			{
				foreach (IMyCubeBlock block in cache.BlocksOfType(weaponType))
				{
					WeaponTargeting weapon;
					if (!Registrar.TryGetValue(block.EntityId, out weapon))
					{
						Logger.AlwaysLog("Failed to get block: " + block.nameWithId(), Logger.severity.WARNING);
						continue;
					}
					if (weapon.CurrentControl != WeaponTargeting.Control.Off)
					{
						Log.DebugLog("Active turret: " + weapon.CubeBlock.DisplayNameText);
						m_weapons_all.Add(weapon);

						weapon.CubeBlock.OnClosing += Weapon_OnClosing;
					}
				}
			}

			m_weapons_fixed.ApplyAdditions();
			m_weapons_all.ApplyAdditions();

			m_weaponArmed = m_weapons_all.Count != 0;
			m_weaponDataDirty = m_weaponArmed;
			if (m_weaponArmed)
				Log.DebugLog("Now armed", Logger.severity.DEBUG);
			else
				Log.DebugLog("Failed to arm", Logger.severity.DEBUG);
		}

		private void Disarm()
		{
			if (!m_weaponArmed)
				return;

			Log.DebugLog("Disarming", Logger.severity.DEBUG);

			foreach (FixedWeapon weapon in m_weapons_fixed)
				weapon.EngagerReleaseControl();

			foreach (WeaponTargeting weapon in m_weapons_all)
				weapon.CubeBlock.OnClosing -= Weapon_OnClosing;

			m_weapons_fixed.ClearImmediate();
			m_weapons_all.ClearImmediate();

			m_weaponArmed = false;
			m_weaponDataDirty = true;
		}

		/// <summary>
		/// Finds a primary weapon for m_weapon_primary and m_weapon_primary_pseudo.
		/// A primary weapon can be any working weapon with ammo.
		/// Preference is given to fixed weapons and weapons with targets.
		/// If no weapons have ammo, m_weapon_primary and m_weapon_primary_pseudo will be null.
		/// </summary>
		private void GetPrimaryWeapon()
		{
			if (m_weapon_primary != null && m_weapon_primary.CubeBlock.IsWorking && m_weapon_primary.CurrentTarget.Entity != null)
				return;

			WeaponTargeting weapon_primary = null;

			bool removed = false;
			foreach (FixedWeapon weapon in m_weapons_fixed)
				if (weapon.CubeBlock.IsWorking)
				{
					if (weapon.HasAmmo)
					{
						weapon_primary = weapon;
						if (weapon.CurrentTarget.Entity != null)
						{
							Log.DebugLog("has target: " + weapon.CubeBlock.DisplayNameText);
							break;
						}
					}
					else
						Log.DebugLog("no ammo: " + weapon.CubeBlock.DisplayNameText);
				}
				else
				{
					Log.DebugLog("not working: " + weapon.CubeBlock.DisplayNameText);
					m_weapons_fixed.Remove(weapon);
					weapon.EngagerReleaseControl();
					removed = true;
				}

			if (weapon_primary == null)
				foreach (WeaponTargeting weapon in m_weapons_all)
					if (weapon.CubeBlock.IsWorking)
					{
						if (weapon.HasAmmo)
						{
							weapon_primary = weapon;
							if (weapon.CurrentTarget.Entity != null)
							{
								Log.DebugLog("has target: " + weapon.CubeBlock.DisplayNameText);
								break;
							}
						}
						else
							Log.DebugLog("no ammo: " + weapon.CubeBlock.DisplayNameText);
					}
					else
					{
						Log.DebugLog("not working: " + weapon.CubeBlock.DisplayNameText);
						m_weapons_all.Remove(weapon);
						removed = true;
					}

			if (removed)
			{
				m_weapons_fixed.ApplyRemovals();
				m_weapons_all.ApplyRemovals();
				m_weaponDataDirty = true;
			}

			if (weapon_primary == null)
			{
				m_weapon_primary = null;
				m_weapon_primary_pseudo = null;
				return;
			}

			if (m_weapon_primary != weapon_primary)
			{
				m_weapon_primary = weapon_primary;
				IMyCubeBlock faceBlock;
				FixedWeapon fixedWeapon = weapon_primary as FixedWeapon;
				if (fixedWeapon != null && fixedWeapon.CubeBlock.CubeGrid != m_controlBlock.CubeGrid)
				{
					faceBlock = fixedWeapon.MotorTurretFaceBlock();
					Log.DebugLog("MotorTurretFaceBlock == null", Logger.severity.FATAL, condition: faceBlock == null);
				}
				else
					faceBlock = weapon_primary.CubeBlock;

				if (m_mover.SignificantGravity())
				{
					if (m_mover.Thrust.Standard.LocalMatrix.Forward == faceBlock.LocalMatrix.Forward)
					{
						Log.DebugLog("primary forward matches Standard forward");
						Matrix localMatrix = m_mover.Thrust.Standard.LocalMatrix;
						localMatrix.Translation = faceBlock.LocalMatrix.Translation;
						m_weapon_primary_pseudo = new PseudoBlock(() => faceBlock.CubeGrid, localMatrix);
						return;
					}
					if (m_mover.Thrust.Gravity.LocalMatrix.Forward == faceBlock.LocalMatrix.Forward)
					{
						Log.DebugLog("primary forward matches Gravity forward");
						Matrix localMatrix = m_mover.Thrust.Gravity.LocalMatrix;
						localMatrix.Translation = faceBlock.LocalMatrix.Translation;
						m_weapon_primary_pseudo = new PseudoBlock(() => faceBlock.CubeGrid, localMatrix);
						return;
					}
					Log.DebugLog("cannot match primary forward to a standard flight matrix. primary forward: " + faceBlock.LocalMatrix.Forward +
						", Standard forward: " + m_mover.Thrust.Standard.LocalMatrix.Forward + ", gravity forward: " + m_mover.Thrust.Gravity.LocalMatrix.Forward);
				}
				m_weapon_primary_pseudo = new PseudoBlock(faceBlock);
			}
		}

		private void UpdateWeaponData()
		{
			m_weaponRange_min = float.MaxValue;
			m_cumulative_targeting.Clear();
			m_destroySet = false;

			foreach (WeaponTargeting weapon in m_weapons_all)
			{
				if (!weapon.CubeBlock.IsWorking)
				{
					m_weapons_all.Remove(weapon);
					FixedWeapon asFixed = weapon as FixedWeapon;
					if (asFixed != null)
					{
						m_weapons_fixed.Remove(asFixed);
						asFixed.EngagerReleaseControl();
					}
					continue;
				}

				bool destroy = weapon.Options.CanTargetType(TargetType.Destroy);

				if (!weapon.Options.CanTargetType(TargetType.AllGrid) && !destroy)
					continue;

				float TargetingRange = weapon.Options.TargetingRange;
				if (TargetingRange < 1f)
					continue;

				if (TargetingRange < m_weaponRange_min)
					m_weaponRange_min = TargetingRange;

				if (m_destroySet)
					continue;

				if (destroy)
				{
					Log.DebugLog("destroy set for " + weapon.CubeBlock.DisplayNameText);
					m_destroySet = true;
					m_cumulative_targeting.Clear();
					continue;
				}
				else
					Log.DebugLog("destroy NOT set for " + weapon.CubeBlock.DisplayNameText);

				foreach (TargetType type in CumulativeTypes)
					if (weapon.Options.CanTargetType(type))
						AddToCumulative(type, weapon.Options.listOfBlocks);
			}

			m_weapons_fixed.ApplyRemovals();
			m_weapons_all.ApplyRemovals();

			if (m_weapons_all.Count == 0)
			{
				Log.DebugLog("No working weapons, " + GetType().Name + " is done here", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavEngage();
			}

			m_weaponDataDirty = false;
		}

		private void AddToCumulative(TargetType type, BlockTypeList blocks)
		{
			if (blocks == null)
				return;
			Log.DebugLog("adding to type: " + type + ", count: " + blocks.BlockNamesContain.Length);

			if (type == TargetType.AllGrid)
			{
				AddToCumulative(TargetType.SmallGrid, blocks);
				AddToCumulative(TargetType.LargeGrid, blocks);
				AddToCumulative(TargetType.Station, blocks);
				return;
			}

			BlockTypeList targetBlocks;
			if (m_cumulative_targeting.TryGetValue(type, out targetBlocks))
				m_cumulative_targeting[type] = BlockTypeList.Union(targetBlocks, blocks);
			else
				m_cumulative_targeting[type] = blocks;
		}

		private void Weapon_OnClosing(IMyEntity obj)
		{ m_weaponDataDirty = true; }

	}
}
