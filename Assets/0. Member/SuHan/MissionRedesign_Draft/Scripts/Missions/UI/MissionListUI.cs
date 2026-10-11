using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 메인 게임 화면 왼쪽 미션 목록: 단체 미션(모두)과 내 개인 미션을 한 줄씩 보여 준다 (6c). 옛 MissionUI 를 대신한다.
    /// [동작] 미션 관리자의 화면용 정보(Client — 이 PC 가 봐도 되는 것만)가 바뀌었다는 알림(Changed)마다 줄을 다시 만든다.
    ///  어떤 줄을 어느 칸에 둘지는 순수 규칙 MissionListRules. 줄 글자는 기존 줄 프리팹(MissionUIItem.SetData — "[단체] 이름 ( p / r )")에 맡기고,
    ///  끝난 미션만 줄 끝에 초록 "완료"를 이어 붙인다 (개인 미션은 끝나면 장치가 원래대로 돌아가 숫자만으로는 알아보기 어렵다).
    /// [미션 관리자를 기다리는 이유] 씬 오브젝트라 러너가 등록한 뒤에야 Client 가 생긴다 → 생길 때까지 매 프레임 확인하고 한 번만 구독한다.
    /// [팀원 코드] MissionUIItem 은 부르기만 하고 고치지 않는다. 옛 MissionUI 는 메인 씬 도구가 이 컴포넌트로 바꿔 끼운다.
    /// [근거] 6c 결정 — 사용자 결정 A (질문 1: 옛 형식 + 완료 표시, 질문 2: 새 컴포넌트로 바꿔 끼우기)
    /// </summary>
    public class MissionListUI : MonoBehaviour
    {
        // 끝난 미션 줄 끝에 붙이는 표시 (줄 프리팹 글자는 리치 텍스트를 쓴다)
        private const string CompletedSuffix = "  <color=#7CFC7C>완료</color>";

        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private MissionManager manager;

        [Tooltip("한 줄 프리팹 (기존 MissionUIItem_Text)")]
        [SerializeField] private MissionUIItem itemPrefab;

        [Tooltip("단체 미션 줄이 들어갈 칸")]
        [SerializeField] private Transform sharedRoot;

        [Tooltip("내 개인 미션 줄이 들어갈 칸")]
        [SerializeField] private Transform personalRoot;

        private MissionClientState client;
        private readonly List<MissionListLine> lines = new List<MissionListLine>();

        private void Update()
        {
            if (client != null)
                return;

            if (manager == null)
                manager = FindFirstObjectByType<MissionManager>();

            if (manager == null || manager.Client == null)
                return;

            client = manager.Client;
            client.Changed += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (client != null)
                client.Changed -= Rebuild;
        }

        private void Rebuild()
        {
            MissionListRules.Build(client.Team, client.Mine, lines);

            Clear(sharedRoot);
            Clear(personalRoot);

            foreach (MissionListLine line in lines)
            {
                Transform parent = line.Personal ? personalRoot : sharedRoot;

                if (itemPrefab == null || parent == null)
                    continue;

                MissionUIItem item = Instantiate(itemPrefab, parent);
                item.SetData(line.Label, line.Name, line.Progress, line.Required);

                if (!line.Completed)
                    continue;

                TMP_Text text = item.GetComponentInChildren<TMP_Text>();

                if (text != null)
                    text.text += CompletedSuffix;
            }
        }

        private static void Clear(Transform parent)
        {
            if (parent == null)
                return;

            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
