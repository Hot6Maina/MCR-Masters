using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyUI : MonoBehaviour
{
    public Button readyButton; // Button�� Inspector���� �������� �ʾƵ� �˴ϴ�.
    private CustomNetworkRoomPlayer roomPlayer;

    void Start()
    {
        // �������� ���� �� ���� ����
        if (!NetworkClient.active)
        {
            Debug.LogWarning("LobbyUI: This script is intended to run on the client.");
            return;
        }

        // ���� �÷��̾� ��������
        var networkIdentity = NetworkClient.connection.identity;
        roomPlayer = networkIdentity?.GetComponent<CustomNetworkRoomPlayer>();
        if (roomPlayer == null)
        {
            Debug.LogError("LobbyUI: Unable to find CustomNetworkRoomPlayer for the local client.");
            return;
        }

        // ReadyButton ���� ã��
        readyButton = GameObject.Find("ReadyButton")?.GetComponent<Button>();
        if (readyButton == null)
        {
            Debug.LogError("LobbyUI: ReadyButton not found. Check the GameObject name.");
            return;
        }

        // ��ư Ŭ�� �̺�Ʈ �߰�
        readyButton.onClick.AddListener(OnReadyButtonClicked);

        // �ʱ� ��ư ���� ����
        UpdateButtonUI(false);
    }

    public void OnReadyButtonClicked()
    {
        if (roomPlayer != null)
        {
            // ���� ������ �ݴ�� ����
            bool newReadyState = !roomPlayer.readyToBegin;
            roomPlayer.CmdChangeReadyState(newReadyState);

            // UI ������Ʈ
            UpdateButtonUI(newReadyState);
        }
    }

    void UpdateButtonUI(bool isReady)
    {
        TMP_Text buttonText = readyButton.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            buttonText.text = isReady ? "Cancel" : "Ready";
        }
        else
        {
            Debug.LogError("LobbyUI: TMP_Text component not found on ReadyButton.");
        }
    }
}
