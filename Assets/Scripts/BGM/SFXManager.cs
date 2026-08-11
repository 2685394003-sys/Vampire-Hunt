using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SFXManager : MonoBehaviour
{
    // 单例，其他脚本直接调用
    public static SFXManager Instance;

    [Header("音效音量 / SFX Volume")]
    public float attackVolume = 0.7f;
    [Header("攻击音效集合 / Attack SFX Clips")]
    public AudioClip attackSound;
    [Header("受伤音效集合 / Hurt SFX Clips")]
    public AudioClip huntSound;
    [Header("死亡音效集合 / Death SFX Clips")]
    public AudioClip deadSound;

    private AudioSource sfxAudioSource;


    void Awake()
    {
        // 单例初始化
        if (Instance == null)
        {
            Instance = this;
            // 想要切换场景不消失就取消下面注释
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 获取自身AudioSource
        sfxAudioSource = GetComponent<AudioSource>();
        // 攻击音效是2D全局声音
        sfxAudioSource.spatialBlend = 0;
    }

    /// <summary>
    /// 外部调用：播放攻击音效
    /// </summary>
    public void PlayAttackSFX()
    {
        // 判空防报错
        if (sfxAudioSource == null || attackSound == null) return;
        
        // PlayOneShot 支持音效叠加，连续挥砍不会截断声音
        sfxAudioSource.PlayOneShot(attackSound, attackVolume);
    }

    public void PlayAttackhunt()
    {
        sfxAudioSource.PlayOneShot(huntSound, attackVolume);
    }

    public void PlayAttackdead()
    {
        sfxAudioSource.PlayOneShot(deadSound, attackVolume);
    }

    public void SetSFXVolume(float value)
    {
        attackVolume = Mathf.Clamp01(value);
        sfxAudioSource.volume = attackVolume;
    }

}