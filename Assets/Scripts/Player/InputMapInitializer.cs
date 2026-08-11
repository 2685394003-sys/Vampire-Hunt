using UnityEngine;
using UnityEngine.InputSystem;

public class InputMapInitializer : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputAsset;

    void Start()
    {
        // 遍历所有分组，全部关闭
        foreach(var map in inputAsset.actionMaps)
        {
            map.Disable();
        }
        // 开启默认Player分组
        inputAsset.FindActionMap("Player").Enable();
    }
}
