using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using UnityEngine;
using Mirror;
using Game.Shared;
using System.Collections;
using DataTransfer;
using Mirror.BouncyCastle.Security;




public class ServerManager : NetworkBehaviour
{
    private List<int> tileDeck = new();
    private const int MaxPlayers = 4;

    private Hand[] handList = new Hand[MaxPlayers];
    private List<int>[] kawaTilesList = new List<int>[MaxPlayers];

    // need to init
    private int[] visibleTileCounts = new int[35];
    // winning condition은 쯔모, 타패, 가깡 일때 체크
    private WinningCondition winningCondition;
    //

    private const int MINIMUM_HU_SCORE = 1;
    private const int TotalTiles = 144;
    private int tileDrawIndexLeft = 0;
    private int tileDrawIndexRight = TotalTiles - 1;

    private int activePlayerCount = 0;
    private PlayerManager[] PlayerManagers;
    private int[] playerWindToIndex;
    private int[] playerScoreArray;

    private int CurrentRound = 0;
    private Wind RoundWind = Wind.EAST;
    private bool gameStarted = false;
    private int CurrentPlayerWindIndex;
    private int ScoreSourcePlayerWindIndex;

    private int popupResponses = 0; // 클라이언트의 확인 응답 수
    private int ActionTurnId = 0;

    public int GetCurrentPlayerWindIndex()
    {
        return CurrentPlayerWindIndex;
    }

    private void DestoryAllPopups()
    {
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] != null)
            {
                PlayerManagers[i].TargetDeletePopup(PlayerManagers[i].connectionToClient);
            }
        }
    }
    public void OnClientPopupComplete()
    {
        popupResponses++;
        Debug.Log($"[OnClientPopupComplete] {popupResponses} players responsed.");
        // 모든 클라이언트가 응답했거나 10초가 경과하면 새 라운드 시작
        if (popupResponses >= MaxPlayers)
        {
            DestoryAllPopups();
            StartCoroutine(StartNewRoundCoroutine());
        }
    }

    public void FinalizeRoundScore(int playerIndex)
    {
        Debug.Log($"[FinalizeRoundScore] Starting score finalization for Player Name: {PlayerManagers[playerIndex].PlayerName}, Player {playerIndex}.");

        // HandData 생성
        HandData handData = HandConverter.ConvertToHandData(handList[playerIndex]);
        if (winningCondition.IsDiscarded)
        {
            if (winningCondition.WinningTile < 0 || winningCondition.WinningTile >= 34)
            {
                return;
            }
            handData.ClosedTiles[winningCondition.WinningTile]++;
        }
        Debug.Log($"[FinalizeRoundScore] HandData created for Player {playerIndex}.\n HandData : {handData}");

        // WinningConditionData 생성
        WinningConditionData winningConditionData = HandConverter.ConvertToWinningConditionData(winningCondition, (int)RoundWind, playerIndex + (int)Wind.EAST);
        Debug.Log($"[FinalizeRoundScore] WinningConditionData created for Player {playerIndex}.\n WinningConditionData : {winningConditionData}");

        // 점수 계산
        List<YakuScoreData> huYakuScoreArray = ScoreCalculatorInterop.GetHuYakuScores(handData, winningConditionData);
        Debug.Log($"[FinalizeRoundScore] {huYakuScoreArray.Count} Yaku(s) calculated for Player {playerIndex}.");

        int totalScore = 0;
        foreach (var score in huYakuScoreArray)
        {
            Debug.Log($"[FinalizeRoundScore] Yaku: {YakuDictionary.dict[score.YakuId]}, Score: {score.Score}");
            totalScore += score.Score;
        }
        Debug.Log($"[FinalizeRoundScore] Total score for Player {playerIndex}: {totalScore}");

        // 최소 점수 체크
        if (totalScore < MINIMUM_HU_SCORE)
        {
            Debug.Log($"[FinalizeRoundScore] Total score {totalScore} is less than minimum required {MINIMUM_HU_SCORE}. Exiting.");
            return;
        }
        CalculateScoreAndUpdate(playerIndex, totalScore);
        // 초기화
        popupResponses = 0;
        Debug.Log($"[FinalizeRoundScore] Sending popup to all clients. Total Players: {NetworkServer.connections.Count}");

        // 모든 클라이언트에 점수 데이터 전송
        foreach (var conn in NetworkServer.connections)
        {
            if (conn.Value?.identity == null)
            {
                Debug.LogWarning($"[FinalizeRoundScore] Connection {conn.Key} has no valid identity. Skipping.");
                continue;
            }

            PlayerManager playerManager = conn.Value.identity.GetComponent<PlayerManager>();
            if (playerManager != null)
            {
                Debug.Log($"[FinalizeRoundScore] Sending TargetShowRoundScore to PlayerManager of connection {conn.Key}.");
                playerManager.TargetShowRoundScore(conn.Value, playerIndex, huYakuScoreArray, totalScore);
            }
            else
            {
                Debug.LogError($"[FinalizeRoundScore] PlayerManager not found for connection {conn.Key}. Skipping.");
            }
        }

        Debug.Log("[FinalizeRoundScore] Popup data sent to all clients.");
    }


    private void CalculateScoreAndUpdate(int playerWindIndex, int score)
    {
        if (playerWindIndex == ScoreSourcePlayerWindIndex)
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (i == playerWindToIndex[playerWindIndex])
                {
                    playerScoreArray[i] += (score + 8) * 3;
                }
                else
                {
                    playerScoreArray[i] -= score + 8;
                }
            }
        }
        else
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (i == playerWindToIndex[playerWindIndex])
                {
                    playerScoreArray[i] += score + 8 * 3;
                }
                else if (i == playerWindToIndex[ScoreSourcePlayerWindIndex])
                {
                    playerScoreArray[i] -= score + 8;
                }
                else
                {
                    playerScoreArray[i] -= 8;
                }
            }
        }
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (PlayerManagers[i] == null)
                continue;
            int playerIndex = PlayerManagers[i].PlayerIndex;
            PlayerManagers[i].UpdateCurrentScore(playerScoreArray[playerIndex]);
        }
    }

    public int GetRoundIndex()
    {
        return CurrentRound % 4 + ((CurrentRound % 4 == 0) ? 4 : 0);
    }


    public int GetTilesLeft()
    {
        return tileDrawIndexRight - tileDrawIndexLeft + 1;
    }

    private void BroadcastRoundIndex()
    {
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] == null)
                continue;
            PlayerManagers[i].RpcUpdateRoundIndex(GetRoundIndex());
        }
    }

    private void BroadcastTilesLeft()
    {
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] == null)
                continue;
            PlayerManagers[i].RpcUpdateTilesLeft(GetTilesLeft());
        }
    }

    private void CheckActionAfterTsumo(int tileId, int playerWindIndex)
    {
        Debug.Log($"[CheckActionAfterTsumo] Checking call and Hu for Player {playerWindIndex} tsumo tileId: {tileId}");

        TotalActionPriorityList = new List<ActionPriorityInfo>();
        SelectedActionPrioritySet = new HashSet<ActionPriorityInfo>();

        TotalActionPriorityList.AddRange(GetPossibleHuChoice(tileId, playerWindIndex));
        TotalActionPriorityList.AddRange(GetPossibleKanChoices(tileId, playerWindIndex));
        TotalActionPriorityList.AddRange(GetPossibleFlowerChoices(tileId, playerWindIndex));

        TotalActionPriorityList.Sort();

        if (TotalActionPriorityList.Count > 0)
        {
            PlayerManagers[playerWindIndex].TargetShowActionButtons(PlayerManagers[playerWindIndex].connectionToClient, playerWindIndex, TotalActionPriorityList, ActionTurnId, tileId);
        }
    }

    private void DoTsumo(int playerWindIndex)
    {
        var drawnTiles = DrawTiles(1);
        if (drawnTiles == null || drawnTiles.Count == 0)
        {
            Debug.Log($"[DoTsumo] Player {playerWindIndex}: No more tiles to draw.");
            StartCoroutine(StartNewRoundCoroutine());
            return;
        }
        BroadcastTilesLeft();
        ActionTurnId++;
        // TsumoOneTile 호출
        int tile = drawnTiles[0];
        Debug.Log($"[DoTsumo] Player {playerWindIndex} drew tile: {TileDictionary.NumToString[tile]}");

        int tsumoResult = handList[playerWindIndex].TsumoOneTile(tile);
        if (tsumoResult != 0)
        {
            Debug.LogError($"[ProceedNextTurn] Player {playerWindIndex}: Failed to tsumo tile {tile}. TsumoResult: {tsumoResult}");
            return;
        }

        winningCondition.WinningTile = tile;
        winningCondition.IsDiscarded = false;
        if (tile >= 0 && tile < 34)
        {
            if (visibleTileCounts[tile] == 3)
            {
                winningCondition.IsLastTileOfItsKind = true;
            }
        }
        if (GetTilesLeft() == 0)
        {
            winningCondition.IsLastTileInTheGame = true;
        }


        // Tsumo 호출 (TargetPerformTsumo 사용)
        Debug.Log($"[ProceedNextTurn] Sending Tsumo tile {TileDictionary.NumToString[tile]} to Player {playerWindIndex}.");
        PlayerManagers[playerWindIndex].TargetTsumoTile(PlayerManagers[playerWindIndex].connectionToClient, tile, PlayerManagers[playerWindIndex].PlayerIndex);

        CheckActionAfterTsumo(tile, playerWindIndex);
    }

    public void ProceedNextTurn(int playerWindIndex)
    {
        if (tileDrawIndexRight - tileDrawIndexLeft < 0)
        {
            Debug.Log($"[ProceedNextTurn] Player {playerWindIndex}: No more tiles to draw.");
            StartCoroutine(StartNewRoundCoroutine());
            return;
        }
        Debug.Log($"[ProceedNextTurn] Starting turn for playerWindIndex: {playerWindIndex}, playerIndex: {PlayerManagers[playerWindIndex].PlayerIndex}");


        PlayerManagers[playerWindIndex].SetPlayerTurn(false);
        playerWindIndex = (playerWindIndex + 1) % MaxPlayers;
        CurrentPlayerWindIndex = playerWindIndex;
        PlayerManagers[playerWindIndex].SetPlayerTurn(true);

        Debug.Log($"[ProceedNextTurn] Next turn for playerWindIndex: {playerWindIndex}, playerIndex: {PlayerManagers[playerWindIndex].PlayerIndex}");


        DoTsumo(playerWindIndex);
    }

    private int GetPriorityFromWind(int playerWindIndex)
    {
        int priority = playerWindIndex - CurrentPlayerWindIndex;
        if (priority < 0)
        {
            priority += MaxPlayers;
        }
        return priority;
    }

    private int GetWindFromPriority(int priority)
    {
        int playerWindIndex = priority + CurrentPlayerWindIndex;
        if (playerWindIndex >= MaxPlayers)
        {
            playerWindIndex -= MaxPlayers;
        }
        return playerWindIndex;
    }

    private List<ActionPriorityInfo> GetPossibleHuChoice(int tileId, int playerWindIndex)
    {
        HandData handData = HandConverter.ConvertToHandData(handList[playerWindIndex]);
        if (winningCondition.IsDiscarded)
            handData.ClosedTiles[tileId] += 1;
        WinningConditionData winningConditionData = HandConverter.ConvertToWinningConditionData(winningCondition, (int)RoundWind, playerWindIndex + (int)Wind.EAST);
        List<YakuScoreData> huYakuScoreArray = ScoreCalculatorInterop.GetHuYakuScores(handData, winningConditionData);
        int totalScore = 0;
        Debug.Log("Hu Yaku Scores:");
        foreach (var score in huYakuScoreArray)
        {
            Debug.Log($"Yaku Name: {YakuDictionary.dict[score.YakuId]}, Score: {score.Score}");
            totalScore += score.Score;
        }
        List<ActionPriorityInfo> resultList = new List<ActionPriorityInfo>();
        if (totalScore >= MINIMUM_HU_SCORE)
        {
            resultList.Add(new ActionPriorityInfo(ActionType.HU, GetPriorityFromWind(playerWindIndex), tileId));
        }
        return resultList;
    }
    private List<ActionPriorityInfo> GetPossiblePonChoice(int tileId, int playerWindIndex)
    {
        List<ActionPriorityInfo> resultList = new List<ActionPriorityInfo>();
        if (winningCondition == null ||  !winningCondition.IsDiscarded || playerWindIndex == CurrentPlayerWindIndex || tileId < 0 || tileId >= 34)
        {
            return resultList;
        }
        if (handList[playerWindIndex].ClosedTiles[tileId] >= 2)
        {
            resultList.Add(new ActionPriorityInfo(ActionType.PON, GetPriorityFromWind(playerWindIndex), tileId));
        }
        return resultList;
    }
    private List<ActionPriorityInfo> GetPossibleKanChoices(int tileId, int playerWindIndex)
    {
        List<ActionPriorityInfo> resultList = new List<ActionPriorityInfo>();
        if (winningCondition == null || tileId < 0 || tileId >= 34)
        {
            return resultList;
        }
        if (handList[playerWindIndex].ClosedTiles[tileId] >= 3)
        {
            resultList.Add(new ActionPriorityInfo(ActionType.KAN, GetPriorityFromWind(playerWindIndex), tileId));
        }
        return resultList;
    }
    private List<ActionPriorityInfo> GetPossibleFlowerChoices(int tileId, int playerWindIndex)
    {
        List<ActionPriorityInfo> resultList = new List<ActionPriorityInfo>();
        if (winningCondition == null || winningCondition.IsDiscarded || playerWindIndex != CurrentPlayerWindIndex)
        {
            return resultList;
        }
        if (handList[playerWindIndex].ClosedTiles[34] >= 1 || tileId == 34)
        {
            resultList.Add(new ActionPriorityInfo(ActionType.FLOWER, GetPriorityFromWind(playerWindIndex), 34));
        }
        return resultList;
    }


    private List<ActionPriorityInfo> GetPossibleChiiChoices(int tileId, int playerWindIndex)
    {
        List<ActionPriorityInfo> resultList = new List<ActionPriorityInfo>();
        if (winningCondition == null || !winningCondition.IsDiscarded ||  GetPriorityFromWind(playerWindIndex) != 1 || tileId < 0 || tileId >= 27)
        {
            return resultList;
        }
        // x x+1 x+2   x <= 7 (index <= 6)
        // x-1 x x+1   x >= 2 && x <= 8 (index >= 1 && index <= 7)
        // x-2 x-1 x   x >= 3 (index >= 2)
        if (tileId % 9 <= 6)
        {
            if (handList[playerWindIndex].ClosedTiles[tileId + 1] >= 1 && handList[playerWindIndex].ClosedTiles[tileId + 2] >= 1)
            {
                resultList.Add(new ActionPriorityInfo(ActionType.CHII, GetPriorityFromWind(playerWindIndex), tileId));
            }
        }
        if (tileId % 9 >= 1 && tileId % 9 <= 7)
        {
            if (handList[playerWindIndex].ClosedTiles[tileId - 1] >= 1 && handList[playerWindIndex].ClosedTiles[tileId + 1] >= 1)
            {
                resultList.Add(new ActionPriorityInfo(ActionType.CHII, GetPriorityFromWind(playerWindIndex), tileId - 1));
            }
        }
        if (tileId % 9 >= 2)
        {
            if (handList[playerWindIndex].ClosedTiles[tileId - 2] >= 1 && handList[playerWindIndex].ClosedTiles[tileId - 1] >= 1)
            {
                resultList.Add(new ActionPriorityInfo(ActionType.CHII, GetPriorityFromWind(playerWindIndex), tileId - 2));
            }
        }
        return resultList;
    }

    private List<ActionPriorityInfo> TotalActionPriorityList;
    private HashSet<ActionPriorityInfo> SelectedActionPrioritySet;

    private void DebugTotalActionPriorityList()
    {
        if (TotalActionPriorityList == null || TotalActionPriorityList.Count == 0)
        {
            Debug.Log("TotalActionPriorityList is empty or null.");
            return;
        }

        Debug.Log("TotalActionPriorityList contents:");
        for (int i = 0; i < TotalActionPriorityList.Count; i++)
        {
            Debug.Log($"[{i}] {TotalActionPriorityList[i]}");
        }
    }


    public void ReceiveActionDecision(int playerWindIndex, ActionPriorityInfo actionPriorityInfo, int actionTurnId, int tileId)
    {
        if (actionTurnId != ActionTurnId)
        {
            return;
        }
        int priority = actionPriorityInfo.Priority;
        List<ActionPriorityInfo> NewTotalList = new List<ActionPriorityInfo>();
        foreach (ActionPriorityInfo action in TotalActionPriorityList)
        {
            if (priority == action.Priority)
            {
                continue;
            }
            NewTotalList.Add(action);
        }
        DebugTotalActionPriorityList();
        TotalActionPriorityList = NewTotalList;
        if (actionPriorityInfo.Type != ActionType.SKIP)
        {
            TotalActionPriorityList.Add(actionPriorityInfo);
            SelectedActionPrioritySet.Add(actionPriorityInfo);
        }
        TotalActionPriorityList.Sort();
        DebugTotalActionPriorityList();
        if (TotalActionPriorityList.Count == 0)
        {
            // go to next turn
            kawaTilesList[playerWindIndex].Add(tileId);
            ProceedNextTurn(playerWindIndex);
        }
        else if (SelectedActionPrioritySet.Contains(TotalActionPriorityList[0]))
        {
            // 
            // TODO: Should Stop And Clear All Buttons in Client by Cmd
            // TODO: Do Call Logic by TargetRPC
            var action = TotalActionPriorityList[0];
            int actionWindIndex = GetWindFromPriority(action.Priority);
            Debug.Log($"action: {action}, actionWindIndex: {actionWindIndex}");
            if (action.Type == ActionType.HU)
            {
                for (int i = 0; i < MaxPlayers; i++)
                {
                    if (PlayerManagers[i] == null)
                        continue;
                    PlayerManagers[i].TargetClearButtons(PlayerManagers[i].connectionToClient);
                }
                ScoreSourcePlayerWindIndex = CurrentPlayerWindIndex;
                FinalizeRoundScore(actionWindIndex);
            }
            else
            {
                for (int i = 0; i < MaxPlayers; i++)
                {
                    if (PlayerManagers[i] == null)
                        continue;
                    PlayerManagers[i].TargetClearButtonsAndDoCallAction(PlayerManagers[i].connectionToClient, TotalActionPriorityList[0], tileId, playerWindToIndex[actionWindIndex]);
                }
            }
        }
    }

    private void CheckActionAfterDiscard(int tileId, int playerWindIndex)
    {
        Debug.Log($"[CheckCall] Checking call and Hu for Player {playerWindIndex} discarded with tileId: {tileId}");

        List<ActionPriorityInfo>[] ActionPriorityList = new List<ActionPriorityInfo>[MaxPlayers];
        for (int i = 0;i < MaxPlayers; i++)
        {
            ActionPriorityList[i] = new List<ActionPriorityInfo>();
        }
        TotalActionPriorityList = new List<ActionPriorityInfo>();
        SelectedActionPrioritySet = new HashSet<ActionPriorityInfo>();
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (i == playerWindIndex) continue;
            ActionPriorityList[i].AddRange(GetPossibleHuChoice(tileId, i));
            ActionPriorityList[i].AddRange(GetPossibleKanChoices(tileId, i));
            ActionPriorityList[i].AddRange(GetPossiblePonChoice(tileId, i));
            ActionPriorityList[i].AddRange(GetPossibleChiiChoices(tileId, i));
            ActionPriorityList[i].Sort();
            TotalActionPriorityList.AddRange(ActionPriorityList[i]);
        }

        TotalActionPriorityList.Sort();

        if (TotalActionPriorityList.Count > 0)
        {
            Debug.Log($"[CheckCall] Call logic for Player {playerWindIndex} will be implemented here.");


            for (int i = 0; i < MaxPlayers; ++i) 
            { 
                if (ActionPriorityList[i].Count == 0)
                {
                    continue;
                }
                PlayerManagers[i].TargetShowActionButtons(PlayerManagers[i].connectionToClient, playerWindIndex, ActionPriorityList[i], ActionTurnId, tileId);
            }
        }
        else
        {
            Debug.Log($"[CheckCall] No call or hu. Adding tile {TileDictionary.NumToString[tileId]} to kawa for Player {(Wind) Wind.EAST + playerWindIndex}.");
            kawaTilesList[playerWindIndex].Add(tileId);
            ProceedNextTurn(playerWindIndex);
        }
    }

    private void DoDiscard(int tileId, int playerWindIndex)
    {
        Debug.Log($"[DoDiscard] Player {playerWindIndex} discarding tile {TileDictionary.NumToString[tileId]}.");
        PlayerManagers[playerWindIndex].SetPlayerTurn(false);
        handList[playerWindIndex].DiscardOneTile(tileId);
        ActionTurnId++;
        winningCondition.WinningTile = tileId;
        winningCondition.IsDiscarded = true;
        if (tileId >= 0 && tileId < 34)
        {
            visibleTileCounts[tileId]++;
            if (visibleTileCounts[tileId] == 4)
            {
                winningCondition.IsLastTileOfItsKind = true;
            }
        }
        if (GetTilesLeft() == 0)
        {
            winningCondition.IsLastTileInTheGame = true;
        }


        Debug.Log($"[DoDiscard] Updated hand for Player {playerWindIndex}. Remaining count for tileId {tileId}: {handList[playerWindIndex].ClosedTiles[tileId]}.");

        CheckActionAfterDiscard(tileId, playerWindIndex);
    }

    public int IsVaildDiscard(int tileID, int PlayerIndex)
    {
        Debug.Log($"[IsVaildDiscard] Validating discard for tileID {tileID} by PlayerIndex {PlayerIndex}.");

        for (int i = 0; i < playerWindToIndex.Length; i++)
        {
            if (playerWindToIndex[i] == PlayerIndex)
            {
                Debug.Log($"[IsVaildDiscard] Found matching PlayerIndex at index {i}.");

                if (handList[i].ClosedTiles[tileID] > 0)
                {
                    Debug.Log($"[IsVaildDiscard] Valid discard. Tile count for tileID {tileID}: {handList[i].ClosedTiles[tileID]}.");
                    DoDiscard(tileID, i);
                    return 1;
                }
                else
                {
                    Debug.LogError($"[IsVaildDiscard] Invalid discard. No tiles left for tileID {tileID}.");
                    return 0;
                }
            }
        }

        Debug.LogError($"[IsVaildDiscard] No matching PlayerIndex found for {PlayerIndex}.");
        return 0;
    }


    public int GetActivePlayerCount()
    {
        return activePlayerCount;
    }

    void Start()
    {
        // 안전장치: 서버에서만 실행되도록 확인
        if (!NetworkServer.active)
        {
            Debug.LogWarning("ServerManager is not running on the server. Destroying this object.");
            Destroy(gameObject); // 클라이언트에서 생성된 경우 제거
            return;
        }

        Debug.Log("ServerManager starting...");


    }

    private void BroadcastPlayerIndices()
    {
        playerWindToIndex = new int[PlayerManagers.Length];
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] != null)
            {
                playerWindToIndex[i] = PlayerManagers[i].PlayerIndex;
            }
        }
        // 각 PlayerManager에 RPC 호출

        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            var playerManager = PlayerManagers[i];
            if (playerManager == null)
            {
                Debug.LogWarning("PlayerManager is null. Skipping...");
                continue;
            }

            if (playerManager.connectionToClient == null)
            {
                Debug.LogWarning($"PlayerManager for PlayerIndex {playerManager.PlayerIndex} has a null connectionToClient. Skipping...");
                continue;
            }

            Debug.Log($"PlayerManager for PlayerIndex {playerManager.PlayerIndex} has a valid connectionToClient: ConnectionId {playerManager.connectionToClient.connectionId}");

            // RPC 호출
            playerManager.TargetSetEnemyIndexMap(playerManager.connectionToClient, playerWindToIndex, i);
        }
    }



    bool CanDrawTile()
    {
        return tileDrawIndexLeft <= tileDrawIndexRight;
    }

    private IEnumerator WaitForInitialization()
    {
        yield return new WaitForSeconds(0.5f); // 0.5초 대기 (필요 시 조정)
        InitializePlayers();
    }
    public void IncrementPlayerCount()
    {
        activePlayerCount++;
        Debug.Log($"Player connected. ActivePlayerCount: {activePlayerCount}");

        if (activePlayerCount == MaxPlayers)
        {
            Debug.Log($"All {MaxPlayers} players are active. Initializing players...");
            StartCoroutine(WaitForInitialization());
        }
    }

    public void DecrementPlayerCount()
    {
        activePlayerCount--;
        Debug.Log($"Player disconnected. ActivePlayerCount: {activePlayerCount}");
    }

    public void InitializePlayers()
    {
        Debug.Log("ServerManager initialize players...");
        // 씬에서 PlayerManager를 찾음
        PlayerManagers = UnityEngine.Object.FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);

        // Debug: PlayerManagers 배열 상태 출력
        Debug.Log($"Found {PlayerManagers.Length} PlayerManagers in the scene.");

        // PlayerManagers 배열 검증
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] == null)
            {
                Debug.LogWarning($"PlayerManagers[{i}] is null.");
            }
            else
            {
                Debug.Log($"PlayerManagers[{i}] - PlayerIndex: {PlayerManagers[i].PlayerIndex}, PlayerName: {PlayerManagers[i].PlayerName}");
            }
        }

        if (PlayerManagers.Length != MaxPlayers)
        {
            Debug.LogError($"Expected {MaxPlayers} PlayerManagers, but found {PlayerManagers.Length}. Game cannot proceed.");
            return;
        }

        AssignPlayerIndicesAndNames();
        StartCoroutine(GameStarted());
    }

    private void AssignPlayerIndicesAndNames()
    {
        // 플레이어 인덱스를 섞기
        var indices = Enumerable.Range(0, PlayerManagers.Length).ToList();
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

        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] != null)
            {
                PlayerManagers[i].PlayerIndex = indices[i];
                PlayerManagers[i].PlayerName = $"Player {indices[i] + 1}";

                Debug.Log($"PlayerIndex: {PlayerManagers[i].PlayerIndex}, PlayerName: {PlayerManagers[i].PlayerName} 할당 완료");
            }
            else
            {
                Debug.LogWarning($"PlayerManagers[{i}]가 null입니다. 건너뜁니다.");
            }
        }
        // 디버깅용 출력
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] != null)
            {
                Debug.Log($"PlayerManagers[{i}] - PlayerName: {PlayerManagers[i].PlayerName}, PlayerIndex: {PlayerManagers[i].PlayerIndex}");
            }
        }
    }

    public IEnumerator GameStarted()
    {
        if (gameStarted)
        {
            Debug.LogWarning("Game already started. Ignoring duplicate call.");
            yield break;
        }

        gameStarted = true;

        Debug.Log("Game started. Ready for the first round initialization.");
        Debug.Log($"PlayerManagers count: {PlayerManagers?.Length}");
        playerScoreArray = new int[MaxPlayers];
        for (int i = 0; i < MaxPlayers; i++)
        {
            playerScoreArray[i] = 0;
        }
        // InitializePlayersCoroutine 호출로 중복 제거
        yield return StartCoroutine(InitializePlayersCoroutine());

        // 플레이어 초기화가 완료되면 새로운 라운드 시작
        StartNewRounds();
    }




    public void StartNewRounds()
    {
        Debug.Log("Players first seat:");
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            Debug.Log($"Index {i}: Player {PlayerManagers[i].PlayerName}, PlayerIndex {PlayerManagers[i].PlayerIndex} - Wind: {PlayerManagers[i].playerStatus.SeatWind}");
        }

        StartCoroutine(StartNewRoundCoroutine());
    }



    public IEnumerator StartNewRoundCoroutine()
    {
        if (CurrentRound >= 16)
        {
            Debug.Log("Game over. All rounds completed.");
            yield break;
        }

        CurrentRound++;
        if (CurrentRound > 1 && CurrentRound % 4 == 1)
        {
            RoundWind++;
        }
        InitializeTiles();
        if (CurrentRound > 1)
        {
            AdjustPositionsBeforeRound();
        }

        BroadcastPlayerIndices();
        BroadcastRoundIndex();
        Debug.Log($"New round started: Round {CurrentRound}, Wind: {RoundWind}");
        

        // to avoid representing last discarded tile from the previous round
        yield return new WaitForSeconds(0.1f);
        yield return StartCoroutine(InitializePlayersCoroutine());
        yield return new WaitForSeconds(0.1f);
        
        ShuffleTiles();
        
        
        //DealTilesToPlayers();

        // Warning: Test Function!!
        DealTilesToPlayersTest();
        // 
        Debug.Log("Initialization of players completed. Proceeding to next turn.");
        ProceedNextTurn(3);
    }

    private IEnumerator InitializePlayersCoroutine()
    {
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            if (PlayerManagers[i] == null)
            {
                Debug.LogError($"PlayerManager at index {i} is null.");
                continue;
            }
            PlayerManagers[i].SetInitializeFlagFalse();
            Debug.Log($"Passing initialization data to PlayerManager[{i}].");
            PlayerManagers[i].InitializePlayerOnClient(Wind.EAST + i, RoundWind);

            // 클라이언트 쪽 초기화 완료를 대기
            yield return new WaitUntil(() => PlayerManagers[i].IsInitializationComplete());
        }
    }



    private void AdjustPositionsBeforeRound()
    {
        int[] changeIndex = new int[PlayerManagers.Length];
        PlayerManager[] tempManagers = new PlayerManager[PlayerManagers.Length];
        if (CurrentRound % 4 == 1)
        {
            if (RoundWind == Wind.SOUTH || RoundWind == Wind.NORTH)
            {
                changeIndex[0] = 2;
                changeIndex[1] = 1;
                changeIndex[2] = 0;
                changeIndex[3] = 3;
            }
            else if (RoundWind == Wind.WEST)
            {
                changeIndex[0] = 0;
                changeIndex[1] = 3;
                changeIndex[2] = 1;
                changeIndex[3] = 2;
            }
        }
        else
        {
            changeIndex[0] = 1;
            changeIndex[1] = 2;
            changeIndex[2] = 3;
            changeIndex[3] = 0;
        }


        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            tempManagers[i] = PlayerManagers[changeIndex[i]];
        }

        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            PlayerManagers[i] = tempManagers[i];
        }
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            PlayerManagers[i].playerStatus = new PlayerStatus(PlayerManagers[i].playerStatus.CurrentScore, i + Wind.EAST, RoundWind);
        }

        Debug.Log("Player positions adjusted after round and reassigned.");
        for (int i = 0; i < PlayerManagers.Length; i++)
        {
            Debug.Log($"Index {i}: Player {PlayerManagers[i].PlayerName} - Wind: {PlayerManagers[i].playerStatus.SeatWind}");
        }
    }

    private void InitializeTiles()
    {
        tileDeck.Clear();
        handList = new Hand[MaxPlayers];
        kawaTilesList = new List<int>[MaxPlayers];
        visibleTileCounts = new int[35];
        winningCondition = new WinningCondition();

        tileDrawIndexLeft = 0;
        //tileDrawIndexRight = TotalTiles - 1;
        for (int i = 0; i < MaxPlayers; i++)
        {
            handList[i] = new Hand();
            kawaTilesList[i] = new();
            visibleTileCounts[i] = 0;
        }
        for (int tileNum = 0; tileNum < 18; tileNum++)
        //for (int tileNum = 0; tileNum < 34; tileNum++)
        {
            for (int i = 0; i < 4; i++)
            {
                tileDeck.Add(tileNum);
            }
        }

        //for (int i = 0; i < 8; i++)
        //{
        //    tileDeck.Add(34); // 0f tiles
        //}
        tileDrawIndexRight = tileDeck.Count - 1;
        Debug.Log("Tile deck initialized with " + tileDeck.Count + " tiles.");
    }

    private void ShuffleTiles()
    {
        using (var rng = RandomNumberGenerator.Create())
        {
            int n = tileDeck.Count;
            while (n > 1)
            {
                byte[] box = new byte[4];
                rng.GetBytes(box);
                int k = (int)(BitConverter.ToUInt32(box, 0) % n);
                n--;
                int temp = tileDeck[n];
                tileDeck[n] = tileDeck[k];
                tileDeck[k] = temp;
            }
        }

        Debug.Log("Tiles shuffled.");
    }


    public List<int> DrawTiles(int count)
    {
        if (tileDrawIndexRight - tileDrawIndexLeft + 1 < count)
        {
            Debug.LogWarning("Not enough tiles left in the deck.");
            return null;
        }

        var drawnTiles = tileDeck.GetRange(tileDrawIndexLeft, count);
        tileDrawIndexLeft += count;
        return drawnTiles;
    }


    private void DealTilesToPlayersTest()
    {
        handList[0].DrawFirstHand(new List<int> { 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 8, 8 });
        handList[1].DrawFirstHand(new List<int> { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 });
        handList[2].DrawFirstHand(new List<int> { 27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33 });
        handList[3].DrawFirstHand(new List<int> { 33, 27, 27, 27, 28, 28, 28, 29, 29, 29, 30, 30, 30 });
        SpawnFirstHands();
    }


    private void DealTilesToPlayers()
    {
        for (int i = 0; i < MaxPlayers; ++i)
        {
            var handTiles = DrawTiles(13);
            if (handTiles == null)
            {
                Debug.LogError("Not enough tiles to deal to player " + PlayerManagers[i].PlayerName);
                return;
            }
            BroadcastTilesLeft();
            handTiles.Sort();
            handList[i].DrawFirstHand(handTiles);
            Debug.Log("Dealt starting hand to player " + PlayerManagers[i].PlayerName);
        }

        SpawnFirstHands();
    }

    private void SpawnFirstHands()
    {
        Debug.Log($"{PlayerManagers.Length} Players in SpawnFirstHands function.");
        for (int i = 0; i < MaxPlayers; ++i)
        {
            if (PlayerManagers[i] == null)
            {
                Debug.LogWarning($"PlayerManager at index {i} is null. Skipping.");
                continue;
            }

            Debug.Log($"PlayerManager at index {i}, Player Name: {PlayerManagers[i].PlayerName}, Player Index: {PlayerManagers[i].PlayerIndex}, try to Spawn First hand...");
            // 클라이언트에게 타겟팅된 데이터 전송
            PlayerManagers[i].TargetSpawnFirstHand(PlayerManagers[i].connectionToClient, handList[i].ClosedTiles);
        }

        Debug.Log("SpawnFirstHands called with TargetRpc on all clients.");
    }
}