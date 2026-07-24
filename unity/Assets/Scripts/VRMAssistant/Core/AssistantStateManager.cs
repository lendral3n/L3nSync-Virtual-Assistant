using System;
using UnityEngine;

namespace VRMAssistant.Core
{
    /// <summary>
    /// Holder + dispatcher state asisten VRM.
    /// Component lain (Orchestrator, animations) subscribe OnStateChanged untuk reaksi.
    /// </summary>
    public class AssistantStateManager : MonoBehaviour
    {
        [Header("Initial State")]
        [SerializeField] private AssistantState initialState = AssistantState.Idle;

        /// <summary>State asisten saat ini.</summary>
        public AssistantState CurrentState { get; private set; }

        /// <summary>State sebelumnya (untuk transisi logic).</summary>
        public AssistantState PreviousState { get; private set; }

        /// <summary>Event saat state berubah. Param: (previous, new).</summary>
        public event Action<AssistantState, AssistantState> OnStateChanged;

        private void Awake()
        {
            CurrentState = initialState;
            PreviousState = initialState;
        }

        private void Start()
        {
            // Fire initial state agar listener bisa initialize sesuai state pertama
            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }

        /// <summary>
        /// Switch ke state baru. Tidak akan fire event jika state sama dengan current.
        /// </summary>
        public void SetState(AssistantState newState)
        {
            if (newState == CurrentState) return;

            PreviousState = CurrentState;
            CurrentState = newState;

            Debug.Log($"[StateManager] {PreviousState} → {CurrentState}");
            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }

        // Helper methods untuk dipanggil dari Inspector / UnityEvent
        public void SetIdle() => SetState(AssistantState.Idle);
        public void SetActive() => SetState(AssistantState.Active);
        public void SetThinking() => SetState(AssistantState.Thinking);
        public void SetListening() => SetState(AssistantState.Listening);
        public void SetSpeaking() => SetState(AssistantState.Speaking);
    }
}
