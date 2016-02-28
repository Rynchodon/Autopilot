using System; // (partial) from mscorlib.dll
using System.Collections.Generic; // from mscorlib.dll, System.dll, System.Core.dll, and VRage.Library.dll
using System.Text; // from mscorlib.dll

using Sandbox.ModAPI.Ingame; // from Sandbox.Common.dll
using Sandbox.ModAPI.Interfaces; // (partial) from Sandbox.Common.dll
using VRage.Game; // from VRage.Game.dll
using VRageMath; // from VRage.Math.dll

/*
 * allowed namespaces for in game script:
 * 
 * System.Collections; // from mscorlib.dll
 * System.Globalization // from mscorlib.dll
 * System.Linq // from System.Core.dll
 * System.Text // from mscorlib.dll
 * System.Text.RegularExpressions // from System.dll
 * 
 * Medieval.ObjectBuilders // from MedievalEngineers.ObjectBuilders.dll
 * Medieval.ObjectBuilders.Definitions // from MedievalEngineers.ObjectBuilders.dll
 * Sandbox.Common.ObjectBuilders // from MedievalEngineers.ObjectBuilders.dll and SpaceEngineers.ObjectBuilders.dll
 * Sandbox.Common.ObjectBuilders.Definitions // from SpaceEngineers.ObjectBuilders.dll
 * Sandbox.Game.Gui // from Sandbox.Game.dll
 * VRage.ObjectBuilders // from VRage.Game.dll
 */

namespace Rynchodon.Programmable
{
  public class HandleDetectedEntities : MyGridProgram
  {

    /*
     * Handles entities detected by ARMS
     * Add [ Handle Detected ] to the name of a programmable block for ARMS to give it detected entities
     * Detected entities will be passed via "arguments" of Main
     * Detected entities can be displayed on a text panel by applying an action on the text panel
     */

    const string tab = "    ";

    const string displayAction = "DisplayEntities";

    /// <summary>These are defined by Rynchodon.AntennaRelay.ProgrammableBlock</summary>
    const char fieldSeparator = '�', entitySeparator = '�';

    const byte Relation_None = 0, Relation_Enemy = 1, Relation_Neutral = 2,
      Relation_Faction = 4, Relation_Owner = 8;
    const byte EntityType_None = 0, EntityType_Grid = 1, EntityType_Character = 2,
      EntityType_Missile = 3, EntityType_Unknown = 4;

    /// <summary>Time between sounding alarm (hours, minutes, seconds)</summary>
    TimeSpan alarmInterval = new TimeSpan(0, 0, 10);

    /// <summary>The next time the alarm will be allowed to sound.</summary>
    DateTime nextAlarmTime;

    /// <summary>List of enemy entity IDs</summary>
    List<TerminalActionParameter> enemies = new List<TerminalActionParameter>();

    /// <summary>List of owner entities IDs contact has been lost with</summary>
    List<TerminalActionParameter> lostContact = new List<TerminalActionParameter>();

    public void Main(string arguments)
    {
      enemies.Clear();

      DetectedEntityData entityData;
      foreach (string serialized in arguments.Split(entitySeparator))
        if (DetectedEntityData.TryDeserialize(serialized, out entityData))
        {
          if (entityData.relations == Relation_Enemy)
          {
            enemies.Add(TerminalActionParameter.Get(entityData.entityId));

            // sound alarm if enemy is near
            if (DateTime.UtcNow >= nextAlarmTime &&
              (entityData.volume > 100 || entityData.volume == 0f) &&
              entityData.secondsSinceDetected < 60 &&
              Vector3D.DistanceSquared(Me.GetPosition(), entityData.predictedPosition) < 10000 * 10000)
            {
              nextAlarmTime = DateTime.UtcNow + alarmInterval;

              IMySoundBlock alarm = GridTerminalSystem.GetBlockWithName("Proximity Alarm")
                as IMySoundBlock;
              if (alarm == null)
              {
                Terminate("Proximity Alarm is not a sound block");
                return;
              }
              else
                alarm.ApplyAction("PlaySound");
            }
          }
          else if (entityData.relations == Relation_Owner && entityData.secondsSinceDetected > 10)
            lostContact.Add(TerminalActionParameter.Get(entityData.entityId));
        }
        else
        {
          Terminate("Deserialize failed: " + serialized);
          return;
        }

      DisplayEntitiesOnPanel("Wide LCD panel for Enemy", enemies);
      DisplayEntitiesOnPanel("Wide LCD panel for Lost Contact", lostContact);
    }

    /// <summary>
    /// Sends entity IDs to a text panel for display. To display GPS or Entity ID on the text panel,
    /// the appropriate command should be added to the text panel.
    /// </summary>
    /// <param name="panelName">The name of the text panel</param>
    /// <param name="entityIds">List of entity IDs to display. Order of the list does not affect
    /// the order they will be displayed in.</param>
    void DisplayEntitiesOnPanel(string panelName, List<TerminalActionParameter> entityIds)
    {
      IMyTerminalBlock term = GridTerminalSystem.GetBlockWithName(panelName);
      if (term == null)
      {
        Terminate(panelName + " does not exist or is not accessible");
        return;
      }
      IMyTextPanel panel = term as IMyTextPanel;
      if (panel == null)
      {
        Terminate(panelName + " is not a text panel");
        return;
      }
      ITerminalAction act = panel.GetActionWithName(displayAction);
      if (act == null)
      {
        Terminate("ARMS actions are not loaded. ARMS must be present in the first world loaded after " +
          "launching Space Engineers for actions to be loaded.");
        return;
      }
      panel.ApplyAction(displayAction, entityIds);
    }

    /// <summary>
    /// Echo a message and disable the Programmable block
    /// </summary>
    /// <param name="message">Message to Echo</param>
    void Terminate(string message)
    {
      Echo(message);
      Me.RequestEnable(false);
    }

    /// <summary>
    /// Contains information about a detected entity.
    /// </summary>
    class DetectedEntityData
    {

      /// <summary>
      /// Attempts to create a DetectedEntityData from a string.
      /// </summary>
      /// <param name="serialized">The serialized object.</param>
      /// <param name="deserialized">The deserialized object</param>
      /// <returns>True iff the string could be deserialized.</returns>
      public static bool TryDeserialize(string serialized, out DetectedEntityData deserialized)
      {
        deserialized = new DetectedEntityData();
        string[] fields = serialized.Split(fieldSeparator);
        int index = 0;

        if (!long.TryParse(fields[index++], out deserialized.entityId))
          return false;
        if (!byte.TryParse(fields[index++], out deserialized.relations))
          return false;
        if (!byte.TryParse(fields[index++], out deserialized.type))
          return false;
        deserialized.name = fields[index++];
        if (!bool.TryParse(fields[index++], out deserialized.hasRadar))
          return false;
        if (!bool.TryParse(fields[index++], out deserialized.hasJammer))
          return false;
        if (!int.TryParse(fields[index++], out  deserialized.secondsSinceDetected))
          return false;
        {
          double x, y, z;
          if (!double.TryParse(fields[index++], out x))
            return false;
          if (!double.TryParse(fields[index++], out y))
            return false;
          if (!double.TryParse(fields[index++], out z))
            return false;
          deserialized.predictedPosition = new Vector3D(x, y, z);
        }
        {
          float x, y, z;
          if (!float.TryParse(fields[index++], out x))
            return false;
          if (!float.TryParse(fields[index++], out y))
            return false;
          if (!float.TryParse(fields[index++], out z))
            return false;
          deserialized.lastKnownVelocity = new Vector3(x, y, z);
        }

        // not always present
        float.TryParse(fields[index++], out deserialized.volume);

        return true;
      }

      public long entityId;
      public byte relations;
      public byte type;
      public string name;
      public bool hasRadar;
      public bool hasJammer;
      public int secondsSinceDetected;
      public Vector3D predictedPosition;
      public Vector3 lastKnownVelocity;
      public float volume;

      private DetectedEntityData() { }

    }

  }
}