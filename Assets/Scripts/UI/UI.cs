using UnityEngine;
using UnityEngine.UI;

public class UI : MonoBehaviour
{
    public Slider HpSlider;

    private void Update()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetLocalPlayer();
        if (player == null || HpSlider == null)
            return;

        HpSlider.maxValue = player.MaxHealth;
        HpSlider.value = player.CurrentHealth;
    }
}
