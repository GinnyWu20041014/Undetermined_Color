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

    private void SetLight(bool enabled)
    {
        isLightOn = enabled && playerLight != null;
        if (playerLight != null)
        {
            playerLight.enabled = isLightOn;
        }
    }
}
