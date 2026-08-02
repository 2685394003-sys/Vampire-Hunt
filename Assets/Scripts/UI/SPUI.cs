using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SPUI : MonoBehaviour
{
    private void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        UpdateUI(); // 每帧刷新，数值改动立刻同步
    }

    public Slider SpSlider;

    private void UpdateUI()
    {
        SpSlider.maxValue = StatsManager.Instance.maxStamina;
        SpSlider.value = StatsManager.Instance.currentStamina;

    }
}
