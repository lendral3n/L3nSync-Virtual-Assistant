using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BvhBrowser
{
    /// <summary>
    /// Parser + evaluator BVH (Biovision Hierarchy) — port bersih dari BvhParser.kt (Android).
    /// HIERARCHY: pohon joint (OFFSET + CHANNELS). MOTION: data frame per baris.
    /// FK menghasilkan posisi world tiap joint per frame (untuk stick-figure) dan
    /// rotasi lokal per joint (untuk retarget ke VRM di Tahap 2).
    /// </summary>
    public class BvhJoint
    {
        public string name;
        public int parent = -1;         // index di flat list, -1 = root
        public Vector3 offset;
        public string[] channels;       // urutan PENTING (mis. Zrotation Xrotation Yrotation)
        public int channelStart;        // kolom pertama joint ini di data frame
        public bool isEndSite;
    }

    public class BvhClip
    {
        public List<BvhJoint> joints = new List<BvhJoint>();
        public float[][] frames;        // [frame][channel]
        public float frameTime = 1f / 30f;
        public int FrameCount => frames != null ? frames.Length : 0;
        public int ChannelCount { get; private set; }

        /// <summary>Posisi world semua joint untuk frame f (out.Length = joints.Count).</summary>
        public void EvaluateWorld(int f, Vector3[] outPos)
        {
            if (frames == null || frames.Length == 0) return;
            var frame = frames[Mathf.Clamp(f, 0, frames.Length - 1)];
            var world = new Matrix4x4[joints.Count];

            for (int i = 0; i < joints.Count; i++)
            {
                var j = joints[i];
                Vector3 t = j.offset;
                // pass 1: posisi (root punya Xposition/Yposition/Zposition)
                int ci = j.channelStart;
                if (j.channels != null)
                {
                    foreach (var ch in j.channels)
                    {
                        float v = ci < frame.Length ? frame[ci] : 0f;
                        if (ch == "Xposition") t.x += v;
                        else if (ch == "Yposition") t.y += v;
                        else if (ch == "Zposition") t.z += v;
                        ci++;
                    }
                }
                Matrix4x4 local = Matrix4x4.Translate(t) * RotationMatrix(j, frame);
                world[i] = j.parent >= 0 ? world[j.parent] * local : local;

                Vector3 p = world[i].GetColumn(3);
                outPos[i] = p;
            }
        }

        /// <summary>Rotasi lokal per joint (quaternion) untuk frame f — dipakai retarget Tahap 2.</summary>
        public void EvaluateLocalRotations(int f, Quaternion[] outRot, out Vector3 rootPos)
        {
            rootPos = Vector3.zero;
            if (frames == null || frames.Length == 0) return;
            var frame = frames[Mathf.Clamp(f, 0, frames.Length - 1)];
            for (int i = 0; i < joints.Count; i++)
            {
                var j = joints[i];
                outRot[i] = RotationQuat(j, frame);
                if (j.parent < 0)
                {
                    Vector3 t = j.offset; int ci = j.channelStart;
                    if (j.channels != null)
                        foreach (var ch in j.channels)
                        {
                            float v = ci < frame.Length ? frame[ci] : 0f;
                            if (ch == "Xposition") t.x += v;
                            else if (ch == "Yposition") t.y += v;
                            else if (ch == "Zposition") t.z += v;
                            ci++;
                        }
                    rootPos = t;
                }
            }
        }

        private static Matrix4x4 RotationMatrix(BvhJoint j, float[] frame)
            => Matrix4x4.Rotate(RotationQuat(j, frame));

        // Rotasi digabung SESUAI urutan channel (post-multiply, sama seperti versi Kotlin).
        private static Quaternion RotationQuat(BvhJoint j, float[] frame)
        {
            Quaternion q = Quaternion.identity;
            if (j.channels == null) return q;
            int ci = j.channelStart;
            foreach (var ch in j.channels)
            {
                float v = ci < frame.Length ? frame[ci] : 0f;
                if (ch == "Xrotation") q = q * Quaternion.AngleAxis(v, Vector3.right);
                else if (ch == "Yrotation") q = q * Quaternion.AngleAxis(v, Vector3.up);
                else if (ch == "Zrotation") q = q * Quaternion.AngleAxis(v, Vector3.forward);
                ci++;
            }
            return q;
        }

        // ---------------- Parsing ----------------

        public static BvhClip Parse(string path) => ParseText(File.ReadAllText(path));

        public static BvhClip ParseText(string text)
        {
            var clip = new BvhClip();
            var tokens = Tokenize(text, out int motionTokenIndex, out string[] lines, out int motionLineIndex);

            // --- HIERARCHY ---
            int cursor = 0;
            int channelCursor = 0;
            // cari ROOT
            while (cursor < tokens.Count && tokens[cursor] != "ROOT") cursor++;
            if (cursor >= tokens.Count) throw new System.Exception("BVH: ROOT tidak ketemu");
            ParseJoint(tokens, ref cursor, -1, clip.joints, ref channelCursor);
            clip.ChannelCount = channelCursor;

            // --- MOTION ---
            ParseMotion(lines, motionLineIndex, clip);
            return clip;
        }

        // Token untuk HIERARCHY (di-split whitespace) + simpan baris untuk MOTION.
        private static List<string> Tokenize(string text, out int motionTokenIndex, out string[] lines, out int motionLineIndex)
        {
            lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            motionLineIndex = -1;
            var hierTokens = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == "MOTION") { motionLineIndex = i; break; }
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                hierTokens.AddRange(parts);
            }
            motionTokenIndex = hierTokens.Count;
            return hierTokens;
        }

        // Recursive-descent: dipanggil saat tokens[cursor] == "ROOT" atau "JOINT".
        private static void ParseJoint(List<string> t, ref int cursor, int parentIndex,
                                       List<BvhJoint> joints, ref int channelCursor)
        {
            // t[cursor] = ROOT/JOINT, t[cursor+1] = name
            cursor++; // skip ROOT/JOINT
            string name = t[cursor++];
            var joint = new BvhJoint { name = name, parent = parentIndex, channels = new string[0] };
            int myIndex = joints.Count;
            joints.Add(joint);

            if (t[cursor] == "{") cursor++;
            while (cursor < t.Count)
            {
                string tok = t[cursor];
                if (tok == "OFFSET")
                {
                    joint.offset = new Vector3(
                        ParseF(t[cursor + 1]), ParseF(t[cursor + 2]), ParseF(t[cursor + 3]));
                    cursor += 4;
                }
                else if (tok == "CHANNELS")
                {
                    int n = int.Parse(t[cursor + 1], CultureInfo.InvariantCulture);
                    joint.channels = new string[n];
                    for (int k = 0; k < n; k++) joint.channels[k] = t[cursor + 2 + k];
                    joint.channelStart = channelCursor;
                    channelCursor += n;
                    cursor += 2 + n;
                }
                else if (tok == "JOINT")
                {
                    ParseJoint(t, ref cursor, myIndex, joints, ref channelCursor);
                }
                else if (tok == "End")
                {
                    // End Site { OFFSET x y z }
                    var end = new BvhJoint { name = name + "_End", parent = myIndex, isEndSite = true, channels = new string[0] };
                    cursor += 2; // End Site
                    if (t[cursor] == "{") cursor++;
                    while (cursor < t.Count && t[cursor] != "}")
                    {
                        if (t[cursor] == "OFFSET")
                        {
                            end.offset = new Vector3(ParseF(t[cursor + 1]), ParseF(t[cursor + 2]), ParseF(t[cursor + 3]));
                            cursor += 4;
                        }
                        else cursor++;
                    }
                    if (cursor < t.Count && t[cursor] == "}") cursor++; // }
                    joints.Add(end);
                }
                else if (tok == "}")
                {
                    cursor++;
                    return;
                }
                else cursor++;
            }
        }

        private static void ParseMotion(string[] lines, int motionLineIndex, BvhClip clip)
        {
            var frameList = new List<float[]>();
            for (int i = motionLineIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Frames:"))
                    continue; // panjang aktual dari data
                if (line.StartsWith("Frame Time:"))
                {
                    clip.frameTime = ParseF(line.Substring(line.IndexOf(':') + 1).Trim());
                    continue;
                }
                var parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                var row = new float[parts.Length];
                for (int k = 0; k < parts.Length; k++) row[k] = ParseF(parts[k]);
                frameList.Add(row);
            }
            clip.frames = frameList.ToArray();
        }

        private static float ParseF(string s)
            => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
