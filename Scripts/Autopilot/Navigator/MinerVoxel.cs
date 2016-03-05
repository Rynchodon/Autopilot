using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Data;
using Rynchodon.Autopilot.Harvest;
using Rynchodon.Autopilot.Movement;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.Autopilot.Navigator
{

	/// <summary>
	/// Mines an IMyVoxelBase
	/// Will not insist on rotation control until it is ready to start mining.
	/// </summary>
	public class MinerVoxel : NavigatorMover, INavigatorRotator
	{

		private const float FullAmount_Abort = 0.9f, FullAmount_Return = 0.1f;

		private enum State : byte { GetTarget, Approaching, Rotating, MoveTo, Mining, Mining_Escape, Mining_Tunnel, Move_Away }

		private readonly Logger m_logger;
		private readonly byte[] OreTargets;
		private readonly float m_longestDimension;

		private MultiBlock<MyObjectBuilder_Drill> m_navDrill;
		private State value_state;
		private Line m_approach;
		private Vector3D m_depositPos;
		private Vector3 m_currentTarget;
		private string m_depositOre;
		private ulong m_nextCheck_drillFull;
		private float m_current_drillFull;
		private float m_closestDistToTarget;

		private IMyVoxelBase m_targetVoxel;

		private bool isMiningPlanet
		{
			get { return m_targetVoxel is MyPlanet; }
		}

		private State m_state
		{
			get
			{ return value_state; }
			set
			{
				m_logger.debugLog("Changing state to " + value, "m_state()");
				value_state = value;
				switch (value)
				{
					case State.GetTarget:
						EnableDrills(false);
						if (DrillFullness() >= FullAmount_Return)
						{
							m_logger.debugLog("Drills are full, time to go home", "m_state()");
							m_navSet.OnTaskComplete_NavRot();
							m_mover.StopMove();
							m_mover.StopRotate();
							return;
						}
						else if (m_mover.ThrustersOverWorked(0.8f))
						{
							m_logger.debugLog("Thrusters are overworked, time to go home", "set_m_state()");
							m_navSet.OnTaskComplete_NavRot();
							m_mover.StopMove();
							m_mover.StopRotate();
							return;
						}
						else
						{
							// request ore detector update
							m_logger.debugLog("Requesting ore update", "m_state()");
							m_navSet.OnTaskComplete_NavMove();
							OreDetector.SearchForMaterial(m_mover.Block, OreTargets, OnOreSearchComplete);
						}
						break;
					case State.Approaching:
						m_currentTarget = m_approach.From;
						break;
					case State.Rotating:
						m_currentTarget = m_depositPos;
						break;
					case State.MoveTo:
						EnableDrills(true);
						m_navSet.Settings_Task_NavMove.IgnoreAsteroid = true;
						break;
					case State.Mining:
						{
							Vector3 pos = m_navDrill.WorldPosition;
							m_currentTarget = pos + (m_depositPos - pos) * 2f;
							m_navSet.Settings_Task_NavMove.SpeedTarget = 1f;
							break;
						}
					case State.Mining_Escape:
						EnableDrills(false);
						GetExteriorPoint(m_navDrill.WorldPosition, m_navDrill.WorldMatrix.Forward, m_longestDimension * 2f, point => m_currentTarget = point);
						break;
					case State.Mining_Tunnel:
						if (isMiningPlanet)
						{
							m_logger.debugLog("Cannot tunnel through a planet, care to guess why?", "set_m_state()");
							m_state = State.Mining_Escape;
							return;
						}
						EnableDrills(true);
						GetExteriorPoint(m_navDrill.WorldPosition, m_navDrill.WorldMatrix.Backward, m_longestDimension * 2f, point => m_currentTarget = point);
						break;
					case State.Move_Away:
						{
							EnableDrills(false);
							m_navSet.Settings_Task_NavMove.SpeedTarget = 10f;
							Vector3 pos = m_navDrill.WorldPosition;
							m_currentTarget = pos + Vector3.Normalize(pos - m_targetVoxel.GetCentre()) * 100f;
							break;
						}
					default:
						VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State not implemented: " + value);
						break;
				}
				m_logger.debugLog("Current target: " + m_currentTarget + ", current position: " + m_navDrill.WorldPosition, "m_state()");
				m_mover.StopMove();
				m_mover.StopRotate();
				m_mover.IsStuck = false;
				m_navSet.OnTaskComplete_NavWay();
			}
		}

		public MinerVoxel(Mover mover, AllNavigationSettings navSet, byte[] OreTargets)
			: base(mover, navSet)
		{
			this.m_logger = new Logger(GetType().Name, m_controlBlock.CubeBlock, () => m_state.ToString());
			this.OreTargets = OreTargets;

			// get blocks
			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);

			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null || allDrills.Count == 0)
			{
				m_logger.debugLog("No Drills!", "MinerVoxel()", Logger.severity.INFO);
				return;
			}
			if (MyAPIGateway.Session.CreativeMode)
				foreach (IMyShipDrill drill in allDrills)
					if (drill.UseConveyorSystem)
						drill.ApplyAction("UseConveyor");

			// if a drill has been chosen by player, use it
			PseudoBlock navBlock = m_navSet.Settings_Current.NavigationBlock;
			if (navBlock.Block is IMyShipDrill)
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(navBlock.Block);
			else
				m_navDrill = new MultiBlock<MyObjectBuilder_Drill>(() => m_mover.Block.CubeGrid);

			if (m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("no working drills", "MinerVoxel()", Logger.severity.INFO);
				return;
			}

			m_longestDimension = m_controlBlock.CubeGrid.GetLongestDim();

			m_navSet.Settings_Task_NavRot.NavigatorMover = this;
			m_state = State.GetTarget;
		}

		public override void Move()
		{
			if (m_state != State.Mining_Escape && m_navDrill.FunctionalBlocks == 0)
			{
				m_logger.debugLog("No drills, must escape!", "Move()");
				m_state = State.Mining_Escape;
			}

			switch (m_state)
			{
				case State.GetTarget:
					m_mover.StopMove();
					return;
				case State.Approaching:
					// measure distance from line, but move to a point
					Vector3 closestPoint = m_approach.ClosestPoint(m_navDrill.WorldPosition);
					if (Vector3.DistanceSquared(closestPoint, m_navDrill.WorldPosition) < m_longestDimension * m_longestDimension)
					{
						m_logger.debugLog("Finished approach", "Move()", Logger.severity.DEBUG);
						m_state = State.Rotating;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_state = State.Mining_Escape;
						return;
					}
					break;
				case State.Rotating:
					m_mover.StopMove();
					if (m_mover.IsStuck)
					{
						m_state = State.Mining_Escape;
						return;
					}
					return;
				case State.MoveTo:
					if (m_navSet.Settings_Current.Distance < m_longestDimension)
					{
						m_logger.debugLog("Reached asteroid", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_state = State.Mining_Escape;
						return;
					}
					if (IsNearVoxel())
						m_navSet.Settings_Task_NavMove.SpeedTarget = 1f;
					break;
				case State.Mining:
					// do not check for inside asteroid as we may not have reached it yet and target is inside asteroid
					if (DrillFullness() > FullAmount_Abort)
					{
						m_logger.debugLog("Drills are full, aborting", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_mover.ThrustersOverWorked())
					{
						m_logger.debugLog("Drills overworked, aborting", "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}
					if (m_navSet.Settings_Current.Distance < 1f)
					{
						m_logger.debugLog("Reached position: " + m_currentTarget, "Move()", Logger.severity.DEBUG);
						m_state = State.Mining_Escape;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_state = State.Mining_Escape;
						return;
					}

					break;
				case State.Mining_Escape:
					if (!IsNearVoxel())
					{
						m_logger.debugLog("left voxel", "Move()");
						m_state = State.Move_Away;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", "Move()");
						Logger.debugNotify("Stuck", 16);
						m_state = State.Mining_Tunnel;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Backward * 100d;
					}

					break;
				case State.Mining_Tunnel:
					if (!IsNearVoxel())
					{
						m_logger.debugLog("left voxel", "Mine()");
						m_state = State.Move_Away;
						return;
					}

					if (m_mover.IsStuck)
					{
						m_state = State.Mining_Escape;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = m_navDrill.WorldPosition + m_navDrill.WorldMatrix.Forward * 100d;
					}

					break;
				case State.Move_Away:
					if (m_targetVoxel == null)
					{
						m_logger.debugLog("no asteroid centre", "Move()");
						m_state = State.GetTarget;
						return;
					}
					if (!IsNearVoxel(2d))
					{
						m_logger.debugLog("far enough away", "Move()");
						m_state = State.GetTarget;
						return;
					}
					if (m_mover.IsStuck)
					{
						m_logger.debugLog("Stuck", "Move()");
						Logger.debugNotify("Stuck", 16);
						m_state = State.Mining_Tunnel;
						return;
					}

					if (m_navSet.Settings_Current.Distance < 1f)
					{
						Vector3 pos = m_navDrill.WorldPosition;
						m_currentTarget = pos + Vector3.Normalize(pos - m_targetVoxel.GetCentre()) * 100f;
					}

					break;
				default:
					VRage.Exceptions.ThrowIf<NotImplementedException>(true, "State: " + m_state);
					break;
			}
			MoveCurrent();
		}

		private static readonly Random myRand = new Random();

		public void Rotate()
		{
			if (isMiningPlanet)
			{
				switch (m_state)
				{
					case State.Approaching:
						m_mover.StopRotate();
						return;
					case State.Rotating:
						if (m_navSet.DirectionMatched())
						{
							m_logger.debugLog("Finished rotating", "Rotate()", Logger.severity.INFO);
							m_state = State.MoveTo;
							m_mover.StopRotate();
							return;
						}
						break;
				}
				m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, m_mover.WorldGravity));
				return;
			}

			if (m_navDrill.FunctionalBlocks == 0)
				return;

			switch (m_state)
			{
				case State.Approaching:
				default:
					if (m_navSet.Settings_Current.Distance < m_longestDimension)
					{
						m_mover.StopRotate();
						return;
					}
					break;
				case State.GetTarget:
				case State.Mining_Escape:
				case State.Move_Away:
					m_mover.StopRotate();
					return;
				case State.Rotating:
					if (m_navSet.DirectionMatched())
					{
						m_logger.debugLog("Finished rotating", "Rotate()", Logger.severity.INFO);
						m_state = State.MoveTo;
						m_mover.StopRotate();
						return;
					}
					break;
			}

			if (m_state != State.Rotating && m_navSet.Settings_Current.Distance < 3f)
			{
				m_mover.StopRotate();
				return;
			}

			Vector3 direction = m_currentTarget - m_navDrill.WorldPosition;
			//m_logger.debugLog("rotating to face " + m_currentTarget, "Rotate()");
			if (m_state == State.Approaching)
				m_mover.CalcRotate();
			else
				m_mover.CalcRotate(m_navDrill, RelativeDirection3F.FromWorld(m_controlBlock.CubeGrid, direction));
		}

		public override void AppendCustomInfo(StringBuilder customInfo)
		{
			if (m_state == State.GetTarget)
			{
				customInfo.AppendLine("Searching for ore");
				return;
			}

			customInfo.Append("Mining ");
			customInfo.AppendLine(m_depositOre);

			switch (m_state)
			{
				case State.Approaching:
					customInfo.AppendLine("Approaching asteroid");
					break;
				case State.Rotating:
					customInfo.AppendLine("Rotating to face deposit");
					customInfo.Append("Angle: ");
					customInfo.AppendLine(MathHelper.ToDegrees(m_navSet.Settings_Current.DistanceAngle).ToString());
					break;
				case State.MoveTo:
					customInfo.Append("Moving to ");
					customInfo.AppendLine(m_currentTarget.ToPretty());
					break;
				case State.Mining:
					customInfo.AppendLine("Mining deposit at ");
					customInfo.AppendLine(m_depositPos.ToPretty());
					break;
				case State.Mining_Escape:
					customInfo.AppendLine("Leaving asteroid");
					break;
				case State.Mining_Tunnel:
					customInfo.AppendLine("Tunneling");
					break;
				case State.Move_Away:
					customInfo.AppendLine("Moving away from asteroid");
					break;
			}
		}

		/// <summary>
		/// <para>In survival, returns fraction of drills filled</para>
		/// <para>In creative, returns content per drill * 0.01</para>
		/// </summary>
		private float DrillFullness()
		{
			if (Globals.UpdateCount < m_nextCheck_drillFull)
				return m_current_drillFull;
			m_nextCheck_drillFull = Globals.UpdateCount + 100ul;

			MyFixedPoint content = 0, capacity = 0;
			int drillCount = 0;

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", "DrillFullness()", Logger.severity.INFO);
				return float.MaxValue;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", "DrillFullness()", Logger.severity.INFO);
				return float.MaxValue;
			}

			foreach (Ingame.IMyShipDrill drill in allDrills)
			{
				MyInventoryBase drillInventory = ((MyEntity)drill).GetInventoryBase(0);

				content += drillInventory.CurrentVolume;
				capacity += drillInventory.MaxVolume;
				drillCount++;
			}

			if (MyAPIGateway.Session.CreativeMode)
				m_current_drillFull = (float)content * 0.01f / drillCount;
			else
				m_current_drillFull = (float)content / (float)capacity;

			return m_current_drillFull;
		}

		private void EnableDrills(bool enable)
		{
			if (enable)
				m_logger.debugLog("Enabling drills", "EnableDrills()", Logger.severity.DEBUG);
			else
				m_logger.debugLog("Disabling drills", "EnableDrills()", Logger.severity.DEBUG);

			var cache = CubeGridCache.GetFor(m_controlBlock.CubeGrid);
			if (cache == null)
			{
				m_logger.debugLog("Failed to get cache", "EnableDrills()", Logger.severity.INFO);
				return;
			}
			var allDrills = cache.GetBlocksOfType(typeof(MyObjectBuilder_Drill));
			if (allDrills == null)
			{
				m_logger.debugLog("Failed to get block list", "EnableDrills()", Logger.severity.INFO);
				return;
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				foreach (IMyShipDrill drill in allDrills)
					if (!drill.Closed)
						drill.RequestEnable(enable);
			}, m_logger);
		}

		private void OnOreSearchComplete(bool success, Vector3D orePosition, IMyVoxelBase foundMap, string oreName)
		{
			if (!success)
			{
				m_logger.debugLog("No ore target found", "OnOreSearchComplete()", Logger.severity.INFO);
				m_navSet.OnTaskComplete_NavRot();
				return;
			}

			m_targetVoxel = foundMap;
			m_depositPos = orePosition;
			m_depositOre = oreName;

			Vector3 toCentre = Vector3.Normalize(m_targetVoxel.GetCentre() - m_depositPos);
			float bufferDist = Math.Max(m_longestDimension, m_navSet.Settings_Current.DestinationRadius) * 10f;
			GetExteriorPoint(m_depositPos, toCentre, bufferDist, exterior => {
				m_approach = new Line(exterior - toCentre * bufferDist, exterior);
				m_state = State.Approaching;
			});
		}

		private void GetExteriorPoint(Vector3 startPoint, Vector3 direction, float buffer, Action<Vector3> callback)
		{
			if (m_targetVoxel is IMyVoxelMap)
				callback(GetExteriorPoint_Asteroid(startPoint, direction, buffer));
			else
				GetExteriorPoint_Planet(startPoint, direction, buffer, callback);
		}

		/// <summary>
		/// Gets a point outside of an asteroid.
		/// </summary>
		/// <param name="startPoint">Where to start the search from, must be inside WorldAABB, can be inside or outside asteroid.</param>
		/// <param name="direction">Direction from outside asteroid towards inside asteroid</param>
		/// <param name="buffer">Minimum distance between the voxel and exterior point</param>
		private Vector3 GetExteriorPoint_Asteroid(Vector3 startPoint, Vector3 direction, float buffer)
		{
			IMyVoxelMap voxel = m_targetVoxel as IMyVoxelMap;
			if (voxel == null)
			{
				m_logger.alwaysLog("m_targetVoxel is not IMyVoxelMap: " + m_targetVoxel.getBestName(), "GetSurfacePoint()", Logger.severity.FATAL);
				throw new InvalidOperationException("m_targetVoxel is not IMyVoxelMap");
			}

			Vector3 v = direction * m_targetVoxel.LocalAABB.GetLongestDim();
			Capsule surfaceFinder = new Capsule(startPoint - v, startPoint + v, buffer);
			Vector3? obstruction;
			if (surfaceFinder.Intersects(voxel, out obstruction))
				return obstruction.Value;
			else
			{
				m_logger.debugLog("Failed to intersect asteroid, using surfaceFinder.P0", "GetSurfacePoint()", Logger.severity.WARNING);
				return surfaceFinder.P0;
			}
		}

		/// <summary>
		/// Gets a point outside of a planet.
		/// </summary>
		/// <param name="startPoint">Where to start the search from, can be inside or outside planet.</param>
		/// <param name="direction">Direction from outside of planet to inside planet.</param>
		/// <param name="buffer">Minimum distance between planet surface and exterior point</param>
		/// <param name="callback">Will be invoked game thread with result</param>
		private void GetExteriorPoint_Planet(Vector3D startPoint, Vector3 direction, float buffer, Action<Vector3> callback)
		{
			MyPlanet planet = m_targetVoxel as MyPlanet;
			if (planet == null)
			{
				m_logger.alwaysLog("m_targetVoxel is not MyPlanet: " + m_targetVoxel.getBestName(), "GetSurfacePoint()", Logger.severity.FATAL);
				throw new InvalidOperationException("m_targetVoxel is not MyPlanet");
			}

			MyAPIGateway.Utilities.TryInvokeOnGameThread(() => {
				Vector3 surfacePoint = planet.GetClosestSurfacePointGlobal(ref startPoint);
				Vector3 exteriorPoint = surfacePoint - direction * buffer;
				callback(exteriorPoint);
			}, m_logger);
		}

		private bool IsNearVoxel(double lengthMulti = 1f)
		{
			BoundingSphereD surround = new BoundingSphereD(m_navDrill.Grid.GetCentre(), m_longestDimension * lengthMulti);
			if (m_targetVoxel is IMyVoxelMap)
				return m_targetVoxel.GetIntersectionWithSphere(ref surround);
			else
				return false;
		}

		private void MoveCurrent()
		{ m_mover.CalcMove(m_navDrill, m_currentTarget, Vector3.Zero, m_state == State.MoveTo); }

	}
}
