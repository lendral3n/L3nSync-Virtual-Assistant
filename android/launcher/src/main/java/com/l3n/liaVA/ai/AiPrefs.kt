package com.l3n.liaVA.ai

import android.content.Context
import android.content.SharedPreferences
import com.l3n.liaVA.BuildConfig

/**
 * Penyimpanan API key + setting AI. Pakai SharedPreferences MODE_PRIVATE
 * (storage app-private, tidak world-readable). Key TIDAK pernah keluar device.
 *
 * Diisi user langsung via UI Settings — bukan hardcode. Untuk personal app
 * single-user ini cukup; kalau butuh lebih ketat bisa upgrade ke
 * EncryptedSharedPreferences (androidx.security-crypto).
 */
class AiPrefs(context: Context) {

    private val prefs: SharedPreferences =
        context.getSharedPreferences("lia_ai_prefs", Context.MODE_PRIVATE)

    // Prioritas: nilai yang user simpan di app > default dari secrets.properties (BuildConfig).
    // Jadi key tetap ada setelah reinstall tanpa perlu ketik ulang di HP.
    var geminiApiKey: String
        get() = prefs.getString(KEY_GEMINI, null)?.takeIf { it.isNotBlank() }
            ?: BuildConfig.GEMINI_API_KEY
        set(v) = prefs.edit().putString(KEY_GEMINI, v.trim()).apply()

    var elevenLabsApiKey: String
        get() = prefs.getString(KEY_ELEVEN, null)?.takeIf { it.isNotBlank() }
            ?: BuildConfig.ELEVENLABS_API_KEY
        set(v) = prefs.edit().putString(KEY_ELEVEN, v.trim()).apply()

    /** Voice ID ElevenLabs untuk Lia (default "Fuu"). Bisa di-override user. */
    var voiceId: String
        get() = prefs.getString(KEY_VOICE, DEFAULT_VOICE_ID).orEmpty().ifBlank { DEFAULT_VOICE_ID }
        set(v) = prefs.edit().putString(KEY_VOICE, v.trim()).apply()

    /** TTS aktif? Kalau false, Lia hanya jawab teks (hemat kuota ElevenLabs). */
    var ttsEnabled: Boolean
        get() = prefs.getBoolean(KEY_TTS_ON, true)
        set(v) = prefs.edit().putBoolean(KEY_TTS_ON, v).apply()

    val hasGeminiKey: Boolean get() = geminiApiKey.isNotBlank()
    val hasElevenKey: Boolean get() = elevenLabsApiKey.isNotBlank()

    companion object {
        private const val KEY_GEMINI = "gemini_api_key"
        private const val KEY_ELEVEN = "elevenlabs_api_key"
        private const val KEY_VOICE = "elevenlabs_voice_id"
        private const val KEY_TTS_ON = "tts_enabled"
        // "kuon - Anime Cute Voice" (professional) — verified jalan di plan payg 2026-07-22.
        // Cloned voices (Kawai gMIZZ…/Momo 34WDf…) DIBLOKIR di payg (ivc_not_permitted).
        const val DEFAULT_VOICE_ID = "B8gJV1IhpuegLxdpXFOE"
    }
}
