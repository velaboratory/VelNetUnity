using UnityEngine;
using VelNet;

public class MouseDragger : MonoBehaviour
{
	private Camera cam;
	public string[] draggableTags = { "draggable" };
	private NetworkObject draggingObject;

	private void Start()
	{
		cam = Camera.main;
	}

	private void Update()
	{
		if (Input.GetMouseButtonDown(0))
		{
			if (Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit hit))
			{
				foreach (string draggableTag in draggableTags)
				{
					if (hit.transform.CompareTag(draggableTag) || (hit.transform.parent != null && hit.transform.parent.CompareTag(draggableTag)))
					{
						NetworkObject netObj = hit.transform.GetComponent<NetworkObject>();
						netObj ??= hit.transform.GetComponentInParent<NetworkObject>();
						if (netObj == null) break;
						netObj.TakeOwnership();
						draggingObject = netObj;
						break;
					}
				}
			}
		}
		else if (Input.GetMouseButtonUp(0))
		{
			draggingObject = null;
		}
		else if (Input.GetMouseButton(0) && draggingObject != null)
		{
			draggingObject.transform.position = cam.ScreenPointToRay(Input.mousePosition).direction * Vector3.Distance(draggingObject.transform.position, cam.transform.position) + cam.transform.position;
		}
	}
}