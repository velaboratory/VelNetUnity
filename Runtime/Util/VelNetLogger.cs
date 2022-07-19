using UnityEngine;

namespace VelNet
{
	public static class VelNetLogger
	{
		public static void Info(string message, Object context = null)
		{
			if (VelNetManager.instance != null && VelNetManager.instance.debugMessages)
			{
				Debug.Log($"[VelNet] {message}", context);
			}
		}

		public static void Error(string message, Object context = null)
		{
			Debug.LogError($"[VelNet] {message}", context);
		}
	}
}