using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SFXVolumeSync : MonoBehaviour
{
    public Slider volumeSlider;

    void Start()
    {
        // 场景未挂载 SFXManager（或无滑条）时直接跳过，不报错
        if (SFXManager.Instance == null || volumeSlider == null)
            return;

        // 初始化滑条数值和当前音量同步
        volumeSlider.value = SFXManager.Instance.attackVolume;
        // 滑条变化自动调用音量函数
        volumeSlider.onValueChanged.AddListener(SFXManager.Instance.SetSFXVolume);
    }
}
