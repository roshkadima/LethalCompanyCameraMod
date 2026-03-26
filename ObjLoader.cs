using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Globalization;

namespace ContentCameraMod
{
    public static class ObjLoader
    {
        public static Mesh LoadOBJ(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            List<Vector3> temp_vertices = new List<Vector3>();
            List<Vector3> temp_normals = new List<Vector3>();
            List<Vector2> temp_uvs = new List<Vector2>();

            string[] lines = File.ReadAllLines(filePath);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("v "))
                {
                    temp_vertices.Add(ParseVector3(trimmed));
                }
                else if (trimmed.StartsWith("vn "))
                {
                    temp_normals.Add(ParseVector3(trimmed));
                }
                else if (trimmed.StartsWith("vt "))
                {
                    temp_uvs.Add(ParseVector2(trimmed));
                }
                else if (trimmed.StartsWith("f "))
                {
                    string[] parts = trimmed.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 2; i < parts.Length - 1; i++)
                    {
                        AddFacePoint(parts[1], temp_vertices, temp_uvs, temp_normals, vertices, uvs, normals, triangles);
                        AddFacePoint(parts[i], temp_vertices, temp_uvs, temp_normals, vertices, uvs, normals, triangles);
                        AddFacePoint(parts[i + 1], temp_vertices, temp_uvs, temp_normals, vertices, uvs, normals, triangles);
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(filePath);
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            RecenterMeshToBoundsCenter(mesh);
            return mesh;
        }

        /// <summary>
        /// Moves vertex data so the axis-aligned bounds center is at the origin (fixes Blender exports with a far-off pivot).
        /// </summary>
        public static void RecenterMeshToBoundsCenter(Mesh mesh)
        {
            if (mesh == null) return;
            Vector3 c = mesh.bounds.center;
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] -= c;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        private static void AddFacePoint(string part, List<Vector3> temp_v, List<Vector2> temp_uv, List<Vector3> temp_vn, 
                                        List<Vector3> v, List<Vector2> uv, List<Vector3> vn, List<int> tri)
        {
            string[] subParts = part.Split('/');
            
            int vIndex = int.Parse(subParts[0]) - 1;
            int uvIndex = (subParts.Length > 1 && !string.IsNullOrEmpty(subParts[1])) ? int.Parse(subParts[1]) - 1 : -1;
            int nIndex = (subParts.Length > 2 && !string.IsNullOrEmpty(subParts[2])) ? int.Parse(subParts[2]) - 1 : -1;

            v.Add(temp_v[vIndex]);
            Vector2 uvCoord = (uvIndex >= 0 && uvIndex < temp_uv.Count) ? temp_uv[uvIndex] : Vector2.zero;
            // Flip V coordinate (typical Blender-Unity mismatch)
            uvCoord.y = 1.0f - uvCoord.y; 
            uv.Add(uvCoord);
            vn.Add((nIndex >= 0 && nIndex < temp_vn.Count) ? temp_vn[nIndex] : Vector3.up);
            tri.Add(v.Count - 1);
        }

        private static Vector3 ParseVector3(string line)
        {
            string[] parts = line.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return new Vector3(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture)
            );
        }

        private static Vector2 ParseVector2(string line)
        {
            string[] parts = line.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return new Vector2(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture)
            );
        }
    }
}
