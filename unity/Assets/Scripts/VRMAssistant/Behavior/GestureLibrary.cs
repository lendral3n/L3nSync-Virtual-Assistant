using System.Collections.Generic;
using UnityEngine;

namespace VRMAssistant.Behavior
{
    /// <summary>
    /// Katalog TUNGGAL semua animasi Lia (33): mocap Bandai/Mixamo + VRMA + persistensi
    /// pilihan user. Dipakai BehaviorScheduler (pool otomatis), AnimationSettingsPanel
    /// (checklist + preview), dan LiaPersona (daftar gesture yang boleh dipilih AI).
    ///
    /// Persistensi: PlayerPrefs menyimpan CSV nama yang DIAKTIFKAN. Bila belum pernah diset
    /// (user baru) → default = 8 pose kurasi. Jadi menambah animasi ke katalog tidak
    /// otomatis membanjiri pool — user memilih sendiri lewat panel ⚙ Animasi.
    /// </summary>
    public static class GestureLibrary
    {
        public struct Gesture
        {
            public string name;   // = nama clip mocap / nama file VRMA (tanpa .vrma)
            public bool vrma;     // true = VRMA (StreamingAssets), false = mocap (Resources)
            public string label;  // teks tampil di UI (Indonesia)
            public string source; // "Bandai" | "Mixamo" | "VRMA" — untuk grup di panel
            public bool pool;     // true = boleh masuk pool gesture acak/AI (false = lokomotor/idle)
        }

        // Semua animasi yang ADA di project (Resources/MocapClips + StreamingAssets/VRMA).
        public static readonly Gesture[] All =
        {
            // --- Mocap Bandai Namco (Assets/Animations/Mocap → Resources/MocapClips) ---
            new Gesture { name = "Call",       vrma = false, label = "Memanggil",         source = "Bandai", pool = true  },
            new Gesture { name = "Respond",    vrma = false, label = "Merespon",          source = "Bandai", pool = true  },
            new Gesture { name = "Bow",        vrma = false, label = "Membungkuk",        source = "Bandai", pool = true  },
            new Gesture { name = "Bye",        vrma = false, label = "Dadah",             source = "Bandai", pool = true  },
            new Gesture { name = "ByeBye",     vrma = false, label = "Dadah (2 tangan)",  source = "Bandai", pool = true  },
            new Gesture { name = "WaveHand",   vrma = false, label = "Melambai",          source = "Bandai", pool = true  },
            new Gesture { name = "WaveBoth",   vrma = false, label = "Melambai 2 tangan", source = "Bandai", pool = true  },
            new Gesture { name = "RaiseHand",  vrma = false, label = "Angkat tangan",     source = "Bandai", pool = true  },
            new Gesture { name = "DanceShort", vrma = false, label = "Menari",            source = "Bandai", pool = true  },
            new Gesture { name = "Walk",       vrma = false, label = "Jalan (di tempat)", source = "Bandai", pool = false },
            new Gesture { name = "Run",        vrma = false, label = "Lari (di tempat)",  source = "Bandai", pool = false },

            // --- Mixamo (Assets/Animations/Mixamo → Resources/MocapClips) ---
            new Gesture { name = "Laughing",   vrma = false, label = "Tertawa",           source = "Mixamo", pool = true  },
            new Gesture { name = "HappyIdle",  vrma = false, label = "Idle ceria",        source = "Mixamo", pool = false },
            new Gesture { name = "IdleVar",    vrma = false, label = "Idle (dasar)",      source = "Mixamo", pool = false },
            new Gesture { name = "FemWalk",    vrma = false, label = "Jalan (feminin)",   source = "Mixamo", pool = false },

            // --- VRMA (Assets/StreamingAssets/VRMA/*.vrma) ---
            new Gesture { name = "LookAround", vrma = true, label = "Lihat sekeliling", source = "VRMA", pool = true },
            new Gesture { name = "Goodbye",    vrma = true, label = "Selamat tinggal",  source = "VRMA", pool = true },
            new Gesture { name = "Angry",      vrma = true, label = "Marah",            source = "VRMA", pool = true },
            new Gesture { name = "ModelPose",  vrma = true, label = "Pose model",       source = "VRMA", pool = true },
            new Gesture { name = "Blush",      vrma = true, label = "Malu-malu",        source = "VRMA", pool = true },
            new Gesture { name = "Clapping",   vrma = true, label = "Tepuk tangan",     source = "VRMA", pool = true },
            new Gesture { name = "Greeting",   vrma = true, label = "Menyapa",          source = "VRMA", pool = true },
            new Gesture { name = "Jump",       vrma = true, label = "Melompat",         source = "VRMA", pool = true },
            new Gesture { name = "Peace",      vrma = true, label = "Peace ✌",          source = "VRMA", pool = true },
            new Gesture { name = "Relax",      vrma = true, label = "Santai",           source = "VRMA", pool = true },
            new Gesture { name = "Sad",        vrma = true, label = "Sedih",            source = "VRMA", pool = true },
            new Gesture { name = "Shoot",      vrma = true, label = "Tembak (gaya)",    source = "VRMA", pool = true },
            new Gesture { name = "ShowBody",   vrma = true, label = "Pamer pose",       source = "VRMA", pool = true },
            new Gesture { name = "Sleepy",     vrma = true, label = "Mengantuk",        source = "VRMA", pool = true },
            new Gesture { name = "Spin",       vrma = true, label = "Berputar",         source = "VRMA", pool = true },
            new Gesture { name = "Squat",      vrma = true, label = "Jongkok",          source = "VRMA", pool = true },
            new Gesture { name = "Surprised",  vrma = true, label = "Kaget",            source = "VRMA", pool = true },
            new Gesture { name = "Thinking",   vrma = true, label = "Berpikir",         source = "VRMA", pool = true },
        };

        // Default aktif (8 pose kurasi Lendra) bila user belum pernah menyetel pilihan.
        private static readonly string[] DefaultEnabled =
            { "Call", "Respond", "Bow", "Bye", "LookAround", "Goodbye", "Angry", "ModelPose" };

        private const string PrefsKeyEnabled = "liava_enabled_gestures";
        private static HashSet<string> _enabled;

        private static void EnsureLoaded()
        {
            if (_enabled != null) return;
            _enabled = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var csv = PlayerPrefs.GetString(PrefsKeyEnabled, null);
            if (string.IsNullOrEmpty(csv))
            {
                // User baru → pakai default kurasi.
                foreach (var n in DefaultEnabled) _enabled.Add(n);
            }
            else
            {
                foreach (var s in csv.Split(','))
                    if (!string.IsNullOrEmpty(s)) _enabled.Add(s.Trim());
            }
        }

        private static void Persist()
        {
            PlayerPrefs.SetString(PrefsKeyEnabled, string.Join(",", _enabled));
            PlayerPrefs.Save();
        }

        /// <summary>Gesture aktif dipakai scheduler/AI? (default: 8 kurasi)</summary>
        public static bool IsEnabled(string name)
        {
            EnsureLoaded();
            return _enabled.Contains(name ?? "");
        }

        public static void SetEnabled(string name, bool enabled)
        {
            EnsureLoaded();
            if (enabled) _enabled.Add(name);
            else _enabled.Remove(name);
            Persist();
        }

        public static int EnabledCount()
        {
            EnsureLoaded();
            int c = 0;
            foreach (var g in All) if (_enabled.Contains(g.name)) c++;
            return c;
        }

        /// <summary>Nama gesture yang aktif (untuk daftar pilihan AI di LiaPersona).</summary>
        public static List<string> EnabledGestureNames()
        {
            EnsureLoaded();
            var list = new List<string>();
            foreach (var g in All) if (_enabled.Contains(g.name)) list.Add(g.name);
            return list;
        }
    }
}
