using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI : MonoBehaviour
{
    private void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        UpdateUI(); // 每帧刷新，数值改动立刻同步
    }

    public Slider HpSlider;

    private void UpdateUI()
    {
        HpSlider.maxValue = StatsManager.Instance.maxHealth;
        HpSlider.value = StatsManager.Instance.currentHealth;

    }
}
