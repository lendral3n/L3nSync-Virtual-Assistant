# BONE MAP — KOHAKU VRM
## Dokumentasi Hasil Eksplorasi BoneExplorer

**Karakter:** Kohaku_dress_1.10_VRM  
**Tool:** BoneExplorer v4  
**Engine:** Unity 2022.3.62f1 LTS + UniVRM v0  
**Sumber:** Catatan manual dari sesi eksplorasi (17 Maret 2026)

---

## Keterangan Simbol

| Simbol | Arti |
|--------|------|
| `(+)` | Nilai positif — arah positif rotasi |
| `(−)` | Nilai negatif — arah negatif rotasi |
| ⛔ | Tidak bisa digerakkan langsung (dikontrol `VRMSpringBone`) |
| ❓ | Nilai tidak terbaca jelas dari catatan — perlu verifikasi |

---

## 1. TUBUH UTAMA

> **Catatan:** Acuan/utama. Semua bone mengikuti Hips. Bersifat karakter (character root).

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Pinggul | `Hips` | Pusat gravitasi. Semua bone mengikuti. | Condong depan | Condong belakang | Putar kanan | Putar kiri | Miring kiri | Miring kanan |
| Tulang Belakang | `Spine` | Batang tubuh bawah | Bungkuk depan | Bungkuk belakang | Putar kanan | Putar kiri | Condong kiri | Condong kanan |
| Dada | `Chest` | Batang tubuh atas, dari bawah bahu sampai kepala | Bungkuk depan | Bungkuk belakang | Putar kanan | Putar kiri | Miring kiri | Miring kanan |

---

## 2. DADA / BREAST

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Dada Kiri | `LeftBreast` | Bone payudara kiri. Efek physics jiggle. | Turun | Naik | Ke dalam | Ke luar | Rotasi miring ke luar | Rotasi miring ke dalam |
| Dada Kanan | `RightBreast` | Bone payudara kanan. Efek physics jiggle. | Turun | Naik | Ke luar | Ke dalam | Rotasi miring ke dalam | Rotasi miring ke luar |

---

## 3. KEPALA

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Leher | `Neck` | Menghubungkan kepala ke tubuh. Dari leher sampai kepala. | Angguk depan | Dongak ke belakang | Toleh kanan | Toleh kiri | Miring ke bahu kiri | Miring ke bahu kanan |
| Kepala | `Head` | Kepala utama. Rotasi halus. | Angguk kecil depan | Angguk ke belakang | Toleh kanan | Toleh kiri | Miring lucu / penasaran kiri | Miring kanan |

---

## 4. TELINGA / FOXEAR ⛔

> ⛔ **Semua bone telinga tidak bisa digerakkan langsung dari BoneExplorer.**  
> Dikontrol oleh `VRMSpringBone` di object `secondary`.  
> **Solusi:** Gunakan `PhysicsBoneController.SetEarState()` untuk override pose di atas SpringBone.

| Bone | `Bone Name` | Status | Catatan |
|------|-------------|--------|---------|
| Telinga Kiri — Pangkal | `Left_Foxear_01` | ⛔ SpringBone | Bisa di-override dengan `EarState.Alert / Happy / Sad` dll |
| Telinga Kiri — Tengah | `Left_Foxear_02` | ⛔ SpringBone | Ikut Foxear_01 |
| Telinga Kiri — Ujung | `Left_Foxear_03` | ⛔ SpringBone | Ikut Foxear_01 |
| Telinga Kanan — Pangkal | `Right_Foxear_01` | ⛔ SpringBone | Bisa di-override dengan `EarState` |
| Telinga Kanan — Tengah | `Right_Foxear_02` | ⛔ SpringBone | Ikut Foxear_01 |
| Telinga Kanan — Ujung | `Right_Foxear_03` | ⛔ SpringBone | Ikut Foxear_01 |

---

## 5. MATA

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Mata Kiri | `LeftEye` | Bola mata kiri. Arah pandang. | Lirik bawah | Lirik atas | Lirik kanan | Lirik kiri | Roll / putar ke bawah | Roll / putar ke atas |
| Mata Kanan | `RightEye` | Bola mata kanan. Arah pandang. | Lirik bawah | Lirik atas | Lirik kanan | Lirik kiri | Roll ke atas | Roll ke bawah |

---

## 6. LENGAN KIRI

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Bahu Kiri | `LeftShoulder` | Pangkal bahu kiri | Turun | Naik | Maju ke depan | ke Belakang | ke Bawah | ke Atas |
| Lengan Atas Kiri | `LeftArm` | Upper arm. Kontrol utama posisi lengan. | Turun | Naik | Maju ke depan | ke Belakang | ke Bawah | ke Atas |
| Siku Kiri | `LeftForeArm` | Pergerakan hanya dari siku – ujung jari | ke Bawah | ke Atas | Maju ke depan | ke Belakang | Twist pergelangan bawah | Twist pergelangan atas |
| Pergelangan Kiri | `LeftHand` | Pergelangan tangan kiri | ke Bawah | ke Atas | ke Depan | ke Belakang | Roll ke bawah | Roll ke atas |

---

## 7. JARI KIRI — IBU JARI

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Ibu Jari Kiri 1 | `LeftHandThumb1` | Pangkal ibu jari. **Paling berpengaruh ke pose tangan.** | Tekuk ke telapak dalam | ke Luar | Putar ke samping luar | ke Dalam | Buka | Tutup |
| Ibu Jari Kiri 2 | `LeftHandThumb2` | Segmen tengah ibu jari | Tekuk ke bawah | ke Atas | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |
| Ibu Jari Kiri 3 | `LeftHandThumb3` | Ujung ibu jari | Tekuk ke dalam | ke Luar | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |

---

## 8. JARI KIRI — TELUNJUK

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Telunjuk Kiri 1 | `LeftHandIndex1` | Tekuk ke dalam | ke Luar | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |
| Telunjuk Kiri 2 | `LeftHandIndex2` | Tekuk ke dalam | ke Luar | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |
| Telunjuk Kiri 3 | `LeftHandIndex3` | Tekuk ke dalam | ke Luar | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |

---

## 9. JARI KIRI — TENGAH

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Jari Tengah Kiri 1 | `LeftHandMiddle1` | Tekuk ke depan | ke Belakang | Geser ke dalam | ke Luar | Roll ke bawah | Roll ke atas |
| Jari Tengah Kiri 2 | `LeftHandMiddle2` | Tekuk ke depan | ke Belakang | Geser ke dalam | ke Luar | Roll ke bawah | Roll ke atas |
| Jari Tengah Kiri 3 | `LeftHandMiddle3` | Tekuk ke depan | ke Belakang | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |

---

## 10. JARI KIRI — MANIS

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Jari Manis Kiri 1 | `LeftHandRing1` | Pangkal jari manis | Tekuk ke depan | ke Belakang | Geser ke dalam | ke Luar | Roll ke bawah | Roll ke atas |
| Jari Manis Kiri 2 | `LeftHandRing2` | Ruas tengah | Tekuk ke atas | ke Bawah | Geser ke depan | ke Belakang | Roll ke bawah | Roll ke atas |
| Jari Manis Kiri 3 | `LeftHandRing3` | Ujung jari manis | Geser ke depan ❓ | ke Atas ❓ | Geser ke luar | ke Dalam | Roll ke bawah | Roll ke atas |

---

## 11. JARI KIRI — KELINGKING

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Kelingking Kiri 1 | `LeftHandPinky1` | Pangkal kelingking | Tekuk ke depan | ke Belakang | Geser ke dalam | ke Belakang ❓ | Roll ke bawah | Roll ke atas |
| Kelingking Kiri 2 | `LeftHandPinky2` | Ruas tengah kelingking | Tekut depan | ke Dalam ❓ | Geser ke dalam | ke Belakang ❓ | Roll ke bawah | Roll ke atas |
| Kelingking Kiri 3 | `LeftHandPinky3` | Ujung kelingking | Tekuk ke depan | ke Belakang | Geser ke depan ❓ | ke Belakang ❓ | Roll ke bawah | Roll ke atas |

---

## 12. LENGAN KANAN

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Bahu Kanan | `RightShoulder` | Pangkal bahu kanan | ke Depan | ke Belakang | ❓ | ❓ | Roll ke atas | Roll ke bawah |
| Lengan Atas Kanan | `RightArm` | Upper arm kanan. Kontrol utama. | ke Depan | ke Belakang | ke Belakang ❓ | ke Depan ❓ | Roll ke atas | Roll ke bawah |
| Siku Kanan | `RightForeArm` | Upper arm ke lengan bawah kanan | ke Depan | ke Belakang | Belakang ❓ | Geser ❓ | ❓ | ❓ |
| Pergelangan Kanan | `RightHand` | Siku kanan / pergelangan | ke Depan ❓ | ke Belakang ❓ | Belakang | Depan | ❓ | ❓ |

> ⚠️ **Catatan:** Beberapa nilai sumbu Lengan Kanan tidak terbaca jelas dari catatan. Verifikasi diperlukan dengan BoneExplorer / BonePose.

---

## 13. JARI KANAN — IBU JARI

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Ibu Jari Kanan 1 | `RightHandThumb1` | Pangkal ibu jari kanan | Tekuk ke dalam | ke Luar | Putar ke dalam | ke Luar | Buka ke dalam | ke Bawah ❓ |
| Ibu Jari Kanan 2 | `RightHandThumb2` | Segmen tengah | Tekuk ke bawah | ke Atas | Geser ke dalam | ke Luar | Buka ke dalam | ke Atas ❓ |
| Ibu Jari Kanan 3 | `RightHandThumb3` | Ujung ibu jari kanan | Tekuk ke dalam | ke Luar | ❓ | ❓ | ❓ | ❓ |

---

## 14. JARI KANAN — TELUNJUK

> ✅ Data jelas terbaca dari catatan.

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Telunjuk Kanan 1 | `RightHandIndex1` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |
| Telunjuk Kanan 2 | `RightHandIndex2` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |
| Telunjuk Kanan 3 | `RightHandIndex3` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |

---

## 15. JARI KANAN — TENGAH

> ✅ Data jelas terbaca dari catatan.

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Jari Tengah Kanan 1 | `RightHandMiddle1` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |
| Jari Tengah Kanan 2 | `RightHandMiddle2` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |
| Jari Tengah Kanan 3 | `RightHandMiddle3` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |

---

## 16. JARI KANAN — MANIS

> ✅ Data jelas terbaca dari catatan.

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Jari Manis Kanan 1 | `RightHandRing1` | Tekuk ke belakang | ke arah Depan | Geser ke tengah | ke arah ibu jari | Roll ke atas | ke Bawah |
| Jari Manis Kanan 2 | `RightHandRing2` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke ibu jari | Roll ke atas | ke Bawah |
| Jari Manis Kanan 3 | `RightHandRing3` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke ibu jari | Roll ke atas | ke Bawah |

---

## 17. JARI KANAN — KELINGKING

> ✅ Data jelas terbaca dari catatan.

| Bone | `Bone Name` | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|-------|-------|-------|-------|-------|-------|
| Kelingking Kanan 1 | `RightHandPinky1` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke ibu jari | Roll ke atas | ke Bawah |
| Kelingking Kanan 2 | `RightHandPinky2` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke ibu jari | Roll ke atas | ke Bawah |
| Kelingking Kanan 3 | `RightHandPinky3` | Tekuk ke belakang | ke Depan | Geser ke tengah | ke ibu jari | Roll ke atas | ke Bawah |

---

## 18. KAKI KIRI

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Paha Kiri | `LeftUpLeg` | Paha / upper leg kiri | Kaki ke belakang | ke Depan | Ke dalam | Ke luar | Putar toe-in | Putar toe-out |
| Betis Kiri | `LeftLeg` | Betis / lower leg kiri | Tekuk ke belakang | ke Depan | Twist dalam | Twist luar | Geser ke dalam | Geser ke luar |
| Telapak Kaki Kiri | `LeftFoot` | Pergelangan kaki kiri | Tekuk ke belakang | ke Depan | Geser ke dalam | ke Luar | Roll ke luar | Roll ke dalam |
| Jari Kaki Kiri | `LeftToeBase` | Jari-jari kaki kiri | Jari turun | Jari naik | Geser dalam | Geser luar | Roll luar | Roll dalam |

---

## 19. KAKI KANAN

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Paha Kanan | `RightUpLeg` | Paha / upper leg kanan | Kaki ke belakang | ke Luar ❓ | Buka ke luar | ke Dalam | Putar ke luar | ke Dalam |
| Betis Kanan | `RightLeg` | Betis / lower leg kanan | Tekuk ke belakang | ke Depan | Twist ke luar | ke Dalam | Buka ke luar | ke Dalam |
| Telapak Kaki Kanan | `RightFoot` | Pergelangan kaki kanan | Tekuk ke belakang | ke Depan | Buka ke luar | ke Dalam | Roll ke luar | ke Dalam |
| Jari Kaki Kanan | `RightToeBase` | Jari-jari kaki kanan | Jari turun | Jari naik | Geser ke luar | ke Dalam | Roll ke luar | ke Dalam |

---

## 20. EKOR / TAIL

> ⛔ **Tail_02 sampai Tail_06 tidak bisa digerakkan langsung** — dikontrol `VRMSpringBone`.  
> **Solusi:** Gunakan `PhysicsBoneController.SetTailState()` untuk override (WagSlow, WagFast, Raised, Drooped, dll).

| Bone | `Bone Name` | Status | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|--------|-------|-------|-------|-------|-------|-------|
| Ekor Pangkal | `Tail_01` | ✅ Bisa digerakkan | Naik ke atas | Turun | Geser kiri | ke Kanan | Miring kanan | Miring kiri |
| Ekor Bag. 2 | `Tail_02` | ⛔ SpringBone | — | — | — | — | — | — |
| Ekor Bag. 3 | `Tail_03` | ⛔ SpringBone | — | — | — | — | — | — |
| Ekor Bag. 4 | `Tail_04` | ⛔ SpringBone | — | — | — | — | — | — |
| Ekor Bag. 5 | `Tail_05` | ⛔ SpringBone | — | — | — | — | — | — |
| Ekor Ujung | `Tail_06` | ⛔ SpringBone | — | — | — | — | — | — |

---

## 21. ROK PHYSICS / SKIRT

> 💡 SkirtRoot menggerakkan seluruh rok secara bersamaan. Setiap chain rok anak juga mengikuti SpringBone.

| Bone | `Bone Name` | Catatan | X (+) | X (−) | Y (+) | Y (−) | Z (+) | Z (−) |
|------|-------------|---------|-------|-------|-------|-------|-------|-------|
| Rok Root | `SkirtRoot` | Root seluruh chain rok (12 chain) | Memanjang / naik | Turun | Geser kiri | ke Kanan | Mengayun bahan ❓ | ke Kiri ❓ |

---

## Ringkasan Status Bone

| Kategori | Total Bone | Bisa Digerakkan | ⛔ SpringBone | ❓ Perlu Verifikasi |
|----------|-----------|-----------------|--------------|---------------------|
| Tubuh Utama | 3 | ✅ 3 | — | — |
| Dada/Breast | 2 | ✅ 2 | — | — |
| Kepala | 2 | ✅ 2 | — | — |
| Telinga | 6 | — | ⛔ 6 | — |
| Mata | 2 | ✅ 2 | — | — |
| Lengan Kiri | 4 | ✅ 4 | — | — |
| Jari Kiri | 15 | ✅ 15 | — | 6 (Manis & Kelingking) |
| Lengan Kanan | 4 | ✅ 4 | — | 4 (semua, catatan buram) |
| Jari Kanan Ibu Jari | 3 | ✅ 3 | — | 2 |
| Jari Kanan Index–Pinky | 12 | ✅ 12 | — | — |
| Kaki Kiri | 4 | ✅ 4 | — | — |
| Kaki Kanan | 4 | ✅ 4 | — | 1 (Paha Kanan X-) |
| Ekor | 6 | ✅ 1 | ⛔ 5 | — |
| Rok | 1 | ✅ 1 | — | Z axis |
| **TOTAL** | **68** | **✅ 56** | **⛔ 11** | **~13** |

---

## Catatan Penting

```
1. SPRINGBONE CONFLICT
   Bone yang dikontrol SpringBone (Telinga, Tail_02–06):
   → Jangan matikan SpringBone saat animasi normal
   → Gunakan PhysicsBoneController (ExecutionOrder +200)
     untuk Slerp blend di atas SpringBone

2. JARI KIRI vs KANAN
   Sumbu Y jari kiri berbeda arah dengan jari kanan karena mirroring:
   - Kiri:  Y(+) = ke luar, Y(-) = ke dalam
   - Kanan: Y(+) = ke tengah (arah ibu jari), Y(-) = ke luar

3. LENGAN KANAN (❓)
   Beberapa sumbu Lengan Kanan (RightShoulder, RightArm, RightForeArm, RightHand)
   tidak terbaca jelas dari catatan. Lakukan verifikasi ulang di BoneExplorer
   dengan filter "Right" dan tab BONE.

4. ROK PHYSICS
   SkirtRoot langsung memindahkan seluruh rok.
   Chain individual (Skirt_Front_01, Skirt_Back_01, dll) juga bisa dikontrol
   tapi efeknya tumpang tindih dengan SpringBone physics.
```

---

*Dokumen ini dibuat berdasarkan catatan eksplorasi manual. Nilai ❓ perlu verifikasi ulang di Unity.*