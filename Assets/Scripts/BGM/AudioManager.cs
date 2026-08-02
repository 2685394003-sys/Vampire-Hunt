using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("背景音乐歌单列表 / BGM Playlist")]
    public List<AudioClip> bgmList;
    [Header("是否随机播放 / Shuffle")]
    public bool playRandom = false;
    [Header("基础音量 / Base Volume")]
    public float baseVolume = 0.6f;

    private AudioSource bgmSource;
    private int currentSongIndex = 0;
    // 新增标记：是否是玩家主动暂停，用来阻止暂停时自动切歌
    private bool isManuallyPaused = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        bgmSource = GetComponent<AudioSource>();

        bgmSource.volume = baseVolume;
        bgmSource.loop = false; // 单首不循环，播完自动切下一首
    }

    void Start()
    {
        // 自动开始播放歌单
        PlayNextSong();
    }

    void Update()
    {
        // 修复：只有不是手动暂停、且歌曲播放完毕，才切下一首
        if (!isManuallyPaused && !bgmSource.isPlaying && bgmList.Count > 0)
        {
            PlayNextSong();
        }
    }

    /// <summary>
    /// 播放下一首BGM（顺序/随机自动区分）
    /// </summary>
    public void PlayNextSong()
    {
        if (bgmList.Count == 0) return;

        // 切歌前清空手动暂停标记
        isManuallyPaused = false;
        bgmSource.Stop();

        if (playRandom)
        {
            // 随机选一首，避免连续重复同一首
            int newIndex;
            do
            {
                newIndex = Random.Range(0, bgmList.Count);
            } while (newIndex == currentSongIndex && bgmList.Count > 1);
            currentSongIndex = newIndex;
        }
        else
        {
            // 顺序循环，播到最后回到第一首
            currentSongIndex = (currentSongIndex + 1) % bgmList.Count;
        }

        bgmSource.clip = bgmList[currentSongIndex];
        bgmSource.Play();
    }

    /// <summary>
    /// 手动切上一首
    /// </summary>
    public void PlayLastSong()
    {
        // 无歌单直接返回
        if (bgmList.Count == 0) return;

        // 随机模式提示并退出（随机无“上一首”概念）
        if (playRandom)
        {
            Debug.LogWarning("随机播放模式下无法切换上一首！");
            return;
        }

        isManuallyPaused = false;
        bgmSource.Stop();

        // 倒序计算索引，防止负数
        currentSongIndex = (currentSongIndex - 1 + bgmList.Count) % bgmList.Count;
        bgmSource.clip = bgmList[currentSongIndex];
        bgmSource.Play();
    }

    public void StopBGM()
    {
        isManuallyPaused = true;
        bgmSource.Stop();
    }

    public void ToggleBGM()
    {
        if (bgmSource.isPlaying)
        {
            bgmSource.Pause();
            isManuallyPaused = true; // 标记为手动暂停，阻止自动切歌
        }
        else
        {
            bgmSource.UnPause();
            isManuallyPaused = false; // 恢复播放，解除暂停标记
        }
    }

    public void SetBGMVolume(float value)
    {
        baseVolume = Mathf.Clamp01(value);
        bgmSource.volume = baseVolume;
    }
}