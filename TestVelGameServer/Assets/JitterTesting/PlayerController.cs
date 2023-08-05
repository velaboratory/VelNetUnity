using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace VelNet
{
	public class PlayerController : MonoBehaviour
	{
		public NetworkObject networkObject;

		public float radius = 5;
		private float angle = 0;
		public float angularSpeed = 1f;
		public float speed = 1f;
		private float direction = 1;
		public float width = 5f;

		private Vector3 lastPos = Vector3.zero;
		private Queue<float> queue = new Queue<float>();
		public int queueLength = 1000;
		public TMP_Text text;
		private Rigidbody rb;

		private void Start()
		{
			rb = GetComponent<Rigidbody>();
		}

		private void Update()
		{
			if (networkObject.IsMine)
			{
				rb.velocity = Vector3.right * (speed * direction);

				if (transform.position.x > width)
				{
					direction = -1;
				}

				if (transform.position.x < -width)
				{
					direction = 1;
				}

				// transform.position = new Vector3(radius * Mathf.Cos(angle), 0, radius * Mathf.Sin(angle));
				// transform.eulerAngles = new Vector3(0, -angle * Mathf.Rad2Deg, 0);
				angle += Time.deltaTime * angularSpeed;
			}
			else
			{
			}

			// show jitter
			if (lastPos != Vector3.zero)
			{
				float distance = Vector3.Distance(lastPos, transform.position);
				queue.Enqueue(distance);
				while (queue.Count > queueLength)
				{
					queue.Dequeue();
				}

				text.text = StandardDeviation(queue).ToString("N4");
			}

			lastPos = transform.position;
		}

		public static float StandardDeviation(Queue<float> values)
		{
			float avg = values.Average();
			return Mathf.Sqrt(values.Average(v => Mathf.Pow(v - avg, 2f)));
		}
	}
}