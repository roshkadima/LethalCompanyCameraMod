using UnityEngine;
using System.IO;
using System;
using BepInEx;
using System.Reflection;

namespace ContentCameraMod
{
    public class VideoCameraItem : GrabbableObject
    {
        private bool isRecording = false;
        private VideoRecorder recorder;
        private Camera itemCamera;
        private CameraOverlay overlay;
        private float totalShift = 0.45f;

        private AudioSource audioSource;
        private AudioClip startBeep;
        private AudioClip stopBeep;

        /// <summary>Pro-flashlight body/light transform; custom mesh parents here so it follows the same hand anchor as the lens.</summary>
        private Transform _visualAttach;
        private Light _itemLight;

        /// <summary>Uniform scale for recentersized OBJ (~handheld tool size vs pro-flashlight grip).</summary>
        private const float CameraBodyUniformScale = 1.35f;

        public override void Start()
        {
            base.Start();

            // Ensure the camera arrives fully charged
            if (insertedBattery != null)
            {
                insertedBattery.charge = 1f;
                insertedBattery.empty = false;
            }
            else
            {
                insertedBattery = new Battery(false, 1f);
            }
            
            GameObject camObj = new GameObject("CameraLens");
            
            _itemLight = this.GetComponentInChildren<Light>();
            if (_itemLight != null)
            {
                _visualAttach = _itemLight.transform;
                camObj.transform.SetParent(_visualAttach, false);
                camObj.transform.localPosition = Vector3.zero;
                camObj.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _visualAttach = transform;
                camObj.transform.SetParent(this.transform, false);
                camObj.transform.localPosition = new Vector3(0f, 0f, 0.5f); 
                camObj.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); 
            }

            itemCamera = camObj.AddComponent<Camera>();
            itemCamera.enabled = false;
            itemCamera.fieldOfView = 80f;
            var listener = camObj.GetComponent<AudioListener>();
            if (listener != null) Destroy(listener);

            overlay = new CameraOverlay();

            recorder = this.gameObject.AddComponent<VideoRecorder>();
            recorder.Initialize(itemCamera, overlay);

            // Shift everything up to prevent sinking into the floor (visuals AND recording camera)
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                // Don't shift the scan node, but DO shift the lens and model pieces
                if (child.name != "ScanNode")
                {
                    child.localPosition += new Vector3(0, totalShift, 0); 
                }
            }

            // Apply custom model and texture
            LoadAndApplyModelAndTexture();

            // ===== Audio source + generate beeps =====
            audioSource = this.gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; // 3D sound
            audioSource.volume = 0.6f;
            audioSource.playOnAwake = false;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 15f;

            startBeep = GenerateBeep(880f, 0.15f, 48000);  // High-pitched short beep
            stopBeep = GenerateBeep(440f, 0.25f, 48000);   // Lower longer beep
        }

        private AudioClip GenerateBeep(float frequency, float duration, int sampleRate)
        {
            int sampleCount = (int)(sampleRate * duration);
            AudioClip clip = AudioClip.Create("beep", sampleCount, 1, sampleRate, false);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // Sine wave with fade-out envelope
                float envelope = 1f - (t / duration);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.4f * envelope;
            }
            clip.SetData(samples, 0);
            return clip;
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (buttonDown) 
            {
                isRecording = !isRecording;
                isBeingUsed = isRecording; 

                if (isRecording)
                {
                    StartRecording();
                }
                else
                {
                    StopRecording();
                }
            }
        }

        private void StartRecording()
        {
            if (playerHeldBy != null && playerHeldBy.gameplayCamera != null)
            {
                itemCamera.cullingMask = playerHeldBy.gameplayCamera.cullingMask;
            }

            overlay?.StartOverlay(() => insertedBattery != null ? insertedBattery.charge : 0f);

            // Play start beep
            if (audioSource != null && startBeep != null)
            {
                audioSource.PlayOneShot(startBeep);
            }

            ContentCameraPlugin.Instance.LoggerObj.LogInfo("Started recording video...");
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = $"CameraRecord_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            recorder.StartRecording(Path.Combine(desktopPath, fileName));
        }

        private void StopRecording()
        {
            overlay?.StopOverlay();

            // Play stop beep
            if (audioSource != null && stopBeep != null)
            {
                audioSource.PlayOneShot(stopBeep);
            }

            ContentCameraPlugin.Instance.LoggerObj.LogInfo("Stopped recording video.");
            recorder.StopRecording();
        }

        private void LoadAndApplyModelAndTexture()
        {
            try
            {
                string dllPath = Assembly.GetExecutingAssembly().Location;
                string modFolder = Path.GetDirectoryName(dllPath);
                
                // Robust path finding helper (checks multiple possible locations)
                string FindAssetPath(string fileName, string subFolder)
                {
                    string[] possiblefolders = new string[] { 
                        modFolder, 
                        Path.GetDirectoryName(modFolder), 
                        Path.GetDirectoryName(Path.GetDirectoryName(modFolder)), 
                        Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(modFolder))) 
                    };

                    foreach (var folder in possiblefolders)
                    {
                        if (string.IsNullOrEmpty(folder)) continue;
                        
                        // Try exact subfolder match
                        string target = Path.Combine(folder, subFolder, fileName);
                        if (File.Exists(target)) return target;
                        
                        // Try just the first level of subfolder (e.g. models/ instead of models/camera/)
                        string parentSubFolder = subFolder.Split(Path.DirectorySeparatorChar)[0];
                        target = Path.Combine(folder, parentSubFolder, fileName);
                        if (File.Exists(target)) return target;

                        // Try directly in the subfolder folder name (e.g. models/camera.obj if passed models/camera)
                        target = Path.Combine(folder, "models", fileName);
                        if (File.Exists(target)) return target;
                        
                        target = Path.Combine(folder, "assets", fileName);
                        if (File.Exists(target)) return target;
                    }
                    return Path.Combine(modFolder, subFolder, fileName); // Default fallback
                }

                string objPath = FindAssetPath("camera.obj", Path.Combine("models", "camera"));
                if (!File.Exists(objPath))
                {
                    objPath = FindAssetPath("camera_with_tripot.obj", Path.Combine("models", "camera_with_3"));
                }
                string texPath = FindAssetPath("Camera.png", "assets");

                ContentCameraPlugin.Instance.LoggerObj.LogInfo($"Attempting to load assets. Derived modFolder: {modFolder}");
                ContentCameraPlugin.Instance.LoggerObj.LogInfo($"Resolved OBJ path: {objPath}");
                ContentCameraPlugin.Instance.LoggerObj.LogInfo($"Resolved PNG path: {texPath}");

                Mesh customMesh = null;
                if (File.Exists(objPath))
                {
                    ContentCameraPlugin.Instance.LoggerObj.LogInfo("Found OBJ file, parsing...");
                    customMesh = ObjLoader.LoadOBJ(objPath);
                    if (customMesh != null) ContentCameraPlugin.Instance.LoggerObj.LogInfo("Successfully parsed OBJ mesh.");
                }
                else
                {
                    ContentCameraPlugin.Instance.LoggerObj.LogWarning("OBJ file not found!");
                }

                Texture2D customTex = null;
                if (File.Exists(texPath))
                {
                    ContentCameraPlugin.Instance.LoggerObj.LogInfo("Found PNG file, loading...");
                    byte[] fileData = File.ReadAllBytes(texPath);
                    customTex = new Texture2D(2, 2);
                    if (customTex.LoadImage(fileData))
                    {
                        ContentCameraPlugin.Instance.LoggerObj.LogInfo("Successfully loaded PNG texture.");
                    }
                }
                else
                {
                    ContentCameraPlugin.Instance.LoggerObj.LogWarning("PNG file not found!");
                }

                if (customMesh != null)
                {
                    Renderer templateRenderer = null;
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.gameObject.name == "CameraLens" || r.gameObject.name == "ScanNode")
                            continue;
                        templateRenderer = r;
                        break;
                    }

                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.gameObject.name == "CameraLens" || r.gameObject.name == "ScanNode")
                            continue;
                        r.enabled = false;
                    }

                    if (_itemLight != null)
                        _itemLight.enabled = false;

                    Transform attach = _visualAttach != null ? _visualAttach : transform;
                    GameObject modelObj = new GameObject("CustomCameraModel");
                    modelObj.transform.SetParent(attach, false);
                    modelObj.layer = this.gameObject.layer;
                    modelObj.transform.localPosition = Vector3.zero;
                    modelObj.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    modelObj.transform.localScale = Vector3.one * CameraBodyUniformScale;

                    MeshFilter filter = modelObj.AddComponent<MeshFilter>();
                    filter.mesh = customMesh;

                    MeshRenderer renderer = modelObj.AddComponent<MeshRenderer>();
                    Material mat = (templateRenderer != null)
                        ? new Material(templateRenderer.material)
                        : new Material(Shader.Find("HDRP/Lit"));
                    
                    if (customTex != null)
                    {
                        mat.mainTexture = customTex;
                        mat.SetTexture("_BaseMap", customTex);
                        mat.SetTexture("_BaseColorMap", customTex);
                        mat.SetTexture("_MainTex", customTex);
                        mat.SetColor("_BaseColor", Color.white);
                        mat.SetColor("_Color", Color.white);
                        if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.0f);
                        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0.0f);
                    }
                    
                    renderer.material = mat;
                    
                    // Disable LODGroups to ensure our model is always visible
                    foreach (var lod in GetComponentsInChildren<LODGroup>(true))
                    {
                        lod.enabled = false;
                    }

                    ContentCameraPlugin.Instance.LoggerObj.LogInfo("Successfully created dedicated CustomCameraModel GameObject.");
                }
                else if (customTex != null)
                {
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.gameObject.name == "CameraLens") continue;
                        r.material.mainTexture = customTex;
                        r.material.SetTexture("_BaseColorMap", customTex);
                        r.material.SetTexture("_BaseMap", customTex);
                        r.material.color = Color.white;
                    }
                    ContentCameraPlugin.Instance.LoggerObj.LogInfo("Applied texture to original mesh (fallback).");
                }

                // Force upright rotation in item properties
                itemProperties.restingRotation = Vector3.zero;
            }
            catch (Exception ex)
            {
                ContentCameraPlugin.Instance.LoggerObj.LogError($"Error in model/texture loading: {ex.StackTrace}");
            }
        }

        public override void DiscardItem()
        {
            if (playerHeldBy != null)
            {
                // Always stay upright!
                itemProperties.restingRotation = Vector3.zero; 
            }
            base.DiscardItem();
        }

        public override void Update()
        {
            base.Update();
            
            // Re-force upright rotation if not held, to prevent flipping
            if (!isHeld)
            {
                if (itemProperties.restingRotation != Vector3.zero)
                {
                    itemProperties.restingRotation = Vector3.zero;
                }
            }

            if (isHeld && isRecording && insertedBattery != null)
            {
                float drainMultiplier = UpgradeManager.CameraLevel == 1 ? 1f : (UpgradeManager.CameraLevel == 2 ? 0.6f : 0.3f);
                if (drainMultiplier < 1f)
                {
                    insertedBattery.charge += (Time.deltaTime / itemProperties.batteryUsage) * (1f - drainMultiplier);
                }

                if (insertedBattery.charge <= 0)
                {
                    isRecording = false;
                    StopRecording();
                    isBeingUsed = false;
                }
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            
            if (isRecording && itemCamera != null && playerHeldBy != null && playerHeldBy.gameplayCamera != null)
            {
                // Copy player's look direction exactly
                itemCamera.transform.rotation = playerHeldBy.gameplayCamera.transform.rotation;

                // Add gentle procedural sway on top
                float t = Time.time;
                float shakeSpeed = 1.2f;
                float shakeAmount = 1.0f;

                float pitch = (Mathf.PerlinNoise(t * shakeSpeed, 10.5f) - 0.5f) * shakeAmount;
                float yaw   = (Mathf.PerlinNoise(t * shakeSpeed + 50.3f, 30.7f) - 0.5f) * shakeAmount;
                float roll  = (Mathf.PerlinNoise(t * shakeSpeed + 100.1f, 70.9f) - 0.5f) * (shakeAmount * 0.3f);

                itemCamera.transform.rotation *= Quaternion.Euler(pitch, yaw, roll);
            }
        }

        private void OnGUI()
        {
            // Only draw if recording and held by the local player
            if (isRecording && recorder != null && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController)
            {
                RenderTexture rt = recorder.GetRenderTexture();
                if (rt != null)
                {
                    float screenHeight = Screen.height;
                    float screenWidth = Screen.width;
                    
                    // Dynamic scaling based on resolution
                    float width = 320f * (screenWidth / 1920f);
                    float height = 200f * (screenWidth / 1920f);

                    // Position: Bottom-right, but raised up to "camera level" (above the hand area)
                    float posX = screenWidth - width - 40f * (screenWidth / 1920f);
                    float posY = screenHeight - height - 180f * (screenWidth / 1920f);

                    Rect rect = new Rect(posX, posY, width, height);
                    
                    // Background frame/border
                    GUI.color = new Color(0, 0, 0, 0.9f);
                    GUI.Box(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), "");
                    
                    // Live feed
                    GUI.color = Color.white;
                    GUI.DrawTexture(rect, rt);
                    
                    // Small recording indicator on the preview itself
                    if ((Time.time % 1.0f) < 0.7f)
                    {
                        GUI.color = Color.red;
                        GUI.Label(new Rect(rect.x + 10, rect.y + 10, 150, 25), "● REC LIVE");
                    }
                }
            }
        }
    }
}
