using System.Collections;
using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 스테이션에 붙은 "누르는 부품"(버튼·레버·자판). 옛 MissionButton 을 옮긴 것.
    ///  좌클릭(IInteractable) → station.PressPart(partIndex) → 호스트에서 OnPartPressed(actor, partIndex)
    ///  눌림 애니메이션은 이 PC 에서만 재생한다 (결과는 스테이션의 동기화 상태로 나타남).
    ///
    /// [NetworkObject 없음] 부품은 스테이션의 NetworkObject 를 대신 알려준다(TargetObject). 옛 구조와 같다 (명세 S5).
    /// [배치 규칙] 부품은 "자기 콜라이더가 있는 오브젝트"(또는 스테이션보다 아래 조상)에 둔다.
    ///  감지기는 콜라이더에서 위로 올라가며 처음 만나는 ITargetable 을 잡는데, MissionStation 도 ITargetable 이라
    ///  부품을 스테이션과 같은 오브젝트에 두면 스테이션이 먼저 잡혀 클릭이 부품에 전달되지 않는다.
    /// [재사용] 발전기 버튼(0), 안테나 고정 버튼, 차단기 레버(0~5), 전선 완료 레버(0), 코드 자판(0~9) 이 모두 이 부품 하나로 된다.
    /// [눌림 연출] 위치 이동(pressOffset) + 회전(pressRotation, 2b 명세 2-5 — 전선 레버가 당겨졌다 돌아오는 연출). 둘 다 0 이면 움직이지 않는다.
    /// </summary>
    public class StationButton : MonoBehaviour, ITargetable, IInteractable
    {
        [SerializeField] private MissionStation station;

        [Tooltip("스테이션이 어느 부품인지 구분하는 번호. 예) 차단기 레버 0~5")]
        [SerializeField] private int partIndex;

        [Header("버튼 애니메이션")]
        [SerializeField] private Transform buttonVisual;
        [SerializeField] private Vector3 pressOffset;

        [Tooltip("눌렸을 때 더할 로컬 회전(오일러 각). 레버처럼 당기는 부품용.")]
        [SerializeField] private Vector3 pressRotation;

        [SerializeField] private float pressTime = 0.1f;
        [SerializeField] private float returnTime = 0.1f;

        private Vector3 startPosition;
        private Quaternion startRotation;
        private Coroutine pressRoutine;

        public NetworkObject TargetObject => station != null ? station.Object : null;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<MissionStation>();

            // 버튼이 눌린 뒤 돌아올 원래 위치 · 회전
            if (buttonVisual != null)
            {
                startPosition = buttonVisual.localPosition;
                startRotation = buttonVisual.localRotation;
            }
        }

        public void Interact()
        {
            if (station == null || station.Object == null || !station.Object.IsValid || station.Completed)
                return;

            station.PressPart(partIndex);

            if (buttonVisual == null)
                return;

            // 연속 클릭 시 기존 애니메이션을 중단하고 처음부터 다시
            if (pressRoutine != null)
                StopCoroutine(pressRoutine);

            ApplyPress(0f);
            pressRoutine = StartCoroutine(PressAnimation());
        }

        private IEnumerator PressAnimation()
        {
            yield return Animate(0f, 1f, pressTime);
            yield return Animate(1f, 0f, returnTime);

            pressRoutine = null;
        }

        private IEnumerator Animate(float from, float to, float duration)
        {
            float time = 0f;

            while (time < duration)
            {
                time += Time.deltaTime;
                ApplyPress(Mathf.Lerp(from, to, time / duration));
                yield return null;
            }

            ApplyPress(to);
        }

        /// <summary>0 = 원래 자리, 1 = 끝까지 눌림.</summary>
        private void ApplyPress(float amount)
        {
            buttonVisual.localPosition = startPosition + pressOffset * amount;
            buttonVisual.localRotation = startRotation * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(pressRotation), amount);
        }

        private void OnDisable()
        {
            if (pressRoutine != null)
                StopCoroutine(pressRoutine);

            pressRoutine = null;

            if (buttonVisual != null)
                ApplyPress(0f);
        }
    }
}
