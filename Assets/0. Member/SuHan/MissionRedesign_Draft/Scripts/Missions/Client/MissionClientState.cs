using System;
using System.Collections.Generic;
using Fusion;

namespace TrustNoOne.Missions
{
    /// <summary>UI 가 읽는 목표 하나의 스냅샷 (읽기 전용 값).</summary>
    public readonly struct ObjectiveView
    {
        public readonly int Slot;
        public readonly MissionDefinition Definition;

        /// <summary>단체 미션이거나 "내 것"이면 PlayerRef.None. 게임 종료 후 공개된 남의 목표일 때만 그 주인.</summary>
        public readonly PlayerRef Owner;

        public readonly int Progress;
        public readonly int Required;
        public readonly ObjectiveStatus Status;

        public ObjectiveView(
            int slot, MissionDefinition definition, PlayerRef owner, int progress, int required, ObjectiveStatus status)
        {
            Slot = slot;
            Definition = definition;
            Owner = owner;
            Progress = progress;
            Required = required;
            Status = status;
        }
    }

    /// <summary>
    /// [역할] 이 PC(로컬 플레이어)가 "화면에 그려도 되는" 미션 정보의 로컬 캐시. UI 는 이 클래스만 읽는다.
    ///
    /// [기획서 근거] 클라이언트 역할: 미션 UI 표시, 개인 미션·행동 목표 표시, 단체 미션 진행도 표시,
    ///  획득한 단서 표시, 게임 종료 시 전체 미션 결과 표시. ("클라이언트는 표시만 한다")
    ///
    /// [데이터 출처 — 비공개 원칙]
    ///  - Team / 진행바 / 탈출 해금 : 호스트가 [Networked] 로 전원에게 복제한 공개 데이터
    ///  - Mine                      : 호스트가 "나에게만" RPC 로 보낸 개인 데이터 (개인 미션·행동 목표·살인마 미션)
    ///  - Revealed                  : 게임 종료 후 호스트가 방송한 전체 공개 데이터
    ///  호스트 프로세스도 이 클래스를 통해서만 자기 UI 를 그리므로, 호스트 플레이어의 화면에도 남의 개인 미션이 나오지 않는다.
    ///
    /// [Apply* 메서드가 internal 인 이유] 데이터를 밀어 넣는 쪽은 MissionManager 하나뿐이다.
    ///  UI 코드가 실수로 상태를 바꾸지 못하게 막는다.
    /// </summary>
    public class MissionClientState
    {
        private readonly MissionPool pool;

        private readonly List<ObjectiveView> team = new List<ObjectiveView>();
        private readonly List<ObjectiveView> mine = new List<ObjectiveView>();
        private readonly List<ObjectiveView> revealed = new List<ObjectiveView>();

        public MissionClientState(MissionPool pool, PlayerRef localPlayer)
        {
            this.pool = pool;
            LocalPlayer = localPlayer;
        }

        public PlayerRef LocalPlayer { get; }

        /// <summary>내가 단체 미션에 참여하는가 (시민이면 true, 살인마면 false).</summary>
        public bool IsTeamParticipant { get; private set; }

        public IReadOnlyList<ObjectiveView> Team => team;
        public IReadOnlyList<ObjectiveView> Mine => mine;
        public IReadOnlyList<ObjectiveView> Revealed => revealed;

        public int CitizenProgressCurrent { get; private set; }
        public int CitizenProgressRequired { get; private set; }
        public bool TeamCompleted { get; private set; }
        public bool EscapeUnlocked { get; private set; }

        /// <summary>시민 전체 합산 진행도 0~1 (HUD 진행바).</summary>
        public float CitizenProgress01 =>
            CitizenProgressRequired <= 0 ? 0f : Math.Min(1f, (float)CitizenProgressCurrent / CitizenProgressRequired);

        /// <summary>공개/개인 상태가 바뀔 때마다 발생 (HUD/지도 Panel 갱신용).</summary>
        public event Action Changed;

        /// <summary>게임 종료 후 전체 공개 데이터가 도착/변경될 때 발생 (결과 화면용).</summary>
        public event Action RevealedChanged;

        /// <summary>
        /// 미션 오브젝트의 "연출용" 상호작용 표시 여부: 이 행동이 내 목표를 진행시키는가?
        /// 기획서 UI 목업의 "해당 미션이 없으면 표시하지 않음"에 대응한다.
        /// ⚠ 이건 연출(콜라이더/프롬프트 끄기)일 뿐이고, 실제 허용 여부는 호스트가 다시 검사한다 (우회 방지).
        /// </summary>
        public bool IsEventRelevant(MissionEventType eventType)
        {
            foreach (ObjectiveView view in mine)
            {
                if (IsProgressable(view, eventType))
                    return true;
            }

            if (IsTeamParticipant)
            {
                foreach (ObjectiveView view in team)
                {
                    if (IsProgressable(view, eventType))
                        return true;
                }
            }

            return false;
        }

        private static bool IsProgressable(ObjectiveView view, MissionEventType eventType)
        {
            return view.Status == ObjectiveStatus.InProgress
                && view.Definition != null
                && view.Definition.Kind != ObjectiveKind.Avoid
                && view.Definition.Trigger == eventType;
        }

        // ───────────── MissionManager 전용 입력 ─────────────

        /// <summary>공개 데이터([Networked]) 반영. 값이 같으면 아무것도 하지 않는다 (매 프레임 호출돼도 안전).</summary>
        internal void ApplyPublic(
            TeamMissionState[] states, int count, int progressCurrent, int progressRequired,
            bool teamCompleted, bool escapeUnlocked)
        {
            bool changed = false;

            if (count != team.Count)
            {
                changed = true;
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    ObjectiveView existing = team[i];

                    if (existing.Definition != pool.Get(states[i].DefIndex) ||
                        existing.Progress != states[i].Progress ||
                        existing.Required != states[i].Required ||
                        existing.Status != states[i].Status)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                team.Clear();

                for (int i = 0; i < count; i++)
                {
                    team.Add(new ObjectiveView(
                        i, pool.Get(states[i].DefIndex), PlayerRef.None,
                        states[i].Progress, states[i].Required, states[i].Status));
                }
            }

            if (CitizenProgressCurrent != progressCurrent || CitizenProgressRequired != progressRequired ||
                TeamCompleted != teamCompleted || EscapeUnlocked != escapeUnlocked)
            {
                CitizenProgressCurrent = progressCurrent;
                CitizenProgressRequired = progressRequired;
                TeamCompleted = teamCompleted;
                EscapeUnlocked = escapeUnlocked;
                changed = true;
            }

            if (changed)
                Changed?.Invoke();
        }

        /// <summary>내 개인 목표 하나 반영 (슬롯 기준으로 추가/갱신).</summary>
        internal void ApplyPrivate(int slot, int defIndex, int progress, int required, ObjectiveStatus status)
        {
            ObjectiveView view = new ObjectiveView(slot, pool.Get(defIndex), PlayerRef.None, progress, required, status);

            int existingIndex = mine.FindIndex(v => v.Slot == slot);

            if (existingIndex >= 0)
            {
                ObjectiveView existing = mine[existingIndex];

                if (existing.Definition == view.Definition && existing.Progress == progress &&
                    existing.Required == required && existing.Status == status)
                {
                    return;
                }

                mine[existingIndex] = view;
            }
            else
            {
                // 슬롯 순서(= 생성 순서)를 유지해서 UI 목록 순서가 흔들리지 않게 한다.
                int insertAt = mine.FindIndex(v => v.Slot > slot);

                if (insertAt < 0)
                    mine.Add(view);
                else
                    mine.Insert(insertAt, view);
            }

            Changed?.Invoke();
        }

        internal void ApplyParticipation(bool isTeamParticipant)
        {
            if (IsTeamParticipant == isTeamParticipant)
                return;

            IsTeamParticipant = isTeamParticipant;
            Changed?.Invoke();
        }

        /// <summary>게임 종료 후 공개된 목표 하나 반영 ((주인, 슬롯) 기준).</summary>
        internal void ApplyReveal(
            PlayerRef owner, int slot, int defIndex, int progress, int required, ObjectiveStatus status)
        {
            ObjectiveView view = new ObjectiveView(slot, pool.Get(defIndex), owner, progress, required, status);

            int existingIndex = revealed.FindIndex(v => v.Owner == owner && v.Slot == slot);

            if (existingIndex >= 0)
                revealed[existingIndex] = view;
            else
                revealed.Add(view);

            RevealedChanged?.Invoke();
        }

        /// <summary>새 판 시작 등으로 전부 비운다.</summary>
        internal void ApplyClear()
        {
            team.Clear();
            mine.Clear();
            revealed.Clear();
            IsTeamParticipant = false;
            CitizenProgressCurrent = 0;
            CitizenProgressRequired = 0;
            TeamCompleted = false;
            EscapeUnlocked = false;

            Changed?.Invoke();
            RevealedChanged?.Invoke();
        }
    }
}
