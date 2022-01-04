using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class NetworkGUI : MonoBehaviour
{
    public NetworkManager networkManager;
    public InputField userInput;
    public InputField sendInput;
    public InputField roomInput;
    public Text messages;
    public List<string> messageBuffer;


    public void handleSend()
    {
        if(sendInput.text != "")
        {
            networkManager.sendTo(NetworkManager.MessageType.OTHERS,sendInput.text);
        }
    }
    public void handleLogin()
    {
        if(userInput.text != "")
        {
            networkManager.login(userInput.text, "nopass");
        }
    }
    public void handleJoin()
    {
        if(roomInput.text != "")
        {
           
            networkManager.join(roomInput.text);
        }

    }

    // Start is called before the first frame update
    void Start()
    {
        networkManager.messageReceived += (m) => {
            string s = m.type + ":" + m.sender +":" + m.text;
            messageBuffer.Add(s);
            messages.text = "";


            

            if(messageBuffer.Count > 10)
            {
                messageBuffer.RemoveAt(0);
            }
            for(int i = 0; i < messageBuffer.Count; i++)
            {
                messages.text = messages.text + messageBuffer[i] + "\n";
            }
        };
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
