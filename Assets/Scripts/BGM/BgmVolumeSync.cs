using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VolumeSlider : MonoBehaviour
{
    public Slider volumeSlider;

    void Start()
    {
        // 初始化滑条数值和当前音量同步
        volumeSlider.value = AudioManager.Instance.baseVolume;
        // 滑条变化自动调用音量函数
        volumeSlider.onValueChanged.AddListener(AudioManager.Instance.SetBGMVolume);
    }
}