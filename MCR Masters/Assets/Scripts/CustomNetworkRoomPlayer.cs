using Mirror;
using UnityEngine;

public class CustomNetworkRoomPlayer : NetworkRoomPlayer
{
    // ���ο� �޼���: ���� CmdChangeReadyState�� Ȱ���ϰ� Ŀ���� ���� �߰�
    public void ChangeReadyState(bool ready)
    {
        // �⺻ CmdChangeReadyState ȣ��
        CmdChangeReadyState(ready);

        // �߰� ����
        Debug.Log($"Custom logic: Player {index} is now {(ready ? "ready" : "not ready")}.");
    }

    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        Debug.Log($"Player {index} entered the room.");
    }

    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
        Debug.Log($"Player {index} left the room.");
    }

}
