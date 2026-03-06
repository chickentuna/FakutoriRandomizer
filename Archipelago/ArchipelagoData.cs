using Newtonsoft.Json;
using System.Collections.Generic;

namespace FakutoriArchipelago.Archipelago;

public class ArchipelagoData
{
    public string Uri;
    public string SlotName;
    public string Password;
    public int Index;

    /// <summary>
    /// seed for this archipelago data. Can be used when loading a file to verify the session the player is trying to
    /// load is valid to the room it's connecting to.
    /// </summary>
    private string seed;

    public Dictionary<string, object> slotData;

    public bool NeedSlotData => slotData == null;

    public ArchipelagoData()
    {
        Uri = "localhost:38281";
        SlotName = "chickentuna";
    }

    public ArchipelagoData(string uri, string slotName, string password)
    {
        Uri = uri;
        SlotName = slotName;
        Password = password;
    }

    /// <summary>
    /// assigns the slot data and seed to our data handler. any necessary setup using this data can be done here.
    /// </summary>
    /// <param name="roomSlotData">slot data of your slot from the room</param>
    /// <param name="roomSeed">seed name of this session</param>
    public void SetupSession(Dictionary<string, object> roomSlotData, string roomSeed)
    {
        slotData = roomSlotData;
        Plugin.BepinLogger.LogInfo("Slot Data received:");
        foreach (var kv in slotData)
        {
            Plugin.BepinLogger.LogInfo($"    {kv.Key}: {kv.Value} ({kv.Value.GetType()})");
        }
        seed = roomSeed;
    }

    /// <summary>
    /// returns the object as a json string to be written to a file which you can then load
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return JsonConvert.SerializeObject(this);
    }
}