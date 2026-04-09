using Fakutori.Grid;
using HarmonyLib;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(Notifications))]
class NotificationsPatch
{
    public static bool AllowQueueNotification = false;

    [HarmonyPatch("QueueNotification")]
    [HarmonyPrefix]
    static bool PreQueueNotification(Notifications __instance, NotificationType type, BlockData block, GridCell onCell)
    {
        if (AllowQueueNotification)
        {
            return true;
        }
        return false;
    }
}
