================================================================
DOKUMENTASI SISTEM ANIMASI KARAKTER VRM
Kohaku — AI Character Animation System
Unity 6000.3.10f1 + UniVRM v0
================================================================

DAFTAR FILE SCRIPT
──────────────────
Core/
  CharacterState.cs               ← Enum: state, emosi, personality
  VRMAnimationController.cs       ← Master controller semua sistem

Systems/
  IdleSystem.cs                   ← Breathing + body sway (base layer)
  AttentionSystem.cs              ← Kepala menatap target/kamera
  LipSyncSystem.cs                ← A/I/U/E/O dari audio ElevenLabs
  FacialMicroSystem.cs            ← Auto-blink + micro eye wander
  EmotionSystem.cs                ← Joy/Angry/Sorrow/Fun/Neutral
  TailSystem.cs                   ← Ekor bergerak sesuai emosi
  EarSystem.cs                    ← Telinga fox sesuai emosi
  BodyMotionSystem.cs             ← Condong tubuh sesuai state
  GestureSystem.cs                ← Wave, point (gerakan tangan)
  ThinkingSystem.cs               ← Kepala miring saat AI proses
  ListeningSystem.cs              ← Anggukan attentive saat user bicara
  ReactionSystem.cs               ← Nod, shake, recoil
  GreetingSystem.cs               ← Sapa + bow + wave
  FarewellAndSurpriseSystems.cs   ← Perpisahan + kaget
  FocusPersonalityMicroSystems.cs ← User focus + personality + micro idle


================================================================
CARA SETUP DI UNITY (STEP BY STEP)
================================================================

STEP 1 – Buat folder script
───────────────────────────
Di Project window, klik kanan → Create → Folder
Buat struktur:
  Assets/Scripts/Character/Core/
  Assets/Scripts/Character/Systems/

Drag file .cs ke folder yang sesuai.


STEP 2 – Pisah file multi-class
───────────────────────────────
File FarewellAndSurpriseSystems.cs berisi 2 class:
  • FarewellSystem
  • SurpriseSystem

File FocusPersonalityMicroSystems.cs berisi 3 class:
  • UserFocusSystem
  • PersonalitySystem
  • MicroIdleSystem

Unity bisa compile class dari satu file.
TIDAK perlu dipisah, tapi pastikan nama file ≠ nama class.


STEP 3 – Attach semua komponen ke karakter
───────────────────────────────────────────
1. Di Hierarchy, klik GameObject karakter Kohaku.
2. Di Inspector → Add Component, tambahkan:

   [Core]
   ✓ VRMAnimationController      ← wajib, tambah pertama

   [Systems]
   ✓ IdleSystem
   ✓ AttentionSystem
   ✓ LipSyncSystem
   ✓ FacialMicroSystem
   ✓ EmotionSystem
   ✓ TailSystem
   ✓ EarSystem
   ✓ BodyMotionSystem
   ✓ GestureSystem
   ✓ ThinkingSystem
   ✓ ListeningSystem
   ✓ ReactionSystem
   ✓ GreetingSystem
   ✓ FarewellSystem
   ✓ SurpriseSystem
   ✓ UserFocusSystem
   ✓ PersonalitySystem
   ✓ MicroIdleSystem

SEMUA komponen di satu GameObject yang sama.


STEP 4 – Setup TailSystem (penting)
─────────────────────────────────────
TailSystem bisa auto-find bone lewat nama.
Tapi lebih aman assign manual:

1. Klik TailSystem di Inspector
2. Field "Tail Bones" → ubah Size = 6
3. Drag dari Hierarchy:
   Element 0 → Tail_01
   Element 1 → Tail_02
   Element 2 → Tail_03
   Element 3 → Tail_04
   Element 4 → Tail_05
   Element 5 → Tail_06

Letak bone di Hierarchy:
  Hips atau Spine → ... → Tail_01 → Tail_02 → ...


STEP 5 – Setup EarSystem
─────────────────────────
1. Klik EarSystem di Inspector
2. Field "Left Ear"  → drag Left_Foxear_01 dari Hierarchy
3. Field "Right Ear" → drag Right_Foxear_01 dari Hierarchy

Jika tidak di-assign, sistem akan auto-find via nama.


STEP 6 – Setup AttentionSystem
────────────────────────────────
Default: auto target ke Main Camera.
Biarkan "Auto Target Main Camera" = true.

Jika ingin target custom (misalnya posisi layar sentuh user):
  var attention = GetComponent<AttentionSystem>();
  attention.SetLookTarget(worldPosition);


STEP 7 – Setup LipSyncSystem untuk ElevenLabs
───────────────────────────────────────────────
1. Pastikan karakter punya AudioSource component.
2. LipSyncSystem akan auto-find AudioSource.

Cara pakai dari kode:
  var lipSync = GetComponent<LipSyncSystem>();
  // Setelah dapat AudioClip dari ElevenLabs:
  lipSync.PlayWithLipSync(audioClip);

PlayWithLipSync otomatis:
  → Play audio
  → Set state ke Speaking
  → Jalankan lip sync
  → Setelah selesai, kembali ke Idle


STEP 8 – Play di Unity untuk test
───────────────────────────────────
Tekan Play. Karakter seharusnya:
  ✓ Bernafas (chest naik-turun)
  ✓ Tubuh sway pelan
  ✓ Menatap Main Camera
  ✓ Blink otomatis
  ✓ Mata sedikit melirik acak
  ✓ Emosi neutral di wajah
  ✓ Ekor bergoyang pelan
  ✓ Telinga di posisi neutral


================================================================
CARA TRIGGER ANIMASI DARI CODE
================================================================

──────────────────────────────
Dari script AI backend kamu:
──────────────────────────────

var ctrl = VRMAnimationController.Instance;

// User mulai bicara → karakter listen
ctrl.SetState(CharacterState.Listening);

// Kirim ke AI → karakter thinking
ctrl.SetState(CharacterState.Thinking);

// AI jawab → speak dengan lip sync
ctrl.SetState(CharacterState.Speaking);
lipSync.PlayWithLipSync(clip);

// Sapa user saat pertama buka app
ctrl.SetState(CharacterState.Greeting);

// Pamit
ctrl.SetState(CharacterState.Farewell);

// Trigger kaget
ctrl.SetState(CharacterState.Surprised);

──────────────────────────────
Ganti emosi:
──────────────────────────────
ctrl.SetEmotion(EmotionState.Joy);
ctrl.SetEmotion(EmotionState.Sorrow);
ctrl.SetEmotion(EmotionState.Angry);
ctrl.SetEmotion(EmotionState.Fun);
ctrl.SetEmotion(EmotionState.Neutral);

──────────────────────────────
Trigger reaksi manual:
──────────────────────────────
var reaction = GetComponent<ReactionSystem>();
reaction.TriggerNod();       // angguk (setuju)
reaction.TriggerShake();     // geleng (tidak)
reaction.TriggerRecoil();    // recoil (kaget kecil)

──────────────────────────────
User focus:
──────────────────────────────
// Panggil ini setiap kali user kirim pesan
var focus = GetComponent<UserFocusSystem>();
focus.RegisterInteraction();

──────────────────────────────
Set personality di awal app:
──────────────────────────────
var personality = GetComponent<PersonalitySystem>();
personality.SetTrait(PersonalityTrait.Cheerful);
// Pilihan: Cheerful, Calm, Energetic, Shy


================================================================
EXECUTION ORDER (URUTAN BERJALAN PER FRAME)
================================================================

Order   System               Bone yang disentuh
──────  ───────────────────  ────────────────────────────────
-100    IdleSystem           Spine, Chest, UpperChest, Shoulder (SET dari rest)
 -90    AttentionSystem      Neck, Head (Slerp ke target)
 -80    LipSyncSystem        BlendShape A/I/U/E/O
 -70    FacialMicroSystem    BlendShape Blink, LookUp/Down/Left/Right
 -60    EmotionSystem        BlendShape Joy/Angry/Sorrow/Fun/Neutral
 -50    TailSystem           Tail_01 ~ Tail_06 (SET dari rest)
 -50    EarSystem            Left/Right Foxear
 -45    BodyMotionSystem     Spine (MULTIPLY di atas Idle)
 -40    GestureSystem        RightUpperArm, RightLowerArm
 -35    ThinkingSystem       Head (MULTIPLY), BlendShape LookUp
 -30    ListeningSystem      Head (MULTIPLY)
   0    ReactionSystem       Head (override saat Coroutine aktif)
   0    GreetingSystem       Coroutine via GestureSystem + Spine
   0    FarewellSystem       Coroutine via GestureSystem
   0    SurpriseSystem       Coroutine via Reaction + Ear + BlendShape
   0    UserFocusSystem      → AttentionSystem target
   0    PersonalitySystem    → multiplier ke sistem lain
+100    MicroIdleSystem      Head (MULTIPLY, terakhir), Shoulder


================================================================
ARSITEKTUR BLENDSHAPE
================================================================

Semua blendshape melewati VRMAnimationController.

WriteBlendShape()         → mode MAX (nilai tertinggi menang)
                             Digunakan: emosi, blink, eye direction

WriteBlendShapeAdditive() → mode ADD (nilai dijumlah, klem 0-1)
                             Digunakan: lip sync (overlap emosi)

Tidak ada sistem yang langsung call VRMBlendShapeProxy.
Semua melalui controller. Apply() dipanggil SEKALI per frame.


================================================================
TIPS PERFORMA
================================================================

1. Tail & Ear pakai LateUpdate → tidak mahal (bone manipulation)
2. LipSyncSystem sample audio 256 float per Update → ringan
3. MicroIdleSystem hanya update target setiap 1-2 detik
4. Semua Coroutine berhenti saat scene di-pause

Untuk Android:
• Kurangi tailBones ke 4 (hapus Tail_05, Tail_06) jika FPS drop
• Set microIntensity = 0 untuk mode ultra-low-end
• BlendShape Apply() paling mahal → jalankan di LateUpdate saja (sudah)


================================================================
TROUBLESHOOTING
================================================================

ERROR: "VRMAnimationController.Instance is null"
→ Pastikan VRMAnimationController adalah komponen PERTAMA di-awake.
→ Cek semua sistem di GameObject yang SAMA dengan controller.

ERROR: Tulang tidak bergerak
→ Pastikan karakter punya Animator component dengan rig Humanoid.
→ Cek di Animator → Avatar → Inspector apakah Humanoid valid.

ERROR: Ekor tidak bergerak
→ TailSystem butuh assign Tail Bones di Inspector.
→ Atau nama bone harus persis "Tail_01" sampai "Tail_06".

ERROR: Telinga tidak bergerak  
→ EarSystem butuh nama persis "Left_Foxear_01" dan "Right_Foxear_01".
→ Atau drag manual di Inspector.

ERROR: Lip sync tidak jalan
→ Pastikan ada AudioSource di GameObject yang sama.
→ Panggil PlayWithLipSync(clip) bukan audioSource.Play() langsung.

WAJAH kaku / tidak berubah
→ Cek VRMBlendShapeProxy ada di karakter.
→ Di Play mode, cek Inspector VRMBlendShapeProxy → lihat nilai berubah.
================================================================
