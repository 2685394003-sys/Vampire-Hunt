using UnityEngine;
using UnityEngine.UI;

public class StaminaUI : MonoBehaviour
{
    [SerializeField] private Slider staminaSlider;

    private void Update()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetLocalPlayer();
        if (player == null || staminaSlider == null)
            return;

        staminaSlider.maxValue = player.MaxStamina;
        staminaSlider.value = player.CurrentStamina;
    }
}
