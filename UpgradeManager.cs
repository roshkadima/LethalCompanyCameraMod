using HarmonyLib;
using UnityEngine;

namespace ContentCameraMod
{
    [HarmonyPatch(typeof(Terminal))]
    public static class UpgradeManager
    {
        public static int CameraLevel = 1; // 1: 640x480, 2: 1280x720, 3: 1920x1080
        
        [HarmonyPatch("ParsePlayerSentence")]
        [HarmonyPrefix]
        public static bool ParseSentence(Terminal __instance, ref TerminalNode __result)
        {
            if (__instance.screenText == null) return true;
            if (__instance.textAdded <= 0 || __instance.screenText.text.Length < __instance.textAdded) return true;
            string text = __instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded).ToLower().Trim();
            
            if (text == "upgrade camera")
            {
                if (CameraLevel >= 3)
                {
                    __result = CreateNode("Camera is already max level.\n\n");
                    return false;
                }
                
                int cost = 200 * CameraLevel;
                if (__instance.groupCredits >= cost)
                {
                    __instance.groupCredits -= cost;
                    CameraLevel++;
                    
                    // Sync credits with the server
                    __instance.SyncGroupCreditsServerRpc(__instance.groupCredits, __instance.numberOfItemsInDropship);
                    // Usually you play a sound here but for simplicity we skip it
                    
                    __result = CreateNode($"Upgraded Camera to level {CameraLevel}!\nNew battery life and video quality applied.\nYour balance: {__instance.groupCredits}\n\n");
                }
                else
                {
                    __result = CreateNode($"Not enough credits. You need {cost} credits.\n\n");
                }
                return false;
            }
            return true;
        }

        private static TerminalNode CreateNode(string text)
        {
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.displayText = text;
            node.clearPreviousText = true;
            node.maxCharactersToType = 50;
            return node;
        }
    }
}
