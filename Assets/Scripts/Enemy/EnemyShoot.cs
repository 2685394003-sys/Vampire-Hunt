using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyShoot : MonoBehaviour
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    private EnemyMovement enemyMove;

    void Start()
    {
        // 拿到同物体上的EnemyMovement
        enemyMove = GetComponent<EnemyMovement>();
    }
    
    public void Shoot()
    {
        Transform targetPlayer = null;
        if(enemyMove != null)
        {
            targetPlayer = enemyMove.GetPlayerTarget();
        }

        GameObject bulletObj = Instantiate(bulletPrefab,firePoint.position,Quaternion.identity);

        Shoot bullet = bulletObj.GetComponent<Shoot>();

        if(bullet != null && targetPlayer != null)
        {
            bullet.targetPlayer = targetPlayer;
        }
    }
}
