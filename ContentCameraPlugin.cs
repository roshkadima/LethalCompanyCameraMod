using BepInEx;
using HarmonyLib;

namespace ContentCameraMod
{
    [BepInPlugin("com.yourname.contentcameramod", "Content Camera Mod", "1.0.0")]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class ContentCameraPlugin : BaseUnityPlugin
    {
        public static ContentCameraPlugin Instance;
        public BepInEx.Logging.ManualLogSource LoggerObj { get; private set; }
        private readonly Harmony harmony = new Harmony("com.yourname.contentcameramod");

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            LoggerObj = Logger;

            Logger.LogInfo("ContentCameraMod is loading...");

            // Patch all Harmony patches
            harmony.PatchAll();

            Logger.LogInfo("ContentCameraMod loaded successfully!");
        }
    }
}
