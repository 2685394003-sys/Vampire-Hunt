using UnityEngine;
using UnityEngine.UI;

public class StaminaUI : MonoBehaviour
{
    [SerializeField] private Slider staminaSlider;
    [SerializeField] private PlayerDash playerDash;

    private void Update()
    {
        if (playerDash == null || staminaSlider == null || StatsManager.Instance == null)
            return;

        staminaSlider.maxValue = StatsManager.Instance.maxStamina;
    }
}
