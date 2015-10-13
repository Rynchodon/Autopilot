﻿using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Attached
{
	/// Not derived from AttachableBlockPair because testing for attached is fast but getting attached block is slow.
	public static class StatorRotor
	{
		private static readonly Logger myLogger = new Logger("StatorRotor");

		/// <summary>
		/// Tries to get a rotor attached to a stator.
		/// </summary>
		/// <param name="stator">stator attached to rotor</param>
		/// <param name="rotor">rotor attached to stator</param>
		/// <returns>true iff successful</returns>
		/// Not an extension because TryGetStator() is not an extension.
		public static bool TryGetRotor(IMyMotorStator stator, out IMyCubeBlock rotor)
		{
			Stator value;
			if (!Stator.registry.TryGetValue(stator.EntityId, out value))
			{
				myLogger.alwaysLog("failed to get stator from registry: " + stator.DisplayNameText, "TryGetRotor()", Logger.severity.WARNING);
				rotor = null;
				return false;
			}
			if (value.partner == null)
			{
				rotor = null;
				return false;
			}
			rotor = value.partner.myRotor;
			return true;
		}

		/// <summary>
		/// Tries to get a stator attached to a rotor.
		/// </summary>
		/// <param name="rotor">rotor attached to stator</param>
		/// <param name="stator">stator attached to rotor</param>
		/// <returns>true iff successful</returns>
		/// Not an extension because IMyCubeBlock are rarely rotors.
		public static bool TryGetStator(IMyCubeBlock rotor, out IMyMotorStator stator)
		{
			Rotor value;
			if (!Rotor.registry.TryGetValue(rotor.EntityId, out value))
			{
				myLogger.alwaysLog("failed to get rotor from registry: " + rotor.DisplayNameText, "TryGetRotor()", Logger.severity.WARNING);
				stator = null;
				return false;
			}
			if (value.partner == null)
			{
				stator = null;
				return false;
			}
			stator = value.partner.myStator;
			return true;
		}

		public class Stator : AttachableBlockBase
		{
			internal static Dictionary<long, Stator> registry = new Dictionary<long, Stator>();

			internal readonly IMyMotorStator myStator;
			internal Rotor partner;

			private readonly Logger myLogger;

			public Stator(IMyCubeBlock block)
				: base(block, AttachedGrid.AttachmentKind.Motor)
			{
				this.myLogger = new Logger("Stator", block);
				this.myStator = block as IMyMotorStator;
				registry.Add(this.myStator.EntityId, this);
				this.myStator.OnClosing += myStator_OnClosing;
			}

			private void myStator_OnClosing(IMyEntity obj)
			{
				myLogger.debugLog("entered myStator_OnClosing()", "myStator_OnClosing()");
				myStator.OnClosing -= myStator_OnClosing;
				registry.Remove(myStator.EntityId);
				myLogger.debugLog("leaving myStator_OnClosing()", "myStator_OnClosing()");
			}

			/// This will not work correctly if a rotor is replaced in less than 10 updates.
			public void Update10()
			{
				if (partner == null)
				{
					if (myStator.IsAttached)
					{
						MyObjectBuilder_MotorStator statorBuilder = (myStator as IMyCubeBlock).GetSlimObjectBuilder_Safe() as MyObjectBuilder_MotorStator;
						if (Rotor.registry.TryGetValue(statorBuilder.RotorEntityId, out partner))
						{
							myLogger.debugLog("Set partner to " + partner.myRotor.DisplayNameText, "Update10()", Logger.severity.INFO);
							Attach(partner.myRotor);
							partner.partner = this;
						}
						else
							myLogger.alwaysLog("Failed to set partner, Rotor not in registry.", "Update10()", Logger.severity.WARNING);
					}
				}
				else // partner != null
					if (!myStator.IsAttached)
					{
						myLogger.debugLog("Removing partner " + partner.myRotor.DisplayNameText, "Update10()", Logger.severity.INFO);
						Detach();
						partner.partner = null;
						partner = null;
					}
			}
		}

		public class Rotor : AttachableBlockBase
		{
			internal static Dictionary<long, Rotor> registry = new Dictionary<long, Rotor>();

			internal readonly IMyCubeBlock myRotor;
			internal Stator partner;

			public Rotor(IMyCubeBlock block)
				: base(block, AttachedGrid.AttachmentKind.Motor)
			{
				this.myRotor = block;
				registry.Add(this.myRotor.EntityId, this);
				this.myRotor.OnClosing += myRotor_OnClosing;
			}

			private void myRotor_OnClosing(IMyEntity obj)
			{
				myLogger.debugLog("entered myRotor_OnClosing()", "myRotor_OnClosing()");
				myRotor.OnClosing -= myRotor_OnClosing;
				registry.Remove(myRotor.EntityId);
				myLogger.debugLog("leaving myRotor_OnClosing()", "myRotor_OnClosing()");
			}
		}
	}
}