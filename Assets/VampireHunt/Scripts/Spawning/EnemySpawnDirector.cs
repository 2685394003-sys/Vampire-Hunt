using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Progression;
using VampireHunt.Run;

namespace VampireHunt.Spawning
{
    /// <summary>Single server-side entry point for regular enemy spawning.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnDirector : MonoBehaviour
    {
        private const int SpawnCandidateAttempts = 12;

        [Header("Dependencies")]
        [SerializeField] private VampireHuntGameManager runManager;
        [SerializeField] private EnemyAffixRunState enemyAffixState;
        [SerializeField] private EnemyArchetypeAsset meleeArchetype;

        [Header("Budget")]
        [Min(1)] [SerializeField] private int softEnemyCap = 20;
        [Min(0.1f)] [SerializeField] private float spawnInterval = 1.5f;
        [Min(0f)] [SerializeField] private float initialDelay = 1f;

        [Header("Spawn Ring")]
        [Min(1f)] [SerializeField] private float minimumPlayerDistance = 12f;
        [Min(1f)] [SerializeField] private float maximumPlayerDistance = 20f;
        [SerializeField] private LayerMask groundMask = -1;
        [SerializeField] private LayerMask blockingMask = 0;

        private readonly List<EnemyNetworkActor> m_ActiveEnemies = new List<EnemyNetworkActor>();
        private float m_NextSpawnTime;
        private ulong m_NextEntityId = 1UL << 32;
        private bool m_MissingConfigurationReported;

        private void Awake()
        {
            if (runManager == null) runManager = GetComponent<VampireHuntGameManager>();
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
            if (enemyAffixState == null) enemyAffixState = GetComponent<EnemyAffixRunState>();
            if (enemyAffixState == null) enemyAffixState = FindAnyObjectByType<EnemyAffixRunState>();
            maximumPlayerDistance = Mathf.Max(minimumPlayerDistance, maximumPlayerDistance);
        }

        private void OnEnable()
        {
            m_NextSpawnTime = Time.unscaledTime + initialDelay;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer) return;
            if (runManager == null || runManager.CurrentSnapshot.Phase != RunPhase.Exploring) return;

            RemoveDespawnedEnemies();
            if (m_ActiveEnemies.Count >= softEnemyCap || Time.unscaledTime < m_NextSpawnTime) return;
            m_NextSpawnTime = Time.unscaledTime + spawnInterval;

            TrySpawnEnemy(manager);
        }

        private void TrySpawnEnemy(NetworkManager manager)
        {
            if (meleeArchetype == null || meleeArchetype.NetworkPrefab == null)
            {
                if (!m_MissingConfigurationReported)
                {
                    m_MissingConfigurationReported = true;
                    Debug.LogError("[EnemySpawnDirector] Melee archetype or its network prefab is missing.", this);
                }
                return;
            }

            if (!TryGetPlayerCentroid(manager, out Vector3 center)) return;
            if (!TryFindSpawnPosition(center, out Vector3 spawnPosition)) return;

            NetworkObject instance = Instantiate(meleeArchetype.NetworkPrefab, spawnPosition, Quaternion.identity);
            if (!instance.TryGetComponent<EnemyNetworkActor>(out var actor))
            {
                Debug.LogError("[EnemySpawnDirector] Enemy prefab has no EnemyNetworkActor.", instance);
                Destroy(instance.gameObject);
                return;
            }

            actor.PrepareServerSpawn(
                ++m_NextEntityId,
                enemyAffixState != null
                    ? enemyAffixState.CaptureSpawnSnapshot()
                    : EnemyAffixSpawnSnapshot.Empty);
            instance.Spawn();
            m_ActiveEnemies.Add(actor);
        }

        private bool TryGetPlayerCentroid(NetworkManager manager, out Vector3 center)
        {
            center = Vector3.zero;
            int count = 0;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned) continue;
                center += player.transform.position;
                count++;
            }

            if (count == 0) return false;
            center /= count;
            return true;
        }

        private bool TryFindSpawnPosition(Vector3 center, out Vector3 position)
        {
            for (int attempt = 0; attempt < SpawnCandidateAttempts; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized;
                float distance = Random.Range(minimumPlayerDistance, maximumPlayerDistance);
                Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y) * distance;
                Vector3 rayOrigin = candidate + Vector3.up * 40f;

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 80f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    candidate = hit.point + Vector3.up * 0.05f;
                }

                Vector3 bottom = candidate + Vector3.up * 0.25f;
                Vector3 top = candidate + Vector3.up * 1.55f;
                if (Physics.CheckCapsule(bottom, top, 0.45f, blockingMask, QueryTriggerInteraction.Ignore)) continue;

                position = candidate;
                return true;
            }

            position = default;
            return false;
        }

        private void RemoveDespawnedEnemies()
        {
            for (int i = m_ActiveEnemies.Count - 1; i >= 0; i--)
            {
                EnemyNetworkActor enemy = m_ActiveEnemies[i];
                if (enemy == null || !enemy.IsSpawned)
                {
                    m_ActiveEnemies.RemoveAt(i);
                }
            }
        }
    }
}
