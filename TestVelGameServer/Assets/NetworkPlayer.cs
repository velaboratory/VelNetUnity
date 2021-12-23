using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System;

public class NetworkPlayer : MonoBehaviour
{
    public int userid;
    public string username;
    public string room;
    public NetworkManager manager;
    Vector3 networkPosition;

    public bool isLocal = false;
    // Start is called before the first frame update
    void Start()
    {
        if (manager.userid == userid) //this is me, I send updates
        {
            StartCoroutine(syncTransformCoroutine());
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (userid != manager.userid) //not me, I move to wherever I should
        {
            transform.position = Vector3.Lerp(transform.position, networkPosition, .1f);
        }
        else
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 delta = h * Vector3.right + v * Vector3.up;
            transform.position = transform.position + delta * Time.deltaTime;
        }
    }

    IEnumerator syncTransformCoroutine()
    {
        while (true)
        {
            manager.sendTo(0, "1," + transform.position.x + "," + transform.position.y + "," + transform.position.z + ";");
            yield return new WaitForSeconds(.1f);
        }
    }
    public void handleMessage(NetworkManager.Message m)
    {
        //we need to parse the message

        //types of messages
        string[] messages = m.text.Split(';'); //messages are split by ;
        for(int i = 0; i < messages.Length; i++)
        {
            //individual message parameters separated by comma
            string[] sections = messages[i].Split(',');

            switch (sections[0])
            {
                case "1": //update transform of self
                    {
                        float x = float.Parse(sections[1]);
                        float y = float.Parse(sections[2]);
                        float z = float.Parse(sections[3]);
                        networkPosition = new Vector3(x, y, z);
                        break;
                    }
                case "2": //audio data
                    {
                        byte[] headers = Convert.FromBase64String(sections[1]);
                        byte[] data = Convert.FromBase64String(sections[2]);
                        this.GetComponent<VelVoiceController>()?.receiveAudioFrame(headers, data);
                        break;
                    }
            }
        }

    }
    public void sendAudioData(byte[] headers, byte[] data)
    {
        //base64 encode
        string b64_headers = Convert.ToBase64String(headers);
        string b64_data = Convert.ToBase64String(data);
        manager.sendTo(0, "2," + b64_headers +","+b64_data + ";");
    }
}
