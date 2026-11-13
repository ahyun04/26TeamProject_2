using System.Collections.Generic;
using UnityEngine;

namespace TrustNoOne.Gimmicks
{
    /// <summary>
    /// [역할] 살인마 암전 — 팔 전용 카메라(BlackoutScreenRig)가 그릴 팔을 팔 레이어(29)에 올려 둔다.
    ///  - 물건을 들었을 때 (팀원 1인칭 팔이 켜져 있음): 팀원 팔과 그 아래 든 물건 모델을 레이어 29 로 옮긴다.
    ///    오브젝트마다 원래 레이어를 기억하고, 암전 중에 새로 생긴 모델(물건 바꿈)도 매 프레임 옮긴다. 끝나면 되돌린다.
    ///  - 빈손일 때 (팀원 팔이 없거나 꺼져 있음): 같은 팔 프리팹으로 만든 내 복사본을 켠다 — 처음 필요할 때 한 번 만들고,
    ///    내 카메라의 자식 · 처음부터 레이어 29 · 팀원과 같은 방법(initialize)으로 내 캐릭터 자세를 따라 한다.
    /// [이유] 팀원 PlayerFirstPersonItemView 는 빈손이면 팔을 끄고, 팔을 만드는 함수는 비공개다 (조사 1-1).
    ///  팀원 상태(엄지척 · 미션 동작 · 든 물건)를 건드리지 않으려고 빈손 팔은 따로 만든 복사본으로 보인다 — 끝나면 복사본만 끄면 된다.
    /// [근거] 16b 명세 KB7 (빈손이어도 팔) · KB26 (든 물건도 보임) · KB30 (레이어 29) · KB34 · KB35 · KB40 (레이어 이름 경고)
    /// </summary>
    public class BlackoutArms : MonoBehaviour
    {
        /// <summary>팔 레이어 번호 — 9 번부터는 팀원이 다음에 붙이기 쉽고, 31 은 팀원 CCTV 의 미리보기 촬영용이라 피했다 (KB30)</summary>
        public const int ArmsLayer = 29;

        /// <summary>Tags and Layers 의 User Layer 29 에 넣을 이름 (공유 파일 TagManager.asset — 사용자가 입력)</summary>
        public const string ArmsLayerName = "KillerBlackoutArms";

        [Tooltip("빈손 팔 복사본에 쓸 팀원 1인칭 팔 프리팹 (0. Member/Dinosaur/Prefab/PlayerFirstPersonArms.prefab — 읽기만). 메뉴가 연결한다")]
        [SerializeField] private PlayerFirstPersonArmsComponent armsPrefab;

        [Tooltip("빈손 팔 복사본의 카메라 기준 위치. 기본 = 팀원 PlayerFirstPersonItemView.armsPosition. Play 중에 바꿔 보고 정한 숫자를 알려 주세요 (위험 1)")]
        [SerializeField] private Vector3 copyLocalPosition = new Vector3(-0.08f, -1.05f, 0.25f);

        [Tooltip("빈손 팔 복사본의 카메라 기준 각도 (도). 빈손 대기 자세는 팔을 내린 자세라 손이 화면 아래로 빠지면 기울여 맞춘다")]
        [SerializeField] private Vector3 copyLocalEuler = Vector3.zero;

        private PlayerFirstPersonArmsComponent copy;
        private PlayerFirstPersonArmsComponent teammateArms;
        private PlayerHealth myHealth;      // 복사본을 만든 내 플레이어 (팔이 스스로 숨는 조건을 같이 본다)
        private bool copyFailed;
        private readonly Dictionary<GameObject, int> savedLayers = new Dictionary<GameObject, int>();
        private readonly List<Transform> scratch = new List<Transform>();

        /// <summary>지금 보이는 팔 (디버그 패널 — KB40)</summary>
        public BlackoutArmsSource CurrentSource { get; private set; }

        private void Start()
        {
            // KB40: 레이어 이름 확인 — 이름이 없어도 번호로 동작하지만, 다른 이름이면 다른 용도와 섞일 수 있다
            string layerName = LayerMask.LayerToName(ArmsLayer);

            if (layerName == ArmsLayerName)
                return;

            if (string.IsNullOrEmpty(layerName))
                Debug.LogWarning($"[BlackoutArms] 레이어 {ArmsLayer} 에 이름이 없습니다 — 번호로 동작합니다. 메뉴 Edit > Project Settings > Tags and Layers 의 User Layer {ArmsLayer} 에 '{ArmsLayerName}' 을 넣어 주세요.");
            else
                Debug.LogWarning($"[BlackoutArms] 레이어 {ArmsLayer} 이름이 '{layerName}' 입니다 ('{ArmsLayerName}' 이어야 함) — 다른 용도로 쓰는 레이어라면 암전 팔 카메라에 그 오브젝트도 그려집니다.");
        }

        /// <summary>팀원 1인칭 팔이 지금 켜져 있는가 (= 물건 · 엄지척 · 미션 동작). 팔은 내 카메라 바로 아래에 만들어진다 (조사 1-1)</summary>
        public bool IsTeammateArmsShown(Camera cam)
        {
            PlayerFirstPersonArmsComponent arms = FindTeammateArms(cam);
            return arms != null && arms.gameObject.activeSelf;
        }

        /// <summary>규칙의 결과대로 팔을 준비한다. 꺼짐이거나 카메라가 없으면 원래대로 되돌린다.</summary>
        public void Apply(Camera cam, BlackoutVisualState state)
        {
            if (!state.Active || cam == null)
            {
                Restore();
                return;
            }

            if (state.Arms == BlackoutArmsSource.Teammate)
            {
                HideCopy();
                MoveTeammateArmsToLayer(cam);
                CurrentSource = BlackoutArmsSource.Teammate;
            }
            else
            {
                // 암전 중 물건을 버려 팀원 팔이 꺼졌으면 그 레이어부터 되돌리고 복사본으로
                RestoreTeammateLayers();
                CurrentSource = ShowCopy(cam) ? BlackoutArmsSource.Copy : BlackoutArmsSource.None;
            }
        }

        /// <summary>KB35: 복사본을 끄고 팀원 팔 레이어를 되돌린다</summary>
        public void Restore()
        {
            HideCopy();
            RestoreTeammateLayers();
            CurrentSource = BlackoutArmsSource.None;
        }

        private void OnDisable()
        {
            Restore();
        }

        private void OnDestroy()
        {
            Restore();

            if (copy != null)
                Destroy(copy.gameObject);
        }

        private PlayerFirstPersonArmsComponent FindTeammateArms(Camera cam)
        {
            if (cam == null)
                return null;

            // 찾아 둔 것이 그대로 내 카메라 아래에 있으면 다시 찾지 않는다
            if (teammateArms != null && teammateArms.transform.parent == cam.transform)
                return teammateArms;

            teammateArms = null;

            foreach (PlayerFirstPersonArmsComponent arms in cam.GetComponentsInChildren<PlayerFirstPersonArmsComponent>(true))
            {
                if (arms != copy)
                {
                    teammateArms = arms;
                    break;
                }
            }

            return teammateArms;
        }

        private void MoveTeammateArmsToLayer(Camera cam)
        {
            PlayerFirstPersonArmsComponent arms = FindTeammateArms(cam);

            if (arms == null)
                return;

            scratch.Clear();
            arms.GetComponentsInChildren(true, scratch);

            foreach (Transform t in scratch)
            {
                GameObject go = t.gameObject;

                if (go.layer == ArmsLayer)
                    continue;

                if (!savedLayers.ContainsKey(go))
                    savedLayers.Add(go, go.layer);

                go.layer = ArmsLayer;
            }
        }

        private void RestoreTeammateLayers()
        {
            if (savedLayers.Count == 0)
                return;

            foreach (KeyValuePair<GameObject, int> pair in savedLayers)
            {
                // 암전 중에 버리거나 바꾼 물건 모델은 이미 지워졌을 수 있다
                if (pair.Key != null)
                    pair.Key.layer = pair.Value;
            }

            savedLayers.Clear();
        }

        /// <summary>빈손 팔 복사본을 켠다. 만들 수 없으면(프리팹 없음 · 팔 참조 오류) 거짓 — 한 번만 알린다</summary>
        private bool ShowCopy(Camera cam)
        {
            // 카메라가 바뀌었으면 (다시 스폰) 새 카메라 아래에 다시 만든다
            if (copy != null && copy.transform.parent != cam.transform)
            {
                Destroy(copy.gameObject);
                copy = null;
            }

            if (copy == null && !CreateCopy(cam))
                return false;

            // 팔 컴포넌트는 죽음 · 탈출 · 게임 끝(canActWithoutCctv 거짓)이면 스스로 숨는다 (실행 순서 -25 라 이 코드보다 먼저 돈다).
            // 그때 다시 켜면 매 프레임 켜짐 · 꺼짐이 엇갈리므로 켜지 않는다
            if (myHealth != null && !myHealth.canActWithoutCctv)
            {
                HideCopy();
                return false;
            }

            // Play 중 Inspector 에서 바꾼 값을 바로 보이게 매 프레임 넣는다 (위험 1 — 숫자를 받아 상수로 옮김)
            copy.transform.localPosition = copyLocalPosition;
            copy.transform.localRotation = Quaternion.Euler(copyLocalEuler);

            if (!copy.gameObject.activeSelf)
                copy.show();

            return true;
        }

        private bool CreateCopy(Camera cam)
        {
            if (copyFailed)
                return false;

            if (armsPrefab == null)
            {
                Debug.LogWarning("[BlackoutArms] 팔 프리팹 칸이 비어 빈손 팔을 보일 수 없습니다 — 메뉴 SuHan/Setup Solo Mission Test 를 다시 실행하세요.");
                copyFailed = true;
                return false;
            }

            myHealth = cam.GetComponentInParent<PlayerHealth>();
            Animator animator = FindMyAnimator(cam, myHealth);
            copy = Instantiate(armsPrefab, cam.transform);
            copy.name = "KillerBlackoutArmsCopy";
            SetLayerRecursively(copy.transform, ArmsLayer);

            if (animator == null || !copy.initialize(animator))
            {
                Debug.LogError("[BlackoutArms] 빈손 팔 복사본을 내 캐릭터 자세에 연결하지 못했습니다 (Animator · 팔 프리팹 참조 확인).");
                Destroy(copy.gameObject);
                copy = null;
                copyFailed = true;
                return false;
            }

            copy.hide();
            return true;
        }

        /// <summary>내 캐릭터 Animator — 팀원 PlayerFirstPersonItemView 와 같이 플레이어 아래에서 찾되, 카메라 아래(팔 · 복사본)는 뺀다</summary>
        private static Animator FindMyAnimator(Camera cam, PlayerHealth health)
        {
            Transform root = health != null ? health.transform : cam.transform.root;

            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.transform.IsChildOf(cam.transform))
                    return animator;
            }

            return null;
        }

        private void HideCopy()
        {
            if (copy != null && copy.gameObject.activeSelf)
                copy.hide();
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
