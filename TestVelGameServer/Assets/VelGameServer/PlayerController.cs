using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : NetworkObject
{

    public Vector3 targetPosition;
    public Quaternion targetRotation;


    public byte[] getSyncMessage()
    {
        float[] data = new float[7];
        for (int i = 0; i < 3; i++)
        {
            data[i] = transform.position[i];
            data[i + 3] = transform.rotation[i];
        }
        data[6] = transform.rotation[3];

        byte[] toReturn = new byte[sizeof(float) * data.Length];
        Buffer.BlockCopy(data, 0, toReturn, 0, toReturn.Length);
        return toReturn;
    }

    public override void handleMessage(string identifier, byte[] message)
    {
        switch (identifier)
        {
            case "s":
                float[] data = new float[7];
                Buffer.BlockCopy(message, 0, data, 0, message.Length);
                for (int i = 0; i < 3; i++)
                {
                    targetPosition[i] = data[i];
                    targetRotation[i] = data[i + 3];
                }
                targetRotation[3] = data[6];
                break;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(syncBehavior());
    }

    IEnumerator syncBehavior()
    {
        while (true)
        {
            if (owner != null && owner.isLocal)
            {

                owner.sendMessage(this, "s", getSyncMessage());
            }
            yield return new WaitForSeconds(.1f);
        }
    }
    // Update is called once per frame
    void Update()
    {
        if (owner != null && !owner.isLocal)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, .1f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, .1f);
        }
        else if(owner != null && owner.isLocal)
        {
            Vector3 movement = new Vector3();
            movement.x += Input.GetAxis("Horizontal");
            movement.y += Input.GetAxis("Vertical");
            movement.z = 0;
            transform.Translate(movement * Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                owner.networkInstantiate("TestNetworkedGameObject");
            }

            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                foreach(KeyValuePair<string,NetworkObject> kvp in owner.manager.objects)
                {
                    owner.takeOwnership(kvp.Key);
                }
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                foreach (KeyValuePair<string, NetworkObject> kvp in owner.manager.objects)
                {
                    owner.networkDestroy(kvp.Key);
                }
            }
        }
    }
}
