using Fusion;
using UnityEngine;

public class MissionUI : MonoBehaviour
{
    [SerializeField] private MissionSystem missionSystem;
    [SerializeField] private MissionUIItem itemPrefab;
    [SerializeField] private Transform sharedRoot;
    [SerializeField] private Transform personalRoot;

    private int lastStateHash = int.MinValue;
    private bool wasInitialized;

    private void Update()
    {
        if (missionSystem == null)
            return;

        if (missionSystem.Object == null || !missionSystem.Object.IsValid)
            return;

        if (!missionSystem.Initialized)
            return;

        int currentHash = BuildStateHash();

        if (currentHash == lastStateHash)
            return;

        lastStateHash = currentHash;
        RefreshUI();
    }

    private int BuildStateHash()
    {
        int hash = 17;

        hash = hash * 31 + missionSystem.MissionCount;

        for (int i = 0; i < missionSystem.MissionCount; i++)
        {
            MissionState state = missionSystem.Missions[i];

            hash = hash * 31 + state.MissionId;
            hash = hash * 31 + state.Owner.GetHashCode();
            hash = hash * 31 + state.MissionType.GetHashCode();
            hash = hash * 31 + state.Progress;
            hash = hash * 31 + state.RequiredCount;
            hash = hash * 31 + state.Status.GetHashCode();
        }

        return hash;
    }

    private void RefreshUI()
    {
        ClearAll();

        PlayerRef localPlayer = missionSystem.Runner.LocalPlayer;

        for (int i = 0; i < missionSystem.MissionCount; i++)
        {
            MissionState state = missionSystem.Missions[i];
            MissionData data = missionSystem.GetMissionData(state.MissionId);

            if (data == null)
                continue;

            if (state.MissionType == MissionType.Shared)
            {
                CreateItem(sharedRoot, "단체", data.MissionName, state.Progress, state.RequiredCount);
                continue;
            }

            if (state.MissionType == MissionType.Personal && state.Owner == localPlayer)
            {
                CreateItem(personalRoot, "개인", data.MissionName, state.Progress, state.RequiredCount);
            }
        }
    }

    private void CreateItem(Transform parent, string typeLabel, string missionName, int progress, int requiredCount)
    {
        if (itemPrefab == null || parent == null)
            return;

        MissionUIItem item = Instantiate(itemPrefab, parent);
        item.SetData(typeLabel, missionName, progress, requiredCount);
    }

    private void ClearAll()
    {
        ClearChildren(sharedRoot);
        ClearChildren(personalRoot);
    }

    private void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}