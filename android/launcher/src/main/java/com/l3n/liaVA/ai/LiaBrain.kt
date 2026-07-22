package com.l3n.liaVA.ai

import android.content.Context
import android.util.Log
import com.l3n.liaVA.AssistantStateName
import com.l3n.liaVA.UnityBridge
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Otak Lia — orkestrasi 1 giliran ngobrol:
 *   user text → Gemini (jawaban + emosi + gesture) → ElevenLabs (suara)
 *             → Unity (ekspresi, gesture, PlayAudio yang drive Speaking + lipsync)
 *
 * Unity meng-handle state Speaking + lipsync + balik Idle sendiri saat PlayAudio.
 * Kalau TTS mati/gagal, fallback: set state Speaking sebentar (lipsync sintetis).
 */
class LiaBrain(context: Context, private val prefs: AiPrefs) {

    private val appContext = context.applicationContext
    private val gemini = GeminiClient(prefs)
    private val eleven = ElevenLabsClient(appContext, prefs)

    /** Riwayat percakapan (role model/user) — dibatasi supaya prompt tidak membengkak. */
    private val history = ArrayDeque<GeminiClient.Turn>()

    data class ChatMessage(val fromUser: Boolean, val text: String)

    private val _messages = MutableStateFlow<List<ChatMessage>>(emptyList())
    val messages: StateFlow<List<ChatMessage>> = _messages.asStateFlow()

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()

    val ready: Boolean get() = prefs.hasGeminiKey

    /**
     * Proses satu pesan user. Suspend — panggil dari coroutine (viewModelScope/lifecycleScope).
     * Meng-update [messages] untuk UI, dan menggerakkan karakter via UnityBridge.
     */
    suspend fun send(userText: String) {
        val text = userText.trim()
        if (text.isBlank() || _busy.value) return

        appendMessage(ChatMessage(fromUser = true, text = text))
        _busy.value = true

        // Lia "mendengarkan lalu berpikir"
        UnityBridge.setState(AssistantStateName.Thinking)

        try {
            when (val res = gemini.chat(text, history.toList())) {
                is GeminiClient.Result.Error -> {
                    UnityBridge.setState(AssistantStateName.Idle)
                    appendMessage(ChatMessage(fromUser = false, text = "⚠️ ${res.message}"))
                }
                is GeminiClient.Result.Ok -> {
                    val reply = res.reply
                    // Simpan ke riwayat
                    pushHistory("user", text)
                    pushHistory("model", reply.say)
                    appendMessage(ChatMessage(fromUser = false, text = reply.say))

                    // Ekspresi wajah sesuai emosi
                    applyEmotion(reply.emotion)
                    // Gesture opsional
                    reply.gesture?.let { UnityBridge.playGesture(it) }

                    // Suara + lipsync (Unity handle Speaking → Idle)
                    speak(reply.say, reply.emotion)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "send() error", e)
            UnityBridge.setState(AssistantStateName.Idle)
            appendMessage(ChatMessage(fromUser = false, text = "⚠️ Terjadi kesalahan: ${e.message}"))
        } finally {
            _busy.value = false
        }
    }

    private suspend fun speak(text: String, emotion: String) {
        if (!prefs.ttsEnabled || !prefs.hasElevenKey) {
            // Tanpa TTS: tetap animasikan mulut sintetis sebentar via state Speaking.
            UnityBridge.setState(AssistantStateName.Speaking)
            return
        }
        when (val res = eleven.synthesize(text, emotion)) {
            is ElevenLabsClient.Result.Ok -> {
                // Unity load file → play → Speaking + lipsync dari audio → balik Idle
                UnityBridge.sendMessage("VRMAssistant", "PlayAudio", res.filePath)
            }
            is ElevenLabsClient.Result.Error -> {
                Log.w(TAG, "TTS gagal: ${res.message}")
                // Fallback: Speaking sintetis supaya tetap terlihat "ngomong"
                UnityBridge.setState(AssistantStateName.Speaking)
            }
        }
    }

    private fun applyEmotion(emotion: String) {
        val mood = when (emotion) {
            "happy" -> "Happy"
            "sad" -> "Sad"
            "angry" -> "Angry"
            "surprised" -> "Surprised"
            else -> "Neutral"
        }
        UnityBridge.sendMessage("VRMAssistant", "SetExpression", "$mood|0.85")
    }

    private fun pushHistory(role: String, text: String) {
        history.addLast(GeminiClient.Turn(role, text))
        // Simpan maksimal MAX_TURNS giliran terakhir (user+model dihitung terpisah)
        while (history.size > MAX_TURNS) history.removeFirst()
    }

    private fun appendMessage(m: ChatMessage) {
        _messages.value = _messages.value + m
    }

    /** Reset percakapan (tombol "obrolan baru"). */
    fun clear() {
        history.clear()
        _messages.value = emptyList()
    }

    companion object {
        private const val TAG = "LiaBrain"
        private const val MAX_TURNS = 16 // ~8 pertukaran user↔Lia
    }
}
