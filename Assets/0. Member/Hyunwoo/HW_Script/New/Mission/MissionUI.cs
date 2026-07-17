using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 역할과 미션 목록을 표시합니다.
/// </summary>
public class MissionUI : MonoBehaviour
{
    [Header("역할 UI")]
    [SerializeField] private TMP_Text roleText;

    [Header("미션 UI")]
    [SerializeField] private Transform missionContent;
    [SerializeField] private MissionItemUI missionItemPrefab;

    private GameSetupSystem gameSetupSystem;
    private MissionSystem missionSystem;

    private PlayerRole localRole;
    private bool hasRole;
    private int lastRevision = -1;

    private void Start()
    {
        roleText.text = "역할 배정 중...";
    }

    private void Update()
    {
        if (gameSetupSystem == null)
            gameSetupSystem = FindFirstObjectByType<GameSetupSystem>();

        if (missionSystem == null)
            missionSystem = FindFirstObjectByType<MissionSystem>();

        if (gameSetupSystem == null || missionSystem == null)
            return;

        if (!gameSetupSystem.HasSpawned || !missionSystem.HasSpawned)
            return;

        if (!gameSetupSystem.IsInitialized || !missionSystem.IsInitialized)
            return;

        if (!gameSetupSystem.TryGetLocalRole(out PlayerRole role))
            return;

        if (!hasRole || localRole != role)
        {
            localRole = role;
            hasRole = true;
            lastRevision = -1;

            RefreshRole();
        }

        if (lastRevision == missionSystem.Revision)
            return;

        RefreshMissions();
        lastRevision = missionSystem.Revision;
    }

    private void RefreshRole()
    {
        roleText.text = localRole == PlayerRole.Citizen ? "역할: 시민" : "역할: 살인자";
    }

    private void RefreshMissions()
    {
        ClearMissionItems();

        PlayerRef localPlayer = gameSetupSystem.Runner.LocalPlayer;

        for (int i = 0; i < missionSystem.MissionCount; i++)
        {
            MissionRuntimeState state = missionSystem.Missions.Get(i);

            if (!ShouldShowMission(state, localPlayer))
                continue;

            if (!missionSystem.TryGetMissionData(state.MissionId, out MissionData missionData))
            {
                Debug.LogWarning($"Mission ID {state.MissionId}에 해당하는 MissionData가 없습니다.");
                continue;
            }

            MissionItemUI missionItem = Instantiate(missionItemPrefab, missionContent);
            missionItem.SetMission(missionData, state, localRole);
        }
    }

    private bool ShouldShowMission(MissionRuntimeState state, PlayerRef localPlayer)
    {
        if (state.MissionKind == MissionKind.Common)
            return true;

        return state.Owner == localPlayer;
    }

    private void ClearMissionItems()
    {
        for (int i = missionContent.childCount - 1; i >= 0; i--)
            Destroy(missionContent.GetChild(i).gameObject);
    }
}