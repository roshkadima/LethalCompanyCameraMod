using UnityEngine;
using System;

namespace ContentCameraMod
{
    public class CameraOverlay
    {
        private Material lineMaterial;
        private float recordStartTime;
        private bool isRecording;
        private Func<float> getBatteryCharge;

        public void StartOverlay(Func<float> batteryFunc)
        {
            getBatteryCharge = batteryFunc;
            recordStartTime = Time.time;
            isRecording = true;
        }

        public void StopOverlay()
        {
            isRecording = false;
        }

        private void CreateLineMaterial()
        {
            if (lineMaterial) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            lineMaterial = new Material(shader);
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", 0);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        public void DrawOverlay(RenderTexture rt)
        {
            if (!isRecording || rt == null) return;
            CreateLineMaterial();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            float w = rt.width;
            float h = rt.height;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, h, 0);
            lineMaterial.SetPass(0);

            float scale = w / 1280f; // Bigger base scale
            float elapsed = Time.time - recordStartTime;

            // ==========================================
            // TOP-LEFT: REC indicator + timer
            // ==========================================
            bool blink = (elapsed % 1.0f) < 0.65f;
            if (blink)
            {
                DrawFilledCircle(40f * scale, 40f * scale, 14f * scale, new Color(1f, 0f, 0f, 0.95f));
            }
            // REC label
            DrawFilledRect(62f * scale, 26f * scale, 80f * scale, 28f * scale, new Color(1f, 0f, 0f, 0.85f));
            // White border around REC
            DrawRect(62f * scale, 26f * scale, 80f * scale, 28f * scale, new Color(1f, 1f, 1f, 0.5f));

            // Timer box
            int mins = (int)(elapsed / 60f);
            int secs = (int)(elapsed % 60f);
            DrawFilledRect(155f * scale, 26f * scale, 100f * scale, 28f * scale, new Color(0f, 0f, 0f, 0.6f));
            DrawRect(155f * scale, 26f * scale, 100f * scale, 28f * scale, new Color(1f, 1f, 1f, 0.3f));

            // ==========================================
            // TOP-RIGHT: Battery
            // ==========================================
            float charge = getBatteryCharge != null ? getBatteryCharge() : 1f;
            float battX = w - 130f * scale;
            float battY = 26f * scale;
            float battW = 70f * scale;
            float battH = 28f * scale;

            // Battery body outline
            DrawRect(battX, battY, battW, battH, Color.white);
            // Battery tip
            DrawFilledRect(battX + battW, battY + 7f * scale, 8f * scale, 14f * scale, Color.white);
            // Battery fill
            Color battColor = charge > 0.5f ? new Color(0.2f, 1f, 0.2f, 0.9f) :
                              charge > 0.2f ? new Color(1f, 0.8f, 0f, 0.9f) :
                                              new Color(1f, 0.15f, 0.15f, 0.9f);
            float fillW = (battW - 6f * scale) * Mathf.Clamp01(charge);
            DrawFilledRect(battX + 3f * scale, battY + 3f * scale, fillW, battH - 6f * scale, battColor);

            // ==========================================
            // TOP-RIGHT below battery: Storage/quality indicator
            // ==========================================
            float qualY = battY + battH + 10f * scale;
            DrawFilledRect(w - 130f * scale, qualY, 78f * scale, 22f * scale, new Color(0f, 0f, 0f, 0.5f));
            DrawRect(w - 130f * scale, qualY, 78f * scale, 22f * scale, new Color(0.5f, 0.8f, 1f, 0.4f));

            // ==========================================
            // BOTTOM-LEFT: Focus indicator (animated brackets)
            // ==========================================
            float focusX = 40f * scale;
            float focusY = h - 70f * scale;
            float focusSize = 36f * scale;
            float focusPulse = 1f + Mathf.Sin(Time.time * 2f) * 0.08f;
            float fs = focusSize * focusPulse;
            Color fc = new Color(0f, 1f, 0.5f, 0.7f);
            float ft = 3f * scale;

            // Focus square brackets
            DrawFilledRect(focusX - fs, focusY - fs, fs * 0.4f, ft, fc);
            DrawFilledRect(focusX - fs, focusY - fs, ft, fs * 0.4f, fc);
            DrawFilledRect(focusX + fs - fs * 0.4f, focusY - fs, fs * 0.4f, ft, fc);
            DrawFilledRect(focusX + fs - ft, focusY - fs, ft, fs * 0.4f, fc);
            DrawFilledRect(focusX - fs, focusY + fs - ft, fs * 0.4f, ft, fc);
            DrawFilledRect(focusX - fs, focusY + fs - fs * 0.4f, ft, fs * 0.4f, fc);
            DrawFilledRect(focusX + fs - fs * 0.4f, focusY + fs - ft, fs * 0.4f, ft, fc);
            DrawFilledRect(focusX + fs - ft, focusY + fs - fs * 0.4f, ft, fs * 0.4f, fc);

            // ==========================================
            // BOTTOM-RIGHT: Date & Time stamp
            // ==========================================
            float stampW = 180f * scale;
            float stampH = 28f * scale;
            float stampX = w - 40f * scale - stampW;
            float stampY = h - 60f * scale;
            DrawFilledRect(stampX, stampY, stampW, stampH, new Color(0f, 0f, 0f, 0.5f));
            DrawRect(stampX, stampY, stampW, stampH, new Color(1f, 0.6f, 0f, 0.4f));

            // ==========================================
            // CENTER: Crosshair with circle
            // ==========================================
            float cx = w / 2f;
            float cy = h / 2f;
            float crossLen = 18f * scale;
            float crossGap = 6f * scale;
            Color crossColor = new Color(1f, 1f, 1f, 0.5f);
            float ct = 2f * scale;

            // Cross lines with gap in center
            DrawFilledRect(cx - crossLen, cy - ct / 2f, crossLen - crossGap, ct, crossColor);
            DrawFilledRect(cx + crossGap, cy - ct / 2f, crossLen - crossGap, ct, crossColor);
            DrawFilledRect(cx - ct / 2f, cy - crossLen, ct, crossLen - crossGap, crossColor);
            DrawFilledRect(cx - ct / 2f, cy + crossGap, ct, crossLen - crossGap, crossColor);

            // Circle around crosshair
            DrawCircleOutline(cx, cy, 30f * scale, new Color(1f, 1f, 1f, 0.25f), 2f * scale);

            // ==========================================
            // CORNER BRACKETS (viewfinder frame)
            // ==========================================
            float bracketLen = 60f * scale;
            float bracketThick = 3f * scale;
            float margin = 30f * scale;
            Color bc = new Color(1f, 1f, 1f, 0.5f);

            // Top-left
            DrawFilledRect(margin, margin, bracketLen, bracketThick, bc);
            DrawFilledRect(margin, margin, bracketThick, bracketLen, bc);
            // Top-right
            DrawFilledRect(w - margin - bracketLen, margin, bracketLen, bracketThick, bc);
            DrawFilledRect(w - margin - bracketThick, margin, bracketThick, bracketLen, bc);
            // Bottom-left
            DrawFilledRect(margin, h - margin - bracketThick, bracketLen, bracketThick, bc);
            DrawFilledRect(margin, h - margin - bracketLen, bracketThick, bracketLen, bc);
            // Bottom-right
            DrawFilledRect(w - margin - bracketLen, h - margin - bracketThick, bracketLen, bracketThick, bc);
            DrawFilledRect(w - margin - bracketThick, h - margin - bracketLen, bracketThick, bracketLen, bc);

            // ==========================================
            // LEFT SIDE: Audio level bars
            // ==========================================
            float barX = 20f * scale;
            float barBaseY = h / 2f + 60f * scale;
            float barW = 8f * scale;
            int barCount = 8;
            for (int i = 0; i < barCount; i++)
            {
                float barH = 6f * scale;
                float by = barBaseY - i * (barH + 3f * scale);
                // Animated: random-ish fill based on Perlin noise
                float level = Mathf.PerlinNoise(Time.time * 3f + i * 0.7f, i * 1.3f);
                Color barColor;
                if (i < 5) barColor = new Color(0.2f, 1f, 0.4f, 0.7f);
                else if (i < 7) barColor = new Color(1f, 0.8f, 0f, 0.7f);
                else barColor = new Color(1f, 0.2f, 0.2f, 0.7f);

                if (level > (float)i / barCount)
                    DrawFilledRect(barX, by, barW, barH, barColor);
                else
                    DrawFilledRect(barX, by, barW, barH, new Color(0.3f, 0.3f, 0.3f, 0.3f));
            }

            // ==========================================
            // SCAN LINES (subtle VHS effect)
            // ==========================================
            Color scanColor = new Color(0f, 0f, 0f, 0.04f);
            for (float y = 0; y < h; y += 3f * scale)
            {
                DrawFilledRect(0, y, w, 1f * scale, scanColor);
            }

            // Thin border around entire frame
            DrawRect(4f * scale, 4f * scale, w - 8f * scale, h - 8f * scale, new Color(1f, 1f, 1f, 0.15f));

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private void DrawFilledRect(float x, float y, float width, float height, Color color)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x + width, y, 0);
            GL.Vertex3(x + width, y + height, 0);
            GL.Vertex3(x, y + height, 0);
            GL.End();
        }

        private void DrawRect(float x, float y, float width, float height, Color color)
        {
            float t = 2f;
            DrawFilledRect(x, y, width, t, color);
            DrawFilledRect(x, y + height - t, width, t, color);
            DrawFilledRect(x, y, t, height, color);
            DrawFilledRect(x + width - t, y, t, height, color);
        }

        private void DrawFilledCircle(float cx, float cy, float radius, Color color)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float a1 = (float)i / segments * Mathf.PI * 2f;
                float a2 = (float)(i + 1) / segments * Mathf.PI * 2f;
                GL.Vertex3(cx, cy, 0);
                GL.Vertex3(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius, 0);
                GL.Vertex3(cx + Mathf.Cos(a2) * radius, cy + Mathf.Sin(a2) * radius, 0);
            }
            GL.End();
        }

        private void DrawCircleOutline(float cx, float cy, float radius, Color color, float thickness)
        {
            int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a1 = (float)i / segments * Mathf.PI * 2f;
                float a2 = (float)(i + 1) / segments * Mathf.PI * 2f;

                float x1 = cx + Mathf.Cos(a1) * radius;
                float y1 = cy + Mathf.Sin(a1) * radius;
                float x2 = cx + Mathf.Cos(a2) * radius;
                float y2 = cy + Mathf.Sin(a2) * radius;

                float dx = x2 - x1;
                float dy = y2 - y1;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 0.001f) continue;
                float nx = -dy / len * thickness * 0.5f;
                float ny = dx / len * thickness * 0.5f;

                GL.Begin(GL.QUADS);
                GL.Color(color);
                GL.Vertex3(x1 + nx, y1 + ny, 0);
                GL.Vertex3(x2 + nx, y2 + ny, 0);
                GL.Vertex3(x2 - nx, y2 - ny, 0);
                GL.Vertex3(x1 - nx, y1 - ny, 0);
                GL.End();
            }
        }
    }
}
