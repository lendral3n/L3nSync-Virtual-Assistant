namespace VRMAssistant.Core
{
    /// <summary>
    /// Enum state untuk asisten VRM.
    /// Menentukan animasi body + facial expression yang aktif.
    /// </summary>
    public enum AssistantState
    {
        /// <summary>Diam, tidak ada interaksi. Breathing + micro-movement.</summary>
        Idle,

        /// <summary>Alert, interaksi dimulai. Nafas cepat, siap respon.</summary>
        Active,

        /// <summary>Menunggu response AI backend. Head tilt + slow breathing.</summary>
        Thinking,

        /// <summary>STT aktif, mendengarkan user. Forward lean + nodding.</summary>
        Listening,

        /// <summary>TTS aktif, berbicara. Body bounce + lip sync.</summary>
        Speaking
    }
}
