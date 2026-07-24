using System;
using UnityEngine;
using VRMAssistant.Core;

namespace VRMAssistant.Behavior
{
    /// <summary>
    /// Single behavior entry — Shimeji-inspired atomic action.
    /// Bisa berupa Animator trigger, hand pose change, atau autonomous move command.
    ///
    /// Inline class (bukan ScriptableObject) untuk simplicity Phase 2.5 — kalau butuh authoring
    /// tooling proper di Phase 3, refactor jadi SO.
    /// </summary>
    [Serializable]
    public class BehaviorEntry
    {
        public enum Kind
        {
            AnimatorTrigger,    // call animator.SetTrigger(triggerName)
            HandPose,           // change hand pose ke handPoseTarget
            AutonomousMove,     // request floating window move (Kotlin side)
            CompositeSequence   // sequence multiple actions (untuk gesture combo)
        }

        [Tooltip("Tipe behavior ini")]
        public Kind kind = Kind.AnimatorTrigger;

        [Tooltip("Nama untuk debug/logging")]
        public string label = "Untitled";

        [Tooltip("Animator trigger name (kalau Kind = AnimatorTrigger)")]
        public string triggerName = "";

        [Tooltip("Target hand pose (kalau Kind = HandPose)")]
        public VRMAssistant.Animation.HandPoseController.HandPose handPoseTarget;

        [Tooltip("Probabilitas relatif (semakin tinggi semakin sering dipilih)")]
        public float weight = 1f;

        [Tooltip("Minimum interval antar trigger entry yang sama (detik)")]
        public float minIntervalSec = 5f;

        [Tooltip("Durasi behavior aktif (untuk one-shot, atau time hold di pose). 0 = instant.")]
        public float durationSec = 0f;

        [Tooltip("State yang allow entry ini di-trigger. Kosong = always.")]
        public AssistantState[] allowedStates;

        [HideInInspector]
        public float lastTriggeredTime = -999f;

        public bool IsAllowedInState(AssistantState state)
        {
            if (allowedStates == null || allowedStates.Length == 0) return true;
            for (int i = 0; i < allowedStates.Length; i++)
                if (allowedStates[i] == state) return true;
            return false;
        }

        public bool CanTrigger(float now) => (now - lastTriggeredTime) >= minIntervalSec;
    }
}
