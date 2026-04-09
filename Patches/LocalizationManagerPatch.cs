using HarmonyLib;
using I2.Loc;
using UnityEngine;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(LocalizationManager))]
class LocalizationManagerPatch
{
    [HarmonyPatch("GetTranslation")]
    [HarmonyPrefix]
    static bool PreGetTranslation(string Term, bool FixForRTL, int maxLineLengthForRTL, bool ignoreRTLnumbers, bool applyParameters, GameObject localParametersRoot, string overrideLanguage, bool allowLocalizedParameters, ref string __result)
    {
        if (Term.StartsWith("notifications/newItem"))
        {
            __result = "Item received from ";
            return false;
        }
        else if (Term.StartsWith("notifications/sentItem"))
        {
            __result = "Item sent to ";
            return false;
        }
        return true;
    }
}
