using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 준비 상태와 버튼 UI를 표시합니다.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("버튼")]
    [SerializeField]
    private Button gameStartButton;

    [SerializeField]
    private Button readyButton;

    [Header("텍스트")]
    [SerializeField]
    private TMP_Text readyButtonText;

    [SerializeField]
    private TMP_Text statusText;

    private LobbyReadySystem readySystem;

    private void Awake()
    {
        gameStartButton.onClick.AddListener(
            OnGameStartClicked);

        readyButton.onClick.AddListener(
            OnReadyClicked);
    }

    private void OnDestroy()
    {
        gameStartButton.onClick.RemoveListener(
            OnGameStartClicked);

        readyButton.onClick.RemoveListener(
            OnReadyClicked);
    }

    private void Update()
    {
        if (readySystem == null)
            readySystem = FindFirstObjectByType<LobbyReadySystem>();

        if (readySystem == null || !readySystem.HasSpawned)
        {
            ShowLoadingState();
            return;
        }

        if (!readySystem.IsInitialized)
        {
            ShowLoadingState();
            return;
        }

        RefreshUI();
    }


    private void RefreshUI()
    {
        bool isHost =
            readySystem.IsLocalHost;

        gameStartButton.gameObject.SetActive(
            isHost);

        readyButton.gameObject.SetActive(
            !isHost);

        readySystem.GetGuestReadyStatus(
            out int readyCount,
            out int guestCount);

        if (isHost)
        {
            RefreshHostUI(
                readyCount,
                guestCount);
        }
        else
        {
            RefreshGuestUI(
                readyCount,
                guestCount);
        }
    }

    private void RefreshHostUI(
        int readyCount,
        int guestCount)
    {
        bool canStart =
            readySystem.CanStartGame();

        gameStartButton.interactable =
            canStart;

        if (readySystem.IsGameStarting)
        {
            statusText.text =
                "게임을 시작하는 중입니다.";

            return;
        }

        if (readySystem.PlayerCount <
            readySystem.MinimumPlayerCount)
        {
            statusText.text =
                $"플레이어 대기 중\n" +
                $"{readySystem.PlayerCount} / " +
                $"{readySystem.MinimumPlayerCount}";

            return;
        }

        if (canStart)
        {
            statusText.text =
                "모든 게스트가 준비했습니다.";

            return;
        }

        statusText.text =
            $"게스트 준비\n" +
            $"{readyCount} / {guestCount}";
    }

    private void RefreshGuestUI(
        int readyCount,
        int guestCount)
    {
        bool isReady =
            readySystem.IsLocalPlayerReady();

        readyButton.interactable =
            !readySystem.IsGameStarting;

        readyButtonText.text =
            isReady
                ? "준비 취소"
                : "준비";

        if (readySystem.IsGameStarting)
        {
            statusText.text =
                "게임을 시작하는 중입니다.";

            return;
        }

        string localState =
            isReady
                ? "준비 완료"
                : "준비 전";

        statusText.text =
            $"내 상태: {localState}\n" +
            $"게스트 준비: " +
            $"{readyCount} / {guestCount}";
    }

    private void ShowLoadingState()
    {
        gameStartButton.gameObject.SetActive(
            false);

        readyButton.gameObject.SetActive(
            false);

        statusText.text =
            "로비 상태를 동기화하는 중입니다.";
    }

    private void OnGameStartClicked()
    {
        readySystem?.TryStartGame();
    }

    private void OnReadyClicked()
    {
        readySystem?.ToggleLocalReady();
    }
}