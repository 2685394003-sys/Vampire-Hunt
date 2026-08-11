using UnityEngine;
using UnityEngine.UI;

public class SPUI : MonoBehaviour
{
    public Slider SpSlider;

    private void Update()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetLocalPlayer();
        if (player == null || SpSlider == null)
            return;

        SpSlider.maxValue = player.MaxStamina;
        SpSlider.value = player.CurrentStamina;
    }
}
