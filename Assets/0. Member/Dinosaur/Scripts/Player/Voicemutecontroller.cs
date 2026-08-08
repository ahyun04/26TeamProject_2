using Photon.Voice.Unity;
using UnityEngine;

namespace LockdownProtocol.Networking
{
    /// <summary>
    /// 로컬 플레이어의 음성 송신 음소거(V키)를 처리한다.
    ///
    /// Recorder.TransmitEnabled만 끄고 켜므로, 음소거해도 다른 사람 목소리는 계속 들린다
    /// (송신만 막고 수신은 막지 않는 것이 일반적인 음소거 동작이며, Photon Voice 공식 문서에도
    /// TransmitEnabled는 수신에 영향을 주지 않는다고 명시되어 있다).
    ///
    /// 순수 로컬 UX 기능이라 네트워크 동기화가 필요 없다. 다른 사람에게 "음소거 중" 표시가
    /// 필요해지면(예: 말풍선 아이콘) 그때 이 상태를 [Networked] bool로 승격하면 된다.
    /// </summary>
    [RequireComponent(typeof(Recorder))]
    public class VoiceMuteController : MonoBehaviour
    {
        [SerializeField] private KeyCode muteKey = KeyCode.V;

        private Recorder _recorder;

        public bool IsMuted { get; private set; }

        private void Awake()
        {
            _recorder = GetComponent<Recorder>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(muteKey))
            {
                ToggleMute();
            }
        }

        private void ToggleMute()
        {
            IsMuted = !IsMuted;
            _recorder.TransmitEnabled = !IsMuted;

            Debug.Log($"[VoiceMuteController] 음소거: {IsMuted}");
        }
    }
}