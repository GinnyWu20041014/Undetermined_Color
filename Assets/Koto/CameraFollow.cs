using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;

    private Vector3 initialCameraPosition;
    private Vector3 initialPlayerPosition;

    void Start()
    {
        // 記錄遊戲開始時的位置
        initialCameraPosition = transform.position;
        initialPlayerPosition = player.position;
    }

    void LateUpdate()
    {
        Vector3 offset = player.position - initialPlayerPosition;

        // 根據玩家相對於初始位置的位移，移動攝影機
        transform.position = initialCameraPosition + new Vector3(
            offset.x,
            0f,
            offset.z
        );
    }
}