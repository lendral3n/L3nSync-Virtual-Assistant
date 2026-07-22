# Blueprint Alur Aplikasi (Developer Perspective)

Dokumen ini menjelaskan rancangan arsitektur dan alur kerja (flow) aplikasi asisten AI floating character secara keseluruhan dari sisi teknis.

## 1. Arsitektur Keseluruhan
Aplikasi ini menggunakan perpaduan **Android Native (Kotlin/Java)** dan **Unity (sebagai Library/Sub-view)**.
1. **Android Application**: Menangani UI aplikasi (tombol Start, halaman konfigurasi), sistem Permission (Overlay/Draw over other apps), Microphone access, integrasi dengan API OpenClaw & ElevenLabs.
2. **Unity Engine (Unity as a Library - UaaL)**: Murni bertugas merender karakter VRM (Kohaku) dengan transparent background dan menjalankan sistem animasi serta LipSync.
3. **OpenClaw (AI Backend)**: Memproses input teks dari percakapan pengguna dan memberikan balasan teks beserta parameter emosi jika ada.
4. **ElevenLabs (TTS)**: Mengubah balasan teks dari AI menjadi file audio (suara).

## 2. Alur Penggunaan (User Journey) & Cara Kerja Sistem
### A. Saat Aplikasi Dibuka (App Launch)
- **User View**: Buka aplikasi, muncul tampilan UI Android (Dashboard/Home). Ada tombol "Start" dan "Settings".
- **Sistem**:
  - Memeriksa *Permissions*: Microphone, Internet, dan *Draw over other apps* (System Alert Window).
  - Me-load konfigurasi (API keys, model pilihan, dsb).

### B. Saat Tombol "Start" Ditekan
- **User View**: Tombol ditekan, UI aplikasi Android bisa diminimize atau kembali ke Home, namun karakter 3D muncul melayang (floating) di atas layar.
- **Sistem**:
  - Android memanggil *Service* (Foreground Service) untuk menampilkan *Floating Window* (WindowManager).
  - Di dalam *Floating Window* tersebut, Android me-mount atau menempatkan **UnityPlayerActivity** (Canvas Unity yang sudah di-set transparent).
  - Unity mulai berjalan (Initialization). Karakter masuk ke state `IdleAnimation`.

### C. Alur Interaksi (Percakapan) & Trigger Animasi
Bagaimana cara animasi dikendalikan secara otomatis? Ini adalah alur detail komunikasi dari Android ke Unity.

1. **User Berbicara**:
   - `Android`: Menangkap suara (*Speech-to-Text* / STT).
   - `Android -> Unity`: Mengirim state "Karakter sedang mendengarkan" via fungsi komunikasi (misal: `UnityPlayer.UnitySendMessage("GameObjectName", "SetState", "Listening")`).
   - `Unity`: Script C# menerima pesan `"Listening"` -> Memicu Animator untuk pindah ke Animasi *Listening*.

2. **Memproses Jawaban (Thinking)**:
   - `Android`: Teks pertanyaan dikirim ke **OpenClaw (AI)**. Sambil menunggu balasan...
   - `Android -> Unity`: Mengirim stat `"Thinking"`.
   - `Unity`: C# script memicu *Thinking Animation*.

3. **Mendapatkan Jawaban (Balasan Teks & Emosi)**:
   - `OpenClaw` mengembalikan teks jawaban (misal: "Halo, senang bertemu denganmu!") dan mendeteksi emosi (misal: `Joy`).
   - `Android`: Memanggil API **ElevenLabs** dengan urutan teks tersebut untuk mendapatkan file Audio (`.mp3` atau `.wav`).

4. **Berbicara & LipSync + Gesture**:
   - `Android`: Menyimpan atau memutar file audio.
   - `Android -> Unity`: Mengirim teks balasan, path audio/byte audio, dan state emosi `Joy`.
   - `Unity`: 
     - Menerima event.
     - Script C# memutar audio melalui `AudioSource`.
     - *LipSyncSystem* bekerja: Menganalisis gelombang audio (Audio Spectrum / OVRLipSync / uLipSync) dan secara otomatis menggerakkan blendshape A I U E O sesuai suara *real-time*.
     - Script C# memicu animasi gesture (Animasi *Greet_Joy* atau *Talking*) berbarengan dengan ekor (*Tail emotion system*), telinga (*Ear system*), atau *Body Motion*.

5. **Kembali ke Idle**:
   - `Unity`: Jika audio selesai diputar, C# script mengembalikan animator kembali ke state `Idle`.
   - Menunggu interaksi selanjutnya.

## 3. Komunikasi Dua Arah (Bridge)
Bagaimana Unity tahu apa yang harus dilakukan?
Karena Unity dijalankan *di dalam* Android, kita menggunakan jembatan (Bridge).
- **Dari Android ke Unity**: Menggunakan method bawaan Unity yaitu `UnitySendMessage("NamaGameObject", "NamaFungsiCS", "ParameterString")`.
  - Android mengatakan: *Mainkan Animasi "Joy"*.
  - Script C# di Unity menangkap itu, dan mengeksekusi `animator.SetTrigger("Joy");`.
- **Dari Unity ke Android**: Menggunakan callback interface Java (JNI - Java Native Interface) atau C# delegates.
  - Unity mengatakan: *Animasi selesai dimainkan, siap menerima perintah*.

## 4. Konfigurasi App (Settings)
- Android UI memegang sistem lokal database / Shared Preferences untuk:
  - OpenClaw parameters (temperature, prompt memory, dsb).
  - Volume Suara, Opsi Karakter.
  - Nilai ini bisa dilempar ke Unity saat start, namun logika utama AI dijalankan di Android backend (Kotlin), Unity murni **sebagai Visualisasi dan Engine Animasi**.

## Kesimpulan Flow
1. User -> (Voice/Touch) -> Android
2. Android -> (API Request) -> OpenClaw & ElevenLabs
3. Android mendapatkan Audio & Text/Emotion -> `UnitySendMessage` -> Unity
4. Unity menjalankan C# System (GestureSystem, LipSyncSystem, IdleSystem) -> Visual Karakter (VRM) memberikan feedback visual ke User.
