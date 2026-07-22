# Roadmap Developer: Asisten AI Floating Character

Dokumen ini adalah peta jalan (roadmap) langkah teknis detail untuk menyelesaikan keseluruhan aplikasi, disusun berurutan agar kamu tahu apa yang harus dikerjakan selanjutnya.

---

## Fase 1: Persiapan Unity & Animasi Karakter (Fase Saat Ini)
**Fokus:** Memastikan karakter VRM bisa digerakkan via sistem animasi Unity dan script secara mandiri.
1. **[SELESAI/BERJALAN]** Setup Unity 3D, Import UniVRM v0, dan Karakter Kohaku.
2. **[BERJALAN]** Belajar membuat *Animation Clips* dasar (Idle, Talking, Thinking, Listening, dsb).
3. **[NEXT]** Mengonfigurasi `Animator Controller`.
   - Menggabungkan *Animation Clips* ke dalam *Animator state machine*.
   - Membuat parameter (misal: Trigger `isThinking`, Trigger `isTalking`, dll) agar animasi bisa berpindah dengan mulus memakai *Transitions* dan *Blend Trees*.
4. **[NEXT]** Mengatur logika C# Modular (Folder `OC-X1/Assets/Scripts/Character/Systems/`).
   - Menyempurnakan `IdleSystem.cs`, `GestureSystem.cs`.
   - Menulis script yang memanggil animator: `animator.SetTrigger("isThinking")`.

## Fase 2: Sistem LipSync & Audio di Unity
**Fokus:** Membuat mulut karakter bergerak sesuai audio yang diberikan.
1. **[NEXT]** Integrasi sistem LipSync.
   - Pilihan: Menggunakan `uLipSync` plugin (gratis & cukup bagus untuk Unity) atau `OVRLipSync`.
   - Setup di karakter Kohaku agar *Blendshape* (A I U E O) bereaksi terhadap komponen `AudioSource`.
2. **[NEXT]** Uji coba audio.
   - Masukkan audio sample (MP3) ke `AudioSource` di Unity.
   - *Play*, pastikan mulut berucap sesuai irama audio secara otomatis (tidak dianimasikan manual pakai keyframe, melainkan pakai script penganalisa frekuensi audio).

## Fase 3: Export Unity sebagai Library (UaaL)
**Fokus:** Mengubah proyek Unity yang tadinya standalone menjadi modul yang bisa disisipkan ke aplikasi Android Native.
1. **[NEXT]** Atur Project Settings Unity untuk Build Android.
   - Set *Export Project* dicentang (Export as Android Studio Project).
   - Set Background Camera menjadi *Solid Color* transparan (Alpha = 0).
2. **[NEXT]** Build/Export dari Unity.
   - Menghasilkan folder file struktur Android (misal `unityLibrary`).
3. **[NEXT]** Integrasi ke Project Aplikasi Android Studio utama.
   - Memasukkan modul `unityLibrary` ke `settings.gradle` dan `build.gradle` project utama Android.

## Fase 4: Sistem Floating Window (Overlay) Android
**Fokus:** Membuat aplikasi mengambang di luar aplikasi utama.
1. **[NEXT]** Setup project Android (Kotlin).
2. **[NEXT]** Buat Foreground Service dan *WindowManager*.
   - Minta UI permission `ACTION_MANAGE_OVERLAY_PERMISSION`.
   - Buat Window transparan yang ukurannya bisa di-drag atau diletakkan di pojok layar.
3. **[NEXT]** Memasukkan `UnityPlayerActivity` ke dalam tampilan Layout (Frame/View) si Floating Window.
   - Test di HP Android: Buka layar menu utama HP, pastikan boneka 3D Kohaku nampil tanpa kotak hitam di belakangnya.

## Fase 5: Integrasi AI & ElevenLabs di Android
**Fokus:** Menyambungkan "otak" bahasa dan suara di dalam aplikasi Android.
1. **[NEXT]** Buat API Client Kotlin untuk **OpenClaw**.
   - Kirim *string* (teks input user), tangkap *string* balasan.
2. **[NEXT]** Buat Speech-to-Text (STT) di Android.
   - Menggunakan Google Speech Recognizer agar suara user jadi teks.
3. **[NEXT]** Buat API Client Kotlin untuk **ElevenLabs**.
   - Kirim *string* balasan AI ke ElevenLabs, *download* atau *stream* ke array byte audio.

## Fase 6: Jembatan Navigasi (Brain to Body)
**Fokus:** Bagaimana Android (Otak) menyuruh Unity (Tubuh) bergerak.
1. **[NEXT]** Membuat fungsi penghubung di Android.
   - Gunakan `UnityPlayer.UnitySendMessage("AvatarControl", "ReceiveAudioAndPlay", "path_to_audio_or_base64");`.
   - `UnityPlayer.UnitySendMessage("AvatarControl", "ChangeEmotion", "Happy");`.
2. **[NEXT]** Menulis listener script di Unity.
   - C# Unity menangkap parameter *Happy*, memanggil *GestureSystem* untuk memainkan animasi bahagia.
   - C# Unity menerima audio dari Android, memasukkan ke `AudioSource`, lalu LipSync jalan otomatis, dan karakter seolah bicara beneran.

## Fase 7: Poles UI App & Optimasi Performa
**Fokus:** Fitur pelengkap dan optimasi aplikasi.
1. **[NEXT]** Desain UI App Utama (untuk setting API Key, volume, pilih karakter).
2. **[NEXT]** Optimasi FPS Unity di Mobile.
   - Batasi framerate, kurangi perhitungan fisika kain (spring bones) jika terlalu berat, sesuaikan resolusi shader.
3. **[NEXT]** Finalisasi dan Build APK ke tahap Production (Play Store).

---

## Ringkasan Transisi Saat Ini (Kamu Berada di Sini)
**Kamu Sedang:** Mempelajari animasi manual Unity (Timeline / Animation Window).
**Tujuan Saat ini:** Agar kamu tahu persis nama-nama animasi (state) dan bagaimana *script C# (GestureSystem, dsb)* akan melakukan panggilan (trigger) untuk mengubah pose karakter. Animasi tidak harus panjang, cukup pose *looping* pendek yang nantinya diputar tergantung perintah Android lewat C#.
**Langkah Berikutnya (Next Step):** Selesaikan logic C# di Unity untuk mengatur "kapan animasi Play, kapan Stop, kapan Idle". Jika character sudah bisa gonta-ganti pose via script Unity secara mulus, baru beralih ke bagian *LipSync* dan *Export to Android*.
