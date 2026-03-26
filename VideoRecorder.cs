using UnityEngine;
using UnityEngine.Rendering;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading;
using System.Collections.Concurrent;
using BepInEx;
using System.Linq;

namespace ContentCameraMod
{
    public class WavWriter 
    {
        private FileStream fs;
        private BinaryWriter bw;
        public int length = 0;
        private int channels;
        private int sampleRate;

        public WavWriter(string path, int sampleRate, int channels) 
        {
            this.sampleRate = sampleRate;
            this.channels = channels;
            fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            bw = new BinaryWriter(fs);
            for(int i = 0; i < 44; i++) bw.Write((byte)0); 
        }

        public void Write(float[] data) 
        {
            if (bw == null) return;
            for(int i = 0; i < data.Length; i++) {
                short val = (short)Mathf.Clamp(data[i] * 32767f, -32768f, 32767f);
                bw.Write(val);
                length++; // length in samples
            }
        }

        public void Close() 
        {
            if (bw == null) return;
            bw.Seek(0, SeekOrigin.Begin);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write((int)(36 + length * 2));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * 2);
            bw.Write((short)(channels * 2));
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write((int)(length * 2));
            bw.Close();
            fs.Close();
            bw = null;
        }
    }

    public class GameAudioRecorder : MonoBehaviour
    {
        public WavWriter writer;
        public bool isRecording;
        private ConcurrentQueue<float[]> buffer = new ConcurrentQueue<float[]>();

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!isRecording || writer == null) return;
            float[] copy = new float[data.Length];
            Array.Copy(data, copy, data.Length);
            buffer.Enqueue(copy);
        }

        void Update()
        {
            if (writer == null) return;
            while(buffer.TryDequeue(out float[] data)) {
                writer.Write(data);
            }
        }
    }

    public class VideoRecorder : MonoBehaviour
    {
        private Camera captureCamera;
        private RenderTexture renderTexture;
        private Process ffmpegProcess;
        private BinaryWriter ffmpegInput;
        
        private Process micProcess;

        private CameraOverlay overlay;

        private int currentWidth;
        private int currentHeight;
        private int currentFps;
        
        private bool isRecording = false;
        private float frameCounter = 0f;

        private string tempVideoPath;
        private string tempGameAudioPath;
        private string tempMicAudioPath;
        private string finalOutputPath;

        private GameAudioRecorder gameAudioRecorder;
        
        public void Initialize(Camera cam, CameraOverlay camOverlay)
        {
            captureCamera = cam;
            overlay = camOverlay;
        }

        public RenderTexture GetRenderTexture()
        {
            return renderTexture;
        }

        public void StartRecording(string outputPath)
        {
            if (isRecording) return;
            finalOutputPath = outputPath;

            string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            tempVideoPath = Path.Combine(pluginDir, "temp_video.mp4");
            tempGameAudioPath = Path.Combine(pluginDir, "temp_game.wav");
            tempMicAudioPath = Path.Combine(pluginDir, "temp_mic.wav");

            currentWidth = UpgradeManager.CameraLevel == 1 ? 640 : (UpgradeManager.CameraLevel == 2 ? 1280 : 1920);
            currentHeight = UpgradeManager.CameraLevel == 1 ? 480 : (UpgradeManager.CameraLevel == 2 ? 720 : 1080);
            currentFps = UpgradeManager.CameraLevel >= 2 ? 30 : 24;

            if (renderTexture != null && (renderTexture.width != currentWidth || renderTexture.height != currentHeight))
            {
                captureCamera.targetTexture = null;
                Destroy(renderTexture);
                renderTexture = null;
            }

            if (renderTexture == null)
            {
                renderTexture = new RenderTexture(currentWidth, currentHeight, 24, RenderTextureFormat.ARGB32);
                captureCamera.targetTexture = renderTexture;
            }
            
            string ffmpegPath = Path.Combine(pluginDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                ffmpegPath = Path.Combine(Paths.PluginPath, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath)) ffmpegPath = "ffmpeg.exe"; 
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -f rawvideo -vcodec rawvideo -s {currentWidth}x{currentHeight} -pix_fmt rgba -r {currentFps} -i - -vf vflip -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{tempVideoPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            try
            {
                ffmpegProcess = Process.Start(psi);
                ffmpegInput = new BinaryWriter(ffmpegProcess.StandardInput.BaseStream);
                
                // Start Game Audio Recording
                AudioListener listener = FindObjectOfType<AudioListener>();
                if (listener != null)
                {
                    gameAudioRecorder = listener.gameObject.GetComponent<GameAudioRecorder>();
                    if (gameAudioRecorder == null) gameAudioRecorder = listener.gameObject.AddComponent<GameAudioRecorder>();
                    
                    gameAudioRecorder.writer = new WavWriter(tempGameAudioPath, AudioSettings.outputSampleRate, 2);
                    gameAudioRecorder.isRecording = true;
                }

                // Start Mic Audio Recording externally so game chat isn't broken
                string selectedMic = IngamePlayerSettings.Instance.settings.micDevice;
                if (string.IsNullOrEmpty(selectedMic) && Microphone.devices.Length > 0)
                {
                    selectedMic = Microphone.devices[0];
                }

                if (!string.IsNullOrEmpty(selectedMic))
                {
                    ProcessStartInfo micPsi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-y -f dshow -i audio=\"{selectedMic}\" -c:a pcm_s16le \"{tempMicAudioPath}\"",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    };
                    try { micProcess = Process.Start(micPsi); } catch { }
                }

                isRecording = true;
                frameCounter = 0f;
            }
            catch (Exception ex)
            {
                ContentCameraPlugin.Instance.LoggerObj.LogError($"Failed to start ffmpeg: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;

            // Immediately stop the game audio recorder on the main thread
            if (gameAudioRecorder != null)
            {
                gameAudioRecorder.isRecording = false;
                gameAudioRecorder.writer?.Close();
            }

            // Close ffmpeg stdin so it finishes encoding
            try { ffmpegInput?.Close(); } catch { }

            // Do all the heavy waiting on a background thread so the game doesn't freeze!
            var videoProc = ffmpegProcess;
            var micProc = micProcess;
            string tvp = tempVideoPath, tga = tempGameAudioPath, tmp = tempMicAudioPath, fop = finalOutputPath;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    videoProc?.WaitForExit(5000);
                    videoProc?.Close();

                    if (micProc != null)
                    {
                        try { micProc.StandardInput.WriteLine("q"); } catch { }
                        micProc.WaitForExit(3000);
                        if (!micProc.HasExited) micProc.Kill();
                        micProc.Close();
                    }

                    MuxFilesAsync(tvp, tga, tmp, fop);
                }
                catch (Exception ex)
                {
                    ContentCameraPlugin.Instance.LoggerObj.LogError($"Error stopping recording: {ex.Message}");
                }
            });

            ffmpegProcess = null;
            ffmpegInput = null;
            micProcess = null;
        }

        private void MuxFilesAsync(string videoPath, string gameAudioPath, string micAudioPath, string outputPath)
        {
            string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string ffmpegPath = Path.Combine(pluginDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath)) ffmpegPath = "ffmpeg.exe";

            string args;
            if (File.Exists(micAudioPath) && new FileInfo(micAudioPath).Length > 100)
            {
                args = $"-y -i \"{videoPath}\" -i \"{gameAudioPath}\" -i \"{micAudioPath}\" -filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[a]\" -map 0:v -map \"[a]\" -c:v copy -c:a aac -b:a 192k \"{outputPath}\"";
            }
            else
            {
                args = $"-y -i \"{videoPath}\" -i \"{gameAudioPath}\" -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k \"{outputPath}\"";
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process muxProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            muxProc.Exited += (s, e) => {
                try {
                    File.Delete(videoPath);
                    File.Delete(gameAudioPath);
                    if (File.Exists(micAudioPath)) File.Delete(micAudioPath);
                } catch { }
                ContentCameraPlugin.Instance.LoggerObj.LogInfo($"Video fully saved to {outputPath}");
            };
            muxProc.Start();
        }

        private void Update()
        {
            if (!isRecording) return;

            frameCounter += Time.deltaTime;
            float frameInterval = 1f / currentFps;

            if (frameCounter >= frameInterval)
            {
                frameCounter -= frameInterval;
                CaptureFrame();
            }
        }

        private void CaptureFrame()
        {
            captureCamera.Render();
            // Draw the HUD overlay directly onto the RenderTexture after scene render
            overlay?.DrawOverlay(renderTexture);

            AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                if (!isRecording || ffmpegInput == null) return;
                
                var data = request.GetData<byte>();
                byte[] bytes = data.ToArray();
                
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (!isRecording) return;
                    lock (ffmpegInput)
                    {
                        try {
                            ffmpegInput.Write(bytes);
                        } catch {}
                    }
                });
            });
        }
        
        void OnDestroy()
        {
            if (isRecording) StopRecording();
        }
    }
}
