using System.Collections.Generic;
using UnityEngine;

namespace VampireHunt.EditorTools
{
    /// <summary>
    /// 网格建造工具的目录：存放开发阶段常用的房屋/地形预制体及其占地。
    /// 这是个纯编辑器资产(asset)，不进运行时构建。
    /// </summary>
    [System.Serializable]
    public class GridBuilderEntry
    {
        public string displayName = "新物体";
        public GameObject prefab;
        [Tooltip("沿世界 X 方向占用的格数 / cells along world X")]
        public int sizeX = 1;
        [Tooltip("沿世界 Z 方向占用的格数 / cells along world Z")]
        public int sizeZ = 1;
        [Tooltip("相对网格地面抬高的高度 / height offset from grid ground")]
        public float yOffset;
        [Tooltip("放到障碍层,使敌人寻路绕开此物体(沿用了 LevelGenerator 的 layer 3 约定) / place on obstacle layer so enemies path around it")]
        public bool placeOnObstacleLayer = true;
    }

    [CreateAssetMenu(fileName = "GridBuilderCatalog", menuName = "Vampire Hunt/Grid Builder Catalog")]
    public class GridBuilderCatalog : ScriptableObject
    {
        public List<GridBuilderEntry> entries = new List<GridBuilderEntry>();
    }
}
