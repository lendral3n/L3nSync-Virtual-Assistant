package com.l3n.liaVA

import android.util.Log
import com.unity3d.player.UnityPlayer

/**
 * Wrapper untuk komunikasi Kotlin → Unity via UnityPlayer.UnitySendMessage.
 * Method receiver di Unity-side: GameObject "VRMAssistant", method-method di AssistantStateManager / Orchestrator.
 *
 * Phase 2C: extend dengan callback Unity → Kotlin via JNI.
 */
object UnityBridge {
    private const val TAG = "UnityBridge"
    private const val ROOT_GAME_OBJECT = "VRMAssistant"

    /** Set state asisten via SetState method di AssistantStateManager. */
    fun setState(state: AssistantStateName) {
        val methodName = when (state) {
            AssistantStateName.Idle -> "SetIdle"
            AssistantStateName.Active -> "SetActive"
            AssistantStateName.Thinking -> "SetThinking"
            AssistantStateName.Listening -> "SetListening"
            AssistantStateName.Speaking -> "SetSpeaking"
        }
        sendMessage(ROOT_GAME_OBJECT, methodName, "")
    }

    /** Trigger one-shot gesture animation (Wave/Peace/Bow dll) via Animator trigger param. */
    fun playGesture(gestureName: String) {
        sendMessage(ROOT_GAME_OBJECT, "TriggerGesture", gestureName)
    }

    /** Ganti karakter runtime. Alias: "dress" (Kohaku dress) / "kimono" (Kohaku kimono putih). */
    fun switchCharacter(nameOrFile: String) {
        sendMessage(ROOT_GAME_OBJECT, "SwitchCharacter", nameOrFile)
    }

    /** Animasi lokomosi saat jendela roam: "walk" (main FemWalk) / "idle" (stop). */
    fun setLocomotion(walking: Boolean) {
        sendMessage(ROOT_GAME_OBJECT, "SetLocomotion", if (walking) "walk" else "idle")
    }

    /** User tap karakter → Lia bereaksi (gesture + lihat user). */
    fun tapReaction() {
        sendMessage(ROOT_GAME_OBJECT, "OnTapReaction", "")
    }

    /** Generic UnitySendMessage wrapper dengan logging. */
    fun sendMessage(gameObject: String, method: String, arg: String) {
        try {
            UnityPlayer.UnitySendMessage(gameObject, method, arg)
            Log.d(TAG, "→ $gameObject.$method('$arg')")
        } catch (e: Exception) {
            Log.e(TAG, "Gagal kirim $gameObject.$method: ${e.message}")
        }
    }
}

/** Mirror enum AssistantState dari Unity-side untuk type safety di Kotlin. */
enum class AssistantStateName {
    Idle, Active, Thinking, Listening, Speaking
}
