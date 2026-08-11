using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class IQUI : MonoBehaviour
{
    private void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        UpdateUI(); // 每帧刷新，数值改动立刻同步
    }

    public Slider IqSlider;

    private void UpdateUI()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetLocalPlayer();
        if (player == null || IqSlider == null) return;

        // Legacy component name is kept so existing scene references survive.
        // The former IQ bar now represents the roguelike Scarlet resource.
        IqSlider.maxValue = player.MaxScarlet;
        IqSlider.value = player.CurrentScarlet;

    }
}
