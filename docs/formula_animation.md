# FORMULA ANIMASI PROCEDURAL — KOHAKU VRM
## Rumus & Parameter untuk Setiap State Animasi

**Referensi Bone Map:** `bonemap_kohaku.md`  
**Script Target:** `IdleSystem.cs`, `EmotionSystem.cs`, `LipSyncSystem.cs`, dll  
**Update:** 17 Maret 2026

---

## Notasi & Simbol

```
t          = Time.time (detik sejak app start)
dt         = Time.deltaTime (durasi 1 frame)
sin(x)     = Mathf.Sin(x)
cos(x)     = Mathf.Cos(x)
lerp(a,b,s)= Mathf.Lerp(a, b, s)
slerp(a,b,s)= Quaternion.Slerp(a, b, s)
Euler(x,y,z)= Quaternion.Euler(x, y, z)
A          = Amplitude (derajat rotasi maksimal)
f          = Frekuensi (Hz = siklus per detik)
ω          = Angular frequency = 2π × f
φ          = Phase offset (radian, untuk versatz antar bone)
P          = Personality multiplier (0.5 = lambat/halus, 2.0 = cepat/energik)
```

---

## FUNGSI DASAR (Helper Functions)

```
// Gelombang sinus standar
Wave(A, f, φ, t)  = A × sin(2π × f × t + φ)

// Gelombang sinus yang selalu positif (0 sampai A)
WavePos(A, f, t)  = A × (sin(2π × f × t) × 0.5 + 0.5)

// Gelombang segitiga (lebih natural dari sinus murni)
Triangle(A, f, t) = A × (2/π) × arcsin(sin(2π × f × t))

// Noise halus (approximasi dengan 2 sinus)
Breath(A, f, t)   = A × sin(2π × f × t) + (A × 0.15) × sin(2π × f × 2.7 × t)

// Lerp smooth (sama seperti SmoothDamp sederhana)
Smooth(current, target, speed, dt) = lerp(current, target, 1 - exp(-speed × dt))
```

---

---

# ① IDLE ANIMATION

> **Tujuan:** Karakter terlihat hidup saat tidak ada interaksi.  
> Efek: nafas, sway halus, micro movement kepala, kedip mata, ekor berayun.

---

## 1.1 Nafas / Breathing — `Chest`

```
// Nafas utama: dada naik-turun (sumbu X)
Chest.rotX = Breath(A_breath, f_breath, t)

// Parameter default:
A_breath = 2.5°          // amplitudo dada naik/turun
f_breath = 0.25 Hz       // 1 napas setiap 4 detik
// Variasi napas agar tidak robotic:
A_breath_variation = 0.3°
f_breath_variation = 0.07 Hz
Chest.rotX = Wave(A_breath, f_breath, 0, t)
           + Wave(A_breath_variation, f_breath_variation, 1.2, t)

// Spine ikut nafas (lebih kecil)
Spine.rotX = Chest.rotX × 0.35    // 35% dari gerakan dada

// Hips ikut nafas (sangat kecil, efek perut)
Hips.rotX  = Chest.rotX × 0.15
```

## 1.2 Body Sway — `Hips`, `Spine`

```
// Ayunan tubuh kiri-kanan (sumbu Z = miring samping)
swayZ(t) = Wave(A_sway, f_sway, 0, t)

A_sway = 1.2°    // amplitudo miring samping
f_sway = 0.18 Hz // sangat lambat (1 siklus ~5.5 detik)

Hips.rotZ  = swayZ(t)
Spine.rotZ = swayZ(t) × 0.6   // spine ikut tapi lebih sedikit

// Ayunan maju-mundur sangat kecil (sumbu X)
Hips.rotX  += Wave(0.4°, 0.18, π/2, t)  // phase 90° dari Z

// Kombinasi: figure-8 halus
Hips.rotX  = Wave(0.4°, 0.18, 0,    t)
Hips.rotZ  = Wave(1.2°, 0.18, π/2,  t)
```

## 1.3 Kepala Micro Movement — `Head`, `Neck`

```
// Kepala sedikit mengangguk (X)
Head.rotX = Wave(0.8°, 0.22, 0.5, t)

// Kepala sedikit menoleh (Y) — versatz dari nafas
Head.rotY = Wave(1.5°, 0.15, 1.8, t)

// Kepala miring sangat kecil (Z)
Head.rotZ = Wave(0.6°, 0.19, 0.9, t)

// Neck mengikuti Head dengan sedikit delay dan lebih kecil
Neck.rotX = Head.rotX × 0.5
Neck.rotY = Head.rotY × 0.4
Neck.rotZ = Head.rotZ × 0.3
```

## 1.4 Posisi Tangan Natural — `LeftArm`, `RightArm`, `LeftForeArm`, `RightForeArm`

```
// Lengan atas: turun sedikit dari T-pose ke posisi natural samping
// (Berdasarkan bone map: Z(+) = ke bawah samping)
LeftArm.rotZ   = -50° + Wave(1.5°, 0.22, 0,   t)   // tangan kiri turun + micro
RightArm.rotZ  =  50° + Wave(1.5°, 0.22, π,   t)   // tangan kanan turun + micro

// Lengan atas sedikit ke depan (X = depan/belakang)
LeftArm.rotX   = -8° + Wave(0.8°, 0.20, 0.3,  t)
RightArm.rotX  = -8° + Wave(0.8°, 0.20, 0.3,  t)

// Siku sedikit tekuk (bukan lurus sempurna)
LeftForeArm.rotX  = -10° + Wave(0.5°, 0.20, 0.8, t)
RightForeArm.rotX = -10° + Wave(0.5°, 0.20, 0.8, t)
```

## 1.5 Auto Blink — `FacialMicroSystem`

```
// Interval kedip acak
blinkInterval = random(3.0, 6.0)  // detik antar kedip
blinkDuration = 0.12              // detik durasi satu kedip

// BlendShape kedip:
t_blink = (t - lastBlinkTime) / blinkDuration
Blink_L = sin(π × clamp01(t_blink)) × 100   // 0..100..0
Blink_R = Blink_L                            // simetris

// Wink (jarang, ~1% chance per interval):
// jika isWink: hanya Blink_L atau Blink_R yang aktif
```

## 1.6 Eye Wander — `LeftEye`, `RightEye`

```
// Mata bergerak pelan ke arah random setiap beberapa detik
wanderInterval = random(2.0, 5.0)
targetEyeX = random(-8°, 8°)     // sumbu X mata = lirik bawah/atas
targetEyeY = random(-6°, 6°)     // sumbu Y mata = lirik kiri/kanan

// Smooth ke target
currentEyeX = Smooth(currentEyeX, targetEyeX, 2.0, dt)
currentEyeY = Smooth(currentEyeY, targetEyeY, 2.0, dt)

LeftEye.rotX  = currentEyeX
LeftEye.rotY  = currentEyeY
RightEye.rotX = currentEyeX
RightEye.rotY = currentEyeY
```

## 1.7 Ringkasan Parameter Idle

| Bone | Sumbu | Formula | A (°) | f (Hz) | φ |
|------|-------|---------|-------|--------|---|
| `Chest` | X | `Wave(A,f,0,t) + Wave(0.3,0.07,1.2,t)` | 2.5 | 0.25 | 0 |
| `Spine` | X | `Chest.rotX × 0.35` | — | — | — |
| `Hips` | X | `Wave(A,f,0,t)` | 0.4 | 0.18 | 0 |
| `Hips` | Z | `Wave(A,f,0,t)` | 1.2 | 0.18 | π/2 |
| `Head` | X | `Wave(A,f,0.5,t)` | 0.8 | 0.22 | 0.5 |
| `Head` | Y | `Wave(A,f,1.8,t)` | 1.5 | 0.15 | 1.8 |
| `Head` | Z | `Wave(A,f,0.9,t)` | 0.6 | 0.19 | 0.9 |
| `LeftArm` | Z | `-50° + Wave(1.5,0.22,0,t)` | 1.5 | 0.22 | 0 |
| `RightArm` | Z | `+50° + Wave(1.5,0.22,π,t)` | 1.5 | 0.22 | π |

---

---

# ② ACTIVE ANIMATION

> **Tujuan:** Karakter terlihat alert, siap, dan bersemangat.  
> Dipicu saat: user baru berinteraksi, greeting, atau state umum saat app digunakan.  
> Perbedaan dari Idle: amplitudo lebih besar, frekuensi lebih tinggi, tubuh sedikit tegak.

---

## 2.1 Body — Lebih Tegak, Slight Lean Forward

```
// Tubuh sedikit condong ke depan (X negatif = maju)
Spine.rotX = -4° + Wave(1.0°, 0.28, 0, t)    // lebih cepat dari idle
Chest.rotX = Breath(2.0°, 0.28, t)            // nafas lebih cepat

// Sway lebih kecil — karakter fokus, tidak melamun
Hips.rotZ  = Wave(0.6°, 0.25, 0, t)           // lebih sempit dari idle (1.2°)
```

## 2.2 Kepala — Lebih Aktif, Sesekali Nod Kecil

```
// Head movement lebih besar dan lebih cepat
Head.rotX = Wave(1.2°, 0.28, 0, t)
Head.rotY = Wave(2.0°, 0.22, 1.0, t)
Head.rotZ = Wave(0.8°, 0.25, 0.5, t)

// Sesekali nod pendek (setiap 8-15 detik)
if (t - lastNodTime > nodInterval):
    // Animasi nod: X turun lalu kembali dalam 0.4 detik
    nod_t = (t - lastNodTime) / 0.4
    Head.rotX += sin(π × clamp01(nod_t)) × 6°
    lastNodTime = t + random(8, 15)
```

## 2.3 Tangan — Sedikit Diangkat (Siap)

```
// Lengan sedikit lebih naik dari idle (Z lebih kecil = tidak terlalu turun)
LeftArm.rotZ  = -40° + Wave(2.0°, 0.28, 0, t)   // 10° lebih naik dari idle (-50°)
RightArm.rotZ =  40° + Wave(2.0°, 0.28, π, t)

// Siku sedikit tekuk lebih natural
LeftForeArm.rotX  = -15° + Wave(0.8°, 0.25, 0.4, t)
RightForeArm.rotX = -15° + Wave(0.8°, 0.25, 0.4, t)
```

## 2.4 Ringkasan Perbedaan Active vs Idle

| Parameter | Idle | Active | Perubahan |
|-----------|------|--------|-----------|
| `f_breath` | 0.25 Hz | 0.28 Hz | +12% lebih cepat |
| `A_breath` | 2.5° | 2.0° | lebih dangkal (fokus) |
| `Hips.rotZ` A | 1.2° | 0.6° | sway lebih kecil |
| `Head.rotX` A | 0.8° | 1.2° | lebih ekspresif |
| `Head.rotY` A | 1.5° | 2.0° | lebih aktif |
| `LeftArm.rotZ` | −50° | −40° | tangan sedikit naik |
| `Body lean X` | 0° | −4° | condong ke depan |

---

---

# ③ THINKING ANIMATION

> **Tujuan:** Kohaku terlihat sedang memproses/berpikir.  
> Dipicu saat: menunggu response dari OpenClaw API.  
> Efek: kepala miring, mata bergerak, body sedikit diam.

---

## 3.1 Kepala — Miring & Bob

```
// Target pose: kepala miring ke kiri (Z positif = miring ke bahu kiri)
// Lerp smooth ke pose thinking, lalu kembali nanti
thinkTiltZ = 10°   // miring ke kiri
thinkTiltX = -3°   // sedikit mendongak (seperti menatap ke atas)

// Kepala miring smooth
currentHeadZ = Smooth(currentHeadZ, thinkTiltZ, 4.0, dt)
currentHeadX = Smooth(currentHeadX, thinkTiltX, 4.0, dt)

// Tambah bob pelan di atas pose thinking
Head.rotZ = currentHeadZ + Wave(1.0°, 0.4, 0, t)   // bob kiri-kanan kecil
Head.rotX = currentHeadX + Wave(0.8°, 0.5, 0, t)   // bob atas-bawah kecil
Head.rotY = Wave(3.0°, 0.3, 0.7, t)                 // menoleh kecil kiri-kanan
```

## 3.2 Mata — Berputar / Menatap ke Atas

```
// Mata bergerak lebih aktif dari idle (seperti sedang "mencari")
wanderInterval_thinking = random(0.8, 2.0)   // lebih cepat dari idle

// Bias ke atas (seperti mengingat sesuatu)
targetEyeX_thinking = random(-15°, -3°)   // lebih sering lirik ke atas (X-)
targetEyeY_thinking = random(-10°, 10°)

// Sesekali mata berputar (glance pattern)
glancePattern:
  t=0.0s  → mata kiri atas  (-12°, -8°)
  t=0.6s  → mata kanan atas (-12°,  8°)
  t=1.2s  → mata tengah atas(-10°,  0°)
  t=2.0s  → random lagi

// BlendShape: mata sedikit menyempit (jito) saat thinking
eye_jito = WavePos(25, 0.4, t)   // menyempit pelan-pelan, 0..25%
```

## 3.3 Body — Lebih Tenang dari Idle

```
// Nafas lebih dalam dan lambat (konsentrasi)
Chest.rotX = Wave(3.0°, 0.20, 0, t)   // lebih dalam, lebih lambat
Spine.rotX = Chest.rotX × 0.3

// Sway hampir berhenti (fokus)
Hips.rotZ  = Wave(0.3°, 0.15, 0, t)   // sangat kecil
```

## 3.4 Tangan — Slight Pull In

```
// Tangan sedikit didekatkan ke tubuh (siku tekuk lebih)
LeftForeArm.rotX  = -20° + Wave(0.5°, 0.25, 0, t)
RightForeArm.rotX = -20° + Wave(0.5°, 0.25, 0, t)

// Pergelangan sedikit ke dalam
LeftHand.rotY  = -5°   // ke belakang sedikit
RightHand.rotY =  5°
```

## 3.5 Ringkasan Parameter Thinking

| Bone | Parameter | Nilai |
|------|-----------|-------|
| `Head.rotZ` | Target tilt | +10° (miring kiri) |
| `Head.rotX` | Target tilt | −3° (mendongak) |
| `Head` bob Z | Wave A, f | 1.0°, 0.4 Hz |
| `Head` bob X | Wave A, f | 0.8°, 0.5 Hz |
| `LeftEye.rotX` | Bias ke atas | range (−15° ke −3°) |
| `eye_jito` | BlendShape | 0–25% oscillate |
| `Chest.rotX` | Nafas | A=3.0°, f=0.20 Hz |
| `Hips.rotZ` | Sway | A=0.3°, f=0.15 Hz |
| Transition speed | Smooth | 4.0 |

---

---

# ④ LISTENING ANIMATION

> **Tujuan:** Kohaku fokus mendengarkan user bicara.  
> Dipicu saat: Android STT aktif merekam suara user.  
> Efek: tubuh condong ke depan, kepala nod, telinga tegak (via PhysicsBoneController).

---

## 4.1 Body — Lean Forward (Fokus)

```
// Tubuh condong ke depan lebih dari Active
leanTarget = -8°   // sumbu X Spine = bungkuk ke depan

// Smooth transition masuk Listening state
currentLean = Smooth(currentLean, leanTarget, 5.0, dt)
Spine.rotX  = currentLean + Wave(0.8°, 0.30, 0, t)
Chest.rotX  = currentLean × 0.5 + Wave(0.6°, 0.30, 0, t)

// Hips stabil (tidak sway saat listening)
Hips.rotZ  = Wave(0.2°, 0.20, 0, t)   // sangat minimal
```

## 4.2 Kepala — Nod Pattern

```
// Kepala mengangguk periodik saat mendengar
nodPattern(t):
  nodInterval = random(2.5, 5.0)   // nod setiap 2.5–5 detik
  nodDuration = 0.35               // durasi satu nod
  nodAmplitude = 5°                // seberapa dalam nod

  nod_progress = (t - lastNod) / nodDuration
  if nod_progress < 1.0:
    Head.rotX += sin(π × nod_progress) × nodAmplitude

// Kepala sedikit condong ke arah source suara (kanan karena mic kanan?)
Head.rotY = -3°   // sedikit menoleh ke kiri (arah user)

// Micro movement tetap aktif
Head.rotX += Wave(0.5°, 0.25, 0, t)
Head.rotZ  = Wave(0.4°, 0.20, 0, t)
```

## 4.3 Telinga — Alert State (via PhysicsBoneController)

```
// Panggil PhysicsBoneController — jangan gerakkan bone langsung
PhysicsBoneController.SetEarState(EarState.Listening)

// Di dalam PhysicsBoneController.cs untuk EarState.Listening:
//   earOverrideStrength = 0.80
//   rotL = Euler(-20°, -15°, -8°)   → telinga kiri tegak ke depan
//   rotR = Euler(-20°, +15°, +8°)   → telinga kanan tegak ke depan
```

## 4.4 Mata — Fokus ke User

```
// Mata fokus ke area kamera/user, tidak wander
targetEyeX = -5°   // sedikit ke atas (menatap muka user)
targetEyeY = 0°    // lurus

currentEyeX = Smooth(currentEyeX, targetEyeX, 3.0, dt)
currentEyeY = Smooth(currentEyeY, targetEyeY, 3.0, dt)

// Kedip lebih jarang saat listening (fokus)
blinkInterval_listening = random(5.0, 9.0)   // 2x lebih jarang dari idle

// BlendShape mata sedikit lebih terbuka
eye_open = WavePos(10, 0.5, t)   // mata sedikit lebih terbuka
```

## 4.5 Tangan — Natural Down (Tidak Menonjol)

```
// Tangan kembali ke posisi natural bawah
LeftArm.rotZ  = Smooth(LeftArm.rotZ, -50°, 3.0, dt)
RightArm.rotZ = Smooth(RightArm.rotZ, 50°, 3.0, dt)
```

## 4.6 Ringkasan Parameter Listening

| Bone | Parameter | Nilai |
|------|-----------|-------|
| `Spine.rotX` | Lean target | −8° |
| `Hips.rotZ` | Sway | A=0.2°, minimal |
| `Head.rotX` | Nod A | 5° per nod |
| `Head.rotY` | Arah user | −3° |
| Nod interval | Random | 2.5–5.0 detik |
| Ear state | PhysicsBone | `EarState.Listening` |
| `eye_open` | BlendShape | 0–10% |
| Blink interval | — | 5.0–9.0 detik |
| Transition speed | Smooth | 5.0 |

---

---

# ⑤ LIPSYNC ANIMATION

> **Tujuan:** Mulut bergerak sinkron dengan audio ElevenLabs.  
> Dipicu saat: `AudioSource.isPlaying == true`.  
> Teknik: Analisis amplitude audio per frame → drive BlendShape mulut.

---

## 5.1 Audio Analysis

```
// Ambil data spectrum dari AudioSource
spectrumData = float[256]
AudioSource.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris)

// Hitung amplitude rata-rata (low frequency = vokal)
amplitude = average(spectrumData[0..15])   // index 0–15 = 0–2600 Hz (vokal range)

// Normalize ke 0–1
amplitudeNorm = clamp01(amplitude × gainFactor)
gainFactor    = 150.0   // tuning: naikkan jika mulut kurang bergerak
```

## 5.2 Vokal Mapping — BlendShape

```
// Pendekatan 1: Simple amplitude drive (cepat diimplementasi)
// Semua vokal didrive dari satu nilai amplitude
mouth_a = amplitudeNorm × 100   // vokal A dominan saat berbicara

// Pendekatan 2: Vokal bergantian (lebih natural)
// Rotasi antar vokal berdasarkan waktu × amplitude
vocalPhase = t × 8.0   // 8 Hz = kecepatan pergantian vokal
vocalIndex = (int)(vocalPhase) % 4   // 0=A, 1=I, 2=U, 3=O

if amplitudeNorm > 0.1:   // ada suara
  switch(vocalIndex):
    case 0: mouth_a = amplitudeNorm × 100; mouth_i = 0; mouth_u = 0
    case 1: mouth_i = amplitudeNorm × 80;  mouth_a = 20; mouth_u = 0
    case 2: mouth_u = amplitudeNorm × 85;  mouth_a = 15; mouth_i = 0
    case 3: mouth_o = amplitudeNorm × 70;  mouth_a = 30; mouth_u = 0
else:
  // Tidak ada suara: mulut menutup smooth
  mouth_a = Smooth(mouth_a, 0, 12.0, dt)
  mouth_i = Smooth(mouth_i, 0, 12.0, dt)
  mouth_u = Smooth(mouth_u, 0, 12.0, dt)
  mouth_o = Smooth(mouth_o, 0, 12.0, dt)
```

## 5.3 Smoothing — Hilangkan Flicker

```
// Semua nilai BlendShape harus di-smooth sebelum diapply
// agar tidak flickering setiap frame

smoothSpeed_open  = 20.0   // cepat membuka mulut
smoothSpeed_close = 14.0   // agak lambat menutup (lebih natural)

// Untuk setiap vokal:
if targetValue > currentValue:
  currentValue = Smooth(currentValue, targetValue, smoothSpeed_open, dt)
else:
  currentValue = Smooth(currentValue, targetValue, smoothSpeed_close, dt)
```

## 5.4 Koreksi Pose Mulut Saat Berbicara

```
// Saat speaking, tambah ekspresi dasar:
// Mulut tidak pernah benar-benar 0 saat berbicara (ada gerakan minimum)
minMouthOpen = 5.0   // selalu ada sedikit mulut terbuka saat audio jalan

// Senyum baseline saat berbicara (Kohaku friendly)
mouth_smile1 = 15.0   // senyum kecil konstan saat speaking

// Gigi terlihat sedikit
mouth_tooth_up = amplitudeNorm × 20   // gigi atas muncul saat vokal besar
```

## 5.5 Formula Lengkap LipSync per Frame

```
// === SETIAP LATEUPDATE FRAME ===

// Step 1: Analisis audio
AudioSource.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris)
amplitude = sum(spectrumData[0..15]) / 16
ampNorm   = clamp01(amplitude × 150.0)

// Step 2: Tentukan target vokal
phase = t × 8.0
idx   = (int)phase % 4
target_a, target_i, target_u, target_o = 0, 0, 0, 0

if ampNorm > 0.08:
  switch(idx):
    0: target_a = ampNorm × 100
    1: target_i = ampNorm × 80;  target_a = ampNorm × 20
    2: target_u = ampNorm × 85;  target_a = ampNorm × 15
    3: target_o = ampNorm × 70;  target_a = ampNorm × 30

// Step 3: Smooth
spd = ampNorm > currentAmp ? 20.0 : 14.0
mouth_a = Smooth(mouth_a, max(target_a, minMouthOpen), spd, dt)
mouth_i = Smooth(mouth_i, target_i, spd, dt)
mouth_u = Smooth(mouth_u, target_u, spd, dt)
mouth_o = Smooth(mouth_o, target_o, spd, dt)

// Step 4: Apply via VRMAnimationController
ctrl.WriteBlendShape("A", mouth_a)
ctrl.WriteBlendShape("I", mouth_i)
ctrl.WriteBlendShape("U", mouth_u)
ctrl.WriteBlendShape("O", mouth_o)
```

## 5.6 Ringkasan Parameter LipSync

| Parameter | Nilai | Keterangan |
|-----------|-------|------------|
| `spectrumData` size | 256 | Standard FFT |
| Frequency range | index 0–15 | ~0–2600 Hz (vokal) |
| `gainFactor` | 150.0 | Tuning amplifikasi |
| Threshold | 0.08 | Batas minimum ada suara |
| `smoothSpeed_open` | 20.0 | Cepat membuka |
| `smoothSpeed_close` | 14.0 | Lambat menutup |
| `minMouthOpen` | 5.0 | Mulut tidak pernah 0 saat audio |
| `mouth_smile1` baseline | 15.0 | Senyum ringan saat bicara |
| Vokal rotation speed | 8.0 Hz | Pergantian vokal |

---

---

# ⑥ SPEAKING ANIMATION

> **Tujuan:** Kohaku terlihat sedang berbicara — kombinasi LipSync + body language aktif.  
> Dipicu saat: `AudioSource.Play()` dipanggil, state = `CharacterState.Speaking`.  
> LipSync berjalan bersamaan. Speaking animation mengurus TUBUH, bukan mulut.

---

## 6.1 Tubuh — Sedikit Bersemangat

```
// Chest: nafas lebih cepat saat berbicara
Chest.rotX = Wave(2.8°, 0.32, 0, t)   // lebih cepat dari idle (0.25)

// Spine: sedikit condong ke depan (lebih dari idle, kurang dari listening)
Spine.rotX = -5° + Wave(1.0°, 0.30, 0, t)

// Body bounce kecil: tubuh sedikit bergerak mengikuti ritme bicara
speakBounce = Wave(0.8°, 2.0, 0, t) × ampNorm   // bounce mengikuti amplitude audio
Hips.rotX  += speakBounce × 0.3
Chest.rotX += speakBounce × 0.5
```

## 6.2 Kepala — Ekspresif

```
// Kepala bergerak lebih aktif dari idle saat bicara
// Kecepatan mengikuti amplitude audio
ampFactor = clamp(ampNorm × 2.0, 0.3, 1.0)

Head.rotX = Wave(2.0°, 0.35, 0.0, t) × ampFactor   // angguk mengikuti bicara
Head.rotY = Wave(3.0°, 0.28, 1.5, t) × ampFactor   // menoleh saat kata tertentu
Head.rotZ = Wave(1.0°, 0.22, 0.8, t)               // miring halus

// Penekanan kata: sesekali nod lebih dalam
// (bisa di-trigger dari ResponseParser saat ada tanda '!')
if emphasisTriggered:
    Head.rotX += sin(π × emphasisProgress / 0.3) × 8°
```

## 6.3 Tangan — Gesture Ringan

```
// Tangan bergerak sedikit saat berbicara (tidak terlalu banyak)
// Variasi halus pada posisi lengan mengikuti amplitude

// Lengan sedikit naik saat ada volume suara
armLiftAmount = ampNorm × 6°
LeftArm.rotZ  = -44° + Wave(3.0°, 0.35, 0, t) - armLiftAmount
RightArm.rotZ =  44° + Wave(3.0°, 0.35, π, t) + armLiftAmount

// Siku sedikit lebih tekuk saat bicara (natural gesture)
LeftForeArm.rotX  = -18° + Wave(1.5°, 0.35, 0.4, t)
RightForeArm.rotX = -18° + Wave(1.5°, 0.35, 0.4, t)

// Pergelangan sedikit bergerak (sangat halus)
LeftHand.rotZ  = Wave(2.0°, 0.4, 0, t) × ampNorm
RightHand.rotZ = Wave(2.0°, 0.4, π, t) × ampNorm
```

## 6.4 Ekspresi Wajah — Sesuai EmotionState

```
// EmotionState diset dari ResponseParser sebelum audio diplay
// Speaking animation hanya TAMBAH pada ekspresi yang sudah ada

switch(currentEmotion):
  Joy:
    mouth_smile5 = lerp(mouth_smile5, 60, 3×dt)
    eye_smile    = lerp(eye_smile, 70, 3×dt)
    other_hoppe  = lerp(other_hoppe, 30, 2×dt)   // pipi merah

  Sorrow:
    blow_komaru  = lerp(blow_komaru, 50, 2×dt)   // alis khawatir
    eye_tareme   = lerp(eye_tareme, 40, 2×dt)    // puppy eyes
    mouth_smile1 = lerp(mouth_smile1, 5, 2×dt)   // senyum sangat kecil

  Angry:
    blow_anger   = lerp(blow_anger, 60, 4×dt)
    blow_down    = lerp(blow_down, 50, 4×dt)
    eye_turime   = lerp(eye_turime, 45, 4×dt)    // tsundere eyes

  Fun:
    mouth_smile9 = lerp(mouth_smile9, 70, 3×dt)
    eye_smile    = lerp(eye_smile, 80, 3×dt)
    other_tere   = lerp(other_tere, 25, 2×dt)    // malu sedikit

  Neutral:
    // Reset semua ekspresi ke baseline
    (semua emosi → 0 via smooth)

// BlendShape diterapkan via VRMAnimationController:
ctrl.WriteBlendShape("Joy",  emotionJoyValue)
// dst...
```

## 6.5 Telinga & Ekor Saat Speaking

```
// Ekor kibas mengikuti mood
switch(currentEmotion):
  Joy:    PhysicsBoneController.SetTailState(TailState.WagFast)
  Sorrow: PhysicsBoneController.SetTailState(TailState.Drooped)
  Angry:  PhysicsBoneController.SetTailState(TailState.Stiff)
  Fun:    PhysicsBoneController.SetTailState(TailState.WagFast)
  Neutral:PhysicsBoneController.SetTailState(TailState.WagSlow)

// Telinga alert saat speaking
PhysicsBoneController.SetEarState(EarState.Alert)
// → earOverrideStrength = 0.85
// → telinga tegak ke depan (mendengar diri sendiri)
```

## 6.6 Auto Return ke Idle

```
// Ketika AudioSource selesai:
// AudioSource.isPlaying == false

// Smooth transition kembali ke Idle:
transitionSpeed = 3.0   // detik untuk kembali ke idle pose

// Semua offset Speaking di-lerp ke 0:
speakBounce → 0
armLiftAmount → 0
Spine.rotX offset → 0

// EmotionState di-reset setelah delay singkat:
emotionFadeDelay = 2.0   // detik
// setelah 2 detik, smooth fade semua emotion BlendShape ke 0

// State resmi diubah:
ctrl.SetState(CharacterState.Idle)
PhysicsBoneController.SyncWithCharacterState("Idle")
```

## 6.7 Ringkasan Parameter Speaking

| Komponen | Parameter | Nilai |
|----------|-----------|-------|
| `Chest.rotX` | f nafas | 0.32 Hz |
| `Spine.rotX` | Lean | −5° |
| `speakBounce` | A, f | 0.8°, 2.0 Hz × ampNorm |
| `Head.rotX` | A, f | 2.0°, 0.35 Hz × ampFactor |
| `Head.rotY` | A, f | 3.0°, 0.28 Hz × ampFactor |
| `LeftArm.rotZ` | Base + gesture | −44° + armLift |
| `armLiftAmount` | Scale | ampNorm × 6° |
| Ear state | PhysicsBone | `EarState.Alert` |
| Emotion fade delay | — | 2.0 detik setelah audio |
| Transition to Idle | Smooth speed | 3.0 |

---

---

# TABEL PERBANDINGAN SEMUA STATE

| Parameter | Idle | Active | Thinking | Listening | Speaking |
|-----------|------|--------|----------|-----------|---------|
| `Chest` f nafas | 0.25 Hz | 0.28 Hz | 0.20 Hz | 0.30 Hz | 0.32 Hz |
| `Chest` A nafas | 2.5° | 2.0° | 3.0° | 2.5° | 2.8° |
| `Hips` sway A | 1.2° | 0.6° | 0.3° | 0.2° | 0.8° |
| `Spine` lean X | 0° | −4° | 0° | −8° | −5° |
| `Head` A rotate | 0.8–1.5° | 1.2–2.0° | 0.8–3.0° | 0.5–5.0° | 2.0–3.0° |
| `Arm` base Z | ±50° | ±40° | ±50° | ±50° | ±44° |
| Eye wander speed | 2–5 s | 1.5–3 s | 0.8–2 s | Fokus kamera | Fokus kamera |
| Blink interval | 3–6 s | 3–6 s | 4–7 s | 5–9 s | 3–5 s |
| Ear state | Idle | Alert | Idle | Listening | Alert |
| Tail state | Idle | Raised | Idle | Raised | Sesuai emosi |
| LipSync | OFF | OFF | OFF | OFF | ON |
| Emotion active | OFF | OFF | eye_jito | eye_open | Sesuai tag |

---

---

# IMPLEMENTASI — Urutan Pengerjaan

```
Langkah 1: Implementasi Idle dulu (paling mudah ditest)
  → IdleSystem.cs + FacialMicroSystem.cs

Langkah 2: Tambah Listening (test dengan trigger manual)
  → ListeningSystem.cs

Langkah 3: Tambah LipSync dengan audio dummy MP3
  → LipSyncSystem.cs + AudioSource

Langkah 4: Tambah Speaking = LipSync + body language
  → Gabung LipSyncSystem + EmotionSystem

Langkah 5: Tambah Thinking
  → ThinkingSystem.cs

Langkah 6: Sambungkan ke OpenClaw response
  → ResponseParser → SetEmotion → Speaking

Langkah 7: Fine-tune semua amplitude & frekuensi
  → Pakai BonePose + BoneExplorer untuk eksperimen nilai
```

---

*Formula ini adalah starting point. Semua nilai A (amplitudo) dan f (frekuensi)  
perlu fine-tuning berdasarkan tampilan nyata di Unity dan di HP Android.*