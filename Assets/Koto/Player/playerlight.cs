using UnityEngine;
using UnityEngine.UI;

public class playerlight : MonoBehaviour
{
    [Header("光源與能量顯示")]
    [InspectorName("玩家光源")]
    [SerializeField] private Light playerLight;
    [InspectorName("能量 Slider")]
    [SerializeField] private Slider energySlider;

    [Header("能量設定")]
    [InspectorName("初始能量")]
    [SerializeField, Min(0f)] private float initialEnergy = 100f;
    [InspectorName("每秒消耗能量")]
    [SerializeField, Min(0f)] private float energyConsumptionPerSecond = 10f;

    [Header("攻擊回復能量")]
    [InspectorName("命中敵人回復能量")]
    [Tooltip("每成功命中一名敵人時回復的能量。")]
    [SerializeField, Min(0f)] private float energyRestoredOnHit = 5f;
    [InspectorName("擊敗敵人額外回復能量")]
    [Tooltip("實體血量歸零或擊敗本體轉為 NPC 時，額外回復的能量；會與命中回復相加。")]
    [SerializeField, Min(0f)] private float energyRestoredOnDefeat = 30f;

    private float currentEnergy;
    private bool isLightOn;

    private void Awake()
    {
        currentEnergy = Mathf.Max(0f, initialEnergy);
        SetLight(false);

        if (energySlider != null)
        {
            energySlider.interactable = false;
            energySlider.minValue = 0f;
            energySlider.maxValue = currentEnergy;
            energySlider.wholeNumbers = false;
            energySlider.SetValueWithoutNotify(currentEnergy);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            SetLight(!isLightOn && currentEnergy > 0f);
        }

        if (!isLightOn) return;

        currentEnergy = Mathf.Max(0f, currentEnergy - Mathf.Max(0f, energyConsumptionPerSecond) * Time.deltaTime);
        if (energySlider != null)
        {
            energySlider.SetValueWithoutNotify(currentEnergy);
        }

        if (currentEnergy <= 0f)
        {
            SetLight(false);
        }
    }

    private void OnDisable()
    {
        SetLight(false);
    }

    /// <summary>由玩家攻擊在確實命中後呼叫；擊敗時額外回復。</summary>
    public void RestoreEnergyFromAttack(bool defeatedEnemy)
    {
        float amount = Mathf.Max(0f, energyRestoredOnHit);
        if (defeatedEnemy) amount += Mathf.Max(0f, energyRestoredOnDefeat);
        currentEnergy = Mathf.Clamp(currentEnergy + amount, 0f, Mathf.Max(0f, initialEnergy));
        if (energySlider != null)
        {
            energySlider.SetValueWithoutNotify(currentEnergy);
        }
    }

    private void SetLight(bool enabled)
    {
        isLightOn = enabled && playerLight != null;
        if (playerLight != null)
        {
            playerLight.enabled = isLightOn;
        }
    }
}
