using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHudUIComponent : MonoBehaviour
{
    [SerializeField] private Image healthFill; //체력 게이지
    [SerializeField] private TMP_Text healthText; //현재 체력과 최대 체력
    [SerializeField] private Image staminaFill; //스태미나 게이지
    [SerializeField] private TMP_Text staminaText; //현재 스태미나와 최대 스태미나
    [SerializeField] private PlayerDamageFeedbackComponent damageFeedback; //피격 화면 연출
    [SerializeField] private CanvasGroup rolePanel; //검은 배경 역할 안내
    [SerializeField] private TMP_Text roleText; //본인의 역할
    [SerializeField, Min(0f)] private float roleHoldSeconds = 1.6f; //역할 안내 유지 시간
    [SerializeField, Min(0.01f)] private float roleFadeSeconds = 1.2f; //역할 안내 사라지는 시간

    private PlayerHealth health; //본인의 체력
    private PlayerStamina stamina; //본인의 스태미나
    private bool subscribed; //중복 이벤트 구독 방지
    private Coroutine roleRoutine; //현재 역할 안내 연출

    internal void initialize(PlayerHealth playerHealth, PlayerStamina playerStamina) //현재 값 표시와 변경 이벤트 연결
    {
        unsubscribe();
        health = playerHealth;
        stamina = playerStamina;
        subscribe();
        if (health != null) refreshHealth(health.CurrentHealth, health.MaxHealth);
        if (stamina != null) refreshStamina(stamina.CurrentStamina, stamina.MaxStamina);
        hideRole();
    }

    private void OnEnable() //표시 재활성화 시 이벤트와 값 복원
    {
        subscribe();
        if (health != null) refreshHealth(health.CurrentHealth, health.MaxHealth);
        if (stamina != null) refreshStamina(stamina.CurrentStamina, stamina.MaxStamina);
    }

    private void OnDisable() //표시 중단 시 구독과 안내 정리
    {
        unsubscribe();
        hideRole();
    }

    private void subscribe() //본인 능력치 변경 구독
    {
        if (subscribed) return;
        if (health != null) health.HealthChanged += refreshHealth;
        if (stamina != null) stamina.StaminaChanged += refreshStamina;
        subscribed = health != null || stamina != null;
    }

    private void unsubscribe() //능력치 변경 구독 해제
    {
        if (!subscribed) return;
        if (health != null) health.HealthChanged -= refreshHealth;
        if (stamina != null) stamina.StaminaChanged -= refreshStamina;
        subscribed = false;
    }

    private void refreshHealth(float current, float maximum) //체력 수치와 게이지 갱신
    {
        if (healthFill != null)
        {
            healthFill.fillAmount = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            healthFill.rectTransform.anchorMax = new Vector2(healthFill.fillAmount, 1f);
        }
        if (healthText != null) healthText.text = $"체력  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
    }

    private void refreshStamina(float current, float maximum) //스태미나 수치와 게이지 갱신
    {
        if (staminaFill != null)
        {
            staminaFill.fillAmount = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            staminaFill.rectTransform.anchorMax = new Vector2(staminaFill.fillAmount, 1f);
        }
        if (staminaText != null) staminaText.text = $"스태미나  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
    }

    internal void showDamage() //피격 연출 시작
    {
        if (damageFeedback != null) damageFeedback.showDamage();
    }

    internal Quaternion getDamageShakeRotation() //카메라가 적용할 피격 회전
    {
        return damageFeedback != null ? damageFeedback.getShakeRotation() : Quaternion.identity;
    }

    internal void showRole(PlayerRole role) //검은 배경에 흰색 역할 표시
    {
        if (rolePanel == null || roleText == null || !isActiveAndEnabled) return;
        hideRole();
        roleText.text = role == PlayerRole.Killer ? "당신은 살인마입니다" : "당신은 시민입니다";
        rolePanel.gameObject.SetActive(true);
        rolePanel.alpha = 1f;
        roleRoutine = StartCoroutine(fadeRole());
    }

    private IEnumerator fadeRole() //역할 안내를 잠시 유지한 뒤 서서히 제거
    {
        float elapsed = 0f;
        while (elapsed < roleHoldSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < roleFadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            rolePanel.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / roleFadeSeconds));
            yield return null;
        }
        rolePanel.alpha = 0f;
        rolePanel.gameObject.SetActive(false);
        roleRoutine = null;
    }

    internal void hideRole() //게임 종료와 표시 해제 시 안내 제거
    {
        if (roleRoutine != null) StopCoroutine(roleRoutine);
        roleRoutine = null;
        if (rolePanel == null) return;
        rolePanel.alpha = 0f;
        rolePanel.gameObject.SetActive(false);
    }
}
