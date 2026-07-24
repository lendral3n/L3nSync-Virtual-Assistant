using UnityEngine;

namespace VRMAssistant.Locomotion
{
    [RequireComponent(typeof(Animator))]
    public class WanderController : MonoBehaviour
    {
        [Header("Wander Area (Unity world units)")]
        [SerializeField] private float minX = -1.5f;
        [SerializeField] private float maxX = 1.5f;
        [SerializeField] private float walkSpeed = 0.3f;
        [SerializeField] private float autoWanderIntervalMin = 30f;
        [SerializeField] private float autoWanderIntervalMax = 60f;
        // OFF sampai ada walk animation — tanpa clip jalan, karakter "meluncur" sambil menghadap samping
        [SerializeField] private bool autoWanderEnabled = false;
        
        private Animator _animator;
        private float _targetX;
        private bool _isWalking;
        private float _nextWanderTime;
        
        private static readonly int WalkDirHash = Animator.StringToHash("WalkDir");
        
        void Awake() { _animator = GetComponent<Animator>(); }
        
        void Start() { ScheduleNextWander(); }
        
        void Update()
        {
            if (_isWalking)
            {
                float dx = _targetX - transform.position.x;
                if (Mathf.Abs(dx) < 0.05f) { StopWalk(); return; }
                int dir = dx > 0 ? 1 : -1;
                transform.position += Vector3.right * dir * walkSpeed * Time.deltaTime;
                // face direction: rotation Y 90° kalau right, 270° kalau left (asumsi character default face +Z forward camera)
                transform.rotation = Quaternion.Euler(0, dir > 0 ? 90 : 270, 0);
            }
            else if (autoWanderEnabled && Time.time > _nextWanderTime)
            {
                TriggerRandomWander();
            }
        }
        
        public void TriggerWanderTo(float targetX)
        {
            _targetX = Mathf.Clamp(targetX, minX, maxX);
            _isWalking = true;
            int dir = _targetX > transform.position.x ? 1 : -1;
            if (_animator.runtimeAnimatorController != null) _animator.SetInteger(WalkDirHash, dir);
        }
        
        public void TriggerRandomWander()
        {
            float x = Random.Range(minX, maxX);
            TriggerWanderTo(x);
        }
        
        public void Stop() { StopWalk(); }
        
        private void StopWalk()
        {
            _isWalking = false;
            if (_animator.runtimeAnimatorController != null) _animator.SetInteger(WalkDirHash, 0);
            // face camera saat idle (rotation Y 0)
            transform.rotation = Quaternion.identity;
            ScheduleNextWander();
        }
        
        private void ScheduleNextWander()
        {
            _nextWanderTime = Time.time + Random.Range(autoWanderIntervalMin, autoWanderIntervalMax);
        }
        
        // Bridge dari Android UnityBridge
        public void WanderTo(string xStr) { if (float.TryParse(xStr, out var x)) TriggerWanderTo(x); }
        public void WanderRandom(string _) => TriggerRandomWander();
        public void WanderStop(string _) => Stop();
    }
}