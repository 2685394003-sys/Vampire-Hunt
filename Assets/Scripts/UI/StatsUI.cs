using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class StatsUI : MonoBehaviour
{
    public GameObject[] statsSlots;
    public CanvasGroup uiCanvasGroup;
    public InputAction toggleStatsAction;

    private bool statsOpen = false;

    private void OnEnable()
    {
        toggleStatsAction.performed += OnToggleStats;
        toggleStatsAction.Enable();
    }

    private void OnDisable()
    {
        toggleStatsAction.performed -= OnToggleStats;
        toggleStatsAction.Disable();
    }

    private void OnToggleStats(InputAction.CallbackContext ctx)
    {
        ToggleStatsPanel();
    }

    public void ToggleStatsPanel()
    {
        if(statsOpen)
        {
            // 关闭面板逻辑
            Time.timeScale = 1;
            uiCanvasGroup.alpha = 0;
            uiCanvasGroup.interactable = false;
            uiCanvasGroup.blocksRaycasts = false;
            statsOpen = false;
        }
        else
        {
            // 打开面板逻辑
            Time.timeScale = 0;
            uiCanvasGroup.alpha = 1;
            uiCanvasGroup.interactable = true;
            uiCanvasGroup.blocksRaycasts = true;
            statsOpen = true;
        }
    }
}