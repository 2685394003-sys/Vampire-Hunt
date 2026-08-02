using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class QuitGameBtn : MonoBehaviour
{

    public void QuitGame()
    {
        // 区分编辑器与打包后游戏
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }    
}
