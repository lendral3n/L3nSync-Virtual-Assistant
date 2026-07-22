package com.l3n.liaVA.ai

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * Client ElevenLabs TTS. Output MP3 → simpan ke cacheDir → kembalikan path lokal
 * supaya Unity (UaaL, satu proses) bisa load via file:// dan putar + lipsync.
 *
 * MP3 dipilih (bukan PCM stream) karena Unity Android decode MP3 native, dan
 * lebih simpel disimpan sebagai file utuh untuk UnityWebRequestMultimedia.
 */
class ElevenLabsClient(
    private val context: Context,
    private val prefs: AiPrefs,
) {
    sealed class Result {
        data class Ok(val filePath: String) : Result()
        data class Error(val message: String) : Result()
    }

    /**
     * Sintesis [text] jadi file MP3. [emotion] menyetel voice settings + tag ekspresif.
     * File ditulis ke cacheDir/lia_tts.mp3 (di-overwrite tiap ucapan).
     */
    suspend fun synthesize(text: String, emotion: String?): Result = withContext(Dispatchers.IO) {
        val apiKey = prefs.elevenLabsApiKey
        if (apiKey.isBlank()) return@withContext Result.Error("API key ElevenLabs belum diisi di Setelan.")
        if (text.isBlank()) return@withContext Result.Error("Teks kosong.")

        val voiceId = prefs.voiceId
        val url = URL("https://api.elevenlabs.io/v1/text-to-speech/$voiceId?output_format=$OUTPUT_FORMAT")
        val body = buildBody(text, emotion)

        var conn: HttpURLConnection? = null
        try {
            conn = (url.openConnection() as HttpURLConnection).apply {
                requestMethod = "POST"
                connectTimeout = 15_000
                readTimeout = 60_000
                doOutput = true
                setRequestProperty("xi-api-key", apiKey)
                setRequestProperty("Content-Type", "application/json")
                setRequestProperty("Accept", "audio/mpeg")
            }
            conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }

            val code = conn.responseCode
            if (code !in 200..299) {
                val err = conn.errorStream?.bufferedReader()?.use { it.readText() }.orEmpty()
                Log.e(TAG, "ElevenLabs HTTP $code: ${err.take(300)}")
                return@withContext Result.Error(mapError(code))
            }

            val outFile = File(context.cacheDir, "lia_tts.mp3")
            conn.inputStream.use { input ->
                outFile.outputStream().use { output -> input.copyTo(output) }
            }
            if (!outFile.exists() || outFile.length() < 256L) {
                return@withContext Result.Error("Audio TTS kosong / gagal ditulis.")
            }
            Log.d(TAG, "TTS saved ${outFile.length() / 1024}KB → ${outFile.absolutePath}")
            Result.Ok(outFile.absolutePath)
        } catch (e: Exception) {
            Log.e(TAG, "ElevenLabs exception", e)
            Result.Error("Gagal sintesis suara: ${e.message ?: "jaringan bermasalah"}")
        } finally {
            conn?.disconnect()
        }
    }

    private fun buildBody(text: String, emotion: String?): JSONObject {
        val (stability, style) = when (emotion?.lowercase()) {
            "happy" -> 0.4 to 0.6
            "sad" -> 0.7 to 0.2
            "angry" -> 0.5 to 0.7
            "surprised" -> 0.4 to 0.6
            else -> 0.55 to 0.35
        }
        return JSONObject().apply {
            put("text", text)
            put("model_id", "eleven_multilingual_v2") // dukung bahasa Indonesia
            put("voice_settings", JSONObject().apply {
                put("stability", stability)
                put("similarity_boost", 0.75)
                put("style", style)
                put("use_speaker_boost", true)
            })
        }
    }

    private fun mapError(code: Int): String = when (code) {
        401 -> "API key ElevenLabs tidak valid."
        422 -> "Voice ID tidak ditemukan / teks tidak didukung."
        429 -> "Kuota ElevenLabs habis."
        in 500..599 -> "Server ElevenLabs bermasalah."
        else -> "ElevenLabs error (HTTP $code)."
    }

    companion object {
        private const val TAG = "ElevenLabsClient"
        // 44.1kHz 128kbps MP3 — kualitas bagus, decode native di Android + Unity.
        private const val OUTPUT_FORMAT = "mp3_44100_128"
    }
}
