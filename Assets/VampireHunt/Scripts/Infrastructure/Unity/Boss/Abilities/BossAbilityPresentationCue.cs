using System;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    public enum BossAbilityCueSpawnMode : byte
    {
        SingleAnchor = 0,
        EachLockedArea = 1,
        DirectionalTravel = 2,
        SweepWaveWarning = 3,
        SweepWaveAttack = 4,
        TrackingLaserTargetMarker = 5,
        TrackingLaserBeam = 6,
        GridCutWarning = 7,
        GridCutBurst = 8
    }

    [Serializable]
    public sealed class BossAbilityPresentationCue
    {
        [Min(0f)] [SerializeField] private float timeFromCastStart;
        [SerializeField] private string cueName = "Cue";
        [SerializeField] private BossAbilityAnchorId anchor = BossAbilityAnchorId.Root;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private bool followAnchor = true;
        [SerializeField] private BossAbilityCueSpawnMode spawnMode = BossAbilityCueSpawnMode.SingleAnchor;
        [Min(0f)] [SerializeField] private float lifetime = 1f;

        [Header("Presentation")]
        [SerializeField] private string animatorTrigger;
        [SerializeField] private GameObject vfxPrefab;
        [SerializeField] private AudioClip audioClip;
        [Range(0f, 1f)] [SerializeField] private float audioVolume = 1f;

        public float TimeFromCastStart => timeFromCastStart;
        public string CueName => cueName;
        public BossAbilityAnchorId Anchor => anchor;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public bool FollowAnchor => followAnchor;
        public BossAbilityCueSpawnMode SpawnMode => spawnMode;
        public float Lifetime => lifetime;
        public string AnimatorTrigger => animatorTrigger;
        public GameObject VfxPrefab => vfxPrefab;
        public AudioClip AudioClip => audioClip;
        public float AudioVolume => audioVolume;
    }
}
