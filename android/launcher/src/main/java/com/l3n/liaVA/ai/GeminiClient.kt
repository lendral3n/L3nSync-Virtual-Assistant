package com.l3n.liaVA.ai

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Client Gemini free tier (Google AI Studio). Endpoint generateContent.
 * API key gratis dari https://aistudio.google.com — user tempel di Settings.
 *
 * Balasan Lia dipaksa JSON (responseMimeType) → di-parse jadi [LiaReply].
 * Tanpa OkHttp/serialization — HttpURLConnection + org.json.
 */
class GeminiClient(private val prefs: AiPrefs) {

    data class LiaReply(val say: String, val emotion: String, val gesture: String?)

    sealed class Result {
        data class Ok(val reply: LiaReply) : Result()
        data class Error(val message: String) : Result()
    }

    /**
     * Kirim pesan user + riwayat singkat. History = pasangan turn lama (role "user"/"model").
     * Return LiaReply hasil parse, atau Error dengan pesan ramah.
     */
    suspend fun chat(userText: String, history: List<Turn>): Result = withContext(Dispatchers.IO) {
        val apiKey = prefs.geminiApiKey
        if (apiKey.isBlank()) return@withContext Result.Error("API key Gemini belum diisi di Setelan.")

        val url = URL("$BASE_URL/$MODEL_ID:generateContent?key=$apiKey")
        val body = buildBody(userText, history)

        var conn: HttpURLConnection? = null
        try {
            conn = (url.openConnection() as HttpURLConnection).apply {
                requestMethod = "POST"
                connectTimeout = 15_000
                readTimeout = 45_000
                doOutput = true
                setRequestProperty("Content-Type", "application/json; charset=utf-8")
            }
            conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }

            val code = conn.responseCode
            val raw = (if (code in 200..299) conn.inputStream else conn.errorStream)
                ?.bufferedReader()?.use { it.readText() }.orEmpty()

            if (code !in 200..299) {
                Log.e(TAG, "Gemini HTTP $code: ${raw.take(300)}")
                return@withContext Result.Error(mapError(code))
            }

            val text = parseCandidateText(raw)
                ?: return@withContext Result.Error("Lia bingung menjawab (respons kosong).")
            val reply = parseLiaReply(text)
            Result.Ok(reply)
        } catch (e: Exception) {
            Log.e(TAG, "Gemini exception", e)
            Result.Error("Gagal terhubung: ${e.message ?: "jaringan bermasalah"}")
        } finally {
            conn?.disconnect()
        }
    }

    private fun buildBody(userText: String, history: List<Turn>): JSONObject {
        val contents = JSONArray()
        // Riwayat percakapan (dibatasi oleh pemanggil)
        for (t in history) {
            contents.put(JSONObject().apply {
                put("role", t.role) // "user" atau "model"
                put("parts", JSONArray().put(JSONObject().put("text", t.text)))
            })
        }
        // Pesan user sekarang
        contents.put(JSONObject().apply {
            put("role", "user")
            put("parts", JSONArray().put(JSONObject().put("text", userText)))
        })

        val safety = JSONArray()
        for (cat in listOf(
            "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH",
            "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT"
        )) {
            safety.put(JSONObject().put("category", cat).put("threshold", "BLOCK_ONLY_HIGH"))
        }

        return JSONObject().apply {
            put("systemInstruction", JSONObject().apply {
                put("parts", JSONArray().put(JSONObject().put("text", LiaPersona.SYSTEM_PROMPT)))
            })
            put("contents", contents)
            put("generationConfig", JSONObject().apply {
                put("responseMimeType", "application/json")
                put("temperature", 0.9)
                put("topP", 0.95)
                put("maxOutputTokens", 512)
            })
            put("safetySettings", safety)
        }
    }

    private fun parseCandidateText(raw: String): String? = runCatching {
        val obj = JSONObject(raw)
        val candidates = obj.optJSONArray("candidates") ?: return@runCatching null
        if (candidates.length() == 0) return@runCatching null
        val parts = candidates.getJSONObject(0)
            .optJSONObject("content")?.optJSONArray("parts") ?: return@runCatching null
        val sb = StringBuilder()
        for (i in 0 until parts.length()) sb.append(parts.getJSONObject(i).optString("text"))
        sb.toString().ifBlank { null }
    }.getOrNull()

    /** Parse JSON balasan Lia. Toleran: kalau bukan JSON, pakai seluruh teks sbg "say". */
    private fun parseLiaReply(text: String): LiaReply {
        val jsonStr = text.trim().let { s ->
            // Kadang model bungkus ```json ... ``` — ekstrak kurung kurawal terluar.
            val start = s.indexOf('{'); val end = s.lastIndexOf('}')
            if (start >= 0 && end > start) s.substring(start, end + 1) else s
        }
        return runCatching {
            val o = JSONObject(jsonStr)
            val say = o.optString("say").ifBlank { text }
            val emotion = o.optString("emotion", "neutral").lowercase()
                .takeIf { it in LiaPersona.VALID_EMOTIONS } ?: "neutral"
            val gestureRaw = o.optString("gesture").trim()
            val gesture = gestureRaw.takeIf {
                it.isNotBlank() && !it.equals("null", true) &&
                    LiaPersona.VALID_GESTURES.any { g -> g.equals(it, true) }
            }?.let { g -> LiaPersona.VALID_GESTURES.first { it.equals(g, true) } }
            LiaReply(say, emotion, gesture)
        }.getOrElse {
            // Bukan JSON valid → jadikan teks apa adanya
            LiaReply(text.trim(), "neutral", null)
        }
    }

    private fun mapError(code: Int): String = when (code) {
        400 -> "Permintaan ditolak (cek API key Gemini)."
        401, 403 -> "API key Gemini tidak valid / tidak berizin."
        429 -> "Kuota Gemini habis untuk saat ini, coba lagi nanti."
        in 500..599 -> "Server Gemini sedang bermasalah."
        else -> "Gemini error (HTTP $code)."
    }

    /** Satu giliran percakapan untuk riwayat. */
    data class Turn(val role: String, val text: String) // role: "user" | "model"

    companion object {
        private const val TAG = "GeminiClient"
        private const val BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models"
        // gemini-2.x sudah deprecated utk key baru (404). gemini-flash-latest = alias
        // model flash terbaru, gratis di free tier — verified jalan 2026-07-22.
        private const val MODEL_ID = "gemini-flash-latest"
    }
}
