using Fusion;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 방화문 레버 패널 하나: 조준 "주소" · 화면 글자 · 꽂힌 레버 모습. 상태는 FireDoorStation 이 가진다. 3d 명세 3-3.
    ///  - 주소 (FD10): 자기 중첩 NetworkObject 를 TargetObject 로 알려 준다. 팀원 아이템 코드가 우클릭 대상의 NetworkObject 까지 3m 를 검사하는데,
    ///    패널은 문 중심에서 3.2m 라 패널마다 위치가 맞는 NetworkObject 가 필요하다 (2c 필터 먼지와 같은 방식).
    ///  - 화면: 방화문 장치가 매 Render 에 문구를 넣어 준다 (LcdDisplay — 바뀔 때만 다시 그림).
    ///  - 꽂힌 레버: 끼움이면 보이고 내림 진행만큼 앞으로 젖힌다 (FD4). 튕겨 나가는 중이면 자리 → landing 포물선 (FD5).
    ///    꽂힌 레버 오브젝트에는 내리기 클릭용 StationButton(부품 번호 = 패널 번호)이 붙어 있다.
    /// [IItemUseTarget] 아이템 우클릭 대상 표시 — 변환기 조준 검증용 (2c F10).
    /// </summary>
    public class FireDoorPanel : MonoBehaviour, ITargetable, IItemUseTarget
    {
        [SerializeField] private FireDoorStation station;

        [Tooltip("패널 번호 (0 · 1) — 꽂힌 레버 버튼의 부품 번호와 같다")]
        [SerializeField] private int index;

        [SerializeField] private LcdDisplay display;

        [Tooltip("꽂힌 레버 (원점 = 회전축). 평소 숨김")]
        [SerializeField] private Transform mountedLever;

        [Tooltip("튕겨 나간 레버가 떨어질 바닥 자리")]
        [SerializeField] private Transform landing;

        [Tooltip("다 내렸을 때 앞으로 젖히는 각도 (로컬 X 축, 도)")]
        [SerializeField] private float pulledAngle = 150f;

        private NetworkObject networkObject;
        private Vector3 leverStartLocalPosition;
        private Quaternion leverStartLocalRotation;
        private bool? leverShown;
        private float shownPull = -2f;
        private float shownPop = -2f;

        public NetworkObject TargetObject => networkObject;
        public FireDoorStation Station => station;
        public int Index => index;
        public Transform Landing => landing;

        private void Awake()
        {
            if (station == null)
                station = GetComponentInParent<FireDoorStation>();

            networkObject = GetComponent<NetworkObject>();

            if (mountedLever != null)
            {
                leverStartLocalPosition = mountedLever.localPosition;
                leverStartLocalRotation = mountedLever.localRotation;
            }
        }

        public void ShowScreen(string text, bool error)
        {
            if (display != null)
                display.Show(text, error);
        }

        /// <param name="mounted">끼움이면 보인다</param>
        /// <param name="pullProgress">0 = 세움, 1 = 다 내림</param>
        /// <param name="popProgress">−1 = 튕겨 나가는 중 아님, 0 ~ 1 = 자리 → landing 포물선 (1 이면 숨김 — 그때부터 아이템)</param>
        /// <param name="popPeak">포물선 꼭대기 높이 (m)</param>
        public void ShowLever(bool mounted, float pullProgress, float popProgress, float popPeak)
        {
            if (mountedLever == null)
                return;

            bool popping = popProgress >= 0f && popProgress < 1f;
            bool visible = mounted || popping;

            if (leverShown != visible)
            {
                leverShown = visible;
                mountedLever.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                shownPull = -2f;
                shownPop = -2f;
                return;
            }

            if (popping)
            {
                if (popProgress == shownPop)
                    return;

                shownPop = popProgress;
                shownPull = -2f;

                // 자리에서 떨어질 자리까지 포물선, 눕혀지며 날아간다
                Vector3 from = mountedLever.parent != null ? mountedLever.parent.TransformPoint(leverStartLocalPosition) : mountedLever.position;
                Vector3 to = landing != null ? landing.position : from;
                mountedLever.position = Vector3.Lerp(from, to, popProgress) + Vector3.up * FireDoorRules.PopHeight(popProgress, popPeak);
                mountedLever.localRotation = leverStartLocalRotation * Quaternion.AngleAxis(90f * popProgress, Vector3.right);
                return;
            }

            if (pullProgress == shownPull)
                return;

            shownPull = pullProgress;
            shownPop = -2f;
            mountedLever.localPosition = leverStartLocalPosition;
            mountedLever.localRotation = leverStartLocalRotation * Quaternion.AngleAxis(pulledAngle * pullProgress, Vector3.right);
        }
    }
}
