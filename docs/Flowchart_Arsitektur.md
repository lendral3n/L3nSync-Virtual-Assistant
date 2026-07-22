# Flowchart Arsitektur & Alur Sistem (ASCII Version)

Berikut adalah visualisasi alur kerja dari aplikasi asisten AI (Kohaku) menggunakan grafis ASCII agar dapat langsung dibaca di text editor apa pun.

```text
================================================================================
                           ALUR KERJA (FLOWCHART) SISTEM
================================================================================

[ 1. INISIALISASI ]
                                 +-------------------------+
                                 |  USER BUKA APLIKASI     |
                                 |  (Cek Izin, Pilih Char) |
                                 +-------------------------+
                                              |
                                              v
                                 +-------------------------+
                                 | SERVICE MENGAMBANG      |
                                 | (Foreground / Overlay)  |
                                 +-------------------------+
                                              |
                                              v
                                 +-------------------------+
                                 | LOAD UNITY & AVATAR 3D  |
                                 | (IdleSystem Berjalan)   |
                                 +-------------------------+

================================================================================

[ 2. INTERAKSI & PROSES AI ]

  [ USER ]                    [ ANDROID APP ]                      [ UNITY ]
     |                               |                                 |
     |--- (Bicara ke Mic) ---------->|                                 |
     |                               |                                 |
     |                               |---- (Set State: Listening) ---->| (Animasi
     |                               |                                 | Mendengarkan)
     |                     +--------------------+                      |
     |                     | Speech-To-Text STT |                      |
     |                     | Mengubah jadi teks |                      |
     |                     +--------------------+                      |
     |                               |                                 |
     |                     +--------------------+                      |
     |                     | Req: OpenClaw API  |                      |
     |                     +--------------------+                      |
     |                               |                                 |
     |                               |---- (Set State: Thinking) ----->| (Animasi
     |                               |                                 | Berpikir)
     |                     +--------------------+                      |
     |                     |  Tunggu Balasan    |                      |
     |                     |  (Teks + Emosi)    |                      |
     |                     +--------------------+                      |
     |                               |                                 |
     |                     +--------------------+                      |
     |                     | Req: ElevenLabs    |                      |
     |                     | Text to Speech     |                      |
     |                     +--------------------+                      |
     |                               |                                 |

================================================================================

[ 3. EKSEKUSI ANIMASI / RESPONSE ]

  [ CLOUD ]                   [ ANDROID APP ]                      [ UNITY ]
     |                               |                                 |
     |--- (Balikan File Audio) ----->|                                 |
     |                               |                                 |
     |                               |== (Kirim Audio & Emosi ID) ====>|
     |                               |                                 |
     |                               |                  +--------------------------+
     |                               |                  | Unity C# Bridge / Sistem |
     |                               |                  +--------------------------+
     |                               |                            |          |
     |                               |            (Mulai Audio)   |          | (Ubah Pose/Muka)
     |                               |                            v          v
     |                               |                +---------------+  +--------------+
     |                               |                | LipSyncSystem |  | Gesture /    |
     |                               |                | & AudioSource |  | EmotionSys   |
     |                               |                +---------------+  +--------------+
     |                               |                            |          |
     |                               |                            v          v
     |                               |                +------------------------------+
     |                               |                |      Avatar 3D (Kohaku)      |
     |                               |                |  - Mulut bergerak (A, I, U)  |
     |                               |                |  - Pose Bicara               |
     |                               |                |  - Emosi (Senang/Sedih)      |
     |                               |                +------------------------------+
     |                               |                               |
     |                               |                (Audio Selesai)|
     |                               |                               v
     |                               |                +------------------------------+
     |                               |                | Kembali ke [ IdleSystem ]    |
     |                               |                +------------------------------+
```
