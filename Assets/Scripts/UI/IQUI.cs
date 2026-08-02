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
        IqSlider.maxValue = StatsManager.Instance.maxIntelligence;
        IqSlider.value = StatsManager.Instance.currentIntelligence;

    }
}
