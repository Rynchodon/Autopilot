﻿#define TRACE

using System.Diagnostics;
using System.Runtime.CompilerServices;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Utility
{
	/// <summary>
	/// Latest attempt to make logging lighter. Classes define a property that creates a single use Logable.
	/// </summary>
	public struct Logable
	{

		public readonly string Context, PrimaryState, SecondaryState;

		public Logable(string Context, string PrimaryState = null, string SecondaryState = null)
		{
			this.Context = Context;
			this.PrimaryState = PrimaryState;
			this.SecondaryState = SecondaryState;
		}
		
		public Logable(IMyEntity entity)
		{
			if (entity == null)
			{
				Context = PrimaryState = SecondaryState = null;
			}
			else if (entity is IMyCubeBlock)
			{
				IMyCubeBlock block = (IMyCubeBlock)entity;
				Context = block.CubeGrid.nameWithId();
				PrimaryState = block.DefinitionDisplayNameText;
				SecondaryState = block.nameWithId();
			}
			else
			{
				Context = entity.nameWithId();
				PrimaryState = SecondaryState = null;
			}
		}

		[Conditional("TRACE")]
		public void TraceLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.TraceLog(toLog, level, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		[Conditional("DEBUG")]
		public void DebugLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.DebugLog(toLog, level, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		[Conditional("PROFILE")]
		public void ProfileLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.ProfileLog(toLog, level, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		public void AlwaysLog(string toLog, Logger.severity level = Logger.severity.TRACE, bool condition = true, [CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			if (condition)
				Logger.ProfileLog(toLog, level, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		[Conditional("TRACE")]
		public void EnteredMember([CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			Logger.TraceLog("entered member", Logger.severity.TRACE, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

		[Conditional("TRACE")]
		public void LeavingMember([CallerFilePath] string filePath = null, [CallerMemberName] string member = null, [CallerLineNumber] int lineNumber = 0)
		{
			Logger.TraceLog("leaving member", Logger.severity.TRACE, Context, PrimaryState, SecondaryState, true, filePath, member, lineNumber);
		}

	}
}