using Mirror;
using System;
using System.Linq; // LINQ �޼��带 ���� �߰�
using System.Collections.Generic; // ����Ʈ ���
using UnityEngine; // Debug Ŭ���� ���
using System.Security.Cryptography; // RandomNumberGenerator ���

public class CustomNetworkRoomManager : NetworkRoomManager
{
    public int RequiredPlayerCount = 4; // �÷��̾� ���� ���� ������ ������ ����

    public override void OnRoomServerPlayersReady()
    {
        // ��� �÷��̾ �غ� �������� Ȯ��
        if (roomSlots.Count == RequiredPlayerCount && roomSlots.All(player => player.readyToBegin))
        {
            Debug.Log("All players are ready. Starting the game...");
            ServerChangeScene(GameplayScene);
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn);
        Debug.Log($"Player connected: {conn.connectionId}. Total players: {roomSlots.Count}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);
        Debug.Log($"Player disconnected: {conn.connectionId}. Total players: {roomSlots.Count}");
    }

    public override void OnRoomServerSceneChanged(string sceneName)
    {
        if (roomSlots == null)
        {
            Debug.LogWarning("roomSlots is null. Ensure it is initialized properly.");
            return;
        }

        base.OnRoomServerSceneChanged(sceneName);

        // ���� �÷��� ������ ��ȯ�� �� �÷��̾� �ε����� �̸� �Ҵ�
        if (sceneName == GameplayScene)
        {
            Debug.Log("Assigning player indices and names...");
            AssignPlayerIndicesAndNames();
        }
    }

    private void AssignPlayerIndicesAndNames()
    {
        // �÷��̾� �ε����� �������� ����
        var indices = Enumerable.Range(0, roomSlots.Count).ToList();
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            for (int n = indices.Count - 1; n > 0; n--)
            {
                byte[] box = new byte[4];
                rng.GetBytes(box);
                int k = BitConverter.ToInt32(box, 0) & int.MaxValue % (n + 1);
                (indices[n], indices[k]) = (indices[k], indices[n]);
            }
        }

        int i = 0; // �ε��� �Ҵ��
        foreach (var roomSlot in roomSlots)
        {
            if (roomSlot == null)
            {
                Debug.LogWarning("A room slot is null. Skipping this slot.");
                continue;
            }

            var roomPlayer = roomSlot.GetComponent<PlayerManager>();
            if (roomPlayer != null)
            {
                roomPlayer.PlayerIndex = indices[i];
                roomPlayer.PlayerName = $"Player {indices[i] + 1}";
                Debug.Log($"Assigned PlayerIndex: {roomPlayer.PlayerIndex}, PlayerName: {roomPlayer.PlayerName}");
                i++;
            }
            else
            {
                Debug.LogWarning("PlayerManager component not found on RoomPlayer. Ensure all slots have the correct components.");
            }
        }
    }
}
