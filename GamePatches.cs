using HarmonyLib;
using UnityEngine;
using System.Linq;
using GameNetcodeStuff;

namespace ContentCameraMod
{
    [HarmonyPatch(typeof(Terminal))] // Patching Terminal because StartOfRound items might not be ready early enough, Terminal has buyableItemsList. Wait, LethalLib needs to process it in Terminal Awake prefix.
    public class GamePatches
    {
        public static Item CameraItemDef;

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        static void TerminalAwakePostfix(Terminal __instance)
        {
            if (CameraItemDef != null) return;

            Item flashlight = Resources.FindObjectsOfTypeAll<Item>().FirstOrDefault(x => x.itemName == "Pro-flashlight");
            if (flashlight == null) return;

            CameraItemDef = ScriptableObject.Instantiate(flashlight);
            CameraItemDef.itemName = "Video Camera";
            CameraItemDef.creditsWorth = 1;
            // WEIGHTLESS FIX: 1.0f in LC means 0 lb weight
            CameraItemDef.weight = 1.0f; 
            // Visuals are shifted locally in VideoCameraItem.Start

            GameObject prefab = Object.Instantiate(flashlight.spawnPrefab);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            prefab.name = "VideoCameraPrefab";

            var oldScript = prefab.GetComponent<GrabbableObject>();
            var newScript = prefab.AddComponent<VideoCameraItem>();
            newScript.itemProperties = CameraItemDef;
            newScript.grabbable = true;
            newScript.grabbableToEnemies = oldScript.grabbableToEnemies;
            Object.Destroy(oldScript);

            // Cosmetic: Tint the flashlight to look different (dark grey/blackish)
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.material != null)
                {
                    renderer.material.color = new Color(0.15f, 0.15f, 0.15f);
                }
            }

            CameraItemDef.spawnPrefab = prefab;

            // Fix duplicate GlobalObjectIdHash that causes AddNetworkPrefab to throw an exception!
            try
            {
                var netObj = prefab.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null)
                {
                    var prop = typeof(Unity.Netcode.NetworkObject).GetProperty("GlobalObjectIdHash", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (prop != null)
                    {
                        prop.SetValue(netObj, (uint)49202102);
                    }
                }
                Unity.Netcode.NetworkManager.Singleton.AddNetworkPrefab(prefab);
            }
            catch (System.Exception ex) { ContentCameraPlugin.Instance.LoggerObj.LogError($"AddNetworkPrefab Error: {ex}"); }

            // Manual Terminal Registration
            var shopItems = __instance.buyableItemsList.ToList();
            if (!shopItems.Contains(CameraItemDef))
            {
                shopItems.Add(CameraItemDef);
                __instance.buyableItemsList = shopItems.ToArray();
            }
            int buyIndex = shopItems.IndexOf(CameraItemDef);

            TerminalKeyword buyKeyword = __instance.terminalNodes.allKeywords.FirstOrDefault(k => k.word == "buy");
            TerminalKeyword confirmKeyword = __instance.terminalNodes.allKeywords.FirstOrDefault(k => k.word == "confirm");
            TerminalKeyword denyKeyword = __instance.terminalNodes.allKeywords.FirstOrDefault(k => k.word == "deny");

            if (buyKeyword != null && confirmKeyword != null && denyKeyword != null)
            {
                TerminalKeyword videoKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
                videoKeyword.name = "VideoKeyword";
                videoKeyword.word = "video";
                videoKeyword.isVerb = false;

                TerminalNode buyConfirmNode = ScriptableObject.CreateInstance<TerminalNode>();
                buyConfirmNode.name = "VideoCameraBuyConfirm";
                buyConfirmNode.displayText = "Ordered 1 Video Camera. Your new balance is [playerCredits].\n\n";
                buyConfirmNode.clearPreviousText = true;
                buyConfirmNode.buyItemIndex = buyIndex;
                buyConfirmNode.itemCost = CameraItemDef.creditsWorth;
                buyConfirmNode.playSyncedClip = 0; 

                TerminalNode buyNode = ScriptableObject.CreateInstance<TerminalNode>();
                buyNode.name = "VideoCameraBuy";
                buyNode.displayText = $"You have requested to order the Video Camera.\nTotal cost of items: {CameraItemDef.creditsWorth}.\n\nPlease CONFIRM or DENY.\n\n";
                buyNode.clearPreviousText = true;
                buyNode.isConfirmationNode = true;
                buyNode.itemCost = CameraItemDef.creditsWorth;
                buyNode.overrideOptions = true; // IMPORTANT for confirmation nodes
                buyNode.terminalOptions = new CompatibleNoun[]
                {
                    new CompatibleNoun { noun = confirmKeyword, result = buyConfirmNode },
                    new CompatibleNoun { noun = denyKeyword, result = denyKeyword.specialKeywordResult ?? ScriptableObject.CreateInstance<TerminalNode>() }
                };

                var buyCompatibleNouns = buyKeyword.compatibleNouns.ToList();
                buyCompatibleNouns.Add(new CompatibleNoun { noun = videoKeyword, result = buyNode });
                buyKeyword.compatibleNouns = buyCompatibleNouns.ToArray();

                var allKws = __instance.terminalNodes.allKeywords.ToList();
                allKws.Add(videoKeyword);
                __instance.terminalNodes.allKeywords = allKws.ToArray();
            }

            ContentCameraPlugin.Instance.LoggerObj.LogInfo("Registered Video Camera to shop manually.");
        }
    }

    [HarmonyPatch(typeof(StartOfRound))]
    public class StarterItemPatches
    {
        private static bool _hasSpawnedOnce = false;

        [HarmonyPatch("StartGame")]
        [HarmonyPostfix]
        static void StartGamePostfix(StartOfRound __instance)
        {
            if (_hasSpawnedOnce) return;
            if (GamePatches.CameraItemDef == null) return;
            if (!Unity.Netcode.NetworkManager.Singleton.IsHost) return;

            __instance.StartCoroutine(SpawnDelayed());
        }

        private static System.Collections.IEnumerator SpawnDelayed()
        {
            yield return new WaitForSeconds(5f); // Wait 5 seconds for safety
            
            if (_hasSpawnedOnce) yield break;

            // elevatorTransform is the most reliable way to find the inside of the ship
            Transform shipElevator = StartOfRound.Instance.elevatorTransform;
            if (shipElevator != null)
            {
                // Spawn in the center of the elevator (ship interior)
                Vector3 spawnPos = shipElevator.position + new Vector3(0, 1.0f, 0); 
                GameObject spawned = Object.Instantiate(GamePatches.CameraItemDef.spawnPrefab, spawnPos, Quaternion.identity);
                var netObj = spawned.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null) 
                {
                    netObj.Spawn();
                    _hasSpawnedOnce = true;
                    ContentCameraPlugin.Instance.LoggerObj.LogInfo("SUCCESS: Spawned starter Video Camera on ship floor using elevatorTransform.");
                }
            }
            else
            {
                ContentCameraPlugin.Instance.LoggerObj.LogWarning("FAILED: elevatorTransform not found for spawning.");
            }
        }
    }

    [HarmonyPatch(typeof(GameNetworkManager))]
    public class InventoryPatches
    {
        [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
        [HarmonyPostfix]
        static void PlayerControllerBAwakePostfix(PlayerControllerB __instance)
        {
            if (__instance.ItemSlots.Length < 5)
            {
                var newSlots = new GrabbableObject[5];
                __instance.ItemSlots.CopyTo(newSlots, 0);
                __instance.ItemSlots = newSlots;
            }
        }

        [HarmonyPatch(typeof(HUDManager), "Start")]
        [HarmonyPostfix]
        static void HUDManagerStartPostfix(HUDManager __instance)
        {
            if (__instance.itemSlotIconFrames.Length < 5)
            {
                // Clone the first slot to create the 5th
                var newFrames = new UnityEngine.UI.Image[5];
                __instance.itemSlotIconFrames.CopyTo(newFrames, 0);
                var newFrameObj = Object.Instantiate(__instance.itemSlotIconFrames[0].gameObject, __instance.itemSlotIconFrames[0].transform.parent);
                newFrames[4] = newFrameObj.GetComponent<UnityEngine.UI.Image>();
                __instance.itemSlotIconFrames = newFrames;

                var newIcons = new UnityEngine.UI.Image[5];
                __instance.itemSlotIcons.CopyTo(newIcons, 0);
                var newIconObj = Object.Instantiate(__instance.itemSlotIcons[0].gameObject, __instance.itemSlotIcons[0].transform.parent);
                newIcons[4] = newIconObj.GetComponent<UnityEngine.UI.Image>();
                __instance.itemSlotIcons = newIcons;
                
                // OFFSET FIX: Shift the 5th slot to the right of the 4th (standard gap is ~70-80 units)
                // Assuming slots are laid out horizontally in a Grid or just absolute positions
                RectTransform rect4 = __instance.itemSlotIconFrames[3].GetComponent<RectTransform>();
                RectTransform rect5Frame = newFrameObj.GetComponent<RectTransform>();
                RectTransform rect5Icon = newIconObj.GetComponent<RectTransform>();

                // Shift by 70 pixels to the right
                rect5Frame.anchoredPosition = rect4.anchoredPosition + new Vector2(70, 0);
                rect5Icon.anchoredPosition = rect4.anchoredPosition + new Vector2(70, 0);
            }
        }
    }
}
