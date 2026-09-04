using System.Collections.Generic;
using Fusion;
using LockdownProtocol.Lobby;
using TMPro;
using UnityEngine;

public class GameResultUI : MonoBehaviour
{
    [SerializeField] private GameEndSystem gameEndSystem;
    [SerializeField] private RoleAssignment roleAssignment;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Transform playerResultList;
    [SerializeField] private PlayerResultEntryUI playerResultEntryPrefab;
    [SerializeField] private string winText = "Win";
    [SerializeField] private string loseText = "Lose";

    private readonly List<PlayerResultEntryUI> spawnedEntries = new();
    private readonly List<PlayerRef> resultPlayers = new();

    private void Awake()
    {
        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (gameEndSystem == null)
            gameEndSystem = FindFirstObjectByType<GameEndSystem>();

        if (roleAssignment == null)
            roleAssignment = FindFirstObjectByType<RoleAssignment>();

        if (gameEndSystem == null)
            return;

        gameEndSystem.OnGameEnded += HandleGameEnded;

        if (gameEndSystem.Object != null && gameEndSystem.Object.IsValid && gameEndSystem.IsGameEnded)
            HandleGameEnded();
    }

    private void OnDisable()
    {
        if (gameEndSystem != null)
            gameEndSystem.OnGameEnded -= HandleGameEnded;
    }

    private void HandleGameEnded()
    {
        if (gameEndSystem == null || resultPanel == null || resultText == null)
            return;

        if (gameEndSystem.Object == null || !gameEndSystem.Object.IsValid)
            return;

        PlayerRef localPlayer = gameEndSystem.Runner.LocalPlayer;
        PlayerResult result = gameEndSystem.GetPlayerResult(localPlayer);

        if (result == PlayerResult.None)
            return;

        resultText.text = result == PlayerResult.Win ? winText : loseText;
        RefreshPlayerResults();
        resultPanel.SetActive(true);
    }

    private void RefreshPlayerResults()
    {
        if (playerResultList == null || playerResultEntryPrefab == null || roleAssignment == null)
            return;

        resultPlayers.Clear();

        foreach (KeyValuePair<PlayerRef, PlayerResult> entry in gameEndSystem.PlayerResults)
            resultPlayers.Add(entry.Key);

        resultPlayers.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
        EnsureEntryCount(resultPlayers.Count);

        for (int i = 0; i < resultPlayers.Count; i++)
        {
            PlayerRef player = resultPlayers[i];
            string roleName = roleAssignment.TryGetRole(player, out PlayerRole role)
                ? role.ToString()
                : string.Empty;

            spawnedEntries[i].gameObject.SetActive(true);
            spawnedEntries[i].SetData(
                GetPlayerName(player),
                roleName,
                gameEndSystem.GetPlayerResult(player).ToString());
        }

        for (int i = resultPlayers.Count; i < spawnedEntries.Count; i++)
            spawnedEntries[i].gameObject.SetActive(false);
    }

    private void EnsureEntryCount(int count)
    {
        while (spawnedEntries.Count < count)
        {
            PlayerResultEntryUI entry = Instantiate(playerResultEntryPrefab, playerResultList);
            spawnedEntries.Add(entry);
        }
    }

    private string GetPlayerName(PlayerRef player)
    {
        NetworkObject playerObject = gameEndSystem.Runner.GetPlayerObject(player);
        LobbyPlayerController playerController = playerObject != null
            ? playerObject.GetComponent<LobbyPlayerController>()
            : null;

        if (playerController != null)
        {
            string nickname = playerController.Nickname.ToString();
            if (!string.IsNullOrWhiteSpace(nickname))
                return nickname;
        }

        return player.ToString();
    }
}
