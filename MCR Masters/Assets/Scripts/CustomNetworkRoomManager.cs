using Mirror;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Security.Cryptography;
using UnityEngine.UI;
using TMPro;

public class CustomNetworkRoomManager : NetworkRoomManager
{
    public int RequiredPlayerCount = 4; // 플레이어 수를 조정 가능한 변수로 설정

    // 리눅스 서버 빌드 테스트시 활성화
    /*
    public override void Start()
    {
        base.Start();

        if (Application.isBatchMode) // 서버 실행 (Headless Mode)
        {
            networkAddress = "0.0.0.0"; // 모든 네트워크 인터페이스에서 연결 대기
            StartServer();
            Debug.Log("Server started in headless mode.");
        }
        else // 클라이언트 실행 (HUD 사용)
        {
            Debug.Log("Client mode. Use the HUD to enter IP and connect.");
        }
    }
    */



    public override void OnRoomServerPlayersReady()
    {
        base.OnRoomServerPlayersReady();
        if (roomSlots.Count == RequiredPlayerCount && roomSlots.All(player => player.readyToBegin))
        {
            Debug.Log("All players are ready.");
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
        if (sceneName == GameplayScene)
        {
            GameObject serverManagerObj = new GameObject("ServerManager");
            ServerManager serverManager = serverManagerObj.AddComponent<ServerManager>();

            Debug.Log("ServerManager created and configured for server-only execution.");
        }

        base.OnRoomServerSceneChanged(sceneName);
        Debug.Log("Scene Changed to {sceneName}..");
    }
}