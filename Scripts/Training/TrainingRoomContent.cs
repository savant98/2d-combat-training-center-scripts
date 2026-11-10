using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 하나의 Room에 장착하는 배경·카메라·특수 연출 모듈이다. 외부 Scene 참조는 Host가 복구한다.
public sealed class TrainingRoomContent : MonoBehaviour
{
    public WaveManager waves;
    public CinemachineCamera roomCamera;
    public RoomTrigger clearPortal;
    public Transform spawnRoot;
    public TileBase terrainTile;
    public Material lineMaterial;
    public Sprite blinderSprite;
    public Material blinderMaterial;
}
